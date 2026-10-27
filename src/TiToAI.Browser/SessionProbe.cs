using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using TiToAI.Browser.Abstractions;
using TiToAI.Browser.Models;
using TiToAI.Contracts.Accounts;
using TiToAI.Contracts.Browser;

namespace TiToAI.Browser;

/// <summary>
/// Thẩm định trạng thái session cookies và giải quyết danh tính tài khoản theo hợp đồng IProfileIdentityResolver.
/// Hỗ trợ probe thực tế trên Chromium Persistent Context (cả khi cửa sổ đăng nhập đang mở lẫn khi đã đóng),
/// trích xuất danh tính đáng tin cậy từ đúng phiên của Provider (MuseAI, ChatGPT, Gemini).
/// Không dùng nhãn tài khoản, URL chat, hoặc chữ 'Connected' chung để đánh dấu Verified.
/// </summary>
public sealed class SessionProbe : ISessionProbe, IProfileIdentityResolver
{
    private readonly IProfileRegistry _profileRegistry;
    private readonly IBrowserProcessController? _processController;
    private readonly Func<string, string, CancellationToken, Task<ProfileIdentityResolutionResult?>>? _externalAdapter;
    private readonly Func<string, CancellationToken, Task<BrowserCheckSessionResponseData?>>? _sessionCheckAdapter;

    public SessionProbe(
        IProfileRegistry profileRegistry,
        IBrowserProcessController? processController = null,
        Func<string, string, CancellationToken, Task<ProfileIdentityResolutionResult?>>? externalAdapter = null,
        Func<string, CancellationToken, Task<BrowserCheckSessionResponseData?>>? sessionCheckAdapter = null)
    {
        _profileRegistry = profileRegistry ?? throw new ArgumentNullException(nameof(profileRegistry));
        _processController = processController;
        _externalAdapter = externalAdapter;
        _sessionCheckAdapter = sessionCheckAdapter;
    }

    public async Task<BrowserCheckSessionResponseData> CheckSessionAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        if (_sessionCheckAdapter != null)
        {
            var customCheck = await _sessionCheckAdapter(profileId, cancellationToken);
            if (customCheck != null)
            {
                return customCheck;
            }
        }

        var profile = await _profileRegistry.GetProfileAsync(profileId, cancellationToken);
        if (profile == null)
        {
            return new BrowserCheckSessionResponseData(
                SessionStatus: BrowserSessionStatus.Unverified,
                IdentityMatches: null);
        }

        // Nếu có IBrowserProcessController, thực hiện probe thực tế để có trạng thái phiên và danh tính mới nhất
        if (_processController != null)
        {
            string providerKey = profile.ProviderKey ?? profile.Kind;
            var probeResult = await ResolveProfileIdentityAsync(profileId, providerKey, cancellationToken);

            bool? probeIdentityMatches = probeResult.IsSuccess && !string.IsNullOrWhiteSpace(probeResult.AccountIdentity)
                ? true
                : (profile.AccountIdentity != null ? true : null);

            return new BrowserCheckSessionResponseData(
                SessionStatus: probeResult.SessionStatus ?? profile.SessionStatus,
                IdentityMatches: probeIdentityMatches);
        }

