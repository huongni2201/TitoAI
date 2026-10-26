using System.IO;
using Microsoft.EntityFrameworkCore;
using TiToAI.Contracts.Settings;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;
using TiToAI.Infrastructure.Storage;

namespace TiToAI.Application.Settings;

public class SettingsValidationException : Exception
{
    public IReadOnlyDictionary<string, List<string>> FieldErrors { get; }

    public SettingsValidationException(Dictionary<string, List<string>> fieldErrors)
        : base("Dữ liệu cấu hình cài đặt không hợp lệ. Vui lòng kiểm tra lại các trường báo lỗi.")
    {
        FieldErrors = fieldErrors ?? new Dictionary<string, List<string>>();
    }

    public SettingsValidationException(string field, string error)
        : base(error)
    {
        FieldErrors = new Dictionary<string, List<string>>
        {
            [field] = new() { error }
        };
    }
}

/// <summary>
/// Dịch vụ quản lý Cài đặt toàn hệ thống (Lưu trữ, Providers AI, Theme)
/// theo SETTINGS_PLAN.md §1–5 và implementation/T09a-storage-settings.md.
/// </summary>
public sealed class SettingsService
{
    public const string KeyTheme = "ui.theme";
    public const string KeyImageProvider = "ai.providers.image";
    public const string KeyScriptProvider = "ai.providers.script";
    public const string KeyVideoPromptProvider = "ai.providers.videoPrompt";

    private readonly DbContextOptions<TiToAIDbContext> _dbOptions;
    private readonly StorageRootRegistry _rootRegistry;

    public SettingsService(
        DbContextOptions<TiToAIDbContext> dbOptions,
        StorageRootRegistry rootRegistry)
    {
        _dbOptions = dbOptions ?? throw new ArgumentNullException(nameof(dbOptions));
        _rootRegistry = rootRegistry ?? throw new ArgumentNullException(nameof(rootRegistry));
    }

    /// <summary>
    /// Lấy toàn bộ thiết lập hiện hành của ứng dụng (SettingsDto).
    /// </summary>
    public async Task<SettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var roots = await _rootRegistry.GetCurrentRootsAsync(cancellationToken).ConfigureAwait(false);

        await using var dbContext = new TiToAIDbContext(_dbOptions);

