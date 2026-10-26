using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using TiToAI.Application.Jobs;
using TiToAI.Browser;
using TiToAI.Browser.Abstractions;
using TiToAI.Browser.Models;
using TiToAI.Contracts.Browser;

namespace TiToAI.Desktop.Hosting;

/// <summary>
/// Cổng kiểm soát (Gatekeeper) kết nối trực tiếp với Browser Runtime thực tế:
/// ProfileRegistry, LoginCoordinator và TabPool để điều phối quyền chiếm tab và ngăn chặn
/// dispatch khi profile đang Quiescing, VisibleWaiting hoặc VisibleActive.
/// </summary>
public sealed class HostBrowserGatekeeper : IBrowserGatekeeper, IDisposable
{
    private readonly ProfileRegistry _profileRegistry;
    private readonly LoginCoordinator _loginCoordinator;
    private readonly ITabPool _tabPool;

    private readonly ConcurrentDictionary<string, string> _profileVisibility = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _profileOperational = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<BrowserProfileStateChangedPayload, Task> _profileStateChangedHandler;

    public HostBrowserGatekeeper(
        ProfileRegistry profileRegistry,
        LoginCoordinator loginCoordinator,
        ITabPool tabPool)
    {
        _profileRegistry = profileRegistry ?? throw new ArgumentNullException(nameof(profileRegistry));
        _loginCoordinator = loginCoordinator ?? throw new ArgumentNullException(nameof(loginCoordinator));
        _tabPool = tabPool ?? throw new ArgumentNullException(nameof(tabPool));

        _profileStateChangedHandler = payload =>
        {
            if (!string.IsNullOrWhiteSpace(payload.ProfileId))
            {
                if (!string.IsNullOrWhiteSpace(payload.VisibilityState))
                {
                    _profileVisibility[payload.ProfileId] = payload.VisibilityState;
                }
                if (!string.IsNullOrWhiteSpace(payload.OperationalState))
                {
                    _profileOperational[payload.ProfileId] = payload.OperationalState;
                }
            }
            return Task.CompletedTask;
        };

        _profileRegistry.ProfileStateChanged += _profileStateChangedHandler;
    }

    public bool IsDispatchPaused(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId)) return false;

        // Nếu profile đang trong quy trình Quiescing: Tạm dừng dispatch để rút cạn tab
        if (_profileOperational.TryGetValue(profileId, out var opState) &&
            string.Equals(opState, BrowserOperationalState.Quiescing, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public bool IsProfileBusyWithLogin(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId)) return false;

        // 1. Kiểm tra Semaphore/CurrentVisible của LoginCoordinator
        if (string.Equals(_loginCoordinator.CurrentVisibleProfileId, profileId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 2. Kiểm tra visibility state (VisibleActive hoặc VisibleWaiting)
        if (_profileVisibility.TryGetValue(profileId, out var visState))
        {
            if (string.Equals(visState, BrowserVisibilityState.VisibleActive, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(visState, BrowserVisibilityState.VisibleWaiting, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsRoleActive(string profileId, string role)
    {
        return _tabPool.IsRoleActive(profileId, role);
    }

    public async Task<IDisposable?> TryAcquireTabLeaseAsync(
        string profileId,
        string role,
        string jobId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var handle = await _tabPool.AcquireTabAsync(profileId, role, jobId, cancellationToken).ConfigureAwait(false);
            return new TabLeaseDisposable(async () =>
            {
                await handle.DisposeAsync().ConfigureAwait(false);
            });
        }
        catch (InvalidOperationException)
        {
            // Role tab đang bận xử lý job khác trong TabPool
            return null;
        }
    }

    public void ReleaseTabLease(string profileId, string role, string jobId)
    {
        _ = _tabPool.ReleaseTabAsync(profileId, role, jobId, CancellationToken.None);
    }

    public bool IsProfileIdle(string profileId)
    {
        return _tabPool.GetActiveJobCount(profileId) == 0;
    }

    public void CloseProfileIfIdle(string profileId)
    {
        // Khi idle: việc đóng browser context sẽ do PlaywrightBrowserController quản lý khi cần
    }

    public void Dispose()
    {
        _profileRegistry.ProfileStateChanged -= _profileStateChangedHandler;
    }

    private sealed class TabLeaseDisposable : IDisposable
    {
        private Func<Task>? _releaseAction;
        public TabLeaseDisposable(Func<Task> releaseAction) => _releaseAction = releaseAction;

        public void Dispose()
        {
            var action = Interlocked.Exchange(ref _releaseAction, null);
            if (action != null)
            {
                _ = action();
            }
        }
    }
}
