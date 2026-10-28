using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TiToAI.Contracts.Content;
using TiToAI.Contracts.Jobs;
using TiToAI.Contracts.Scenes;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;

namespace TiToAI.Application.Content;

public sealed partial class ProjectWorkflowService
{
    private static JobMediaSnapshotDto BuildMediaSnapshot(
        ProjectWorkflowConfigDto config,
        Scene? scene = null)
    {
        var modelId = config.ModelAssetId;
        var productId = config.ProductAssetId;
        var readyId = config.ReadyImageAssetId;
        var sourceImageId = scene?.ImageAssetId ?? readyId ?? productId ?? modelId;
        var audioId = scene?.AudioAssetId ?? (config.AudioOption == AudioOptions.CustomAudio ? config.AudioAssetId : null);
        var duration = scene?.TargetDurationSeconds > 0 ? Convert.ToInt32(scene.TargetDurationSeconds) : config.TargetDurationPerSceneSeconds;
        var audioOpt = scene?.AudioAssetId != null ? AudioOptions.CustomAudio : config.AudioOption;

        var roles = new Dictionary<string, string>();
        if (modelId != null) roles["Model"] = modelId;
        if (productId != null) roles["Product"] = productId;
        if (readyId != null) roles["ReadyImage"] = readyId;
        if (sourceImageId != null) roles["SourceImage"] = sourceImageId;
        if (audioId != null) roles["Audio"] = audioId;

        return new JobMediaSnapshotDto(
            ModelAssetId: modelId,
            ProductAssetId: productId,
            ReadyImageAssetId: readyId,
            SourceImageAssetId: sourceImageId,
            AudioAssetId: audioId,
            DurationSeconds: duration,
            AudioOption: audioOpt,
            MediaRoles: roles
        );
    }

    /// <summary>
    /// Lấy cấu hình quy trình sản xuất video cấp Dự án.
    /// Nếu chưa có, trả về giá trị mặc định hợp lệ.
    /// </summary>
    public async Task<ProjectWorkflowConfigDto> GetWorkflowConfigAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        var normalizedProjectId = NormalizeAndValidateGuid(projectId, nameof(projectId));

