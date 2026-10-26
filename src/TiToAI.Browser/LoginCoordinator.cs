using System.Collections.Concurrent;
using TiToAI.Browser.Abstractions;
using TiToAI.Browser.Models;
using TiToAI.Contracts.Browser;

namespace TiToAI.Browser;

/// <summary>
/// Điều phối quy tắc Global One-Visible-Login Rule bằng SemaphoreSlim(1, 1).
/// Đảm bảo không bao giờ có 2 cửa sổ đăng nhập hiển thị cùng lúc trên desktop Windows.
/// </summary>
public sealed class LoginCoordinator : ILoginCoordinator, IDisposable
{
    private readonly SemaphoreSlim _visibleSemaphore = new(1, 1);
    private readonly ConcurrentQueue<string> _waitingQueue = new();
    private readonly object _stateLock = new();
    private readonly IProfileRegistry? _profileRegistry;

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

    public LoginCoordinator(IProfileRegistry? profileRegistry = null)
    {
        _profileRegistry = profileRegistry;
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

            if (_profileRegistry != null)
            {
                await _profileRegistry.UpdateProfileStateAsync(
                    profileId,
                    visibilityState: BrowserVisibilityState.VisibleActive,
                    operationalState: BrowserOperationalState.Running,
                    cancellationToken: cancellationToken);
            }

            return new BrowserRequestLoginResponseData(
                Queued: false,
                VisibilityState: BrowserVisibilityState.VisibleActive,
                CurrentHolder: profileId);
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

        if (_profileRegistry != null)
        {
            await _profileRegistry.UpdateProfileStateAsync(
                profileId,
                visibilityState: BrowserVisibilityState.Hidden,
                operationalState: BrowserOperationalState.Stopped,
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