        var settings = await dbContext.AppSettings
            .AsNoTracking()
            .Where(s => s.Key == KeyTheme ||
                        s.Key == KeyImageProvider ||
                        s.Key == KeyScriptProvider ||
                        s.Key == KeyVideoPromptProvider)
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken)
            .ConfigureAwait(false);

        var themePreference = settings.GetValueOrDefault(KeyTheme, "system")!;
        var effectiveTheme = string.Equals(themePreference, "light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";

        return new SettingsDto(
            DocumentsPath: roots.DocumentsPath,
            ImagesPath: roots.ImagesPath,
            VideosPath: roots.VideosPath,
            DocumentsRootId: roots.DocumentsRootId,
            ImagesRootId: roots.ImagesRootId,
            VideosRootId: roots.VideosRootId,
            ImageProvider: settings.GetValueOrDefault(KeyImageProvider),
            ScriptProvider: settings.GetValueOrDefault(KeyScriptProvider),
            VideoPromptProvider: settings.GetValueOrDefault(KeyVideoPromptProvider),
            ThemePreference: themePreference,
            EffectiveTheme: effectiveTheme
        );
    }

    /// <summary>
    /// Xác thực đồng thời ba đường dẫn thư mục lưu trữ độc lập (kiểm tra rỗng, ký tự cấm, và write probe).
    /// </summary>
    public Task<ValidateRootsResponse> ValidateRootsAsync(ValidateRootsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var fieldErrors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        ValidateSinglePath("documentsPath", "Tài liệu", request.DocumentsPath, fieldErrors);
        ValidateSinglePath("imagesPath", "Ảnh AI", request.ImagesPath, fieldErrors);
        ValidateSinglePath("videosPath", "Video AI", request.VideosPath, fieldErrors);

        var isValid = fieldErrors.Count == 0;
        return Task.FromResult(new ValidateRootsResponse(isValid, fieldErrors));
    }

    /// <summary>
    /// Lưu toàn bộ thiết lập: Validate atomic trước khi cập nhật.
    /// Nếu một đường dẫn lỗi, hủy bỏ toàn bộ và trả về lỗi chi tiết theo từng trường.
    /// </summary>
    public async Task<SettingsDto> SaveSettingsAsync(SettingsSaveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationResult = await ValidateRootsAsync(
            new ValidateRootsRequest(request.DocumentsPath, request.ImagesPath, request.VideosPath),
            cancellationToken
        ).ConfigureAwait(false);

        if (!validationResult.IsValid)
        {
            throw new SettingsValidationException(validationResult.FieldErrors);
        }

        // Cập nhật Storage Roots và mappings
        await _rootRegistry.UpdateRootsAsync(
            request.DocumentsPath,
            request.ImagesPath,
            request.VideosPath,
            cancellationToken
        ).ConfigureAwait(false);

        // Cập nhật providers AI nếu có truyền
        await using (var dbContext = new TiToAIDbContext(_dbOptions))
        {
            var now = DateTimeOffset.UtcNow.ToString("O");

            if (request.ImageProvider is not null)
            {
                await UpsertSettingAsync(dbContext, KeyImageProvider, request.ImageProvider, now, cancellationToken).ConfigureAwait(false);
            }

            if (request.ScriptProvider is not null)
            {
                await UpsertSettingAsync(dbContext, KeyScriptProvider, request.ScriptProvider, now, cancellationToken).ConfigureAwait(false);
            }

            if (request.VideoPromptProvider is not null)
            {
                await UpsertSettingAsync(dbContext, KeyVideoPromptProvider, request.VideoPromptProvider, now, cancellationToken).ConfigureAwait(false);
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return await GetSettingsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Mở hộp thoại chọn thư mục Windows an toàn.
    /// Trả về đường dẫn được chọn hoặc null nếu người dùng hủy bỏ hoặc môi trường không hỗ trợ hộp thoại GUI.
    /// </summary>
    public Task<BrowseFolderResponse> BrowseFolderAsync(BrowseFolderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? selectedFolder = null;

        try
        {
            // Kiểm tra và thử mở hộp thoại chọn thư mục trên Windows Desktop nếu có UI thread
            if (OperatingSystem.IsWindows())
            {
                selectedFolder = ShowFolderBrowserDialog(request.InitialPath);
            }
        }
        catch
        {
            // Khi chạy headless hoặc không có message loop STA, fallback trả về null
            selectedFolder = null;
        }

        return Task.FromResult(new BrowseFolderResponse(selectedFolder));
    }

    private static string? ShowFolderBrowserDialog(string? initialPath)
    {
        string? result = null;

        var thread = new Thread(() =>
        {
            try
            {
                // Thử reflection tới OpenFolderDialog (WPF .NET 8/10) hoặc FolderBrowserDialog
                var dialogType = Type.GetType("Microsoft.Win32.OpenFolderDialog, PresentationFramework")
                              ?? Type.GetType("System.Windows.Forms.FolderBrowserDialog, System.Windows.Forms");

                if (dialogType is not null)
                {
                    dynamic dialog = Activator.CreateInstance(dialogType)!;
                    if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
                    {
                        try { dialog.InitialDirectory = initialPath; } catch { }
                        try { dialog.SelectedPath = initialPath; } catch { }
                    }

                    dynamic showResult = dialog.ShowDialog();
                    bool isOk = showResult is bool b ? b : (showResult?.ToString() == "OK");
                    if (isOk)
                    {
                        try { result = dialog.FolderName; } catch { }
                        if (string.IsNullOrEmpty(result))
                        {
                            try { result = dialog.SelectedPath; } catch { }
                        }
                    }
                }
            }
            catch
            {
                result = null;
            }
        });

        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }
        thread.Start();
        // Chờ tối đa 60 giây nếu có người dùng thao tác hộp thoại
        if (thread.Join(TimeSpan.FromSeconds(60)))
        {
            return result;
        }

        return null;
    }

    private static void ValidateSinglePath(
        string fieldKey,
        string fieldName,
        string? path,
        Dictionary<string, List<string>> errors)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            AddError(errors, fieldKey, $"Đường dẫn thư mục {fieldName} không được để trống.");
            return;
        }

        string normalized;
        try
        {
            normalized = StoragePathGuard.NormalizePath(path);
        }
        catch (Exception ex)
        {
            AddError(errors, fieldKey, $"Đường dẫn {fieldName} không hợp lệ: {ex.Message}");
            return;
        }

        if (!Path.IsPathRooted(normalized))
        {
            AddError(errors, fieldKey, $"Đường dẫn {fieldName} phải là đường dẫn tuyệt đối (ví dụ C:\\TiToAI\\{fieldKey}).");
            return;
        }

        if (!StoragePathGuard.TryVerifyWritable(normalized, out var writeError))
        {
            AddError(errors, fieldKey, $"Không thể ghi vào thư mục {fieldName}: {writeError}");
        }
    }

    private static void AddError(Dictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var list))
        {
            list = new List<string>();
            errors[key] = list;
        }
        list.Add(message);
    }

    private static async Task UpsertSettingAsync(
        TiToAIDbContext dbContext,
        string key,
        string value,
        string nowUtc,
        CancellationToken cancellationToken)
    {
        var item = await dbContext.AppSettings
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            dbContext.AppSettings.Add(new AppSetting
            {
                Key = key,
                Value = value,
                UpdatedAtUtc = nowUtc
            });
        }
        else
        {
            item.Value = value;
            item.UpdatedAtUtc = nowUtc;
        }
    }
}
