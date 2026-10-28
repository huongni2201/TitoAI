using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using TiToAI.Application.Assets;
using TiToAI.Application.Jobs;
using TiToAI.Browser.Abstractions;
using TiToAI.Browser.Models;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Storage;

namespace TiToAI.Application.Providers;

/// <summary>
/// Runs one MuseAI render in a new side chat of the job's profile: attaches ordered images, sends the prompt,
/// waits for the video, downloads it and stores it as a Video Asset. Selectors come from a read-only DOM
/// inspection of muse.ai on 2026-10-28 and are NOT verified by a live render.
/// </summary>
public sealed class MuseAiPlaywrightAutomation
{
    private const string NewChatUrl = "https://muse.ai/thread/new";
    private static readonly Regex ThreadUrl = new(@"/thread/[0-9a-f-]{36}$", RegexOptions.IgnoreCase);
    private static readonly float RenderTimeoutMs = (float)TimeSpan.FromMinutes(25).TotalMilliseconds;

    private readonly IBrowserProcessController _browser;
    private readonly IProfileRegistry _profiles;
    private readonly DbContextOptions<TiToAIDbContext> _dbOptions;
    private readonly StorageRootRegistry _roots;
    private readonly AssetService _assets;
    private readonly JobActivityLog _activity;

    public MuseAiPlaywrightAutomation(IBrowserProcessController browser, IProfileRegistry profiles,
        DbContextOptions<TiToAIDbContext> dbOptions, StorageRootRegistry roots, AssetService assets)
    {
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _dbOptions = dbOptions ?? throw new ArgumentNullException(nameof(dbOptions));
        _roots = roots ?? throw new ArgumentNullException(nameof(roots));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _activity = new JobActivityLog(dbOptions);
    }

    public async Task<JobExecutionResult> ExecuteRenderAsync(
        JobExecutionContext job,
        Func<Task> beforeSubmit,
        Func<int, string, Task>? progress,
        CancellationToken ct)
    {
        await _activity.WriteAsync(job.JobId, "validate", "Bắt đầu kiểm tra prompt, ảnh và phiên MuseAI.", ct).ConfigureAwait(false);
        var prompt = ReadPrompt(job.PromptSnapshotText);
        var (durationSeconds, imageIds) = ReadRenderInput(job.ConfigSnapshotJson);
        if (string.IsNullOrWhiteSpace(job.ProfileId) || string.IsNullOrWhiteSpace(prompt) || durationSeconds <= 0 || imageIds.Count == 0)
            return new(false, ReasonCode: "invalid_input", ErrorMessage: "Thiếu profile, prompt, thời lượng hoặc ảnh tham chiếu của job.");

        var imagePaths = await ResolveImagePathsAsync(job, imageIds, ct).ConfigureAwait(false);
        var fullPrompt = $"{prompt}\n\nThời lượng yêu cầu: {durationSeconds} giây.";

        var profile = await _profiles.GetProfileAsync(job.ProfileId, ct).ConfigureAwait(false);
        if (profile is null || !string.Equals(profile.ProviderKey, job.ProviderKey, StringComparison.OrdinalIgnoreCase) ||
            profile.SessionStatus != BrowserSessionStatus.Connected || string.IsNullOrWhiteSpace(profile.AccountIdentity))
            return new(false, ReasonCode: "login_required", ErrorMessage: "Phiên MuseAI của tài khoản này chưa được xác minh. Hãy đăng nhập và kiểm tra phiên.");

        var handle = _browser.TryGetContext(job.ProfileId);
        if (handle is null)
            return new(false, ReasonCode: "automation_blocked", ErrorMessage: "Profile MuseAI chưa được mở cho tác vụ render.");

        IPage? page = null;
        var submitted = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            page = await handle.Context.NewPageAsync().ConfigureAwait(false);
            await page.GotoAsync(NewChatUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 }).WaitAsync(ct).ConfigureAwait(false);

