using System.Collections.Concurrent;
using Microsoft.Playwright;
using TiToAI.Browser.Abstractions;
using TiToAI.Browser.Models;

namespace TiToAI.Browser;

/// <summary>
/// Quản lý vòng đời tiến trình Playwright Chromium persistent context trên userDataDir của profile.
/// Tích hợp chặt chẽ với IProfileOwner để chiếm và giải phóng lock .titoai_lock,
/// cấu hình chế độ headless/visible và xử lý lỗi driver Playwright có cấu trúc, không crash ngầm.
/// </summary>
public sealed class PlaywrightBrowserController : IBrowserProcessController
{
    private readonly IProfileRegistry _profileRegistry;
    private readonly IProfileOwner _profileOwner;
    private readonly Func<Task<IPlaywright>>? _playwrightFactory;
    private readonly Func<string, BrowserTypeLaunchPersistentContextOptions, Task<IBrowserContext>>? _customPersistentLauncher;

    private readonly ConcurrentDictionary<string, PlaywrightBrowserContextHandle> _activeContexts = new();
    private IPlaywright? _playwright;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _disposed;

    public PlaywrightBrowserController(
        IProfileRegistry profileRegistry,
        IProfileOwner profileOwner,
        Func<Task<IPlaywright>>? playwrightFactory = null,
        Func<string, BrowserTypeLaunchPersistentContextOptions, Task<IBrowserContext>>? customPersistentLauncher = null)
    {
        _profileRegistry = profileRegistry ?? throw new ArgumentNullException(nameof(profileRegistry));
        _profileOwner = profileOwner ?? throw new ArgumentNullException(nameof(profileOwner));
        _playwrightFactory = playwrightFactory;
        _customPersistentLauncher = customPersistentLauncher;
    }

    public bool IsRunning(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        return _activeContexts.ContainsKey(profileId);
    }

    public async Task<IBrowserContextHandle> LaunchPersistentContextAsync(
        string profileId,
        bool isHeadless,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        if (IsRunning(profileId))
        {
            throw new InvalidOperationException($"Hồ sơ trình duyệt '{profileId}' hiện đang có tiến trình Playwright hoạt động.");
        }

        var profile = await _profileRegistry.GetProfileAsync(profileId, cancellationToken);
        if (profile == null)
        {
            throw new InvalidOperationException($"Không tìm thấy hồ sơ trình duyệt '{profileId}' trong hệ thống.");
        }

        string userDataDir = _profileRegistry.GetUserDataDir(profileId, profile.Kind);
        Directory.CreateDirectory(userDataDir);

        // Chiếm Single-Instance Lock (.titoai_lock) trước khi khởi chạy Playwright persistent context
        var ownerLock = await _profileOwner.AcquireOwnerLockAsync(
            profileId,
            $"PlaywrightProcess-{Guid.NewGuid():N}",
            TimeSpan.FromSeconds(5),
            cancellationToken);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var launchOptions = new BrowserTypeLaunchPersistentContextOptions
            {
                Headless = isHeadless,
                Args = ["--disable-blink-features=AutomationControlled"]
            };

            IBrowserContext browserContext;

            if (_customPersistentLauncher != null)
            {
                browserContext = await _customPersistentLauncher(userDataDir, launchOptions);
            }
            else
            {
                await _initLock.WaitAsync(cancellationToken);
                try
                {
                    if (_playwright == null)
                    {
                        _playwright = _playwrightFactory != null
                            ? await _playwrightFactory()
                            : await Playwright.CreateAsync();
                    }
                }
                finally
                {
                    _initLock.Release();
                }

                browserContext = await _playwright.Chromium.LaunchPersistentContextAsync(
                    userDataDir,
                    launchOptions);
            }

            var handle = new PlaywrightBrowserContextHandle(
                this,
                profileId,
                browserContext,
                ownerLock,
                isHeadless);

            _activeContexts[profileId] = handle;

            await _profileRegistry.UpdateProfileStateAsync(
                profileId,
                operationalState: BrowserOperationalState.Running,
                visibilityState: isHeadless ? BrowserVisibilityState.Hidden : BrowserVisibilityState.VisibleActive,
                cancellationToken: cancellationToken);

            return handle;
        }
        catch (PlaywrightException ex)
        {
            // Bắt lỗi Playwright (ví dụ driver/chromium chưa được cài đặt)
            // Giải phóng lock .titoai_lock ngay lập tức để không rò rỉ tài nguyên
            await ownerLock.DisposeAsync();

            await _profileRegistry.UpdateProfileStateAsync(
                profileId,
                operationalState: BrowserOperationalState.Error,
                reasonCode: "playwright_driver_error",
                cancellationToken: cancellationToken);

            throw new InvalidOperationException(
                $"Khởi chạy Playwright Chromium thất bại cho profile '{profileId}'. " +
                $"Driver hoặc browser binary có thể chưa được cài đặt: {ex.Message}",
                ex);
        }
        catch (Exception)
        {
            // Đảm bảo luôn giải phóng lock nếu có bất kỳ ngoại lệ nào xảy ra khi khởi chạy
            await ownerLock.DisposeAsync();
            throw;
        }
    }

    public async Task CloseContextAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        if (_activeContexts.TryRemove(profileId, out var handle))
        {
            await handle.DisposeAsync();

            await _profileRegistry.UpdateProfileStateAsync(
                profileId,
                operationalState: BrowserOperationalState.Stopped,
                visibilityState: BrowserVisibilityState.Hidden,
                cancellationToken: cancellationToken);
        }
    }

    private void RemoveActiveContext(string profileId)
    {
        _activeContexts.TryRemove(profileId, out _);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var handle in _activeContexts.Values)
        {
            try
            {
                await handle.DisposeAsync();
            }
            catch
            {
                // Bỏ qua lỗi dọn dẹp khi dispose
            }
        }
        _activeContexts.Clear();

        if (_playwright != null)
        {
            try
            {
                _playwright.Dispose();
            }
            catch { }
            _playwright = null;
        }

        _initLock.Dispose();
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private sealed class PlaywrightBrowserContextHandle : IBrowserContextHandle
    {
        private readonly PlaywrightBrowserController _controller;
        private int _isDisposed;

        public string ProfileId { get; }
        public IBrowserContext Context { get; }
        public IProfileOwnerLock OwnerLock { get; }
        public bool IsHeadless { get; }

        public PlaywrightBrowserContextHandle(
            PlaywrightBrowserController controller,
            string profileId,
            IBrowserContext context,
            IProfileOwnerLock ownerLock,
            bool isHeadless)
        {
            _controller = controller;
            ProfileId = profileId;
            Context = context;
            OwnerLock = ownerLock;
            IsHeadless = isHeadless;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                _controller.RemoveActiveContext(ProfileId);

                try
                {
                    await Context.CloseAsync();
                }
                catch
                {
                    // Bỏ qua lỗi đóng ngữ cảnh nếu browser đã tắt
                }

                try
                {
                    await OwnerLock.DisposeAsync();
                }
                catch
                {
                    // Bỏ qua lỗi giải phóng file lock
                }
            }
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
