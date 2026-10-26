using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TiToAI.Application.Prompts;
using TiToAI.Application.Prompts.Models;
using TiToAI.Application.Settings;
using TiToAI.Contracts.Jobs;
using TiToAI.Contracts.Prompts;

namespace TiToAI.Application.Jobs;

/// <summary>
/// Chịu trách nhiệm chụp và đóng băng bất biến (Immutable Input Snapshots) các tham số đầu vào
/// tại thời điểm Job được Enqueue vào hệ thống.
/// Bảo đảm các thay đổi sau này trong Popup Cài đặt hoặc Prompt Editor không làm biến đổi dữ liệu của job cũ.
/// </summary>
public sealed class JobSnapshotFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly SettingsService _settingsService;
    private readonly PromptTemplateService? _promptTemplateService;

    public JobSnapshotFactory(
        SettingsService settingsService,
        PromptTemplateService? promptTemplateService = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _promptTemplateService = promptTemplateService;
    }

    /// <summary>
    /// Chụp và đóng băng toàn bộ tham số đầu vào: AI Provider, Storage Root, Prompt Snapshot và Config Snapshot.
    /// </summary>
    public async Task<JobSnapshotResult> CreateSnapshotAsync(
        JobEnqueueRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!JobTaskType.IsValid(request.TaskType))
        {
            throw new JobValidationException($"Loại tác vụ '{request.TaskType}' không hợp lệ. Các loại cho phép: {string.Join(", ", JobTaskType.All)}.", nameof(request.TaskType));
        }

        // 1. Phân giải Cài đặt hiện tại từ SettingsService
        var currentSettings = await _settingsService.GetSettingsAsync(cancellationToken).ConfigureAwait(false);

        // 2. Phân giải AI Provider Key bất biến (Strict Provider Validation - không fallback ngầm)
        string? providerKey = request.TaskType switch
        {
            JobTaskType.Script => currentSettings.ScriptProvider,
            JobTaskType.Image => currentSettings.ImageProvider,
            JobTaskType.VideoPrompt => currentSettings.VideoPromptProvider,
            JobTaskType.VideoRender => "museai", // VideoRender mặc định dùng MuseAI theo kiến trúc
            _ => null
        };

        if (string.IsNullOrWhiteSpace(providerKey))
        {
            throw new ProviderNotConfiguredException(request.TaskType);
        }

        // 3. Phân giải Storage Root ID bất biến (Đóng băng nơi lưu trữ ảnh/tài liệu/video)
        string? storageRootId = request.TaskType switch
        {
            JobTaskType.Script => currentSettings.DocumentsRootId,
            JobTaskType.Image => currentSettings.ImagesRootId,
            JobTaskType.VideoPrompt => currentSettings.DocumentsRootId,
            JobTaskType.VideoRender => currentSettings.VideosRootId,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(storageRootId))
        {
            throw new StorageRootUnavailableException(request.TaskType);
        }

        // 4. Phân giải Prompt Snapshot bất biến
        string promptSnapshotText;
        if (!string.IsNullOrWhiteSpace(request.CustomPromptText))
        {
            // Custom prompt trực tiếp
            var customSnapshot = new PromptRenderSnapshotDto(
                TemplateId: "custom",
                VersionId: "custom-v1",
                RenderedText: request.CustomPromptText,
                Variables: request.PromptVariables ?? new Dictionary<string, string>(),
                CapturedAtUtc: DateTimeOffset.UtcNow.ToString("o")
            );
            promptSnapshotText = JsonSerializer.Serialize(customSnapshot, JsonOptions);
        }
        else if (!string.IsNullOrWhiteSpace(request.PromptTemplateId))
        {
            if (_promptTemplateService is null)
            {
                throw new InvalidOperationException("PromptTemplateService chưa được nạp trong bộ điều phối JobSnapshotFactory.");
            }

            var snapshotDto = await _promptTemplateService.CreateSnapshotAsync(
                templateId: request.PromptTemplateId,
                purpose: request.TaskType,
                workspaceId: request.WorkspaceId,
                variables: request.PromptVariables,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            promptSnapshotText = JsonSerializer.Serialize(snapshotDto, JsonOptions);
        }
        else
        {
            throw new JobValidationException("Tác vụ yêu cầu phải có 'promptTemplateId' hoặc 'customPromptText'.", nameof(request.CustomPromptText));
        }

        // 5. Đóng gói Config Snapshot JSON
        var configSnapshot = new
        {
            TimeoutSeconds = 60,
            MaxAttempts = 3,
            LeaseDurationSeconds = 45,
            HeartbeatIntervalSeconds = 15,
            CapturedAtUtc = DateTimeOffset.UtcNow.ToString("o")
        };
        string configSnapshotJson = JsonSerializer.Serialize(configSnapshot, JsonOptions);

        return new JobSnapshotResult(
            ProviderKey: providerKey,
            StorageRootId: storageRootId,
            PromptSnapshotText: promptSnapshotText,
            ConfigSnapshotJson: configSnapshotJson
        );
    }
}