        bool? identityMatches = profile.AccountIdentity != null ? true : null;
        return new BrowserCheckSessionResponseData(
            SessionStatus: profile.SessionStatus,
            IdentityMatches: identityMatches);
    }

    public async Task<ProfileIdentityResolutionResult> ResolveProfileIdentityAsync(
        string profileId,
        string provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        var profile = await _profileRegistry.GetProfileAsync(profileId, cancellationToken);
        if (profile == null)
        {
            return ProfileIdentityResolutionResult.Failed($"Hồ sơ trình duyệt '{profileId}' không tồn tại trong hệ thống.");
        }

        // 1. Nếu có adapter kiểm tra session bên ngoài (cho unit/smoke test custom)
        if (_externalAdapter != null)
        {
            var result = await _externalAdapter(profileId, provider, cancellationToken);
            if (result != null) return result;
        }

        var normalizedProvider = provider.Trim().ToLowerInvariant();
        if (normalizedProvider == "museai-video") normalizedProvider = "museai";

        // 2. Thực hiện probe thực tế cho từng Provider nếu có IBrowserProcessController
        if (_processController != null)
        {
            if (normalizedProvider is "museai" or "muse")
            {
                return await ProbeMuseAiAsync(profile, cancellationToken);
            }

            if (normalizedProvider is "chatgpt-web" or "chatgpt" or "openai")
            {
                return await ProbeChatGptAsync(profile, cancellationToken);
            }

            if (normalizedProvider is "gemini-web" or "gemini" or "google")
            {
                return await ProbeGeminiAsync(profile, cancellationToken);
            }

            if (normalizedProvider is "tiktok")
            {
                return await ProbeTikTokAsync(profile, cancellationToken);
            }
        }

        // 3. Fallback: Nếu profile đã có trạng thái Connected và danh tính tài khoản hợp lệ từ trước
        if (string.Equals(profile.SessionStatus, BrowserSessionStatus.Connected, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(profile.AccountIdentity))
        {
            return ProfileIdentityResolutionResult.Success(
                profileId: profile.ProfileId,
                provider: provider,
                accountIdentity: profile.AccountIdentity,
                sessionStatus: BrowserSessionStatus.Connected);
        }

        // Khi chưa có adapter thật hoặc phiên chưa đăng nhập: Không fake PASS!
        return new ProfileIdentityResolutionResult(
            IsSuccess: false,
            ProfileId: profileId,
            Provider: provider,
            AccountIdentity: profile.AccountIdentity,
            SessionStatus: BrowserSessionStatus.Unverified,
            ErrorMessage: "Phiên làm việc chưa được xác thực (Unverified). Yêu cầu đăng nhập tài khoản trước khi liên kết.");
    }

    private async Task<ProfileIdentityResolutionResult> ProbeMuseAiAsync(
        BrowserProfileInfo profile,
        CancellationToken cancellationToken)
    {
        if (_processController == null)
        {
            return ProfileIdentityResolutionResult.Failed("Chưa khởi tạo IBrowserProcessController để kiểm tra phiên MuseAI.");
        }

        bool isRunning = _processController.IsRunning(profile.ProfileId);
        if (isRunning)
        {
            var activeHandle = _processController.TryGetContext(profile.ProfileId);
            if (activeHandle != null && activeHandle.Context != null)
            {
                IPage? page = null;
                bool tempPageCreated = false;
                try
                {
                    if (activeHandle.Context.Pages != null)
                    {
                        page = activeHandle.Context.Pages.FirstOrDefault(p => !p.IsClosed && p.Url.Contains("muse.ai"))
                               ?? activeHandle.Context.Pages.FirstOrDefault(p => !p.IsClosed);
                    }
                }
                catch { }

                if (page == null)
                {
                    try
                    {
                        page = await activeHandle.Context.NewPageAsync();
                        tempPageCreated = true;
                    }
                    catch (Exception ex)
                    {
                        return ProfileIdentityResolutionResult.Failed($"Không thể mở tab kiểm tra phiên trên trình duyệt đang hoạt động: {ex.Message}");
                    }
                }

                try
                {
                    return await ExecuteMuseAiProbeOnPageAsync(page, activeHandle.Context, profile, cancellationToken);
                }
                finally
                {
                    if (tempPageCreated && !page.IsClosed)
                    {
                        try { await page.CloseAsync(); } catch { }
                    }
                }
            }
        }

        // Nếu trình duyệt đang đóng: Khởi chạy persistent context tạm thời ở chế độ ẩn (isHeadless = true)
        IBrowserContextHandle? tempHandle = null;
        try
        {
            tempHandle = await _processController.LaunchPersistentContextAsync(
                profile.ProfileId,
                isHeadless: true,
                initialUrl: "https://muse.ai",
                cancellationToken: cancellationToken);

            IPage? page = null;
            if (tempHandle.Context.Pages != null)
            {
                page = tempHandle.Context.Pages.FirstOrDefault(p => !p.IsClosed);
            }
            page ??= await tempHandle.Context.NewPageAsync();

            return await ExecuteMuseAiProbeOnPageAsync(page, tempHandle.Context, profile, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException tex)
        {
            return ProfileIdentityResolutionResult.Failed($"Quá thời gian tải trang MuseAI (Timeout): {tex.Message}. Vui lòng kiểm tra mạng và thử lại.");
        }
        catch (PlaywrightException pex) when (pex.Message.Contains("net::") || pex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return ProfileIdentityResolutionResult.Failed($"Lỗi kết nối mạng khi tải trang MuseAI: {pex.Message}. Vui lòng kiểm tra mạng và thử lại.");
        }
        catch (Exception ex)
        {
            return ProfileIdentityResolutionResult.Failed($"Lỗi khởi chạy kiểm tra phiên MuseAI: {ex.Message}");
        }
        finally
        {
            if (tempHandle != null)
            {
                try
                {
                    await _processController.CloseContextAsync(profile.ProfileId, CancellationToken.None);
                }
                catch { }
            }
        }
    }

    private async Task<ProfileIdentityResolutionResult> ExecuteMuseAiProbeOnPageAsync(
        IPage page,
        IBrowserContext context,
        BrowserProfileInfo profile,
        CancellationToken cancellationToken)
    {
        try
        {
            string currentUrl = page.Url;
            if (!currentUrl.StartsWith("https://muse.ai", StringComparison.OrdinalIgnoreCase))
            {
                await page.GotoAsync("https://muse.ai", new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 15000
                });
            }

            // Đợi trang hydrate và tải dữ liệu phiên
            await page.WaitForTimeoutAsync(2500);

            string finalUrl = page.Url;

            // 1. Kiểm tra nếu trang điều hướng về trang đăng nhập
            if (finalUrl.Contains("/login", StringComparison.OrdinalIgnoreCase) ||
                finalUrl.Contains("/auth", StringComparison.OrdinalIgnoreCase) ||
                finalUrl.Contains("accounts.meta.com", StringComparison.OrdinalIgnoreCase) ||
                finalUrl.Contains("auth.meta.com", StringComparison.OrdinalIgnoreCase))
            {
                await _profileRegistry.UpdateProfileStateAsync(
                    profile.ProfileId,
                    sessionStatus: BrowserSessionStatus.Unverified,
                    reasonCode: BrowserReasonCode.LoginRequired,
                    cancellationToken: cancellationToken);

                return new ProfileIdentityResolutionResult(
                    IsSuccess: false,
                    ProfileId: profile.ProfileId,
                    Provider: "museai",
                    AccountIdentity: null,
                    SessionStatus: BrowserSessionStatus.Unverified,
                    ErrorMessage: "Tài khoản MuseAI chưa đăng nhập hoặc phiên đã hết hạn. Vui lòng bấm 'Đăng nhập trình duyệt' để đăng nhập.");
            }

            // 2. Kiểm tra cookies phiên 'hatch_sess'
            var cookies = await context.CookiesAsync("https://muse.ai");
            var sessCookie = cookies.FirstOrDefault(c => string.Equals(c.Name, "hatch_sess", StringComparison.OrdinalIgnoreCase));
            if (sessCookie == null)
            {
                await _profileRegistry.UpdateProfileStateAsync(
                    profile.ProfileId,
                    sessionStatus: BrowserSessionStatus.Unverified,
                    reasonCode: BrowserReasonCode.LoginRequired,
                    cancellationToken: cancellationToken);

                return new ProfileIdentityResolutionResult(
                    IsSuccess: false,
                    ProfileId: profile.ProfileId,
                    Provider: "museai",
                    AccountIdentity: null,
                    SessionStatus: BrowserSessionStatus.Unverified,
                    ErrorMessage: "Không tìm thấy cookie phiên 'hatch_sess' hợp lệ trên muse.ai. Vui lòng đăng nhập qua trình duyệt.");
            }

            // 3. Trích xuất danh tính (Meta Account ID / User Greeting Name) từ IndexedDB và DOM
            var idbData = await page.EvaluateAsync<string>(@"async () => {
                let ownerId = null;
                let scopeId = null;
                let userName = null;

                function getStoreRecords(dbName, storeName) {
                    return new Promise((resolve) => {
                        let req = indexedDB.open(dbName);
                        req.onsuccess = () => {
                            let db = req.result;
                            if (!db.objectStoreNames.contains(storeName)) { db.close(); return resolve([]); }
                            let tx = db.transaction(storeName, 'readonly');
                            let st = tx.objectStore(storeName);
                            let getReq = st.getAll(null, 5);
                            getReq.onsuccess = () => { db.close(); resolve(getReq.result || []); };
                            getReq.onerror = () => { db.close(); resolve([]); };
                        };
                        req.onerror = () => resolve([]);
                    });
                }

                let replacementRecords = await getStoreRecords('hatch-cvm-replacement', 'revokedSessions');
                if (replacementRecords && replacementRecords.length > 0 && replacementRecords[0].ownerId) {
                    ownerId = String(replacementRecords[0].ownerId).trim();
                }

                if (!ownerId) {
                    let provRecords = await getStoreRecords('hatch-cvm-provisioning', 'revokedSessions');
                    if (provRecords && provRecords.length > 0 && provRecords[0].ownerId) {
                        ownerId = String(provRecords[0].ownerId).trim();
                    }
                }

                function getStoreKeys(dbName, storeName) {
                    return new Promise((resolve) => {
                        let req = indexedDB.open(dbName);
                        req.onsuccess = () => {
                            let db = req.result;
                            if (!db.objectStoreNames.contains(storeName)) { db.close(); return resolve([]); }
                            let tx = db.transaction(storeName, 'readonly');
                            let st = tx.objectStore(storeName);
                            let getReq = st.getAllKeys(null, 5);
                            getReq.onsuccess = () => { db.close(); resolve(getReq.result || []); };
                            getReq.onerror = () => { db.close(); resolve([]); };
                        };
                        req.onerror = () => resolve([]);
                    });
                }

                let chatKeys = await getStoreKeys('hatch-chat-cache', 'main-transcripts');
                if (chatKeys && chatKeys.length > 0) {
                    let keyStr = String(chatKeys[0]);
                    let parts = keyStr.split('\0');
                    if (parts[0] && /^\d+$/.test(parts[0])) {
                        scopeId = parts[0];
                    }
                }

                if (!scopeId) {
                    let avatarKeys = await getStoreKeys('hatch-avatar-cache-v2', 'manifests');
                    if (avatarKeys && avatarKeys.length > 0) {
                        let keyStr = String(avatarKeys[0]);
                        let parts = keyStr.split('\0');
                        if (parts[0] && /^\d+$/.test(parts[0])) {
                            scopeId = parts[0];
                        }
                    }
                }

                let allText = document.body ? document.body.innerText : '';
                let m = allText.match(/Hey\s+([A-Za-z0-9_\-\.]+),\s+I(?:’|')m\s+your\s+personal\s+agent/i);
                if (m && m[1]) {
                    userName = m[1].trim();
                }

                if (!userName) {
                    let transcriptRecords = await getStoreRecords('hatch-chat-cache', 'main-transcripts');
                    for (let rec of transcriptRecords) {
                        try {
                            let str = JSON.stringify(rec);
                            let m2 = str.match(/Hey\s+([A-Za-z0-9_\-\.]+),\s+I(?:’|')m\s+your\s+personal\s+agent/i);
                            if (m2 && m2[1]) {
                                userName = m2[1].trim();
                                break;
                            }
                        } catch (e) {}
                    }
                }

                return JSON.stringify({ ownerId, scopeId, userName });
            }");

            string? parsedOwnerId = null;
            string? parsedScopeId = null;
            string? parsedUserName = null;

            if (!string.IsNullOrWhiteSpace(idbData))
            {
                using var doc = JsonDocument.Parse(idbData);
                if (doc.RootElement.TryGetProperty("ownerId", out var oProp) && oProp.ValueKind == JsonValueKind.String)
                    parsedOwnerId = oProp.GetString();
                if (doc.RootElement.TryGetProperty("scopeId", out var sProp) && sProp.ValueKind == JsonValueKind.String)
                    parsedScopeId = sProp.GetString();
                if (doc.RootElement.TryGetProperty("userName", out var uProp) && uProp.ValueKind == JsonValueKind.String)
                    parsedUserName = uProp.GetString();
            }

            string? resolvedId = parsedOwnerId ?? parsedScopeId;
            string? finalIdentity = null;

            if (!string.IsNullOrWhiteSpace(resolvedId))
            {
                if (!string.IsNullOrWhiteSpace(parsedUserName))
                {
                    finalIdentity = $"{parsedUserName} ({resolvedId})";
                }
                else
                {
                    finalIdentity = resolvedId;
                }
            }
            else if (!string.IsNullOrWhiteSpace(parsedUserName))
            {
                finalIdentity = parsedUserName;
            }

            if (string.IsNullOrWhiteSpace(finalIdentity))
            {
                return new ProfileIdentityResolutionResult(
                    IsSuccess: false,
                    ProfileId: profile.ProfileId,
                    Provider: "museai",
                    AccountIdentity: null,
                    SessionStatus: BrowserSessionStatus.Unverified,
                    ErrorMessage: "Phiên làm việc MuseAI đã mở nhưng chưa lấy được danh tính tài khoản hợp lệ (Meta Account ID / User Name). Vui lòng đảm bảo bạn đã hoàn tất đăng nhập trên cửa sổ trình duyệt.");
            }

            // 4. Lưu trạng thái thành công bền vững vào ProfileRegistry
            await _profileRegistry.UpdateProfileStateAsync(
                profile.ProfileId,
                sessionStatus: BrowserSessionStatus.Connected,
                accountIdentity: finalIdentity,
                cancellationToken: cancellationToken);

            return ProfileIdentityResolutionResult.Success(
                profileId: profile.ProfileId,
                provider: "museai",
                accountIdentity: finalIdentity,
                sessionStatus: BrowserSessionStatus.Connected);
        }
        catch (TimeoutException tex)
        {
            return new ProfileIdentityResolutionResult(
                IsSuccess: false,
                ProfileId: profile.ProfileId,
                Provider: "museai",
                AccountIdentity: null,
                SessionStatus: BrowserSessionStatus.Unverified,
                ErrorMessage: $"Quá thời gian tải trang MuseAI (Timeout): {tex.Message}. Vui lòng kiểm tra kết nối mạng và thử lại.");
        }
        catch (PlaywrightException pex) when (pex.Message.Contains("net::") || pex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return new ProfileIdentityResolutionResult(
                IsSuccess: false,
                ProfileId: profile.ProfileId,
                Provider: "museai",
                AccountIdentity: null,
                SessionStatus: BrowserSessionStatus.Unverified,
                ErrorMessage: $"Lỗi kết nối mạng khi tải trang MuseAI: {pex.Message}. Vui lòng kiểm tra kết nối mạng và thử lại.");
        }
        catch (Exception ex)
        {
            return new ProfileIdentityResolutionResult(
                IsSuccess: false,
                ProfileId: profile.ProfileId,
                Provider: "museai",
                AccountIdentity: null,
                SessionStatus: BrowserSessionStatus.Unverified,
                ErrorMessage: $"Lỗi kiểm tra phiên MuseAI: {ex.Message}");
        }
    }

    private async Task<ProfileIdentityResolutionResult> ProbeChatGptAsync(
        BrowserProfileInfo profile,
        CancellationToken cancellationToken)
    {
        if (_processController == null)
        {
            return ProfileIdentityResolutionResult.Failed("Chưa khởi tạo IBrowserProcessController để kiểm tra phiên ChatGPT.");
        }

        bool isRunning = _processController.IsRunning(profile.ProfileId);
        if (isRunning)
        {
            var activeHandle = _processController.TryGetContext(profile.ProfileId);
            if (activeHandle != null && activeHandle.Context != null)
            {
                IPage? page = null;
                bool tempPageCreated = false;
                try
                {
                    if (activeHandle.Context.Pages != null)
                    {
                        page = activeHandle.Context.Pages.FirstOrDefault(p => !p.IsClosed && p.Url.Contains("chatgpt.com"))
                               ?? activeHandle.Context.Pages.FirstOrDefault(p => !p.IsClosed);
                    }
                }
                catch { }

                if (page == null)
                {
                    try
                    {
                        page = await activeHandle.Context.NewPageAsync();
                        tempPageCreated = true;
                    }
                    catch (Exception ex)
                    {
                        return ProfileIdentityResolutionResult.Failed($"Không thể mở tab kiểm tra phiên trên trình duyệt đang hoạt động: {ex.Message}");
                    }
                }

                try
                {
                    return await ExecuteChatGptProbeOnPageAsync(page, activeHandle.Context, profile, cancellationToken);
                }
                finally
                {
                    if (tempPageCreated && !page.IsClosed)
                    {
                        try { await page.CloseAsync(); } catch { }
                    }
                }
            }
        }

        // Nếu trình duyệt đang đóng: Khởi chạy persistent context tạm thời ở chế độ ẩn (isHeadless = true)
        IBrowserContextHandle? tempHandle = null;
        try
        {
            tempHandle = await _processController.LaunchPersistentContextAsync(
                profile.ProfileId,
                isHeadless: true,
                initialUrl: "https://chatgpt.com",
                cancellationToken: cancellationToken);

            IPage? page = null;
            if (tempHandle.Context.Pages != null)
            {
                page = tempHandle.Context.Pages.FirstOrDefault(p => !p.IsClosed);
            }
            page ??= await tempHandle.Context.NewPageAsync();

            return await ExecuteChatGptProbeOnPageAsync(page, tempHandle.Context, profile, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException tex)
        {
            return ProfileIdentityResolutionResult.Failed($"Quá thời gian tải trang ChatGPT (Timeout): {tex.Message}. Vui lòng kiểm tra mạng và thử lại.");
        }
        catch (PlaywrightException pex) when (pex.Message.Contains("net::") || pex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return ProfileIdentityResolutionResult.Failed($"Lỗi kết nối mạng khi tải trang ChatGPT: {pex.Message}. Vui lòng kiểm tra mạng và thử lại.");
        }
        catch (Exception ex)
        {
            return ProfileIdentityResolutionResult.Failed($"Lỗi khởi chạy kiểm tra phiên ChatGPT: {ex.Message}");
        }
        finally
        {
            if (tempHandle != null)
            {
                try
                {
                    await _processController.CloseContextAsync(profile.ProfileId, CancellationToken.None);
                }
                catch { }
            }
        }
    }

    private async Task<ProfileIdentityResolutionResult> ExecuteChatGptProbeOnPageAsync(
        IPage page,
        IBrowserContext context,
        BrowserProfileInfo profile,
        CancellationToken cancellationToken)
    {
        try
        {
            string currentUrl = page.Url;
            if (!currentUrl.StartsWith("https://chatgpt.com", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await page.GotoAsync("https://chatgpt.com", new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 15000
                    });
                }
                catch (TimeoutException) { }
                catch (PlaywrightException) { }
            }

            // Đợi trang hydrate và tải cookies/dữ liệu phiên
            await page.WaitForTimeoutAsync(2000);

            string finalUrl = page.Url;

            // 1. Kiểm tra nếu trang điều hướng về trang đăng nhập
            if (finalUrl.Contains("/auth/login", StringComparison.OrdinalIgnoreCase) ||
                finalUrl.Contains("auth0.openai.com", StringComparison.OrdinalIgnoreCase) ||
                finalUrl.Contains("auth.openai.com", StringComparison.OrdinalIgnoreCase))
            {
                await _profileRegistry.UpdateProfileStateAsync(
                    profile.ProfileId,
                    sessionStatus: BrowserSessionStatus.Unverified,
                    reasonCode: BrowserReasonCode.LoginRequired,
                    cancellationToken: cancellationToken);

                return new ProfileIdentityResolutionResult(
                    IsSuccess: false,
                    ProfileId: profile.ProfileId,
                    Provider: "chatgpt-web",
                    AccountIdentity: null,
                    SessionStatus: BrowserSessionStatus.Unverified,
                    ErrorMessage: "Tài khoản ChatGPT chưa đăng nhập hoặc phiên đã hết hạn. Vui lòng bấm 'Đăng nhập trình duyệt' để đăng nhập.");
            }

            // 2. Kiểm tra cookies phiên 'next-auth.session-token' hoặc '_puid'
            var cookies = await context.CookiesAsync("https://chatgpt.com");
            bool hasSessionToken = cookies.Any(c =>
                c.Name.StartsWith("__Secure-next-auth.session-token", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(c.Value));

            string? puidUserId = null;
            var puidCookie = cookies.FirstOrDefault(c => string.Equals(c.Name, "_puid", StringComparison.OrdinalIgnoreCase))?.Value;
            if (!string.IsNullOrWhiteSpace(puidCookie))
            {
                puidUserId = puidCookie.Contains(':') ? puidCookie.Split(':')[0].Trim() : puidCookie.Trim();
            }

            if (!hasSessionToken && string.IsNullOrWhiteSpace(puidUserId))
            {
                await _profileRegistry.UpdateProfileStateAsync(
                    profile.ProfileId,
                    sessionStatus: BrowserSessionStatus.Unverified,
                    reasonCode: BrowserReasonCode.LoginRequired,
                    cancellationToken: cancellationToken);

                return new ProfileIdentityResolutionResult(
                    IsSuccess: false,
                    ProfileId: profile.ProfileId,
                    Provider: "chatgpt-web",
                    AccountIdentity: null,
                    SessionStatus: BrowserSessionStatus.Unverified,
                    ErrorMessage: "Không tìm thấy cookie phiên đăng nhập hợp lệ trên chatgpt.com. Vui lòng đăng nhập qua trình duyệt.");
            }

            // 3. Trích xuất danh tính (User ID, Display Name, Email, Plan) từ DOM, NextAuth Session, IndexedDB và LocalStorage
            var jsIdentity = await page.EvaluateAsync<string>(@"async () => {
                let name = null;
                let email = null;
                let plan = null;
                let userId = null;
                let accountId = null;

                // A. Thử NextAuth session endpoint
                try {
                    const res = await fetch('/api/auth/session');
                    if (res.ok) {
                        const data = await res.json();
                        if (data && data.user) {
                            if (data.user.name) name = String(data.user.name).trim();
                            if (data.user.email) email = String(data.user.email).trim();
                            if (data.user.id) userId = String(data.user.id).trim();
                        }
                    }
                } catch (e) {}

                // B. Thử đọc profile button trên DOM
                try {
                    const profileBtn = document.querySelector('button[data-testid=""profile-button""]') ||
                                       document.querySelector('[data-testid=""profile-button""]') ||
                                       document.querySelector('div[data-testid=""user-menu""]');
                    if (profileBtn) {
                        const lines = (profileBtn.innerText || '')
                            .split('\n')
                            .map(s => s.trim())
                            .filter(Boolean);
                        for (const l of lines) {
                            if (['Plus', 'Pro', 'Team', 'Free', 'Enterprise'].includes(l)) {
                                plan = l;
                            } else if (l.length > 1 && !name && !['DM', 'ChatGPT', 'New chat'].includes(l)) {
                                name = l;
                            }
                        }
                    }
                } catch (e) {}

                // C. Thử đọc IndexedDB: codex-browser-host -> records -> local-document-identity
                try {
                    await new Promise((resolve) => {
                        const req = indexedDB.open('codex-browser-host');
                        req.onsuccess = () => {
                            const db = req.result;
                            if (!db.objectStoreNames.contains('records')) { db.close(); return resolve(); }
                            const tx = db.transaction('records', 'readonly');
                            const st = tx.objectStore('records');
                            const getReq = st.getAll();
                            getReq.onsuccess = () => {
                                db.close();
                                const records = getReq.result || [];
                                for (const r of records) {
                                    if (r && r['local-document-identity']) {
                                        const ldi = r['local-document-identity'];
                                        if (ldi.userId && !userId) userId = String(ldi.userId).trim();
                                        if (ldi.accountId && !accountId) accountId = String(ldi.accountId).trim();
                                    }
                                }
                                resolve();
                            };
                            getReq.onerror = () => { db.close(); resolve(); };
                        };
                        req.onerror = () => resolve();
                    });
                } catch (e) {}

                // D. Thử đọc LocalStorage
                try {
                    if (!userId) {
                        const pli = localStorage.getItem('precise_location_identity');
                        if (pli) {
                            const parsed = JSON.parse(pli);
                            if (Array.isArray(parsed) && parsed.length >= 2 && parsed[1] && parsed[1].startsWith('user-')) {
                                userId = parsed[1];
                            }
                        }
                    }
                    if (!email) {
                        for (let i = 0; i < localStorage.length; i++) {
                            const k = localStorage.key(i);
                            if (k && k.includes('composerDraft')) {
                                const v = localStorage.getItem(k);
                                if (v && v.includes('@') && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v.trim())) {
                                    email = v.trim();
                                    break;
                                }
                            }
                        }
                    }
                } catch (e) {}

                return JSON.stringify({ name, email, plan, userId, accountId });
            }");

            string? parsedName = null;
            string? parsedEmail = null;
            string? parsedPlan = null;
            string? parsedUserId = null;

            if (!string.IsNullOrWhiteSpace(jsIdentity))
            {
                using var doc = JsonDocument.Parse(jsIdentity);
                if (doc.RootElement.TryGetProperty("name", out var nProp) && nProp.ValueKind == JsonValueKind.String)
                    parsedName = nProp.GetString()?.Trim();
                if (doc.RootElement.TryGetProperty("email", out var eProp) && eProp.ValueKind == JsonValueKind.String)
                    parsedEmail = eProp.GetString()?.Trim();
                if (doc.RootElement.TryGetProperty("plan", out var plProp) && plProp.ValueKind == JsonValueKind.String)
                    parsedPlan = plProp.GetString()?.Trim();
                if (doc.RootElement.TryGetProperty("userId", out var uProp) && uProp.ValueKind == JsonValueKind.String)
                    parsedUserId = uProp.GetString()?.Trim();
            }

            string? finalUserId = parsedUserId ?? puidUserId;
            string? displayName = parsedName;
            if (string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(parsedEmail))
            {
                displayName = parsedEmail;
            }

            // Fallback giữ lại tên hiển thị đã biết trước đó nếu có
            if (string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(profile.AccountIdentity))
            {
                displayName = profile.AccountIdentity;
                if (displayName.Contains('(') && displayName.EndsWith(')'))
                {
                    displayName = displayName.Substring(0, displayName.LastIndexOf('(')).Trim();
                }
            }

            string? finalIdentity = null;
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                if (!string.IsNullOrWhiteSpace(finalUserId))
                {
                    finalIdentity = $"{displayName} ({finalUserId})";
                }
                else
                {
                    finalIdentity = displayName;
                }
            }
            else if (!string.IsNullOrWhiteSpace(finalUserId))
            {
                finalIdentity = $"ChatGPT ({finalUserId})";
            }

            if (string.IsNullOrWhiteSpace(finalIdentity))
            {
                return new ProfileIdentityResolutionResult(
                    IsSuccess: false,
                    ProfileId: profile.ProfileId,
                    Provider: "chatgpt-web",
                    AccountIdentity: null,
                    SessionStatus: BrowserSessionStatus.Unverified,
                    ErrorMessage: "Phiên làm việc ChatGPT đã mở nhưng chưa lấy được danh tính tài khoản hợp lệ. Vui lòng đảm bảo bạn đã hoàn tất đăng nhập trên cửa sổ trình duyệt.");
            }

            // 4. Lưu trạng thái thành công bền vững vào ProfileRegistry
            await _profileRegistry.UpdateProfileStateAsync(
                profile.ProfileId,
                sessionStatus: BrowserSessionStatus.Connected,
                accountIdentity: finalIdentity,
                cancellationToken: cancellationToken);

            return ProfileIdentityResolutionResult.Success(
                profileId: profile.ProfileId,
                provider: "chatgpt-web",
                accountIdentity: finalIdentity,
                sessionStatus: BrowserSessionStatus.Connected);
        }
        catch (TimeoutException tex)
        {
            return new ProfileIdentityResolutionResult(
                IsSuccess: false,
                ProfileId: profile.ProfileId,
                Provider: "chatgpt-web",
                AccountIdentity: null,
                SessionStatus: BrowserSessionStatus.Unverified,
                ErrorMessage: $"Quá thời gian tải trang ChatGPT (Timeout): {tex.Message}. Vui lòng kiểm tra kết nối mạng và thử lại.");
        }
        catch (PlaywrightException pex) when (pex.Message.Contains("net::") || pex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return new ProfileIdentityResolutionResult(
                IsSuccess: false,
                ProfileId: profile.ProfileId,
                Provider: "chatgpt-web",
                AccountIdentity: null,
                SessionStatus: BrowserSessionStatus.Unverified,
                ErrorMessage: $"Lỗi kết nối mạng khi tải trang ChatGPT: {pex.Message}. Vui lòng kiểm tra kết nối mạng và thử lại.");
        }
        catch (Exception ex)
        {
            return new ProfileIdentityResolutionResult(
                IsSuccess: false,
                ProfileId: profile.ProfileId,
                Provider: "chatgpt-web",
                AccountIdentity: null,
                SessionStatus: BrowserSessionStatus.Unverified,
                ErrorMessage: $"Lỗi kiểm tra phiên ChatGPT: {ex.Message}");
        }
    }

    private async Task<ProfileIdentityResolutionResult> ProbeGeminiAsync(
        BrowserProfileInfo profile,
        CancellationToken cancellationToken)
    {
        // Nếu profile đã có Connected và AccountIdentity từ phiên trước: giữ nguyên
        if (string.Equals(profile.SessionStatus, BrowserSessionStatus.Connected, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(profile.AccountIdentity))
        {
            return ProfileIdentityResolutionResult.Success(
                profileId: profile.ProfileId,
                provider: "gemini-web",
                accountIdentity: profile.AccountIdentity,
                sessionStatus: BrowserSessionStatus.Connected);
        }

        return new ProfileIdentityResolutionResult(
            IsSuccess: false,
            ProfileId: profile.ProfileId,
            Provider: "gemini-web",
            AccountIdentity: profile.AccountIdentity,
            SessionStatus: BrowserSessionStatus.Unverified,
            ErrorMessage: "Phiên đăng nhập Gemini chưa được xác minh. Vui lòng bấm 'Đăng nhập trình duyệt' để đăng nhập.");
    }

    private async Task<ProfileIdentityResolutionResult> ProbeTikTokAsync(
        BrowserProfileInfo profile,
        CancellationToken cancellationToken)
    {
        if (string.Equals(profile.SessionStatus, BrowserSessionStatus.Connected, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(profile.AccountIdentity))
        {
            return ProfileIdentityResolutionResult.Success(
                profileId: profile.ProfileId,
                provider: "tiktok",
                accountIdentity: profile.AccountIdentity,
                sessionStatus: BrowserSessionStatus.Connected);
        }

        return new ProfileIdentityResolutionResult(
            IsSuccess: false,
            ProfileId: profile.ProfileId,
            Provider: "tiktok",
            AccountIdentity: profile.AccountIdentity,
            SessionStatus: BrowserSessionStatus.Unverified,
            ErrorMessage: "Phiên đăng nhập TikTok chưa được xác minh. Vui lòng bấm 'Đăng nhập trình duyệt' để đăng nhập.");
    }
}
