using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using TiToAI.Browser.Abstractions;
using TiToAI.Browser.Models;
using TiToAI.Contracts.Browser;

namespace TiToAI.Browser;

/// <summary>
/// Điều phối quy tắc Global One-Visible-Login Rule bằng SemaphoreSlim(1, 1).
/// Đảm bảo không bao giờ có 2 cửa sổ đăng nhập hiển thị cùng lúc trên desktop Windows.
/// Tích hợp với IBrowserProcessController để mở cửa sổ Playwright thực tế, giữ Single-Instance Lock,
/// probe identity/session khi hoàn tất và đưa về trạng thái Hidden an toàn.
/// </summary>
public sealed class LoginCoordinator : ILoginCoordinator, IDisposable
{
    private readonly SemaphoreSlim _visibleSemaphore = new(1, 1);
    private readonly ConcurrentQueue<string> _waitingQueue = new();
    private readonly object _stateLock = new();
    private readonly IProfileRegistry? _profileRegistry;
    private readonly IBrowserProcessController? _processController;
    private readonly ISessionProbe? _sessionProbe;

    private string? _currentVisibleProfileId;
    private bool _disposed;

    public string? CurrentVisibleProfileId
    {
        get
        {
            lock (_stateLock)
            {
                return _currentVisibleProfileId;
            }
        }
    }

    public bool IsVisibleLoginActive => CurrentVisibleProfileId != null;

    public LoginCoordinator(
        IProfileRegistry? profileRegistry = null,
        IBrowserProcessController? processController = null,
        ISessionProbe? sessionProbe = null)
    {
        _profileRegistry = profileRegistry;
        _processController = processController;
        _sessionProbe = sessionProbe ?? (profileRegistry != null ? new SessionProbe(profileRegistry) : null);
    }

    public async Task<BrowserRequestLoginResponseData> RequestLoginAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        lock (_stateLock)
        {
            if (string.Equals(_currentVisibleProfileId, profileId, StringComparison.OrdinalIgnoreCase))
            {
                return new BrowserRequestLoginResponseData(
                    Queued: false,
                    VisibilityState: BrowserVisibilityState.VisibleActive,
                    CurrentHolder: profileId);
            }
        }

        // Cố gắng chiếm slot hiển thị ngay lập tức (không chờ)
        bool acquired = await _visibleSemaphore.WaitAsync(TimeSpan.Zero, cancellationToken);
        if (acquired)
        {
            lock (_stateLock)
            {
                _currentVisibleProfileId = profileId;
            }

            try
            {
                if (_profileRegistry != null)
                {
                    await _profileRegistry.UpdateProfileStateAsync(
                        profileId,
                        visibilityState: BrowserVisibilityState.VisibleActive,
                        operationalState: BrowserOperationalState.Running,
                        cancellationToken: cancellationToken);
                }

                // Mở cửa sổ trình duyệt hiển thị (isHeadless = false) qua browser controller
                if (_processController != null && !_processController.IsRunning(profileId))
                {
                    await _processController.LaunchPersistentContextAsync(
                        profileId,
                        isHeadless: false,
                        cancellationToken: cancellationToken);
                }

                return new BrowserRequestLoginResponseData(
                    Queued: false,
                    VisibilityState: BrowserVisibilityState.VisibleActive,
                    CurrentHolder: profileId);
            }
            catch (Exception)
            {
                // Nếu khởi chạy thất bại, giải phóng slot và rethrow
                ReleaseVisibleSlot(profileId);
                if (_profileRegistry != null)
                {
                    await _profileRegistry.UpdateProfileStateAsync(
                        profileId,
                        visibilityState: BrowserVisibilityState.Hidden,
                        operationalState: BrowserOperationalState.Stopped,
                        reasonCode: "launch_failed",
                        cancellationToken: cancellationToken);
                }
                throw;
            }
        }

        // Nếu slot đang bận, xếp hàng chờ với trạng thái VisibleWaiting
        string currentHolder;
        lock (_stateLock)
        {
            currentHolder = _currentVisibleProfileId ?? "unknown";
            _waitingQueue.Enqueue(profileId);
        }

        if (_profileRegistry != null)
        {
            await _profileRegistry.UpdateProfileStateAsync(
                profileId,
                visibilityState: BrowserVisibilityState.VisibleWaiting,
                cancellationToken: cancellationToken);
        }

