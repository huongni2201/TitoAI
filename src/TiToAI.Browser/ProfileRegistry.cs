using System.Security;
using Microsoft.EntityFrameworkCore;
using TiToAI.Browser.Abstractions;
using TiToAI.Browser.Models;
using TiToAI.Contracts.Browser;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;

namespace TiToAI.Browser;

/// <summary>
/// Quản lý danh mục profile trình duyệt, ánh xạ đường dẫn vật lý an toàn và lưu bền vững trạng thái trong SQLite.
/// </summary>
public sealed class ProfileRegistry : IProfileRegistry
{
    private readonly Func<TiToAIDbContext> _dbContextFactory;
    private readonly string _baseProfilesDirectory;
    private readonly ITabPool? _tabPool;

    public event Func<BrowserProfileStateChangedPayload, Task>? ProfileStateChanged;

    public ProfileRegistry(
        Func<TiToAIDbContext> dbContextFactory,
        string? customBaseProfilesDirectory = null,
        ITabPool? tabPool = null)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _tabPool = tabPool;

        if (!string.IsNullOrWhiteSpace(customBaseProfilesDirectory))
        {
            _baseProfilesDirectory = Path.GetFullPath(customBaseProfilesDirectory);
        }
        else
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _baseProfilesDirectory = Path.Combine(localAppData, DatabasePathHelper.DefaultAppFolderName, "profiles");
        }
    }

    public async Task<BrowserProfileInfo?> GetProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        await using var db = _dbContextFactory();
        var entity = await db.BrowserProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == profileId, cancellationToken);

        if (entity == null) return null;

        return MapToInfo(entity);
    }

    public async Task<IReadOnlyList<BrowserProfileInfo>> ListProfilesAsync(string? workspaceId = null, CancellationToken cancellationToken = default)
    {
        await using var db = _dbContextFactory();
        IQueryable<BrowserProfile> query = db.BrowserProfiles.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            // Trả về Shared AI toàn app kèm các profile thuộc workspace đó
            query = query.Where(p => p.Kind == BrowserProfileKind.SharedAI || p.WorkspaceId == workspaceId);
        }

        var entities = await query.ToListAsync(cancellationToken);
        return entities.Select(MapToInfo).ToList();
    }

    public async Task<BrowserProfileInfo> EnsureProfileAsync(
        string profileId,
        string kind,
        string? workspaceId = null,
        string? accountIdentity = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        if (!BrowserProfileKind.IsValid(kind))
        {
            throw new ArgumentException($"Loại hồ sơ '{kind}' không hợp lệ.", nameof(kind));
        }

        await using var db = _dbContextFactory();
        var existing = await db.BrowserProfiles.FirstOrDefaultAsync(p => p.Id == profileId, cancellationToken);

        string nowUtc = DateTime.UtcNow.ToString("o");

        if (existing == null)
        {
            existing = new BrowserProfile
            {
                Id = profileId,
                Kind = kind,
                WorkspaceId = workspaceId,
                AccountIdentity = accountIdentity,
                VisibilityState = BrowserVisibilityState.Hidden,
                SessionStatus = BrowserSessionStatus.Unverified,
                OperationalState = BrowserOperationalState.Stopped,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            };

            db.BrowserProfiles.Add(existing);
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            bool modified = false;
            if (existing.Kind != kind) { existing.Kind = kind; modified = true; }
            if (workspaceId != null && existing.WorkspaceId != workspaceId) { existing.WorkspaceId = workspaceId; modified = true; }
            if (accountIdentity != null && existing.AccountIdentity != accountIdentity) { existing.AccountIdentity = accountIdentity; modified = true; }

            if (modified)
            {
                existing.UpdatedAtUtc = nowUtc;
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return MapToInfo(existing);
    }

    public async Task UpdateProfileStateAsync(
        string profileId,
        string? visibilityState = null,
        string? sessionStatus = null,
        string? operationalState = null,
        string? accountIdentity = null,
        string? reasonCode = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        await using var db = _dbContextFactory();
        var entity = await db.BrowserProfiles.FirstOrDefaultAsync(p => p.Id == profileId, cancellationToken);
        if (entity == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy hồ sơ trình duyệt '{profileId}'.");
        }

        bool changed = false;

        if (visibilityState != null && BrowserVisibilityState.IsValid(visibilityState) && entity.VisibilityState != visibilityState)
        {
            entity.VisibilityState = visibilityState;
            changed = true;
        }

        if (sessionStatus != null && BrowserSessionStatus.IsValid(sessionStatus) && entity.SessionStatus != sessionStatus)
        {
            entity.SessionStatus = sessionStatus;
            changed = true;
        }

        if (operationalState != null && BrowserOperationalState.IsValid(operationalState) && entity.OperationalState != operationalState)
        {
            entity.OperationalState = operationalState;
            changed = true;
        }

        if (accountIdentity != null && entity.AccountIdentity != accountIdentity)
        {
            entity.AccountIdentity = accountIdentity;
            changed = true;
        }

        if (changed)
        {
            entity.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
            await db.SaveChangesAsync(cancellationToken);

            if (ProfileStateChanged != null)
            {
                var payload = new BrowserProfileStateChangedPayload(
                    ProfileId: entity.Id,
                    VisibilityState: entity.VisibilityState,
                    SessionStatus: entity.SessionStatus,
                    OperationalState: entity.OperationalState,
                    ReasonCode: reasonCode);

                await ProfileStateChanged.Invoke(payload);
            }
        }
    }

    public string GetUserDataDir(string profileId, string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        // Bảo vệ Directory Traversal và chống rò rỉ email/username
        if (profileId.Contains('/') || profileId.Contains('\\') || profileId.Contains(".."))
        {
            throw new SecurityException($"Mã profile '{profileId}' chứa ký tự không an toàn.");
        }

        if (profileId.Contains('@'))
        {
            throw new SecurityException("Mã profile tuyệt đối không được chứa email hoặc username.");
        }

        string fullPath = Path.GetFullPath(Path.Combine(_baseProfilesDirectory, profileId));
        string relative = Path.GetRelativePath(_baseProfilesDirectory, fullPath);

        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new SecurityException("Phát hiện đường dẫn thư mục profile vượt ngoài thư mục gốc cho phép.");
        }

        return fullPath;
    }

    public async Task ReconcileCheckpointsAtStartupAsync(CancellationToken cancellationToken = default)
    {
        await using var db = _dbContextFactory();

        string nowUtc = DateTime.UtcNow.ToString("o");

        // 1. Quét các checkpoint handoff chưa được resolve (ResolvedAtUtc == null)
        var pendingCheckpoints = await db.BrowserHandoffCheckpoints
            .Where(c => c.ResolvedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var checkpoint in pendingCheckpoints)
        {
            checkpoint.ResolvedAtUtc = nowUtc;
        }

        // 2. Quét các profile đang ở trạng thái không tĩnh (Running, Quiescing, hoặc VisibleActive)
        // do ứng dụng trước đó bị crash/tắt đột ngột
        var dirtyProfiles = await db.BrowserProfiles
            .Where(p => p.OperationalState == BrowserOperationalState.Running ||
                        p.OperationalState == BrowserOperationalState.Quiescing ||
                        p.VisibilityState != BrowserVisibilityState.Hidden)
            .ToListAsync(cancellationToken);

        foreach (var profile in dirtyProfiles)
        {
            profile.OperationalState = BrowserOperationalState.Stopped;
            profile.VisibilityState = BrowserVisibilityState.Hidden;
            // Không phục hồi Connected từ metadata cũ nếu tiến trình đã chết
            profile.SessionStatus = BrowserSessionStatus.Unverified;
            profile.UpdatedAtUtc = nowUtc;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private BrowserProfileInfo MapToInfo(BrowserProfile entity)
    {
        var info = new BrowserProfileInfo
        {
            ProfileId = entity.Id,
            Kind = entity.Kind,
            WorkspaceId = entity.WorkspaceId,
            AccountIdentity = entity.AccountIdentity,
            ProviderKey = entity.ProviderKey,
            DisplayName = entity.DisplayName,
            Lifecycle = entity.Lifecycle,
            UserDataDir = GetUserDataDir(entity.Id, entity.Kind),
            VisibilityState = entity.VisibilityState,
            SessionStatus = entity.SessionStatus,
            OperationalState = entity.OperationalState,
            ActiveJobCount = _tabPool?.GetActiveJobCount(entity.Id) ?? 0,
            ActiveTabs = _tabPool?.GetActiveTabs(entity.Id).ToList() ?? []
        };

        return info;
    }
}
