using TiToAI.Browser.Abstractions;
using TiToAI.Browser.Models;
using TiToAI.Contracts.Accounts;
using TiToAI.Contracts.Browser;

namespace TiToAI.Browser;

/// <summary>
/// Thẩm định trạng thái session cookies và giải quyết danh tính tài khoản theo hợp đồng IProfileIdentityResolver.
/// Khi chưa có adapter Playwright thật chạy trên trang đích, trả về Unverified và IsSuccess = false (không dùng fake để tuyên bố PASS).
/// </summary>
public sealed class SessionProbe : ISessionProbe, IProfileIdentityResolver
{
    private readonly IProfileRegistry _profileRegistry;
    private readonly Func<string, string, CancellationToken, Task<ProfileIdentityResolutionResult?>>? _externalAdapter;
    private readonly Func<string, CancellationToken, Task<BrowserCheckSessionResponseData?>>? _sessionCheckAdapter;

    public SessionProbe(
        IProfileRegistry profileRegistry,
        Func<string, string, CancellationToken, Task<ProfileIdentityResolutionResult?>>? externalAdapter = null,
        Func<string, CancellationToken, Task<BrowserCheckSessionResponseData?>>? sessionCheckAdapter = null)
    {
        _profileRegistry = profileRegistry ?? throw new ArgumentNullException(nameof(profileRegistry));
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

        // Nếu có adapter kiểm tra session thực tế được truyền vào
        if (_externalAdapter != null)
        {
            var result = await _externalAdapter(profileId, provider, cancellationToken);
            if (result != null) return result;
        }

        // Nếu profile đã có trạng thái Connected và danh tính tài khoản hợp lệ (từ session probe trước)
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
}