            var composer = page.Locator("textarea[placeholder='Message']");
            await composer.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20000 }).WaitAsync(ct).ConfigureAwait(false);
            await page.Locator("input[type='file']").First.SetInputFilesAsync(imagePaths.ToArray()).WaitAsync(ct).ConfigureAwait(false);
            await page.WaitForFunctionAsync("n => document.querySelectorAll('img[src^=\"blob:\"]').length >= n",
                imagePaths.Count, new() { Timeout = 60000 }).WaitAsync(ct).ConfigureAwait(false);
            await _activity.WriteAsync(job.JobId, "media", $"Đã đính kèm {imagePaths.Count} ảnh theo thứ tự.", ct).ConfigureAwait(false);

            await composer.FillAsync(fullPrompt).WaitAsync(ct).ConfigureAwait(false);
            await page.WaitForFunctionAsync("() => { const b = document.querySelector(\"button[aria-label='Send']\"); return !!b && !b.disabled; }",
                null, new() { Timeout = 60000 }).WaitAsync(ct).ConfigureAwait(false);

            await beforeSubmit().ConfigureAwait(false);
            await _activity.WriteAsync(job.JobId, "submit", "Đã ghi checkpoint trước khi gửi.", ct).ConfigureAwait(false);
            submitted = true; // Once the checkpoint is durable, a failed click must be reconciled, never blindly resent.
            await page.Locator("button[aria-label='Send']").ClickAsync().WaitAsync(ct).ConfigureAwait(false);
            await page.WaitForURLAsync(ThreadUrl, new() { Timeout = 30000 }).WaitAsync(ct).ConfigureAwait(false);
            await _activity.WriteAsync(job.JobId, "submit", $"Đã gửi trong side chat {page.Url}.", ct).ConfigureAwait(false);
            if (progress is not null) await progress(50, "Đang chờ MuseAI tạo video").ConfigureAwait(false);

            await page.WaitForFunctionAsync(
                "() => !document.querySelector('[aria-label=\"Assistant is typing\"]') && !!document.querySelector(\"[data-message-role='assistant'] video\")",
                null, new() { Timeout = RenderTimeoutMs }).WaitAsync(ct).ConfigureAwait(false);
            await _activity.WriteAsync(job.JobId, "result", "MuseAI đã trả về video.", ct).ConfigureAwait(false);

            await page.Locator("button[aria-label^='Open animated video']").Last.ClickAsync().WaitAsync(ct).ConfigureAwait(false);
            var download = await page.RunAndWaitForDownloadAsync(
                () => page.Locator("[role=dialog]").GetByRole(AriaRole.Button, new() { Name = "Download" }).ClickAsync(),
                new() { Timeout = 120000 }).WaitAsync(ct).ConfigureAwait(false);

            // Playwright's temp path has no extension; save with .mp4 so the asset service accepts it.
            var downloaded = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mp4");
            try
            {
                await download.SaveAsAsync(downloaded).WaitAsync(ct).ConfigureAwait(false);
                var asset = await _assets.SaveVideoAssetAsync(job.ProjectId, downloaded, ct).ConfigureAwait(false);
                await _activity.WriteAsync(job.JobId, "result", "Đã lưu video MuseAI thành Asset.", ct).ConfigureAwait(false);
                return new(true, ResultReference: asset.AssetId, SideEffectSubmitted: true);
            }
            finally
            {
                File.Delete(downloaded);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (PlaywrightException ex)
        {
            await RecordFailureAsync(job.JobId, submitted ? "Lỗi sau khi gửi; cần đối soát." : "Lỗi thao tác browser trước khi gửi.").ConfigureAwait(false);
            return new(false, ResultReference: submitted ? page?.Url : null,
                ReasonCode: submitted ? "reconcile_required" : "automation_blocked",
                ErrorMessage: $"Không thể hoàn tất thao tác MuseAI: {ex.Message}", SideEffectSubmitted: submitted);
        }
        catch (TimeoutException ex)
        {
            await RecordFailureAsync(job.JobId, submitted ? "Hết thời gian sau khi gửi; cần đối soát." : "Hết thời gian trước khi gửi.").ConfigureAwait(false);
            return new(false, ResultReference: submitted ? page?.Url : null,
                ReasonCode: submitted ? "reconcile_required" : "automation_blocked",
                ErrorMessage: $"Quá thời gian chờ MuseAI: {ex.Message}", SideEffectSubmitted: submitted);
        }
        catch (Exception ex)
        {
            await RecordFailureAsync(job.JobId, submitted ? "Tác vụ lỗi sau khi gửi; cần đối soát." : "Tác vụ lỗi trước khi gửi.").ConfigureAwait(false);
            return new(false, ResultReference: submitted ? page?.Url : null,
                ReasonCode: submitted ? "reconcile_required" : "automation_blocked",
                ErrorMessage: $"Không hoàn tất được render MuseAI: {ex.Message}", SideEffectSubmitted: submitted);
        }
        finally
        {
            if (page is not null && !page.IsClosed)
                try { await page.CloseAsync().ConfigureAwait(false); } catch { }
        }
    }

    private async Task RecordFailureAsync(string jobId, string message)
    {
        try { await _activity.WriteAsync(jobId, "error", message, CancellationToken.None).ConfigureAwait(false); }
        catch { /* Preserve the original provider error. */ }
    }

    private static string ReadPrompt(string? snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot)) return "";
        try
        {
            using var doc = JsonDocument.Parse(snapshot);
            return (doc.RootElement.TryGetProperty("renderedText", out var text) ||
                    doc.RootElement.TryGetProperty("RenderedText", out text)) ? text.GetString()?.Trim() ?? "" : "";
        }
        catch (JsonException)
        {
            return snapshot.Trim();
        }
    }

    private static (int DurationSeconds, IReadOnlyList<string> ImageIds) ReadRenderInput(string? configSnapshotJson)
    {
        if (string.IsNullOrWhiteSpace(configSnapshotJson)) return (0, []);
        using var doc = JsonDocument.Parse(configSnapshotJson);
        if (!doc.RootElement.TryGetProperty("MediaSnapshot", out var media) || media.ValueKind != JsonValueKind.Object)
            return (0, []);
        var duration = media.TryGetProperty("DurationSeconds", out var d) && d.TryGetInt32(out var seconds) ? seconds : 0;
        var ids = media.TryGetProperty("ImageAssetIds", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!)
                .Where(id => !string.IsNullOrWhiteSpace(id)).ToArray()
            : Array.Empty<string>();
        return (duration, ids);
    }

    /// <summary>Resolves ordered image assets to files; rejects missing, out-of-scope or modified files.</summary>
    private async Task<IReadOnlyList<string>> ResolveImagePathsAsync(JobExecutionContext job, IReadOnlyList<string> ids, CancellationToken ct)
    {
        var paths = new List<string>(ids.Count);
        await using var db = new TiToAIDbContext(_dbOptions);
        foreach (var id in ids)
        {
            var asset = await db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct).ConfigureAwait(false);
            if (asset is null || asset.AssetType != "Image" || asset.Status != "Linked" ||
                (asset.ProjectId is not null && asset.ProjectId != job.ProjectId))
                throw new InvalidOperationException($"Ảnh tham chiếu '{id}' không hợp lệ cho Project.");
            var root = await _roots.ResolveRootPathAsync(asset.StorageRootId, ct).ConfigureAwait(false);
            StoragePathGuard.EnsureValidRelativePath(asset.RelativePath);
            var path = Path.GetFullPath(Path.Combine(root, asset.RelativePath));
            if (!StoragePathGuard.IsSubPathOf(root, path) || !File.Exists(path))
                throw new InvalidOperationException($"Ảnh tham chiếu '{id}' bị thiếu hoặc vượt ngoài thư mục lưu trữ.");
            await using var file = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, ct).ConfigureAwait(false));
            if (!hash.Equals(asset.Sha256Hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Ảnh tham chiếu '{id}' đã thay đổi sau khi tạo job.");
            paths.Add(path);
        }
        return paths;
    }
}
