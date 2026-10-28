using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TiToAI.Browser.Abstractions;
using TiToAI.Browser.Models;
using TiToAI.Browser.Publishing;
using TiToAI.Contracts.Publishing;
using TiToAI.Contracts.Content;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;
using TiToAI.Infrastructure.Storage;

namespace TiToAI.Application.Publishing;

public sealed class TikTokPublishingService(
    DbContextOptions<TiToAIDbContext> dbOptions,
    StorageRootRegistry roots,
    IProfileRegistry profiles,
    ITikTokVideoUploader uploader)
{
    private const string AttemptPrefix = "publishing.attempt.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly HashSet<string> Active = new(StringComparer.Ordinal);
    private sealed record StoredAttempt(PublishAttemptDto Attempt, string ProfileId, string Sha256Hash,
        int OwnerProcessId, long OwnerStartedAtTicks);

    public async Task<PublishingListResponse> ListAsync(CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        IReadOnlyList<PublishAttemptDto> attempts;
        try
        {
            await RecoverInterruptedAsync(ct);
            await using var db = new TiToAIDbContext(dbOptions);
            var journal = await db.AppSettings.AsNoTracking().Where(s => s.Key.StartsWith(AttemptPrefix))
                .OrderByDescending(s => s.UpdatedAtUtc).Take(50).Select(s => s.Value).ToListAsync(ct);
            attempts = journal.Select(Read).Select(s => s.Attempt).ToArray();
        }
        finally { Gate.Release(); }

        await using var readDb = new TiToAIDbContext(dbOptions);
        var projects = await readDb.Projects.AsNoTracking().OrderByDescending(p => p.UpdatedAtUtc).ToListAsync(ct);
        var videos = new List<PublishVideoDto>();
        foreach (var project in projects)
        {
            var video = await FindReadyVideoAsync(readDb, project, ct);
            if (video != null) videos.Add(video);
        }
        return new(videos, attempts);
    }

    public async Task<PublishAttemptDto> PublishAsync(PublishVideoRequest request, CancellationToken ct = default)
    {
        ValidateRequest(request);
        var attemptId = Guid.Parse(request.ClientRequestId).ToString("N");
        StoredAttempt stored;
        string filePath;
        await Gate.WaitAsync(ct);
        try
        {
            await using var db = new TiToAIDbContext(dbOptions);
            var prior = await db.AppSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Key == AttemptPrefix + attemptId, ct);
            if (prior != null)
            {
                var existing = Read(prior.Value).Attempt;
                if (existing.ProjectId != request.ProjectId || existing.AssetId != request.AssetId ||
                    existing.AccountId != request.AccountId || existing.Caption != request.Caption || existing.Privacy != request.Privacy)
                    throw new ArgumentException("Mã yêu cầu đã được dùng với nội dung khác.");
                return existing;
            }
            var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.ProjectId, ct)
                ?? throw new KeyNotFoundException("Không tìm thấy dự án.");
            var video = await FindReadyVideoAsync(db, project, ct);
            if (video == null || video.AssetId != request.AssetId)
                throw new InvalidOperationException("Dự án cần đúng một video hoàn chỉnh, chưa stale và không có tác vụ đang chạy.");
            var asset = await db.Assets.AsNoTracking().SingleAsync(a => a.Id == request.AssetId, ct);
            var account = await db.BrowserProfiles.AsNoTracking()
                .Where(p => p.Id == request.AccountId && p.Kind == "TikTok" && p.Lifecycle == "Active")
                .Select(p => new { Id = p.Id, ProfileId = p.Id, AccountIdentity = p.AccountIdentity ?? "" })
                .SingleOrDefaultAsync(ct);
            if (account == null) throw new KeyNotFoundException("Tài khoản TikTok không còn được liên kết.");
            var profile = await profiles.GetProfileAsync(account.ProfileId, ct);
            if (profile == null || profile.Kind != BrowserProfileKind.TikTok || profile.Lifecycle != "Active")
                throw new InvalidOperationException("Tài khoản không có profile TikTok đang hoạt động.");
            if (profile.SessionStatus != BrowserSessionStatus.Connected)
                throw new InvalidOperationException("Hãy đăng nhập và xác minh TikTok trong Cài đặt trước.");
            var accountIdentity = string.IsNullOrWhiteSpace(account.AccountIdentity) ? profile.AccountIdentity : account.AccountIdentity;
            if (string.IsNullOrWhiteSpace(accountIdentity))
                throw new InvalidOperationException("Tài khoản TikTok chưa có danh tính được xác minh trong Cài đặt.");

            var root = await roots.ResolveRootPathAsync(asset.StorageRootId, ct);
            filePath = StoragePathGuard.CombineAndVerify(root, asset.RelativePath);
            if (Path.GetExtension(filePath).ToLowerInvariant() is not (".mp4" or ".mov" or ".webm"))
                throw new InvalidOperationException("Chỉ hỗ trợ video MP4, MOV hoặc WebM.");
            var now = DateTimeOffset.UtcNow.ToString("O");
            stored = new(new(attemptId, project.Id, asset.Id, account.Id, accountIdentity, request.Caption,
                request.Privacy, "Preparing", "Đang kiểm tra phiên, tải video và điền caption…", null, now, now),
                account.ProfileId, asset.Sha256Hash, Environment.ProcessId, Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks);
            if (stored.Sha256Hash.Length != 64) throw new InvalidOperationException("Video thiếu SHA-256 hợp lệ.");

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var guard = GuardKey(stored);
            var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO AppSettings (key, value, updated_at_utc) VALUES ({guard}, {attemptId}, {now})
                ON CONFLICT(key) DO NOTHING
                """, ct);
            if (inserted != 1)
                throw new InvalidOperationException("Video này đã được đăng hoặc đang chờ đối soát trên tài khoản đã chọn. Kiểm tra lịch sử trước.");
            db.AppSettings.Add(new() { Key = AttemptPrefix + attemptId, Value = JsonSerializer.Serialize(stored, JsonOptions), UpdatedAtUtc = now });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            Active.Add(attemptId);
        }
        finally { Gate.Release(); }

        bool submitted = false;
        try
        {
            // Giữ file mở chỉ đọc suốt upload để ngăn sửa/xóa giữa lúc kiểm hash và gửi file trên Windows.
            await using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, ct));
            if (!hash.Equals(stored.Sha256Hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Video đã thay đổi so với bản đã tạo; chưa tải lên TikTok.");
            var url = await uploader.PublishAsync(new(stored.ProfileId, stored.Attempt.AccountIdentity,
                filePath, request.Caption, request.Privacy), async () =>
            {
                ct.ThrowIfCancellationRequested();
                await using var currentDb = new TiToAIDbContext(dbOptions);
                var currentProject = await currentDb.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.ProjectId, ct);
                var currentVideo = currentProject == null ? null : await FindReadyVideoAsync(currentDb, currentProject, ct);
                if (currentVideo?.AssetId != request.AssetId)
                    throw new InvalidOperationException("Đầu ra dự án đã thay đổi trong lúc tải lên; đã dừng trước khi đăng.");
                stored = WithState(stored, "Submitted", "Đã bắt đầu gửi lệnh đăng; đang chờ TikTok xác nhận.");
                submitted = true;
                await SaveAsync(stored, releaseGuard: false);
            }, ct);
            if (!submitted) throw new InvalidOperationException("Adapter không ghi checkpoint trước khi đăng.");
            if (url != null && !TikTokVideoUploader.IsPostUrlForIdentity(url, stored.Attempt.AccountIdentity))
                throw new InvalidOperationException("URL xác nhận bài đăng không khớp tài khoản đã chọn.");
            stored = stored with
            { Attempt = stored.Attempt with { State = "Published", Message = "TikTok đã hiển thị xác nhận đăng video.", ExternalPostUrl = url, UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("O") } };
        }
        catch (Exception ex)
        {
            stored = WithState(stored, submitted ? "Uncertain" : "Failed", submitted
                ? "Chưa xác định TikTok đã đăng hay chưa. Kiểm tra TikTok Studio rồi đối soát; không tự gửi lại."
                : ex is OperationCanceledException ? "Đã dừng trước lệnh đăng; có thể thử lại." : ex.Message);
        }
        await Gate.WaitAsync(CancellationToken.None);
        try
        {
            await SaveAsync(stored, releaseGuard: !submitted && stored.Attempt.State == "Failed");
            return stored.Attempt;
        }
        finally { Active.Remove(attemptId); Gate.Release(); }
    }

    public async Task<PublishAttemptDto> ReconcileAsync(ReconcilePublishRequest request, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            if (Active.Contains(request.AttemptId)) throw new InvalidOperationException("Tác vụ vẫn đang chạy; chưa thể đối soát.");
            await using var db = new TiToAIDbContext(dbOptions);
            var row = await db.AppSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Key == AttemptPrefix + request.AttemptId, ct)
                ?? throw new KeyNotFoundException("Không tìm thấy lần đăng.");
            var stored = Read(row.Value);
            if (HasOtherLiveOwner(stored)) throw new InvalidOperationException("Lần đăng vẫn thuộc tiến trình TiToAI khác; hãy chờ tiến trình đó kết thúc.");
            if (stored.Attempt.State is not ("Submitted" or "Uncertain"))
                throw new InvalidOperationException("Chỉ đối soát lần đăng chưa xác định.");
            if (request.WasPublished && !TikTokVideoUploader.IsPostUrlForIdentity(request.ExternalPostUrl ?? "", stored.Attempt.AccountIdentity))
                throw new ArgumentException("Cần URL video TikTok đúng tài khoản để xác nhận đã đăng.");
            stored = WithState(stored, request.WasPublished ? "UserConfirmedPublished" : "UserConfirmedNotPublished",
                request.WasPublished ? "Người dùng xác nhận đã đăng (chưa xác minh tự động)." : "Người dùng đã kiểm tra TikTok và xác nhận chưa đăng; được phép thử lại.");
            stored = stored with { Attempt = stored.Attempt with { ExternalPostUrl = request.WasPublished ? request.ExternalPostUrl : null } };
            await SaveAsync(stored, releaseGuard: !request.WasPublished);
            return stored.Attempt;
        }
        finally { Gate.Release(); }
    }

    private async Task<PublishVideoDto?> FindReadyVideoAsync(TiToAIDbContext db, Project project, CancellationToken ct)
    {
        if (await db.Jobs.AsNoTracking().AnyAsync(j => j.ProjectId == project.Id &&
            (j.State == "Queued" || j.State == "Reserved" || j.State == "Running" || j.State == "AwaitingUser"), ct)) return null;
        var scenes = await db.Scenes.AsNoTracking().Where(s => s.ProjectId == project.Id).ToListAsync(ct);
        foreach (var scene in scenes)
        {
            var metadata = await db.AppSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Key == "scene.workflow." + scene.Id, ct);
            if (metadata != null)
            {
                using var document = JsonDocument.Parse(metadata.Value);
                if (document.RootElement.EnumerateObject().Any(p => p.Name.Equals("isStale", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.True)) return null;
            }
        }
        string? assetId;
        if (project.CurrentVideoRevisionId != null)
        {
            var revision = await db.VideoRevisions.AsNoTracking().SingleOrDefaultAsync(v =>
                v.Id == project.CurrentVideoRevisionId && v.ProjectId == project.Id && v.State == "Ready", ct);
            assetId = revision?.AssetId;
        }
        else
        {
            // Tương thích output cũ: chỉ một clip đã hoàn tất mới được xem là video cuối.
            assetId = scenes.Count == 1 ? scenes[0].VideoAssetId : null;
        }
        if (assetId == null) return null;
        var asset = await db.Assets.AsNoTracking().SingleOrDefaultAsync(a => a.Id == assetId &&
            (a.ProjectId == project.Id || project.CurrentVideoRevisionId != null) && a.AssetType == "Video" && a.Status == "Linked", ct);
        if (asset == null) return null;
        var content = await db.AppSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Key == "project.video_content." + project.Id, ct);
        var items = content == null ? [] : JsonSerializer.Deserialize<List<VideoContentItemDto>>(content.Value, JsonOptions) ?? [];
        return new(project.Id, project.Name, asset.Id, Path.GetFileName(asset.RelativePath), asset.ByteSize,
            items.FirstOrDefault(i => i.VideoAssetId == asset.Id)?.TikTokContent ?? items.FirstOrDefault()?.TikTokContent ?? "");
    }

    private async Task RecoverInterruptedAsync(CancellationToken ct)
    {
        await using var db = new TiToAIDbContext(dbOptions);
        var rows = await db.AppSettings.AsNoTracking().Where(s => s.Key.StartsWith(AttemptPrefix)).Select(s => s.Value).ToListAsync(ct);
        foreach (var json in rows)
        {
            var item = Read(json);
            if (Active.Contains(item.Attempt.AttemptId) || HasOtherLiveOwner(item)) continue;
            if (item.Attempt.State == "Preparing")
                await SaveAsync(WithState(item, "Failed", "Phiên trước dừng trước lệnh đăng; có thể thử lại."), true);
            else if (item.Attempt.State == "Submitted")
                await SaveAsync(WithState(item, "Uncertain", "Phiên trước dừng sau khi bắt đầu lệnh đăng; cần kiểm tra TikTok trước khi thử lại."), false);
        }
    }

    private async Task SaveAsync(StoredAttempt stored, bool releaseGuard)
    {
        await using var db = new TiToAIDbContext(dbOptions);
        await using var tx = await db.Database.BeginTransactionAsync();
        var row = await db.AppSettings.SingleAsync(s => s.Key == AttemptPrefix + stored.Attempt.AttemptId);
        row.Value = JsonSerializer.Serialize(stored, JsonOptions);
        row.UpdatedAtUtc = stored.Attempt.UpdatedAtUtc;
        if (releaseGuard)
        {
            var guard = await db.AppSettings.SingleOrDefaultAsync(s => s.Key == GuardKey(stored) && s.Value == stored.Attempt.AttemptId);
            if (guard != null) db.AppSettings.Remove(guard);
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private static string GuardKey(StoredAttempt stored) => $"publishing.guard.{stored.ProfileId}.{stored.Sha256Hash.ToLowerInvariant()}";
    private static bool HasOtherLiveOwner(StoredAttempt stored)
    {
        if (stored.OwnerProcessId == Environment.ProcessId || stored.Attempt.State is not ("Preparing" or "Submitted")) return false;
        try
        {
            using var owner = Process.GetProcessById(stored.OwnerProcessId);
            return owner.StartTime.ToUniversalTime().Ticks == stored.OwnerStartedAtTicks && !owner.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return true; }
    }
    private static StoredAttempt Read(string json) => JsonSerializer.Deserialize<StoredAttempt>(json, JsonOptions)
        ?? throw new InvalidOperationException("Lịch sử đăng video không hợp lệ; cần kiểm tra dữ liệu trước khi tiếp tục.");
    private static StoredAttempt WithState(StoredAttempt stored, string state, string message) => stored with
    { Attempt = stored.Attempt with { State = state, Message = message, UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("O") } };

    private static void ValidateRequest(PublishVideoRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Guid.TryParse(request.ClientRequestId, out _) || !Guid.TryParse(request.ProjectId, out _) ||
            !Guid.TryParse(request.AssetId, out _) || !Guid.TryParse(request.AccountId, out _))
            throw new ArgumentException("Mã yêu cầu/dự án/video/tài khoản không hợp lệ.");
        if (string.IsNullOrWhiteSpace(request.Caption) || request.Caption.EnumerateRunes().Count() > 2200)
            throw new ArgumentException("Caption cần từ 1 đến 2.200 ký tự.");
        if (request.Privacy is not ("private" or "friends" or "public")) throw new ArgumentException("Quyền xem không hợp lệ.");
    }
}