        return new BrowserRequestLoginResponseData(
            Queued: true,
            VisibilityState: BrowserVisibilityState.VisibleWaiting,
            CurrentHolder: currentHolder);
    }

    public async Task<BrowserCancelLoginResponseData> CancelLoginAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        bool isCurrentHolder = false;
        lock (_stateLock)
        {
            if (string.Equals(_currentVisibleProfileId, profileId, StringComparison.OrdinalIgnoreCase))
            {
                isCurrentHolder = true;
            }
        }

        if (isCurrentHolder)
        {
            await CloseLoginWindowAsync(profileId, BrowserReasonCode.AuthClosed, cancellationToken);
            return new BrowserCancelLoginResponseData(Canceled: true);
        }

        // Nếu đang trong hàng đợi VisibleWaiting
        if (_profileRegistry != null)
        {
            await _profileRegistry.UpdateProfileStateAsync(
                profileId,
                visibilityState: BrowserVisibilityState.Hidden,
                operationalState: BrowserOperationalState.Stopped,
                reasonCode: BrowserReasonCode.AuthClosed,
                cancellationToken: cancellationToken);
        }

        return new BrowserCancelLoginResponseData(Canceled: true);
    }

    public async Task CloseLoginWindowAsync(
        string profileId,
        string reasonCode,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        // Đóng persistent browser context qua controller nếu đang chạy
        if (_processController != null && _processController.IsRunning(profileId))
        {
            try
            {
                await _processController.CloseContextAsync(profileId, cancellationToken);
            }
            catch
            {
                // Tiếp tục giải phóng slot dù đóng context lỗi
            }
        }

        bool needRelease = false;
        lock (_stateLock)
        {
            if (string.Equals(_currentVisibleProfileId, profileId, StringComparison.OrdinalIgnoreCase))
            {
                _currentVisibleProfileId = null;
                needRelease = true;
            }
        }

        if (needRelease)
        {
            try
            {
                _visibleSemaphore.Release();
            }
            catch (SemaphoreFullException)
            {
                // Bỏ qua nếu semaphore đã đầy
            }
        }

        // Probe identity / session status và đưa về Hidden an toàn
        string newSessionStatus = BrowserSessionStatus.Unverified;
        string newOperationalState = BrowserOperationalState.Stopped;

        if (_sessionProbe != null)
        {
            try
            {
                var probeResult = await _sessionProbe.CheckSessionAsync(profileId, cancellationToken);
                bool isConnected = string.Equals(probeResult.SessionStatus, BrowserSessionStatus.Connected, StringComparison.OrdinalIgnoreCase);
                bool identityOk = probeResult.IdentityMatches != false;

                if (isConnected && identityOk)
                {
                    newSessionStatus = BrowserSessionStatus.Connected;
                    newOperationalState = BrowserOperationalState.Idle;
                }
            }
            catch
            {
                // Giữ unverified
            }
        }
        else if (string.Equals(reasonCode, "auth_ok", StringComparison.OrdinalIgnoreCase) && _profileRegistry != null)
        {
            var currentProfile = await _profileRegistry.GetProfileAsync(profileId, cancellationToken);
            if (currentProfile != null && string.Equals(currentProfile.SessionStatus, BrowserSessionStatus.Connected, StringComparison.OrdinalIgnoreCase))
            {
                newSessionStatus = BrowserSessionStatus.Connected;
                newOperationalState = BrowserOperationalState.Idle;
            }
        }

        if (_profileRegistry != null)
        {
            await _profileRegistry.UpdateProfileStateAsync(
                profileId,
                visibilityState: BrowserVisibilityState.Hidden,
                operationalState: newOperationalState,
                sessionStatus: newSessionStatus,
                reasonCode: string.IsNullOrWhiteSpace(reasonCode) ? BrowserReasonCode.AuthClosed : reasonCode,
                cancellationToken: cancellationToken);
        }
    }

    public async Task<bool> WaitForVisibleSlotAsync(
        string profileId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        bool acquired = await _visibleSemaphore.WaitAsync(timeout, cancellationToken);
        if (acquired)
        {
            lock (_stateLock)
            {
                _currentVisibleProfileId = profileId;
            }

            if (_profileRegistry != null)
            {
                await _profileRegistry.UpdateProfileStateAsync(
                    profileId,
                    visibilityState: BrowserVisibilityState.VisibleActive,
                    operationalState: BrowserOperationalState.Running,
                    cancellationToken: cancellationToken);
            }

            return true;
        }

        return false;
    }

    public void ReleaseVisibleSlot(string profileId)
    {
        bool needRelease = false;
        lock (_stateLock)
        {
            if (string.Equals(_currentVisibleProfileId, profileId, StringComparison.OrdinalIgnoreCase))
            {
                _currentVisibleProfileId = null;
                needRelease = true;
            }
        }

        if (needRelease)
        {
            try
            {
                _visibleSemaphore.Release();
            }
            catch (SemaphoreFullException) { }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _visibleSemaphore.Dispose();
    }
}
