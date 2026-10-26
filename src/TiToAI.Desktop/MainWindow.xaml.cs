using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using TiToAI.Application.Accounts;
using TiToAI.Application.Assets;
using TiToAI.Application.Documents;
using TiToAI.Application.Projects;
using TiToAI.Application.Scenes;
using TiToAI.Application.Settings;
using TiToAI.Application.Workspaces;
using TiToAI.Browser;
using TiToAI.Browser.Abstractions;
using TiToAI.Browser.Models;
using TiToAI.Contracts.Browser;
using TiToAI.Desktop.Bridge;
using TiToAI.Desktop.Bridge.Handlers;
using TiToAI.Desktop.Hosting;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Settings;
using TiToAI.Infrastructure.Storage;

namespace TiToAI.Desktop;

public partial class MainWindow : Window
{
    private readonly SemaphoreSlim _startupLock = new(1, 1);
    private CancellationTokenSource? _startupCts;
    private WorkspaceHandlers? _workspaceHandlers;
    private AccountHandlers? _accountHandlers;
    private PromptHandlers? _promptHandlers;
    private ProjectHandlers? _projectHandlers;
    private SceneHandlers? _sceneHandlers;
    private BrowserHandlers? _browserHandlers;
    private SettingsHandlers? _settingsHandlers;
    private AssetHandlers? _assetHandlers;
    private DocumentHandlers? _documentHandlers;
    private LoginCoordinator? _loginCoordinator;
    private ProfileRegistry? _profileRegistry;
    private Func<BrowserProfileStateChangedPayload, Task>? _profileStateChangedHandler;
    private bool _isClosed;

    public DesktopRuntimePaths RuntimePaths { get; }

    public IHostThemeService ThemeService { get; }

    public IBridgeDispatcher DispatcherInstance { get; }

    public WebView2HostManager HostManager { get; }

    public BridgeMessageRouter? BridgeRouter => HostManager.BridgeRouter;

    public MainWindow()
        : this(App.CurrentRuntimePaths ?? DesktopRuntimePaths.FromCommandLineArgs())
    {
    }