        await using var dbContext = new TiToAIDbContext(_dbOptions);
        var project = await dbContext.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == normalizedProjectId, cancellationToken);

        if (project == null)
        {
            throw new ProjectWorkflowValidationException($"Không tìm thấy dự án '{normalizedProjectId}'.");
        }

        var key = GetConfigKey(normalizedProjectId);
        var appSetting = await dbContext.AppSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

        if (appSetting != null && !string.IsNullOrWhiteSpace(appSetting.Value))
        {
            try
            {
                var stored = JsonSerializer.Deserialize<ProjectWorkflowConfigDto>(appSetting.Value, JsonOptions);
                if (stored != null)
                {
                    return stored;
                }
            }
            catch
            {
                // Fallback nếu JSON hỏng
            }
        }

        // Mặc định cấu hình mới theo CLIP_DURATION_STORYBOARD_PLAN: 1 clip, ưu tiên 15 giây nếu hỗ trợ
        return new ProjectWorkflowConfigDto(
            ProjectId: normalizedProjectId,
            WorkflowMode: WorkflowModes.ModelAndProduct,
            GoalPrompt: string.Empty,
            ModelAssetId: null,
            ProductAssetId: null,
            ReadyImageAssetId: null,
            FoundationImageAssetId: null,
            AudioOption: AudioOptions.None,
            AudioAssetId: null,
            TargetSceneCount: 1,
            TargetDurationPerSceneSeconds: 15,
            RunState: WorkflowRunState.Idle,
            CurrentRunId: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow.ToString("o"),
            ScriptPromptText: null,
            VideoPromptText: null
        );
    }

    /// <summary>
    /// Lưu cấu hình quy trình sản xuất video cấp Dự án.
    /// </summary>
    public async Task<ProjectWorkflowConfigDto> SaveWorkflowConfigAsync(
        SaveProjectWorkflowConfigRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalizedProjectId = NormalizeAndValidateGuid(request.ProjectId, nameof(request.ProjectId));

        if (!WorkflowModes.IsValid(request.WorkflowMode))
        {
            throw new ProjectWorkflowValidationException($"Chế độ '{request.WorkflowMode}' không hợp lệ.");
        }

        await using var dbContext = new TiToAIDbContext(_dbOptions);
        var project = await dbContext.Projects
            .FirstOrDefaultAsync(p => p.Id == normalizedProjectId, cancellationToken);

        if (project == null)
        {
            throw new ProjectWorkflowValidationException($"Không tìm thấy dự án '{normalizedProjectId}'.");
        }

        // Kiểm tra quyền sở hữu các asset được gán
        await ValidateAssetBelongsToProjectAsync(dbContext, normalizedProjectId, request.ModelAssetId, "ảnh mẫu", cancellationToken);
        await ValidateAssetBelongsToProjectAsync(dbContext, normalizedProjectId, request.ProductAssetId, "ảnh sản phẩm", cancellationToken);
        await ValidateAssetBelongsToProjectAsync(dbContext, normalizedProjectId, request.ReadyImageAssetId, "ảnh có sẵn", cancellationToken);
        if (request.AudioOption == AudioOptions.CustomAudio)
        {
            await ValidateAssetBelongsToProjectAsync(dbContext, normalizedProjectId, request.AudioAssetId, "tệp âm thanh", cancellationToken);
        }

        // Lấy config hiện tại để bảo toàn foundation image và runState nếu có
        var existing = await GetWorkflowConfigAsync(normalizedProjectId, cancellationToken);
        bool workflowInputsChanged = existing.WorkflowMode != request.WorkflowMode ||
                                     existing.GoalPrompt != (request.GoalPrompt ?? string.Empty) ||
                                     existing.ModelAssetId != request.ModelAssetId ||
                                     existing.ProductAssetId != request.ProductAssetId ||
                                     existing.ReadyImageAssetId != request.ReadyImageAssetId ||
                                     existing.AudioOption != (request.AudioOption ?? AudioOptions.None) ||
                                     existing.AudioAssetId != (request.AudioOption == AudioOptions.CustomAudio ? request.AudioAssetId : null) ||
                                     (request.TargetDurationPerSceneSeconds > 0 && existing.TargetDurationPerSceneSeconds != request.TargetDurationPerSceneSeconds) ||
                                     existing.ScriptPromptText != (request.ScriptPromptText ?? existing.ScriptPromptText) ||
                                     existing.VideoPromptText != (request.VideoPromptText ?? existing.VideoPromptText);
        if (workflowInputsChanged && existing.RunState is WorkflowRunState.Running or WorkflowRunState.Paused)
        {
            throw new ProjectWorkflowValidationException(
                "Đầu vào của run đang chạy không thể thay đổi. Hãy hủy run rồi bắt đầu run mới để giữ nguyên snapshot.");
        }

        var updated = existing with
        {
            WorkflowMode = request.WorkflowMode,
            GoalPrompt = request.GoalPrompt ?? string.Empty,
            ModelAssetId = request.ModelAssetId,
            ProductAssetId = request.ProductAssetId,
            ReadyImageAssetId = request.ReadyImageAssetId,
            AudioOption = request.AudioOption ?? AudioOptions.None,
            AudioAssetId = request.AudioOption == AudioOptions.CustomAudio ? request.AudioAssetId : null,
            TargetSceneCount = request.TargetSceneCount > 0 ? request.TargetSceneCount : (existing.TargetSceneCount > 0 ? existing.TargetSceneCount : 1),
            TargetDurationPerSceneSeconds = request.TargetDurationPerSceneSeconds > 0 ? request.TargetDurationPerSceneSeconds : (existing.TargetDurationPerSceneSeconds > 0 ? existing.TargetDurationPerSceneSeconds : 15),
            ScriptPromptText = request.ScriptPromptText ?? existing.ScriptPromptText,
            VideoPromptText = request.VideoPromptText ?? existing.VideoPromptText,
            UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("o")
        };

        var key = GetConfigKey(normalizedProjectId);
        var setting = await dbContext.AppSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        bool isInitialConfig = setting == null || string.IsNullOrWhiteSpace(setting.Value);
        var jsonValue = JsonSerializer.Serialize(updated, JsonOptions);

        if (setting == null)
        {
            dbContext.AppSettings.Add(new AppSetting
            {
                Key = key,
                Value = jsonValue,
                UpdatedAtUtc = updated.UpdatedAtUtc
            });
        }
        else
        {
            setting.Value = jsonValue;
            setting.UpdatedAtUtc = updated.UpdatedAtUtc;
        }

        // Áp dụng ma trận phụ thuộc §3.4: Khi đổi input chung (mẫu/sản phẩm/duration/audio), đánh dấu stale các cảnh phụ thuộc
        // Chỉ áp dụng khi không phải là lần đầu tạo cấu hình dự án
        bool modelOrProductChanged = existing.ModelAssetId != request.ModelAssetId ||
                                     existing.ProductAssetId != request.ProductAssetId ||
                                     existing.ReadyImageAssetId != request.ReadyImageAssetId;
        bool workflowModeChanged = existing.WorkflowMode != updated.WorkflowMode;
        bool durationChanged = request.TargetDurationPerSceneSeconds > 0 && existing.TargetDurationPerSceneSeconds != request.TargetDurationPerSceneSeconds;
        bool audioChanged = existing.AudioOption != request.AudioOption || existing.AudioAssetId != request.AudioAssetId;
        bool briefChanged = existing.GoalPrompt != updated.GoalPrompt;

        if (!isInitialConfig && (modelOrProductChanged || workflowModeChanged || durationChanged || audioChanged || briefChanged))
        {
            var scenes = await dbContext.Scenes
                .Where(s => s.ProjectId == normalizedProjectId)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            foreach (var sc in scenes)
            {
                sc.UpdatedAtUtc = updated.UpdatedAtUtc;
                var sKey = GetSceneKey(sc.Id);
                var sSetting = await dbContext.AppSettings.FirstOrDefaultAsync(item => item.Key == sKey, cancellationToken).ConfigureAwait(false);
                SceneWorkflowDetailDto? existingDetail = null;
                if (sSetting != null && !string.IsNullOrWhiteSpace(sSetting.Value))
                {
                    try { existingDetail = JsonSerializer.Deserialize<SceneWorkflowDetailDto>(sSetting.Value, JsonOptions); } catch { }
                }
                var candidateHistory = existingDetail?.VideoCandidateHistory;
                var staleTasks = new HashSet<string>(existingDetail?.StaleTaskTypes ?? Array.Empty<string>(), StringComparer.Ordinal);
                bool independentImageOverride = existingDetail?.ImageStrategy == SceneImageStrategy.DirectInput;
                bool commonImageChanged = modelOrProductChanged || workflowModeChanged;
                if (commonImageChanged && !independentImageOverride)
                {
                    string? oldCommonImage = existing.WorkflowMode == WorkflowModes.ReadyImage
                        ? existing.ReadyImageAssetId
                        : existing.WorkflowMode == WorkflowModes.ProductOnly ? existing.ProductAssetId : null;
                    string? newCommonImage = updated.WorkflowMode == WorkflowModes.ReadyImage
                        ? updated.ReadyImageAssetId
                        : updated.WorkflowMode == WorkflowModes.ProductOnly ? updated.ProductAssetId : null;

                    if (sc.ImageAssetId == oldCommonImage || string.IsNullOrWhiteSpace(sc.ImageAssetId))
                    {
                        sc.ImageAssetId = newCommonImage;
                        sc.UpdatedAtUtc = updated.UpdatedAtUtc;
                    }

                    if (CanGenerateImage(updated.WorkflowMode))
                    {
                        staleTasks.Add(JobTaskType.Image);
                    }
                }
                if (commonImageChanged || durationChanged || audioChanged || briefChanged)
                {
                    staleTasks.Add(JobTaskType.VideoPrompt);
                    staleTasks.Add(JobTaskType.VideoRender);
                }

                string reason = commonImageChanged ? "Ảnh mẫu/sản phẩm dùng cho cảnh đã thay đổi." :
                                durationChanged ? "Thời lượng clip đã thay đổi." :
                                audioChanged ? "Âm thanh dùng cho clip đã thay đổi." :
                                "Yêu cầu nội dung chung đã thay đổi.";

                var metaObj = new SceneWorkflowDetailDto(
                    SceneId: sc.Id,
                    ProjectId: sc.ProjectId,
                    SequenceOrder: sc.SequenceOrder,
                    Name: sc.Name,
                    TargetDurationSeconds: Convert.ToInt32(sc.TargetDurationSeconds),
                    WorkflowStatus: SceneWorkflowStatus.NeedsReview,
                    ImageStrategy: existingDetail?.ImageStrategy ?? SceneImageStrategy.ReuseFoundation,
                    SourceImageAssetId: sc.ImageAssetId,
                    SelectedImageAssetId: sc.ImageAssetId,
                    MotionPromptText: sc.PromptText,
                    AudioOption: sc.AudioAssetId != null ? AudioOptions.CustomAudio : AudioOptions.None,
                    AudioAssetId: sc.AudioAssetId,
                    VideoAssetId: sc.VideoAssetId,
                    IsStale: true,
                    StaleReason: reason,
                    ActiveJobId: null,
                    ActiveJobType: null,
                    ActiveJobState: null,
                    ActiveJobProgressPercent: null,
                    VideoCandidateHistory: candidateHistory,
                    Revision: updated.UpdatedAtUtc,
                    StaleTaskTypes: staleTasks.ToArray()
                );
                var sJson = JsonSerializer.Serialize(metaObj, JsonOptions);
                if (sSetting == null)
                {
                    dbContext.AppSettings.Add(new AppSetting { Key = sKey, Value = sJson, UpdatedAtUtc = updated.UpdatedAtUtc });
                }
                else
                {
                    sSetting.Value = sJson;
                    sSetting.UpdatedAtUtc = updated.UpdatedAtUtc;
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return updated;
    }

    private static bool CanGenerateImage(string workflowMode) =>
        workflowMode == WorkflowModes.ModelAndProduct;
}
