using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using TiToAI.Browser.Abstractions;
using TiToAI.Contracts.Analytics;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;

namespace TiToAI.Application.Analytics;

/// <summary>
/// Backend Engine thực thi kiểm tra phiên, mở cửa sổ đăng nhập hiển thị trên Desktop và
/// tự động tải các CSV Overview, Content, Viewers từ TikTok Studio bằng Playwright,
/// sau đó xử lý thành snapshot bất biến trong SQLite theo từng Không gian làm việc (Workspace).
/// </summary>
public sealed class TikTokAnalyticsSyncService : ITikTokAnalyticsCrawler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly DbContextOptions<TiToAIDbContext> _dbOptions;
    private readonly object _profileRegistry;
    private readonly object _processController;
    private readonly object _loginCoordinator;
    private readonly object _sessionProbe;
    private readonly IAnalyticsService? _analyticsService;

    public TikTokAnalyticsSyncService(
        DbContextOptions<TiToAIDbContext> dbOptions,
        object profileRegistry,
        object processController,
        object loginCoordinator,
        object sessionProbe,
        IAnalyticsService? analyticsService = null)
    {
        _dbOptions = dbOptions ?? throw new ArgumentNullException(nameof(dbOptions));
        _profileRegistry = profileRegistry ?? throw new ArgumentNullException(nameof(profileRegistry));
        _processController = processController ?? throw new ArgumentNullException(nameof(processController));
        _loginCoordinator = loginCoordinator ?? throw new ArgumentNullException(nameof(loginCoordinator));
        _sessionProbe = sessionProbe ?? throw new ArgumentNullException(nameof(sessionProbe));
        _analyticsService = analyticsService;
    }

    /// <summary>
    /// Thẩm định trạng thái session của tài khoản TikTok liên kết với Workspace.
    /// </summary>
    public async Task<TikTokCheckSessionResponseDto> CheckSessionAsync(
        TikTokCheckSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            return new TikTokCheckSessionResponseDto(
                IsLoggedIn: false,
                ProfileId: request.ProfileId,
                AccountIdentity: null,
                SessionStatus: "Unverified",
                ErrorMessage: "WorkspaceId không được để trống.");
        }

        try
        {
            string profileId = await ResolveTikTokProfileIdAsync(request.WorkspaceId, request.ProfileId, cancellationToken);

            var probeResult = await ProbeSessionAsync(profileId, cancellationToken);
            string sessionStatus = probeResult.SessionStatus;
            string? accountIdentity = probeResult.AccountIdentity;
            string? errorMessage = probeResult.ErrorMessage;

            if (string.IsNullOrWhiteSpace(accountIdentity))
            {
                accountIdentity = await GetProfileAccountIdentityAsync(profileId, cancellationToken);
            }

            bool isLoggedIn = probeResult.IsLoggedIn || string.Equals(sessionStatus, "Connected", StringComparison.OrdinalIgnoreCase);

            return new TikTokCheckSessionResponseDto(
                IsLoggedIn: isLoggedIn,
                ProfileId: profileId,
                AccountIdentity: accountIdentity,
                SessionStatus: sessionStatus,
                ErrorMessage: errorMessage);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new TikTokCheckSessionResponseDto(
                IsLoggedIn: false,
                ProfileId: request.ProfileId,
                AccountIdentity: null,
                SessionStatus: "Unverified",
                ErrorMessage: $"Lỗi kiểm tra phiên TikTok: {ex.Message}");
        }
    }

    /// <summary>
    /// Mở cửa sổ trình duyệt Chromium hiển thị (Headless = false) cho người dùng đăng nhập TikTok.
    /// </summary>
    public async Task<TikTokOpenLoginResponseDto> OpenLoginAsync(
        TikTokOpenLoginRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            return new TikTokOpenLoginResponseDto(
                Success: false,
                ProfileId: string.Empty,
                VisibilityState: "Hidden",
                Message: "WorkspaceId không được để trống.");
        }

        try
        {
            string profileId = await ResolveTikTokProfileIdAsync(request.WorkspaceId, request.ProfileId, cancellationToken);
            string targetUrl = !string.IsNullOrWhiteSpace(request.TargetUrl)
                ? request.TargetUrl
                : "https://www.tiktok.com/tiktokstudio";

            var loginResult = await RequestLoginAsync(profileId, targetUrl, cancellationToken);

            return new TikTokOpenLoginResponseDto(
                Success: true,
                ProfileId: profileId,
                VisibilityState: loginResult.VisibilityState,
                Message: "Cửa sổ trình duyệt TikTok đã được mở trên màn hình desktop. Vui lòng hoàn tất đăng nhập tài khoản kênh.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new TikTokOpenLoginResponseDto(
                Success: false,
                ProfileId: request.ProfileId ?? string.Empty,
                VisibilityState: "Hidden",
                Message: $"Không thể mở cửa sổ đăng nhập: {ex.Message}");
        }
    }

    /// <summary>
    /// Thu thập và đồng bộ chỉ số kênh và danh sách video từ TikTok qua trình duyệt về SQLite snapshot.
    /// </summary>
    public async Task<TikTokSyncChannelResponseDto> SyncChannelDataAsync(
        TikTokSyncChannelRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            return new TikTokSyncChannelResponseDto(
                Success: false, SnapshotId: null, RecordCount: 0, ChannelHandle: null,
                Message: "WorkspaceId không được để trống.", ProfileId: request.ProfileId);
        }

        string profileId = await ResolveTikTokProfileIdAsync(request.WorkspaceId, request.ProfileId, cancellationToken);
        var session = await CheckSessionAsync(new TikTokCheckSessionRequest(request.WorkspaceId, profileId), cancellationToken);
        if (!session.IsLoggedIn || string.Equals(session.SessionStatus, "SessionExpired", StringComparison.OrdinalIgnoreCase))
        {
            var login = await OpenLoginAsync(new TikTokOpenLoginRequest(request.WorkspaceId, profileId), cancellationToken);
            return new TikTokSyncChannelResponseDto(
                Success: false, SnapshotId: null, RecordCount: 0, ChannelHandle: session.AccountIdentity,
                Message: login.Success
                    ? "Đã mở TikTok Studio để đăng nhập. Sau khi đăng nhập xong, bấm Kéo dữ liệu lần nữa."
                    : login.Message,
                RequiresLogin: true, ProfileId: profileId);
        }

        if (_analyticsService == null)
        {
            return new TikTokSyncChannelResponseDto(
                Success: false, SnapshotId: null, RecordCount: 0, ChannelHandle: session.AccountIdentity,
                Message: "Dịch vụ lưu dữ liệu phân tích chưa được khởi tạo.", ProfileId: profileId);
        }

        bool ownsContext = false;
        bool ownsPage = false;
        object? handle = IsProcessRunning(profileId) ? TryGetProcessContext(profileId) : null;
        if (handle == null)
        {
            handle = await LaunchPersistentContextAsync(
                profileId,
                isHeadless: true,
                initialUrl: "https://www.tiktok.com/tiktokstudio",
                cancellationToken: cancellationToken);
            ownsContext = true;
        }

        if (handle is not IBrowserContextHandle contextHandle)
        {
            if (ownsContext) await CloseProcessContextAsync(profileId, CancellationToken.None);
            return new TikTokSyncChannelResponseDto(
                Success: false, SnapshotId: null, RecordCount: 0, ChannelHandle: session.AccountIdentity,
                Message: "Không thể mở ngữ cảnh Playwright của tài khoản TikTok.", ProfileId: profileId);
        }
        var context = contextHandle.Context;

        IPage? page = null;
        string? tempDir = null;
        try
        {
            page = context.Pages.FirstOrDefault(p => !p.IsClosed && p.Url.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase));
            if (page == null)
            {
                page = await context.NewPageAsync();
                ownsPage = true;
            }

            if (!page.Url.Contains("tiktokstudio", StringComparison.OrdinalIgnoreCase))
            {
                await page.GotoAsync("https://www.tiktok.com/tiktokstudio", new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 30000
                });
            }

            await ClickExactAsync(page, "Analytics", cancellationToken);
            tempDir = Path.Combine(Path.GetTempPath(), "TiToAI", "tiktok-export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            string overviewCsv = await ExportSectionCsvAsync(page, "Overview", tempDir, cancellationToken);
            string contentCsv = await ExportSectionCsvAsync(page, "Content", tempDir, cancellationToken);
            string viewersCsv = await ExportSectionCsvAsync(page, "Viewers", tempDir, cancellationToken);

            string? accountId;
            await using (var db = new TiToAIDbContext(_dbOptions))
            {
                accountId = await db.Accounts.AsNoTracking()
                    .Where(a => a.WorkspaceId == request.WorkspaceId && a.ProfileId == profileId)
                    .Select(a => a.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            var imported = await _analyticsService.ImportTikTokStudioExportsAsync(
                new ImportTikTokStudioExportsRequest(request.WorkspaceId, accountId, overviewCsv, contentCsv, viewersCsv),
                cancellationToken);

            await UpdateProfileStateAsync(
                profileId,
                sessionStatus: "Connected",
                accountIdentity: session.AccountIdentity,
                cancellationToken: cancellationToken);

            int total = imported.OverviewRecordCount + imported.ContentRecordCount + imported.ViewersRecordCount;
            return new TikTokSyncChannelResponseDto(
                Success: true,
                SnapshotId: imported.SnapshotId,
                RecordCount: total,
                ChannelHandle: session.AccountIdentity,
                Message: $"Đã xuất và lưu TikTok Studio: Overview {imported.OverviewRecordCount}, Content {imported.ContentRecordCount}, Viewers {imported.ViewersRecordCount} dòng.",
                ProfileId: profileId,
                OverviewRecordCount: imported.OverviewRecordCount,
                ContentRecordCount: imported.ContentRecordCount,
                ViewersRecordCount: imported.ViewersRecordCount);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new TikTokSyncChannelResponseDto(
                Success: false, SnapshotId: null, RecordCount: 0, ChannelHandle: session.AccountIdentity,
                Message: $"Không thể xuất hoặc đọc CSV TikTok Studio: {ex.Message}", ProfileId: profileId);
        }
        finally
        {
            if (ownsPage && page != null && !page.IsClosed)
            {
                try { await page.CloseAsync(); } catch { }
            }
            if (ownsContext)
            {
                try { await CloseProcessContextAsync(profileId, CancellationToken.None); } catch { }
            }
            if (tempDir != null && Directory.Exists(tempDir))
            {
                foreach (var file in Directory.EnumerateFiles(tempDir))
                {
                    try { File.Delete(file); } catch { }
                }
                try { Directory.Delete(tempDir); } catch { }
            }
        }
    }

    private static async Task ClickExactAsync(IPage page, string label, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var locators = new[]
        {
            page.GetByRole(AriaRole.Tab, new() { Name = label, Exact = true }),
            page.GetByRole(AriaRole.Button, new() { Name = label, Exact = true }),
            page.GetByRole(AriaRole.Link, new() { Name = label, Exact = true }),
            page.GetByText(label, new() { Exact = true })
        };
        foreach (var locator in locators)
        {
            if (await locator.CountAsync() > 0)
            {
                await locator.First.ClickAsync(new() { Timeout = 10000 });
                return;
            }
        }
        await page.GetByText(label, new() { Exact = true }).First.ClickAsync(new() { Timeout = 15000 });
    }

    private static async Task<string> ExportSectionCsvAsync(
        IPage page,
        string section,
        string tempDir,
        CancellationToken cancellationToken)
    {
        await ClickExactAsync(page, section, cancellationToken);
        var exportButton = page.GetByRole(AriaRole.Button, new()
        {
            NameRegex = new Regex("export|download", RegexOptions.IgnoreCase)
        }).First;
        try
        {
            await exportButton.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 15000
            });
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException($"Không tìm thấy nút xuất dữ liệu trong mục {section}.");
        }

        var downloadTask = page.RunAndWaitForDownloadAsync(
            () => exportButton.ClickAsync(),
            new PageRunAndWaitForDownloadOptions { Timeout = 60000 });

        var csvOptions = new[]
        {
            page.GetByRole(AriaRole.Menuitem, new() { NameRegex = new Regex("csv", RegexOptions.IgnoreCase) }),
            page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("csv", RegexOptions.IgnoreCase) }),
            page.GetByRole(AriaRole.Link, new() { NameRegex = new Regex("csv", RegexOptions.IgnoreCase) })
        };
        for (int attempt = 0; attempt < 20 && !downloadTask.IsCompleted; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var csvOption in csvOptions)
            {
                if (await csvOption.CountAsync() > 0 && await csvOption.First.IsVisibleAsync())
                {
                    await csvOption.First.ClickAsync();
                    attempt = 20;
                    break;
                }
            }
            if (attempt < 20 && !downloadTask.IsCompleted) await page.WaitForTimeoutAsync(250);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var download = await downloadTask.WaitAsync(cancellationToken);
        var fileName = $"{section.ToLowerInvariant()}-{Guid.NewGuid():N}.csv";
        var filePath = Path.Combine(tempDir, fileName);
        await download.SaveAsAsync(filePath);
        return await File.ReadAllTextAsync(filePath, cancellationToken);
    }
    private async Task<string> ResolveTikTokProfileIdAsync(
        string workspaceId,
        string? requestedProfileId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedProfileId))
        {
            return requestedProfileId.Trim();
        }

        await using var dbContext = new TiToAIDbContext(_dbOptions);

        string? workspaceHandle = await dbContext.Workspaces.AsNoTracking()
            .Where(w => w.Id == workspaceId)
            .Select(w => w.ChannelHandle)
            .FirstOrDefaultAsync(cancellationToken);

        var providerAccounts = await dbContext.ProviderAccounts
            .Include(a => a.Profile)
            .AsNoTracking()
            .Where(a => a.ProviderKey == "tiktok" && a.Lifecycle == "Active")
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(workspaceHandle))
        {
            var matchingProfileIds = providerAccounts
                .Where(a => TikTokIdentityMatches(a.VerifiedIdentity ?? a.Profile?.AccountIdentity, workspaceHandle))
                .Select(a => a.ProfileId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (matchingProfileIds.Count == 1) return matchingProfileIds[0];
            if (matchingProfileIds.Count > 1)
            {
                throw new InvalidOperationException($"Có nhiều profile TikTok trùng với kênh {workspaceHandle}; không thể chọn an toàn.");
            }
        }

        var activeProviderProfileIds = providerAccounts
            .Select(a => a.ProfileId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (activeProviderProfileIds.Count == 1) return activeProviderProfileIds[0];
        if (activeProviderProfileIds.Count > 1)
        {
            throw new InvalidOperationException($"Workspace '{workspaceId}' chưa được liên kết với một trong các tài khoản TikTok đang hoạt động.");
        }

        // Ưu tiên profile đang hoạt động ở Cài đặt; liên kết Workspace cũ có thể trỏ tới profile hết hạn.
        var account = await dbContext.Accounts
            .AsNoTracking()
            .Where(a => a.WorkspaceId == workspaceId &&
                        (a.Provider == "tiktok" || a.Provider == "TikTok" || a.Provider == "tiktok-channel") &&
                        a.Status != "Unlinked")
            .OrderByDescending(a => a.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (account != null && !string.IsNullOrWhiteSpace(account.ProfileId))
        {
            return account.ProfileId;
        }

        // Tìm profile trình duyệt cũ theo Workspace.
        var browserProfile = await dbContext.BrowserProfiles
            .AsNoTracking()
            .Where(p => (p.Kind == "TikTok" || p.Kind == "tiktok") &&
                        p.WorkspaceId == workspaceId &&
                        p.Lifecycle == "Active")
            .FirstOrDefaultAsync(cancellationToken);

        if (browserProfile != null && !string.IsNullOrWhiteSpace(browserProfile.Id))
        {
            return browserProfile.Id;
        }

        // 3. Tìm xem có profile TikTok nào khác đang active hoặc profile có sẵn trên máy
        var existingAnyProfiles = await dbContext.BrowserProfiles
            .AsNoTracking()
            .Where(p => (p.Kind == "TikTok" || p.Kind == "tiktok" || p.Id.StartsWith("profile_tiktok_")) &&
                        p.Lifecycle == "Active")
            .ToListAsync(cancellationToken);

        if (existingAnyProfiles.Count == 1 && !string.IsNullOrWhiteSpace(existingAnyProfiles[0].Id))
        {
            return existingAnyProfiles[0].Id;
        }
        if (existingAnyProfiles.Count > 1)
        {
            throw new InvalidOperationException($"Workspace '{workspaceId}' chưa được liên kết với tài khoản TikTok; có nhiều profile nên không thể chọn an toàn.");
        }

        // Kiểm tra thư mục vật lý %LOCALAPPDATA%/TiToAI/profiles
        bool ambiguousProfileDirectories = false;
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string profilesDir = Path.Combine(localAppData, "TiToAI", "profiles");
            if (Directory.Exists(profilesDir))
            {
                var matchDirs = Directory.GetDirectories(profilesDir, "profile_tiktok_*");
                if (matchDirs.Length == 1)
                {
                    string candidateId = Path.GetFileName(matchDirs[0]);
                    if (!string.IsNullOrWhiteSpace(candidateId))
                    {
                        await EnsureProfileAsync(candidateId, "TikTok", workspaceId, cancellationToken);
                        return candidateId;
                    }
                }
                if (matchDirs.Length > 1)
                {
                    ambiguousProfileDirectories = true;
                }
            }
        }
        catch
        {
            // Bỏ qua lỗi truy cập hệ thống file nếu có
        }
        if (ambiguousProfileDirectories)
        {
            throw new InvalidOperationException("Có nhiều thư mục profile TikTok cũ nhưng chưa có liên kết Workspace; cần chọn đúng tài khoản trước khi đồng bộ.");
        }

        // 4. Fallback: Tạo mới profile theo quy chuẩn tiktok-{workspaceId}
        string fallbackProfileId = $"tiktok-{workspaceId}";
        await EnsureProfileAsync(fallbackProfileId, "TikTok", workspaceId, cancellationToken);
        return fallbackProfileId;
    }

    private static bool TikTokIdentityMatches(string? identity, string? channelHandle)
    {
        static string? ExtractHandle(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var match = Regex.Match(value.Trim(), @"^@?([A-Za-z0-9_.-]+)(?:\s*\(.*\))?$");
            return match.Success ? match.Groups[1].Value : null;
        }

        var identityHandle = ExtractHandle(identity);
        var workspaceHandle = ExtractHandle(channelHandle);
        return identityHandle != null && workspaceHandle != null &&
               string.Equals(identityHandle, workspaceHandle, StringComparison.OrdinalIgnoreCase);
    }

    #region Reflection Helpers for Browser Abstractions

    private async Task<(bool IsLoggedIn, string SessionStatus, string? AccountIdentity, string? ErrorMessage)> ProbeSessionAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        // 1. Thử ResolveProfileIdentityAsync nếu _sessionProbe hiện thực IProfileIdentityResolver
        var resolveMethod = _sessionProbe.GetType().GetMethod("ResolveProfileIdentityAsync", new[] { typeof(string), typeof(string), typeof(CancellationToken) });
        if (resolveMethod != null)
        {
            var taskObj = resolveMethod.Invoke(_sessionProbe, new object[] { profileId, "tiktok", cancellationToken });
            if (taskObj is Task task)
            {
                await task.ConfigureAwait(false);
                var resultProp = task.GetType().GetProperty("Result");
                var res = resultProp?.GetValue(task);
                if (res != null)
                {
                    var isSuccess = (bool)(res.GetType().GetProperty("IsSuccess")?.GetValue(res) ?? false);
                    var status = (string?)(res.GetType().GetProperty("SessionStatus")?.GetValue(res)) ?? (isSuccess ? "Connected" : "Unverified");
                    var identity = (string?)(res.GetType().GetProperty("AccountIdentity")?.GetValue(res));
                    var err = (string?)(res.GetType().GetProperty("ErrorMessage")?.GetValue(res));
                    bool loggedIn = string.Equals(status, "Connected", StringComparison.OrdinalIgnoreCase);
                    return (loggedIn, status, identity, err);
                }
            }
        }

        // 2. Fallback sang ISessionProbe.CheckSessionAsync(string, CancellationToken)
        var checkMethod = _sessionProbe.GetType().GetMethod("CheckSessionAsync", new[] { typeof(string), typeof(CancellationToken) });
        if (checkMethod != null)
        {
            var taskObj = checkMethod.Invoke(_sessionProbe, new object[] { profileId, cancellationToken });
            if (taskObj is Task task)
            {
                await task.ConfigureAwait(false);
                var resultProp = task.GetType().GetProperty("Result");
                var res = resultProp?.GetValue(task);
                if (res != null)
                {
                    var status = (string?)(res.GetType().GetProperty("SessionStatus")?.GetValue(res)) ?? "Unverified";
                    bool loggedIn = string.Equals(status, "Connected", StringComparison.OrdinalIgnoreCase);
                    return (loggedIn, status, null, null);
                }
            }
        }

        return (false, "Unverified", null, "Không tìm thấy phương thức thẩm định phiên.");
    }

    private async Task<(bool Queued, string VisibilityState)> RequestLoginAsync(
        string profileId,
        string targetUrl,
        CancellationToken cancellationToken)
    {
        var method = _loginCoordinator.GetType().GetMethod("RequestLoginAsync", new[] { typeof(string), typeof(string), typeof(CancellationToken) })
                     ?? _loginCoordinator.GetType().GetMethod("RequestLoginAsync", new[] { typeof(string), typeof(CancellationToken) });

        if (method != null)
        {
            object[] args = method.GetParameters().Length == 3
                ? new object[] { profileId, targetUrl, cancellationToken }
                : new object[] { profileId, cancellationToken };

            var taskObj = method.Invoke(_loginCoordinator, args);
            if (taskObj is Task task)
            {
                await task.ConfigureAwait(false);
                var resultProp = task.GetType().GetProperty("Result");
                var res = resultProp?.GetValue(task);
                if (res != null)
                {
                    var queued = (bool)(res.GetType().GetProperty("Queued")?.GetValue(res) ?? false);
                    var visibility = (string?)(res.GetType().GetProperty("VisibilityState")?.GetValue(res)) ?? "VisibleWaiting";
                    return (queued, visibility);
                }
            }
        }

        return (false, "VisibleWaiting");
    }

    private async Task EnsureProfileAsync(string profileId, string kind, string? workspaceId, CancellationToken cancellationToken)
    {
        var methods = _profileRegistry.GetType().GetMethods().Where(m => m.Name == "EnsureProfileAsync");
        foreach (var m in methods)
        {
            var ps = m.GetParameters();
            if (ps.Length >= 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(string))
            {
                var args = new object?[ps.Length];
                args[0] = profileId;
                args[1] = kind;
                for (int i = 2; i < ps.Length; i++)
                {
                    if (ps[i].Name?.Equals("workspaceId", StringComparison.OrdinalIgnoreCase) == true)
                        args[i] = workspaceId;
                    else if (ps[i].ParameterType == typeof(CancellationToken))
                        args[i] = cancellationToken;
                    else
                        args[i] = ps[i].DefaultValue ?? null;
                }

                var taskObj = m.Invoke(_profileRegistry, args);
                if (taskObj is Task task)
                {
                    await task.ConfigureAwait(false);
                    return;
                }
            }
        }
    }

    private async Task UpdateProfileStateAsync(
        string profileId,
        string? visibilityState = null,
        string? sessionStatus = null,
        string? operationalState = null,
        string? accountIdentity = null,
        string? reasonCode = null,
        CancellationToken cancellationToken = default)
    {
        var methods = _profileRegistry.GetType().GetMethods().Where(m => m.Name == "UpdateProfileStateAsync");
        foreach (var m in methods)
        {
            var ps = m.GetParameters();
            if (ps.Length > 0 && ps[0].ParameterType == typeof(string))
            {
                var args = new object?[ps.Length];
                args[0] = profileId;
                for (int i = 1; i < ps.Length; i++)
                {
                    var name = ps[i].Name?.ToLowerInvariant();
                    if (name == "visibilitystate") args[i] = visibilityState;
                    else if (name == "sessionstatus") args[i] = sessionStatus;
                    else if (name == "operationalstate") args[i] = operationalState;
                    else if (name == "accountidentity") args[i] = accountIdentity;
                    else if (name == "reasoncode") args[i] = reasonCode;
                    else if (ps[i].ParameterType == typeof(CancellationToken)) args[i] = cancellationToken;
                    else args[i] = ps[i].DefaultValue ?? null;
                }

                var taskObj = m.Invoke(_profileRegistry, args);
                if (taskObj is Task task)
                {
                    await task.ConfigureAwait(false);
                    return;
                }
            }
        }
    }

    private async Task<string?> GetProfileAccountIdentityAsync(string profileId, CancellationToken cancellationToken)
    {
        var m = _profileRegistry.GetType().GetMethod("GetProfileAsync", new[] { typeof(string), typeof(CancellationToken) });
        if (m != null)
        {
            var taskObj = m.Invoke(_profileRegistry, new object[] { profileId, cancellationToken });
            if (taskObj is Task task)
            {
                await task.ConfigureAwait(false);
                var result = task.GetType().GetProperty("Result")?.GetValue(task);
                if (result != null)
                {
                    return (string?)result.GetType().GetProperty("AccountIdentity")?.GetValue(result);
                }
            }
        }
        return null;
    }

    private bool IsProcessRunning(string profileId)
    {
        var m = _processController.GetType().GetMethod("IsRunning", new[] { typeof(string) });
        if (m != null)
        {
            return (bool)(m.Invoke(_processController, new object[] { profileId }) ?? false);
        }
        return false;
    }

    private object? TryGetProcessContext(string profileId)
    {
        var m = _processController.GetType().GetMethod("TryGetContext", new[] { typeof(string) });
        return m?.Invoke(_processController, new object[] { profileId });
    }

    private async Task<object?> LaunchPersistentContextAsync(string profileId, bool isHeadless, string? initialUrl, CancellationToken cancellationToken)
    {
        var methods = _processController.GetType().GetMethods().Where(m => m.Name == "LaunchPersistentContextAsync");
        foreach (var m in methods)
        {
            var ps = m.GetParameters();
            if (ps.Length >= 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(bool))
            {
                var args = new object?[ps.Length];
                args[0] = profileId;
                args[1] = isHeadless;
                for (int i = 2; i < ps.Length; i++)
                {
                    if (ps[i].Name?.Equals("initialUrl", StringComparison.OrdinalIgnoreCase) == true)
                        args[i] = initialUrl;
                    else if (ps[i].ParameterType == typeof(CancellationToken))
                        args[i] = cancellationToken;
                    else
                        args[i] = ps[i].DefaultValue ?? null;
                }

                var taskObj = m.Invoke(_processController, args);
                if (taskObj is Task task)
                {
                    await task.ConfigureAwait(false);
                    return task.GetType().GetProperty("Result")?.GetValue(task);
                }
            }
        }
        return null;
    }

    private async Task CloseProcessContextAsync(string profileId, CancellationToken cancellationToken)
    {
        var m = _processController.GetType().GetMethod("CloseContextAsync", new[] { typeof(string), typeof(CancellationToken) });
        if (m != null)
        {
            var taskObj = m.Invoke(_processController, new object[] { profileId, cancellationToken });
            if (taskObj is Task task)
            {
                await task.ConfigureAwait(false);
            }
        }
    }

    #endregion


}