    public MainWindow(DesktopRuntimePaths runtimePaths)
    {
        RuntimePaths = runtimePaths ?? throw new ArgumentNullException(nameof(runtimePaths));

        InitializeComponent();
        Opacity = 0;

        DispatcherInstance = new BridgeDispatcher();
        ThemeService = new HostThemeService(new ThemePreferenceStore(RuntimePaths.DatabasePath));
        HostManager = new WebView2HostManager(ThemeService, DispatcherInstance, RuntimePaths.WebView2ProfileDirectory);

        // Áp dụng theme tức thời để đồng bộ giao diện cửa sổ và overlay
        ApplyEffectiveTheme(ThemeService.CurrentEffectiveTheme);

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    public void ApplyEffectiveTheme(string effectiveTheme)
    {
        ThemeService.ApplyTheme(effectiveTheme, this, WebViewControl);

        bool isDark = string.Equals(ThemeService.CurrentEffectiveTheme, "dark", StringComparison.OrdinalIgnoreCase);
        ErrorOverlay.Background = new SolidColorBrush(isDark
            ? Color.FromRgb(0x0B, 0x12, 0x20)
            : Color.FromRgb(0xF4, 0xF6, 0xFB));
        ErrorMessageText.Foreground = new SolidColorBrush(isDark
            ? Color.FromRgb(0xA9, 0xB8, 0xCD)
            : Color.FromRgb(0x47, 0x55, 0x69));
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await InitializeStartupAsync();
    }

    private async Task InitializeStartupAsync()
    {
        if (_isClosed)
        {
            return;
        }

        // Hủy yêu cầu khởi động đang dở (nếu có từ lần nhấn trước)
        _startupCts?.Cancel();
        _startupCts?.Dispose();
        _startupCts = new CancellationTokenSource();
        var ct = _startupCts.Token;

        try
        {
            await _startupLock.WaitAsync(ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (_isClosed || ct.IsCancellationRequested)
            {
                return;
            }

            ErrorOverlay.Visibility = Visibility.Collapsed;

            // 1. Dọn dẹp handlers cũ nếu đang thử lại (retry) để tránh nhân đôi listener
            CleanupG2Handlers();

            // 2. Khởi tạo bất đồng bộ SQLite Database & chạy EF Core Migrations (T04)
            var bootstrapper = new SqliteDatabaseBootstrapper();
            var bootstrapResult = await bootstrapper.BootstrapAsync(
                databasePath: RuntimePaths.DatabasePath,
                cancellationToken: ct);

            // Kiểm tra trạng thái đóng cửa sổ hoặc hủy bỏ sau await bootstrap
            if (_isClosed || ct.IsCancellationRequested)
            {
                return;
            }

            if (!bootstrapResult.IsBootstrapSuccessful)
            {
                throw new InvalidOperationException("Khởi tạo cơ sở dữ liệu không thành công.");
            }

            await ThemeService.InitializePreferenceAsync(ct);
            if (_isClosed || ct.IsCancellationRequested)
            {
                return;
            }

            ApplyEffectiveTheme(ThemeService.CurrentEffectiveTheme);

            // 3. ĐĂNG KÝ G2 HANDLERS CHỈ SAU KHI BOOTSTRAP THÀNH CÔNG VÀ CHƯA BỊ HỦY
            // Nối Workspace, Account và Prompt Handlers thật (thay thế projection tạm để tránh trùng lặp workspace.list)
            var connectionString = DatabasePathHelper.BuildConnectionString(bootstrapResult.DatabasePath);
            var dbOptions = new DbContextOptionsBuilder<TiToAIDbContext>()
                .UseSqlite(connectionString)
                .Options;

            var profileRegistry = new ProfileRegistry(() => new TiToAIDbContext(dbOptions));
            await profileRegistry.ReconcileCheckpointsAtStartupAsync(ct);
            await profileRegistry.EnsureProfileAsync("shared_ai", BrowserProfileKind.SharedAI, cancellationToken: ct);

            var sessionProbe = new SessionProbe(profileRegistry);
            var loginCoordinator = new LoginCoordinator(profileRegistry);
            var workspaceService = new WorkspaceService(dbOptions);
            var accountLinkService = new AccountLinkService(dbOptions, sessionProbe);

            _profileRegistry = profileRegistry;
            _loginCoordinator = loginCoordinator;

            _profileStateChangedHandler = async payload =>
            {
                if (BridgeRouter != null)
                {
                    await BridgeRouter.PostEventAsync("browser.profile_state_changed", null, payload).ConfigureAwait(false);
                }
            };
            profileRegistry.ProfileStateChanged += _profileStateChangedHandler;

            _workspaceHandlers = new WorkspaceHandlers(workspaceService, async envelope =>
            {
                if (BridgeRouter != null)
                {
                    await BridgeRouter.PostEventAsync(envelope.EventName, envelope.WorkspaceId, envelope.Payload).ConfigureAwait(false);
                }
            });
            _workspaceHandlers.Register(DispatcherInstance);

            _accountHandlers = new AccountHandlers(accountLinkService);
            _accountHandlers.Register(DispatcherInstance);

            _promptHandlers = new PromptHandlers(dbOptions);
            _promptHandlers.Register(DispatcherInstance);

            var projectService = new ProjectService(dbOptions);
            _projectHandlers = new ProjectHandlers(projectService);
            _projectHandlers.Register(DispatcherInstance);

            var sceneService = new SceneService(dbOptions);
            _sceneHandlers = new SceneHandlers(sceneService);
            _sceneHandlers.Register(DispatcherInstance);

            var browserHandlers = new BrowserHandlers(profileRegistry, loginCoordinator, sessionProbe);
            _browserHandlers = browserHandlers;
            _browserHandlers.Register(DispatcherInstance);

            var rootRegistry = new StorageRootRegistry(dbOptions);
            await rootRegistry.EnsureDefaultRootsAsync(ct).ConfigureAwait(false);
            var fileStore = new AssetFileStore();
            var settingsService = new SettingsService(dbOptions, rootRegistry);
            var assetService = new AssetService(dbOptions, rootRegistry, fileStore);
            var documentExportService = new DocumentExportService(dbOptions, rootRegistry, fileStore);

            _settingsHandlers = new SettingsHandlers(settingsService);
            _settingsHandlers.Register(DispatcherInstance);

            _assetHandlers = new AssetHandlers(assetService);
            _assetHandlers.Register(DispatcherInstance);

            _documentHandlers = new DocumentHandlers(documentExportService);
            _documentHandlers.Register(DispatcherInstance);

            // 4. Khởi tạo WebView2 Host Manager và điều hướng UI
            await HostManager.InitializeAsync(WebViewControl, this);

            // Kiểm tra trạng thái sau await host initialization
            if (_isClosed || ct.IsCancellationRequested)
            {
                CleanupG2Handlers();
                HostManager.Dispose();
                return;
            }

            Opacity = 1;
        }
        catch (OperationCanceledException)
        {
            // Bỏ qua khi cửa sổ bị đóng hoặc tác vụ khởi động bị hủy
        }
        catch (FileNotFoundException)
        {
            if (_isClosed || ct.IsCancellationRequested)
            {
                return;
            }

            CleanupG2Handlers();

            ErrorMessageText.Text = "Không tìm thấy tệp giao diện Web cục bộ.\nVui lòng đảm bảo bản đóng gói ứng dụng đầy đủ và nhấn 'Thử lại'.";
            ApplyEffectiveTheme(ThemeService.CurrentEffectiveTheme);
            Opacity = 1;
            ErrorOverlay.Visibility = Visibility.Visible;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("cơ sở dữ liệu", StringComparison.OrdinalIgnoreCase))
        {
            if (_isClosed || ct.IsCancellationRequested)
            {
                return;
            }

            CleanupG2Handlers();

            ErrorMessageText.Text = "Không thể khởi tạo cơ sở dữ liệu ứng dụng.\nVui lòng kiểm tra quyền truy cập hệ thống và nhấn 'Thử lại'.";
            ApplyEffectiveTheme(ThemeService.CurrentEffectiveTheme);
            Opacity = 1;
            ErrorOverlay.Visibility = Visibility.Visible;
        }
        catch (Exception)
        {
            if (_isClosed || ct.IsCancellationRequested)
            {
                return;
            }

            CleanupG2Handlers();

            ErrorMessageText.Text = "Khởi tạo ứng dụng thất bại.\nVui lòng kiểm tra WebView2 Runtime trên hệ thống và nhấn 'Thử lại'.";
            ApplyEffectiveTheme(ThemeService.CurrentEffectiveTheme);
            Opacity = 1;
            ErrorOverlay.Visibility = Visibility.Visible;
        }
        finally
        {
            _startupLock.Release();
        }
    }

    private void CleanupG2Handlers()
    {
        if (_profileRegistry != null && _profileStateChangedHandler != null)
        {
            _profileRegistry.ProfileStateChanged -= _profileStateChangedHandler;
            _profileStateChangedHandler = null;
        }

        if (_browserHandlers != null)
        {
            _browserHandlers.Unregister(DispatcherInstance);
            _browserHandlers = null;
        }

        _loginCoordinator?.Dispose();
        _loginCoordinator = null;
        _profileRegistry = null;

        if (_workspaceHandlers != null)
        {
            _workspaceHandlers.Unregister(DispatcherInstance);
            _workspaceHandlers = null;
        }
        if (_accountHandlers != null)
        {
            _accountHandlers.Unregister(DispatcherInstance);
            _accountHandlers = null;
        }
        if (_promptHandlers != null)
        {
            _promptHandlers.Unregister(DispatcherInstance);
            _promptHandlers = null;
        }
        if (_projectHandlers != null)
        {
            _projectHandlers.Unregister(DispatcherInstance);
            _projectHandlers = null;
        }
        if (_sceneHandlers != null)
        {
            _sceneHandlers.Unregister(DispatcherInstance);
            _sceneHandlers = null;
        }
        if (_settingsHandlers != null)
        {
            _settingsHandlers.Unregister(DispatcherInstance);
            _settingsHandlers = null;
        }
        if (_assetHandlers != null)
        {
            _assetHandlers.Unregister(DispatcherInstance);
            _assetHandlers = null;
        }
        if (_documentHandlers != null)
        {
            _documentHandlers.Unregister(DispatcherInstance);
            _documentHandlers = null;
        }
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        await InitializeStartupAsync();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _isClosed = true;

        _startupCts?.Cancel();
        _startupCts?.Dispose();
        _startupCts = null;

        CleanupG2Handlers();

        HostManager.Dispose();
        ThemeService.Dispose();
        // Không gọi _startupLock.Dispose() tại đây để tránh ObjectDisposedException
        // nếu InitializeStartupAsync đang chạy và khối finally sẽ gọi _startupLock.Release().
        // SemaphoreSlim sẽ được bộ thu gom rác (GC) dọn dẹp an toàn theo vòng đời đối tượng.
    }
}
