using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using TiToAI.Desktop.Bridge;

namespace TiToAI.Desktop.Hosting;

public sealed class WebView2HostManager : IDisposable
{
    public const string HostName = "appassets.local";
    public const string RootUrl = "https://appassets.local/index.html";

    private readonly IHostThemeService _themeService;
    private readonly IBridgeDispatcher _dispatcher;
    private CoreWebView2? _coreWebView2;

    public string AssetsFolderPath { get; private set; } = string.Empty;

    public string UserDataFolder { get; }

    public BridgeMessageRouter? BridgeRouter { get; private set; }

    public bool IsInitialized { get; private set; }

    public WebView2HostManager(
        IHostThemeService themeService,
        IBridgeDispatcher dispatcher,
        string? userDataFolder = null)
    {
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        UserDataFolder = string.IsNullOrWhiteSpace(userDataFolder)
            ? DesktopRuntimePaths.GetDefaultWebView2ProfileDirectory()
            : userDataFolder;
    }

    public static string ResolveAssetsFolderPath()
    {
        // Đường dẫn thư mục wwwroot trong output của Desktop host
        return Path.Combine(AppContext.BaseDirectory, "wwwroot");
    }

    public async Task InitializeAsync(WebView2 webView, Window window)
    {
        ArgumentNullException.ThrowIfNull(webView);
        ArgumentNullException.ThrowIfNull(window);

        // Áp dụng theme hiệu lực lên Window và WebView2 trước khi hiển thị nội dung để chống flash sáng
        _themeService.ApplyTheme(_themeService.CurrentEffectiveTheme, window, webView);

        AssetsFolderPath = ResolveAssetsFolderPath();

        // Kiểm tra bắt buộc có tệp index.html trong thư mục output wwwroot
        string indexFilePath = Path.Combine(AssetsFolderPath, "index.html");
        if (!File.Exists(indexFilePath))
        {
            throw new FileNotFoundException(
                $"Không tìm thấy tệp giao diện Web 'index.html' tại '{AssetsFolderPath}'. Vui lòng đảm bảo bản build React UI (dist) đã được sao chép vào thư mục wwwroot của host.",
                indexFilePath);
        }

        string userDataFolder = UserDataFolder;
        Directory.CreateDirectory(userDataFolder);

        if (webView.CoreWebView2 == null)
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await webView.EnsureCoreWebView2Async(environment);
        }

        var coreWebView2 = webView.CoreWebView2
            ?? throw new InvalidOperationException("Khởi tạo WebView2 thất bại: CoreWebView2 không được tạo sau khi hoàn tất EnsureCoreWebView2Async.");

        // Hủy đăng ký và dispose router cũ nếu có (trường hợp retry) để tránh duplicate event handlers
        if (_coreWebView2 != null && BridgeRouter != null)
        {
            _coreWebView2.WebMessageReceived -= BridgeRouter.OnWebMessageReceived;
            _coreWebView2.NavigationStarting -= OnNavigationStarting;
            _coreWebView2.NewWindowRequested -= OnNewWindowRequested;
            BridgeRouter.Dispose();
            BridgeRouter = null;
        }

        _coreWebView2 = coreWebView2;

        // Đăng ký Virtual Host Name Mapping theo contract §1.1
        coreWebView2.SetVirtualHostNameToFolderMapping(
            HostName,
            AssetsFolderPath,
            CoreWebView2HostResourceAccessKind.Allow
        );

        // ĐĂNG KÝ EVENT HANDLERS TRƯỚC NAVIGATION
        BridgeRouter = new BridgeMessageRouter(_dispatcher, coreWebView2, window.Dispatcher);

        // Liên kết handler theme của Host với Dispatcher
        _themeService.RegisterBridgeHandlers(_dispatcher, () => BridgeRouter);

        // Đăng ký rào chắn an toàn (loại bỏ đăng ký cũ nếu có để hỗ trợ retry sạch)
        coreWebView2.NavigationStarting -= OnNavigationStarting;
        coreWebView2.NavigationStarting += OnNavigationStarting;

        coreWebView2.NewWindowRequested -= OnNewWindowRequested;
        coreWebView2.NewWindowRequested += OnNewWindowRequested;

        coreWebView2.WebMessageReceived -= BridgeRouter.OnWebMessageReceived;
        coreWebView2.WebMessageReceived += BridgeRouter.OnWebMessageReceived;

        // Bắt đầu điều hướng tới origin an toàn sau khi toàn bộ rào chắn đã đăng ký
        coreWebView2.Navigate(RootUrl);

        IsInitialized = true;
    }

    public void Dispose()
    {
        if (_coreWebView2 != null)
        {
            try
            {
                _coreWebView2.NavigationStarting -= OnNavigationStarting;
                _coreWebView2.NewWindowRequested -= OnNewWindowRequested;
                if (BridgeRouter != null)
                {
                    _coreWebView2.WebMessageReceived -= BridgeRouter.OnWebMessageReceived;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebView2HostManager] Lỗi khi hủy đăng ký sự kiện CoreWebView2: {ex.GetType().Name}");
            }
            _coreWebView2 = null;
        }

        if (BridgeRouter != null)
        {
            BridgeRouter.Dispose();
            BridgeRouter = null;
        }

        IsInitialized = false;
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var navUri) ||
            navUri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(navUri.Host, HostName, StringComparison.OrdinalIgnoreCase) ||
            navUri.Port != 443)
        {
            // Hủy bỏ điều hướng ngoài origin an toàn của host để bảo vệ App Shell
            e.Cancel = true;
            Debug.WriteLine("[WebView2HostManager] Chặn điều hướng ngoài origin cho phép.");
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        // Ngăn chặn WebView2 mở popup hoặc cửa sổ mới ra ngoài
        e.Handled = true;
        Debug.WriteLine("[WebView2HostManager] Chặn yêu cầu mở cửa sổ mới hoặc popup.");
    }
}
