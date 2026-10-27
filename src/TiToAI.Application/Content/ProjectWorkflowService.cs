using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TiToAI.Application.Jobs;
using TiToAI.Application.Providers;
using TiToAI.Contracts.Content;
using TiToAI.Contracts.Jobs;
using TiToAI.Contracts.Providers;
using TiToAI.Contracts.Scenes;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;

namespace TiToAI.Application.Content;

public sealed class ProjectWorkflowValidationException : Exception
{
    public ProjectWorkflowValidationException(string message) : base(message) { }
}

public sealed class SceneConcurrencyConflictException : Exception
{
    public string SceneId { get; }
    public string ExpectedRevision { get; }
    public string ActualRevision { get; }

    public SceneConcurrencyConflictException(string sceneId, string expectedRevision, string actualRevision)
        : base($"Xung đột đồng thời khi cập nhật Cảnh '{sceneId}': Kỳ vọng revision '{expectedRevision}', thực tế '{actualRevision}'.")
    {
        SceneId = sceneId;
        ExpectedRevision = expectedRevision;
        ActualRevision = actualRevision;
    }
}

/// <summary>
/// Ý định chuyển tiếp bước tiếp theo trong quy trình (Continuation Intent).
/// Được commit trong cùng một SQLite transaction với apply marker để bảo đảm phục hồi sau sự cố (Crash Recovery).
/// </summary>
public sealed record WorkflowContinuationIntent(
    string IntentId,
    string WorkspaceId,
    string ProjectId,
    string? SceneId,
    string TaskType,
    string? CustomPromptText,
    JobMediaSnapshotDto? MediaSnapshot,
    string RunId,
    string CreatedAtUtc
);

/// <summary>
/// Bản ghi trạng thái của một lần chạy quy trình (Workflow Run Record).
/// </summary>
public sealed record WorkflowRunRecord(
    string RunId,
    string WorkspaceId,
    string ProjectId,
    string WorkflowMode,
    string GoalPrompt,
    string? ModelAssetId,
    string? ProductAssetId,
    string? ReadyImageAssetId,
    string AudioOption,
    string? AudioAssetId,
    int TargetSceneCount,
    int TargetDurationPerSceneSeconds,
    string RunState,
    string? ClientRequestId,
    IReadOnlyList<string> SelectedSceneIds,
    string CreatedAtUtc,
    string UpdatedAtUtc
);

/// <summary>
/// Dịch vụ quản lý quy trình sản xuất video từ ảnh theo PRODUCT_IMAGE_VIDEO_WORKFLOW_PLAN.md.
/// Điều phối 3 chế độ bắt đầu, lập cảnh có mục đích, tái sử dụng ảnh nền tảng,
/// ma trận phụ thuộc §3.4 và audio tùy chọn.
/// </summary>
public sealed class ProjectWorkflowService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly DbContextOptions<TiToAIDbContext> _dbOptions;
    private readonly JobService? _jobService;
    private readonly Func<StartProjectWorkflowRequest, CancellationToken, Task<int>>? _durableJobEnqueuer;

    public ProjectWorkflowService(
        DbContextOptions<TiToAIDbContext> dbOptions,
        JobService? jobService = null,
        Func<StartProjectWorkflowRequest, CancellationToken, Task<int>>? durableJobEnqueuer = null)
    {
        _dbOptions = dbOptions ?? throw new ArgumentNullException(nameof(dbOptions));
        _jobService = jobService;
        _durableJobEnqueuer = durableJobEnqueuer;
    }

    public ProjectWorkflowService(
        DbContextOptions<TiToAIDbContext> dbOptions,
        Func<StartProjectWorkflowRequest, CancellationToken, Task<int>>? durableJobEnqueuer)
        : this(dbOptions, null, durableJobEnqueuer)
    {
    }

    private static string GetConfigKey(string projectId) => $"project.workflow.config.{projectId}";
    private static string GetSceneKey(string sceneId) => $"scene.workflow.{sceneId}";

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
        string workspaceId,
        string projectId,
        CancellationToken cancellationToken = default)
    {
        var normalizedWorkspaceId = NormalizeAndValidateGuid(workspaceId, nameof(workspaceId));
        var normalizedProjectId = NormalizeAndValidateGuid(projectId, nameof(projectId));

        await using var dbContext = new TiToAIDbContext(_dbOptions);
        var project = await dbContext.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == normalizedProjectId, cancellationToken);

        if (project == null)
        {
            throw new ProjectWorkflowValidationException($"Không tìm thấy dự án '{normalizedProjectId}'.");
        }

        if (project.WorkspaceId != normalizedWorkspaceId)
        {
            throw new UnauthorizedAccessException($"Dự án '{normalizedProjectId}' không thuộc Workspace '{normalizedWorkspaceId}'.");
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
            WorkspaceId: normalizedWorkspaceId,
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
            UpdatedAtUtc: DateTimeOffset.UtcNow.ToString("o")
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
        var normalizedWorkspaceId = NormalizeAndValidateGuid(request.WorkspaceId, nameof(request.WorkspaceId));
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

        if (project.WorkspaceId != normalizedWorkspaceId)
        {
            throw new UnauthorizedAccessException($"Dự án '{normalizedProjectId}' không thuộc Workspace '{normalizedWorkspaceId}'.");
        }

        // Kiểm tra quyền sở hữu các asset được gán
        await ValidateAssetBelongsToWorkspaceAsync(dbContext, normalizedWorkspaceId, request.ModelAssetId, "ảnh mẫu", cancellationToken);
        await ValidateAssetBelongsToWorkspaceAsync(dbContext, normalizedWorkspaceId, request.ProductAssetId, "ảnh sản phẩm", cancellationToken);
        await ValidateAssetBelongsToWorkspaceAsync(dbContext, normalizedWorkspaceId, request.ReadyImageAssetId, "ảnh có sẵn", cancellationToken);
        if (request.AudioOption == AudioOptions.CustomAudio)
        {
            await ValidateAssetBelongsToWorkspaceAsync(dbContext, normalizedWorkspaceId, request.AudioAssetId, "tệp âm thanh", cancellationToken);
        }

        // Lấy config hiện tại để bảo toàn foundation image và runState nếu có
        var existing = await GetWorkflowConfigAsync(normalizedWorkspaceId, normalizedProjectId, cancellationToken);

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
        bool durationChanged = request.TargetDurationPerSceneSeconds > 0 && existing.TargetDurationPerSceneSeconds != request.TargetDurationPerSceneSeconds;
        bool audioChanged = existing.AudioOption != request.AudioOption || existing.AudioAssetId != request.AudioAssetId;

        if (!isInitialConfig && (modelOrProductChanged || durationChanged || audioChanged))
        {
            var scenes = await dbContext.Scenes
                .Where(s => s.ProjectId == normalizedProjectId && s.WorkspaceId == normalizedWorkspaceId && !string.IsNullOrWhiteSpace(s.VideoAssetId))
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            foreach (var sc in scenes)
            {
                var sKey = GetSceneKey(sc.Id);
                var sSetting = await dbContext.AppSettings.FirstOrDefaultAsync(item => item.Key == sKey, cancellationToken).ConfigureAwait(false);
                string reason = modelOrProductChanged ? "Đầu vào chung (mẫu/sản phẩm) đã thay đổi. Cần tạo lại ảnh, prompt và video." :
                                durationChanged ? "Thời lượng chung đã thay đổi. Cần tạo lại prompt và video." :
                                "Âm thanh chung đã thay đổi. Cần tạo lại video.";

                SceneWorkflowDetailDto? existingDetail = null;
                if (sSetting != null && !string.IsNullOrWhiteSpace(sSetting.Value))
                {
                    try { existingDetail = JsonSerializer.Deserialize<SceneWorkflowDetailDto>(sSetting.Value, JsonOptions); } catch { }
                }
                var candidateHistory = existingDetail?.VideoCandidateHistory;

                var metaObj = new SceneWorkflowDetailDto(
                    SceneId: sc.Id,
                    ProjectId: sc.ProjectId,
                    WorkspaceId: sc.WorkspaceId,
                    SequenceOrder: sc.SequenceOrder,
                    Name: sc.Name,
                    TargetDurationSeconds: Convert.ToInt32(sc.TargetDurationSeconds),
                    WorkflowStatus: SceneWorkflowStatus.NeedsReview,
                    ImageStrategy: SceneImageStrategy.ReuseFoundation,
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
                    Revision: updated.UpdatedAtUtc
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

    /// <summary>
    /// Kích hoạt quy trình sản xuất video (Bấm Bắt đầu tạo video một lần duy nhất).
    /// Kiểm tra tính đầy đủ của dữ liệu theo chế độ đã chọn:
    /// - ModelAndProduct: Bắt buộc cả Model và Product.
    /// - ReadyImage: Bắt buộc ReadyImage.
    /// - ProductOnly: Bắt buộc Product.
    /// Tự động lập danh sách cảnh nếu chưa có cảnh nào, gán ảnh nền tảng và viết prompt.
    /// </summary>
    public async Task<StartProjectWorkflowResponseData> StartWorkflowAsync(
        StartProjectWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalizedWorkspaceId = NormalizeAndValidateGuid(request.WorkspaceId, nameof(request.WorkspaceId));
        var normalizedProjectId = NormalizeAndValidateGuid(request.ProjectId, nameof(request.ProjectId));

        var config = await GetWorkflowConfigAsync(normalizedWorkspaceId, normalizedProjectId, cancellationToken);

        await using var dbContext = new TiToAIDbContext(_dbOptions);

        // Guard active run và hỗ trợ Idempotency (FIX-1):
        if (config.RunState == WorkflowRunState.Running && !string.IsNullOrWhiteSpace(config.CurrentRunId))
        {
            var existingRunKey = $"workflow.active_run.{normalizedProjectId}";
            var existingRunSetting = await dbContext.AppSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == existingRunKey, cancellationToken).ConfigureAwait(false);

            if (existingRunSetting != null)
            {
                try
                {
                    var existingRun = JsonSerializer.Deserialize<WorkflowRunRecord>(existingRunSetting.Value, JsonOptions);
                    if (existingRun != null && !string.IsNullOrWhiteSpace(request.ClientRequestId) &&
                        string.Equals(existingRun.ClientRequestId, request.ClientRequestId, StringComparison.Ordinal))
                    {
                        // Request lặp lại cùng ClientRequestId: Trả về run hiện tại một cách Idempotent (không tạo thêm job!)
                        return new StartProjectWorkflowResponseData(
                            RunId: existingRun.RunId,
                            RunState: WorkflowRunState.Running,
                            CreatedSceneCount: existingRun.SelectedSceneIds?.Count ?? 0,
                            Message: "Quy trình đang chạy được trả về an toàn theo ClientRequestId."
                        );
                    }
                }
                catch { }
            }

            throw new ProjectWorkflowValidationException(
                "Dự án đang có quy trình sản xuất video đang chạy (Running). Vui lòng chờ hoàn tất, tạm dừng hoặc hủy trước khi bắt đầu quy trình mới.");
        }

        // 0. Kiểm tra yêu cầu nội dung (GoalPrompt / USER_BRIEF không được để trống cho run tự động mới theo §3.2)
        if (string.IsNullOrWhiteSpace(config.GoalPrompt))
        {
            throw new ProjectWorkflowValidationException(
                "Vui lòng nhập yêu cầu nội dung / phong cách chuyển động trước khi bắt đầu tạo video.");
        }

        // Kiểm tra thời lượng cấu hình có được provider video hỗ trợ hay không (§2, CD04, CD05)
        if (!ProviderCapabilities.IsDurationSupported(ProviderKeys.MuseAI, config.TargetDurationPerSceneSeconds))
        {
            var supported = ProviderCapabilities.GetSupportedDurations(ProviderKeys.MuseAI);
            throw new ProjectWorkflowValidationException(
                $"Thời lượng {config.TargetDurationPerSceneSeconds} giây không được nhà cung cấp video hỗ trợ. Vui lòng chọn lại thời lượng hợp lệ ({string.Join("s, ", supported)}s).");
        }

        // 1. Kiểm tra tính hợp lệ của đầu vào theo chế độ (§2)
        switch (config.WorkflowMode)
        {
            case WorkflowModes.ModelAndProduct:
                if (string.IsNullOrWhiteSpace(config.ModelAssetId) || string.IsNullOrWhiteSpace(config.ProductAssetId))
                {
                    throw new ProjectWorkflowValidationException(
                        "Chế độ 'Mẫu + sản phẩm' yêu cầu cung cấp đầy đủ cả Ảnh mẫu và Ảnh sản phẩm có vai trò rõ ràng.");
                }
                break;

            case WorkflowModes.ReadyImage:
                if (string.IsNullOrWhiteSpace(config.ReadyImageAssetId))
                {
                    throw new ProjectWorkflowValidationException(
                        "Chế độ 'Ảnh đã sẵn sàng' yêu cầu cung cấp Ảnh hoàn thiện (đã mặc/cầm sản phẩm).");
                }
                break;

            case WorkflowModes.ProductOnly:
                if (string.IsNullOrWhiteSpace(config.ProductAssetId))
                {
                    throw new ProjectWorkflowValidationException(
                        "Chế độ 'Chỉ sản phẩm' yêu cầu cung cấp ít nhất một Ảnh sản phẩm.");
                }
                break;

            default:
                throw new ProjectWorkflowValidationException($"Chế độ '{config.WorkflowMode}' không được hỗ trợ.");
        }

        var nowIso = DateTimeOffset.UtcNow.ToString("o");
        var runId = Guid.NewGuid().ToString("D").ToLowerInvariant();

        // 2. Lấy danh sách các cảnh hiện có của dự án
        var existingScenes = await dbContext.Scenes
            .Where(s => s.ProjectId == normalizedProjectId && s.WorkspaceId == normalizedWorkspaceId)
            .OrderBy(s => s.SequenceOrder)
            .ToListAsync(cancellationToken);

        // Kiểm tra thời lượng của từng cảnh hiện có theo capability provider (§2, CD04, CD08)
        var supportedDurations = ProviderCapabilities.GetSupportedDurations(ProviderKeys.MuseAI);
        var invalidDurationScene = existingScenes.FirstOrDefault(s => !supportedDurations.Contains(Convert.ToInt32(s.TargetDurationSeconds)));
        if (invalidDurationScene != null)
        {
            throw new ProjectWorkflowValidationException(
                $"Cảnh '{invalidDurationScene.Name}' có thời lượng {Convert.ToInt32(invalidDurationScene.TargetDurationSeconds)}s không được hỗ trợ bởi nhà cung cấp video. Vui lòng chọn lại thời lượng ({string.Join("s, ", supportedDurations)}s) trước khi bắt đầu.");
        }

        // NGUYÊN TẮC MỐC 1 & MỐC 4:
        // - Không dùng 3 cảnh/prompt hardcode mẫu tĩnh giả mạo là kết quả AI production.
        // - Nếu dự án chưa có cảnh nào:
        //   + Nếu không có enqueuer/jobService: từ chối sinh cảnh mẫu cố định.
        //   + Nếu có jobService hoặc enqueuer: Enqueue Job Script (với SceneId = null) để AI lập danh sách cảnh từ GoalPrompt!
        int enqueuedJobCount = 0;

        if (existingScenes.Count == 0)
        {
            if (_durableJobEnqueuer != null)
            {
                enqueuedJobCount = await _durableJobEnqueuer(request, cancellationToken);
            }
            else if (_jobService != null)
            {
                enqueuedJobCount = await EnqueueScriptPlanningJobAsync(normalizedWorkspaceId, normalizedProjectId, config, cancellationToken);
            }
            else
            {
                throw new ProjectWorkflowValidationException(
                    "Dự án chưa có cảnh nào để sản xuất video. Hệ thống từ chối sinh cảnh mẫu cố định khi chưa có dịch vụ AI lập cảnh. Vui lòng thêm cảnh cho dự án hoặc cấu hình Provider AI.");
            }
        }
        else
        {
            // Cập nhật audio từ project cho các cảnh hiện có nếu scene chưa có audio riêng
            foreach (var s in existingScenes)
            {
                if (config.AudioOption == AudioOptions.CustomAudio && s.AudioAssetId == null && config.AudioAssetId != null)
                {
                    s.AudioAssetId = config.AudioAssetId;
                    s.UpdatedAtUtc = nowIso;
                }
            }

            if (_durableJobEnqueuer != null)
            {
                enqueuedJobCount = await _durableJobEnqueuer(request, cancellationToken);
            }
            else if (_jobService != null)
            {
                enqueuedJobCount = await EnqueueJobsForExistingScenesAsync(dbContext, normalizedWorkspaceId, normalizedProjectId, config, existingScenes, cancellationToken);
            }
        }

        if (enqueuedJobCount <= 0)
        {
            throw new ProjectWorkflowValidationException(
                "Chưa thể bắt đầu sản xuất video: Hệ thống chưa có tác vụ nền bền vững nào được tạo trong hàng đợi (chờ kết nối Provider/Scheduler). Hệ thống từ chối chuyển sang trạng thái Đang chạy để tránh treo tiến trình. Dữ liệu cấu hình của bạn đã được bảo toàn.");
        }

        // 4. CHỈ KHI có công việc bền vững thực sự được enqueue, mới chuyển RunState = Running
        var updatedConfig = config with
        {
            RunState = WorkflowRunState.Running,
            CurrentRunId = runId,
            UpdatedAtUtc = nowIso
        };

        var key = GetConfigKey(normalizedProjectId);
        var setting = await dbContext.AppSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (setting != null)
        {
            setting.Value = JsonSerializer.Serialize(updatedConfig, JsonOptions);
            setting.UpdatedAtUtc = nowIso;
        }

        var activeRun = new WorkflowRunRecord(
            RunId: runId,
            WorkspaceId: normalizedWorkspaceId,
            ProjectId: normalizedProjectId,
            WorkflowMode: config.WorkflowMode,
            GoalPrompt: config.GoalPrompt,
            ModelAssetId: config.ModelAssetId,
            ProductAssetId: config.ProductAssetId,
            ReadyImageAssetId: config.ReadyImageAssetId,
            AudioOption: config.AudioOption,
            AudioAssetId: config.AudioAssetId,
            TargetSceneCount: config.TargetSceneCount,
            TargetDurationPerSceneSeconds: config.TargetDurationPerSceneSeconds,
            RunState: WorkflowRunState.Running,
            ClientRequestId: request.ClientRequestId,
            SelectedSceneIds: existingScenes.Select(s => s.Id).ToList(),
            CreatedAtUtc: nowIso,
            UpdatedAtUtc: nowIso
        );
        var activeRunKey = $"workflow.active_run.{normalizedProjectId}";
        var activeRunSetting = await dbContext.AppSettings.FirstOrDefaultAsync(s => s.Key == activeRunKey, cancellationToken);
        var activeRunJson = JsonSerializer.Serialize(activeRun, JsonOptions);
        if (activeRunSetting == null)
        {
            dbContext.AppSettings.Add(new AppSetting { Key = activeRunKey, Value = activeRunJson, UpdatedAtUtc = nowIso });
        }
        else
        {
            activeRunSetting.Value = activeRunJson;
            activeRunSetting.UpdatedAtUtc = nowIso;
        }

        var prj = await dbContext.Projects.FirstOrDefaultAsync(p => p.Id == normalizedProjectId, cancellationToken);
        if (prj != null)
        {
            prj.Status = "InProgress";
            prj.UpdatedAtUtc = nowIso;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new StartProjectWorkflowResponseData(
            RunId: runId,
            RunState: WorkflowRunState.Running,
            CreatedSceneCount: existingScenes.Count,
            Message: $"Đã bắt đầu quy trình tạo video. {enqueuedJobCount} tác vụ bền vững đã được đưa vào hàng đợi xử lý."
        );
    }

    /// <summary>
    /// Lấy danh sách chi tiết quy trình của tất cả các Cảnh trong Dự án.
    /// </summary>
    public async Task<IReadOnlyList<SceneWorkflowDetailDto>> ListSceneWorkflowDetailsAsync(
        string workspaceId,
        string projectId,
        CancellationToken cancellationToken = default)
    {
        var normalizedWorkspaceId = NormalizeAndValidateGuid(workspaceId, nameof(workspaceId));
        var normalizedProjectId = NormalizeAndValidateGuid(projectId, nameof(projectId));

        await using var dbContext = new TiToAIDbContext(_dbOptions);
        var scenes = await dbContext.Scenes
            .AsNoTracking()
            .Where(s => s.ProjectId == normalizedProjectId && s.WorkspaceId == normalizedWorkspaceId)
            .OrderBy(s => s.SequenceOrder)
            .ToListAsync(cancellationToken);

        var result = new List<SceneWorkflowDetailDto>();

        foreach (var s in scenes)
        {
            var key = GetSceneKey(s.Id);
            var setting = await dbContext.AppSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Key == key, cancellationToken);

            if (setting != null && !string.IsNullOrWhiteSpace(setting.Value))
            {
                try
                {
                    var detail = JsonSerializer.Deserialize<SceneWorkflowDetailDto>(setting.Value, JsonOptions);
                    if (detail != null)
                    {
                        // Luôn đồng bộ dữ liệu cốt lõi từ bảng Scene
                        detail = detail with
                        {
                            Name = s.Name,
                            SequenceOrder = s.SequenceOrder,
                            TargetDurationSeconds = Convert.ToInt32(s.TargetDurationSeconds),
                            SelectedImageAssetId = s.ImageAssetId,
                            MotionPromptText = s.PromptText,
                            AudioAssetId = s.AudioAssetId,
                            VideoAssetId = s.VideoAssetId,
                            Revision = s.UpdatedAtUtc
                        };
                        result.Add(detail);
                        continue;
                    }
                }
                catch
                {
                    // Fallback
                }
            }

            // Fallback tạo từ Scene nếu chưa có AppSetting
            string workflowStatus = !string.IsNullOrWhiteSpace(s.VideoAssetId)
                ? SceneWorkflowStatus.VideoCompleted
                : (!string.IsNullOrWhiteSpace(s.ImageAssetId) && !string.IsNullOrWhiteSpace(s.PromptText))
                    ? SceneWorkflowStatus.ReadyForVideo
                    : (!string.IsNullOrWhiteSpace(s.ImageAssetId))
                        ? SceneWorkflowStatus.PreparingPrompt
                        : SceneWorkflowStatus.PreparingImage;

            result.Add(new SceneWorkflowDetailDto(
                SceneId: s.Id,
                ProjectId: s.ProjectId,
                WorkspaceId: s.WorkspaceId,
                SequenceOrder: s.SequenceOrder,
                Name: s.Name,
                TargetDurationSeconds: Convert.ToInt32(s.TargetDurationSeconds),
                WorkflowStatus: workflowStatus,
                ImageStrategy: SceneImageStrategy.ReuseFoundation,
                SourceImageAssetId: s.ImageAssetId,
                SelectedImageAssetId: s.ImageAssetId,
                MotionPromptText: s.PromptText,
                AudioOption: s.AudioAssetId != null ? AudioOptions.CustomAudio : AudioOptions.None,
                AudioAssetId: s.AudioAssetId,
                VideoAssetId: s.VideoAssetId,
                IsStale: false,
                StaleReason: null,
                ActiveJobId: null,
                ActiveJobType: null,
                ActiveJobState: null,
                ActiveJobProgressPercent: null,
                VideoCandidateHistory: null,
                Revision: s.UpdatedAtUtc
            ));
        }

        return result;
    }

    /// <summary>
    /// Cập nhật đầu vào của Cảnh (Override) và áp dụng ma trận phụ thuộc §3.4:
    /// - Đổi ảnh -> Đánh dấu làm lại prompt và video; không sinh lại ảnh vừa chọn.
    /// - Đổi prompt -> Chỉ làm lại video cảnh đó; giữ ảnh.
    /// - Đổi audio -> Chỉ làm lại video cảnh đó.
    /// </summary>
    public async Task<SceneWorkflowDetailDto> UpdateSceneWorkflowInputAsync(
        UpdateSceneWorkflowInputRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalizedWorkspaceId = NormalizeAndValidateGuid(request.WorkspaceId, nameof(request.WorkspaceId));
        var normalizedProjectId = NormalizeAndValidateGuid(request.ProjectId, nameof(request.ProjectId));
        var normalizedSceneId = NormalizeAndValidateGuid(request.SceneId, nameof(request.SceneId));

        await using var dbContext = new TiToAIDbContext(_dbOptions);
        var scene = await dbContext.Scenes
            .FirstOrDefaultAsync(s => s.Id == normalizedSceneId && s.ProjectId == normalizedProjectId && s.WorkspaceId == normalizedWorkspaceId, cancellationToken);

        if (scene == null)
        {
            throw new ProjectWorkflowValidationException($"Không tìm thấy Cảnh '{normalizedSceneId}'.");
        }

        // Optimistic concurrency check
        if (!string.IsNullOrWhiteSpace(request.ExpectedRevision) && scene.UpdatedAtUtc != request.ExpectedRevision)
        {
            throw new SceneConcurrencyConflictException(scene.Id, request.ExpectedRevision, scene.UpdatedAtUtc);
        }

        var nowIso = DateTimeOffset.UtcNow.ToString("o");
        bool imageChanged = false;
        bool promptChanged = false;

        // 1. Kiểm tra thay đổi ảnh
        if (request.SelectedImageAssetId != null && request.SelectedImageAssetId != scene.ImageAssetId)
        {
            if (!string.IsNullOrWhiteSpace(request.SelectedImageAssetId))
            {
                await ValidateAssetBelongsToWorkspaceAsync(dbContext, normalizedWorkspaceId, request.SelectedImageAssetId, "ảnh được chọn", cancellationToken);
            }
            scene.ImageAssetId = string.IsNullOrWhiteSpace(request.SelectedImageAssetId) ? null : request.SelectedImageAssetId;
            imageChanged = true;
        }

        // 2. Kiểm tra thay đổi prompt
        if (request.MotionPromptText != null && request.MotionPromptText != scene.PromptText)
        {
            scene.PromptText = request.MotionPromptText;
            promptChanged = true;
        }

        // 3. Thay đổi audio
        if (request.AudioOption != null)
        {
            if (request.AudioOption == AudioOptions.CustomAudio && request.AudioAssetId != null)
            {
                await ValidateAssetBelongsToWorkspaceAsync(dbContext, normalizedWorkspaceId, request.AudioAssetId, "tệp âm thanh", cancellationToken);
                scene.AudioAssetId = request.AudioAssetId;
            }
            else if (request.AudioOption == AudioOptions.None)
            {
                scene.AudioAssetId = null;
            }
        }

        if (request.TargetDurationSeconds.HasValue && request.TargetDurationSeconds.Value > 0)
        {
            scene.TargetDurationSeconds = request.TargetDurationSeconds.Value;
        }

        scene.UpdatedAtUtc = nowIso;

        // Áp dụng ma trận phụ thuộc §3.4:
        // Nếu thay ảnh: Prompt và Video cũ không còn đại diện cấu hình hiện tại -> đánh dấu stale
        // Nếu thay prompt: Video cũ không còn đại diện -> đánh dấu stale
        string currentWorkflowStatus = !string.IsNullOrWhiteSpace(scene.VideoAssetId)
            ? SceneWorkflowStatus.VideoCompleted
            : (!string.IsNullOrWhiteSpace(scene.ImageAssetId) && !string.IsNullOrWhiteSpace(scene.PromptText))
                ? SceneWorkflowStatus.ReadyForVideo
                : SceneWorkflowStatus.PreparingImage;

        bool isStale = false;
        string? staleReason = null;

        if (imageChanged && !string.IsNullOrWhiteSpace(scene.VideoAssetId))
        {
            isStale = true;
            staleReason = "Ảnh dùng cho video đã thay đổi. Cần tạo lại video cho cảnh này.";
            currentWorkflowStatus = SceneWorkflowStatus.NeedsReview;
        }
        else if (promptChanged && !string.IsNullOrWhiteSpace(scene.VideoAssetId))
        {
            isStale = true;
            staleReason = "Prompt chuyển động đã thay đổi. Cần tạo lại video cho cảnh này.";
            currentWorkflowStatus = SceneWorkflowStatus.NeedsReview;
        }

        var key = GetSceneKey(scene.Id);
        var setting = await dbContext.AppSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        SceneWorkflowDetailDto? existingSceneMeta = null;
        if (setting != null && !string.IsNullOrWhiteSpace(setting.Value))
        {
            try { existingSceneMeta = JsonSerializer.Deserialize<SceneWorkflowDetailDto>(setting.Value, JsonOptions); } catch { }
        }
        var candidateHistory = existingSceneMeta?.VideoCandidateHistory;

        var sceneDetail = new SceneWorkflowDetailDto(
            SceneId: scene.Id,
            ProjectId: scene.ProjectId,
            WorkspaceId: scene.WorkspaceId,
            SequenceOrder: scene.SequenceOrder,
            Name: scene.Name,
            TargetDurationSeconds: Convert.ToInt32(scene.TargetDurationSeconds),
            WorkflowStatus: currentWorkflowStatus,
            ImageStrategy: request.ImageStrategy ?? SceneImageStrategy.ReuseFoundation,
            SourceImageAssetId: scene.ImageAssetId,
            SelectedImageAssetId: scene.ImageAssetId,
            MotionPromptText: scene.PromptText,
            AudioOption: scene.AudioAssetId != null ? AudioOptions.CustomAudio : AudioOptions.None,
            AudioAssetId: scene.AudioAssetId,
            VideoAssetId: scene.VideoAssetId,
            IsStale: isStale,
            StaleReason: staleReason,
            ActiveJobId: null,
            ActiveJobType: null,
            ActiveJobState: null,
            ActiveJobProgressPercent: null,
            VideoCandidateHistory: candidateHistory,
            Revision: nowIso
        );

        var jsonVal = JsonSerializer.Serialize(sceneDetail, JsonOptions);

        if (setting == null)
        {
            dbContext.AppSettings.Add(new AppSetting
            {
                Key = key,
                Value = jsonVal,
                UpdatedAtUtc = nowIso
            });
        }
        else
        {
            setting.Value = jsonVal;
            setting.UpdatedAtUtc = nowIso;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return sceneDetail;
    }

    /// <summary>
    /// Tạm dừng quy trình đang chạy (Pause Run).
    /// </summary>
    public async Task<ProjectWorkflowConfigDto> PauseWorkflowAsync(
        string workspaceId,
        string projectId,
        CancellationToken cancellationToken = default)
    {
        var config = await GetWorkflowConfigAsync(workspaceId, projectId, cancellationToken);
        var updated = config with
        {
            RunState = WorkflowRunState.Paused,
            UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("o")
        };

        await using var dbContext = new TiToAIDbContext(_dbOptions);
        var key = GetConfigKey(config.ProjectId);
        var setting = await dbContext.AppSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (setting != null)
        {
            setting.Value = JsonSerializer.Serialize(updated, JsonOptions);
            setting.UpdatedAtUtc = updated.UpdatedAtUtc;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return updated;
    }

    /// <summary>
    /// Tiếp tục quy trình sau khi tạm dừng (Resume Run).
    /// </summary>
    public async Task<ProjectWorkflowConfigDto> ResumeWorkflowAsync(
        string workspaceId,
        string projectId,
        CancellationToken cancellationToken = default)
    {
        var normalizedWorkspaceId = NormalizeAndValidateGuid(workspaceId, nameof(workspaceId));
        var normalizedProjectId = NormalizeAndValidateGuid(projectId, nameof(projectId));

        var config = await GetWorkflowConfigAsync(normalizedWorkspaceId, normalizedProjectId, cancellationToken).ConfigureAwait(false);
        var nowIso = DateTimeOffset.UtcNow.ToString("o");
        var updated = config with
        {
            RunState = WorkflowRunState.Running,
            UpdatedAtUtc = nowIso
        };

        await using var dbContext = new TiToAIDbContext(_dbOptions);
        var key = GetConfigKey(config.ProjectId);
        var setting = await dbContext.AppSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken).ConfigureAwait(false);
        if (setting != null)
        {
            setting.Value = JsonSerializer.Serialize(updated, JsonOptions);
            setting.UpdatedAtUtc = updated.UpdatedAtUtc;
        }

        // Kích hoạt tiếp các cảnh chưa xong và chưa có active job
        if (_jobService != null)
        {
            var scenes = await dbContext.Scenes
                .Where(s => s.ProjectId == normalizedProjectId && s.WorkspaceId == normalizedWorkspaceId)
                .OrderBy(s => s.SequenceOrder)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var activeJobSceneIds = await dbContext.Jobs
                .Where(j => j.ProjectId == normalizedProjectId &&
                            (j.State == JobState.Queued || j.State == JobState.Reserved || j.State == JobState.Running || j.State == JobState.AwaitingUser) &&
                            j.SceneId != null)
                .Select(j => j.SceneId!)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var activeSet = new HashSet<string>(activeJobSceneIds);
            var eligibleScenes = scenes.Where(s => !activeSet.Contains(s.Id) && string.IsNullOrWhiteSpace(s.VideoAssetId)).ToList();
            if (eligibleScenes.Count > 0)
            {
                await EnqueueJobsForExistingScenesAsync(dbContext, normalizedWorkspaceId, normalizedProjectId, updated, eligibleScenes, cancellationToken).ConfigureAwait(false);
            }

            await RunWorkflowContinuationSweepAsync(cancellationToken).ConfigureAwait(false);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return updated;
    }

    /// <summary>
    /// Hủy bỏ quy trình đang chạy (Cancel Run).
    /// Đổi RunState = Idle và hủy bỏ các tác vụ đang chờ/đang thực thi theo chuẩn T16.
    /// </summary>
    public async Task<ProjectWorkflowConfigDto> CancelWorkflowAsync(
        string workspaceId,
        string projectId,
        CancellationToken cancellationToken = default)
    {
        var normalizedWorkspaceId = NormalizeAndValidateGuid(workspaceId, nameof(workspaceId));
        var normalizedProjectId = NormalizeAndValidateGuid(projectId, nameof(projectId));

        var config = await GetWorkflowConfigAsync(normalizedWorkspaceId, normalizedProjectId, cancellationToken).ConfigureAwait(false);
        var nowIso = DateTimeOffset.UtcNow.ToString("o");
        var updated = config with
        {
            RunState = WorkflowRunState.Idle,
            UpdatedAtUtc = nowIso
        };

        await using var dbContext = new TiToAIDbContext(_dbOptions);
        var key = GetConfigKey(normalizedProjectId);
        var setting = await dbContext.AppSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken).ConfigureAwait(false);
        if (setting != null)
        {
            setting.Value = JsonSerializer.Serialize(updated, JsonOptions);
            setting.UpdatedAtUtc = updated.UpdatedAtUtc;
        }

        // Xóa active run record
        var activeRunKey = $"workflow.active_run.{normalizedProjectId}";
        var activeRunSetting = await dbContext.AppSettings.FirstOrDefaultAsync(s => s.Key == activeRunKey, cancellationToken).ConfigureAwait(false);
        if (activeRunSetting != null)
        {
            dbContext.AppSettings.Remove(activeRunSetting);
        }

        // Hủy các job đang queued/reserved/running/awaitingUser của project này để giải phóng slot an toàn (PV-R4)
        if (_jobService != null)
        {
            var activeJobIds = await dbContext.Jobs
                .Where(j => j.ProjectId == normalizedProjectId && (j.State == JobState.Queued || j.State == JobState.Reserved || j.State == JobState.Running || j.State == JobState.AwaitingUser))
                .Select(j => j.Id)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            foreach (var jId in activeJobIds)
            {
                try
                {
                    await _jobService.CancelAsync(normalizedWorkspaceId, jId, cancellationToken).ConfigureAwait(false);
                }
                catch { }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return updated;
    }

    /// <summary>
    /// Enqueue tác vụ lập cảnh tự động (Job Script) khi dự án chưa có cảnh nào.
    /// Tổng hợp prompt đầy đủ từ Template Script + GoalPrompt (USER_BRIEF) qua VideoWorkflowPromptComposer.
    /// </summary>
    private async Task<int> EnqueueScriptPlanningJobAsync(
        string workspaceId,
        string projectId,
        ProjectWorkflowConfigDto config,
        CancellationToken cancellationToken)
    {
        if (_jobService == null) return 0;

        var composer = new VideoWorkflowPromptComposer(_dbOptions);
        var composition = await composer.ComposeScriptPlanningAiRequestAsync(workspaceId, config, cancellationToken).ConfigureAwait(false);

        var enqueueRequest = new JobEnqueueRequest(
            WorkspaceId: workspaceId,
            ProjectId: projectId,
            SceneId: null, // Bắt buộc null cho Script theo DB constraint CK_Job_TaskType_Scene
            TaskType: JobTaskType.Script,
            CustomPromptText: composition.ComposedPrompt,
            WorkflowRunId: config.CurrentRunId,
            WorkflowStepKey: "script.planning",
            MediaSnapshot: BuildMediaSnapshot(config)
        );

        var response = await _jobService.EnqueueAsync(enqueueRequest, cancellationToken).ConfigureAwait(false);
        return response != null ? 1 : 0;
    }

    /// <summary>
    /// Enqueue các tác vụ độc lập cho các cảnh đủ điều kiện chạy ngay (§3.3).
    /// </summary>
    public async Task<int> EnqueueJobsForExistingScenesAsync(
        TiToAIDbContext dbContext,
        string workspaceId,
        string projectId,
        ProjectWorkflowConfigDto config,
        List<Scene> scenes,
        CancellationToken cancellationToken)
    {
        if (_jobService == null) return 0;
        int count = 0;
        var composer = new VideoWorkflowPromptComposer(_dbOptions);

        foreach (var scene in scenes)
        {
            // Tự gán ảnh cho ReadyImage / ProductOnly nếu cảnh chưa có
            if (string.IsNullOrWhiteSpace(scene.ImageAssetId))
            {
                if (config.WorkflowMode == WorkflowModes.ReadyImage && !string.IsNullOrWhiteSpace(config.ReadyImageAssetId))
                {
                    scene.ImageAssetId = config.ReadyImageAssetId;
                }
                else if (config.WorkflowMode == WorkflowModes.ProductOnly && !string.IsNullOrWhiteSpace(config.ProductAssetId))
                {
                    scene.ImageAssetId = config.ProductAssetId;
                }
            }

            var snapshot = BuildMediaSnapshot(config, scene);

            // 1. Cảnh chưa có ảnh và ở chế độ ModelAndProduct: Enqueue Job Image
            if (string.IsNullOrWhiteSpace(scene.ImageAssetId) && config.WorkflowMode == WorkflowModes.ModelAndProduct)
            {
                var prompt = scene.PromptText ?? config.GoalPrompt;
                await _jobService.EnqueueAsync(new JobEnqueueRequest(
                    WorkspaceId: workspaceId,
                    ProjectId: projectId,
                    SceneId: scene.Id,
                    TaskType: JobTaskType.Image,
                    CustomPromptText: $"Tạo ảnh người mẫu mặc sản phẩm cho cảnh: {scene.Name}. {prompt}",
                    WorkflowRunId: config.CurrentRunId,
                    WorkflowStepKey: $"image.{scene.Id}",
                    MediaSnapshot: snapshot
                ), cancellationToken).ConfigureAwait(false);
                count++;
            }
            // 2. Cảnh đã có ảnh nhưng chưa có PromptText chuyển động: Enqueue Job VideoPrompt
            // Nối dữ liệu qua VideoWorkflowPromptComposer: Template VideoPrompt + Brief (USER_BRIEF) + Clip Context
            else if (!string.IsNullOrWhiteSpace(scene.ImageAssetId) && string.IsNullOrWhiteSpace(scene.PromptText))
            {
                var composition = await composer.ComposeVideoPromptAiRequestAsync(workspaceId, config, scene, cancellationToken).ConfigureAwait(false);

                await _jobService.EnqueueAsync(new JobEnqueueRequest(
                    WorkspaceId: workspaceId,
                    ProjectId: projectId,
                    SceneId: scene.Id,
                    TaskType: JobTaskType.VideoPrompt,
                    CustomPromptText: composition.ComposedPrompt,
                    WorkflowRunId: config.CurrentRunId,
                    WorkflowStepKey: $"prompt.{scene.Id}",
                    MediaSnapshot: snapshot
                ), cancellationToken).ConfigureAwait(false);
                count++;
            }
            // 3. Cảnh đã có ảnh và đã có PromptText nhưng chưa có Video (hoặc Video cũ bị stale): Enqueue Job VideoRender
            // Gửi trực tiếp PromptText của scene (kết quả do AI writer tạo ra hoặc người dùng chỉnh sửa) cho MuseAI
            else if (!string.IsNullOrWhiteSpace(scene.ImageAssetId) && !string.IsNullOrWhiteSpace(scene.PromptText) && string.IsNullOrWhiteSpace(scene.VideoAssetId))
            {
                await _jobService.EnqueueAsync(new JobEnqueueRequest(
                    WorkspaceId: workspaceId,
                    ProjectId: projectId,
                    SceneId: scene.Id,
                    TaskType: JobTaskType.VideoRender,
                    CustomPromptText: scene.PromptText,
                    WorkflowRunId: config.CurrentRunId,
                    WorkflowStepKey: $"render.{scene.Id}",
                    MediaSnapshot: snapshot
                ), cancellationToken).ConfigureAwait(false);
                count++;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return count;
    }

    /// <summary>
    /// Áp dụng kết quả hợp lệ của một Job hoàn tất thành công (Succeeded) và kích hoạt bước tiếp theo (§3.3).
    /// </summary>
    public async Task<bool> ApplyJobOutputAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId)) return false;

        await using var dbContext = new TiToAIDbContext(_dbOptions);
        var job = await dbContext.Jobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken).ConfigureAwait(false);
        if (job == null || job.State != JobState.Succeeded)
        {
            return false;
        }

        // Idempotency guard: kiểm tra đã áp dụng kết quả của job này chưa (PV-R3)
        var appliedKey = $"workflow.applied.{job.Id}";
        var appliedSetting = await dbContext.AppSettings
            .FirstOrDefaultAsync(s => s.Key == appliedKey, cancellationToken).ConfigureAwait(false);
        if (appliedSetting != null)
        {
            return true;
        }

        var nowIso = DateTimeOffset.UtcNow.ToString("o");
        var config = await GetWorkflowConfigAsync(job.WorkspaceId, job.ProjectId, cancellationToken).ConfigureAwait(false);

        // Đánh dấu job đã được apply trong transaction này
        dbContext.AppSettings.Add(new AppSetting
        {
            Key = appliedKey,
            Value = JsonSerializer.Serialize(new { JobId = job.Id, AppliedAtUtc = nowIso }, JsonOptions),
            UpdatedAtUtc = nowIso
        });

        bool isPaused = config.RunState == WorkflowRunState.Paused;
        WorkflowContinuationIntent? continuationIntent = null;
        var composer = new VideoWorkflowPromptComposer(_dbOptions);

        // 1. Áp dụng kết quả Script -> Tự động tạo các Scene trong SQLite
        if (job.TaskType == JobTaskType.Script)
        {
            if (string.IsNullOrWhiteSpace(job.ResultText))
            {
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            var existingScenes = await dbContext.Scenes
                .Where(s => s.ProjectId == job.ProjectId)
                .OrderBy(s => s.SequenceOrder)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            if (existingScenes.Count > 0)
            {
                // Cảnh đã tồn tại (do người dùng tạo hoặc đã sinh trước đó), KHÔNG nhân bản thêm cảnh mới (PV-R3)
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }

            var sceneItems = ExtractScenePlanItems(job.ResultText, config.TargetSceneCount, config.TargetDurationPerSceneSeconds);
            if (sceneItems.Count > 0)
            {
                var createdScenes = new List<Scene>();
                for (int i = 0; i < sceneItems.Count; i++)
                {
                    var item = sceneItems[i];
                    var newScene = new Scene
                    {
                        Id = Guid.NewGuid().ToString("D").ToLowerInvariant(),
                        ProjectId = job.ProjectId,
                        WorkspaceId = job.WorkspaceId,
                        SequenceOrder = i + 1,
                        Name = item.Name,
                        TargetDurationSeconds = item.TargetDurationSeconds > 0 ? item.TargetDurationSeconds : config.TargetDurationPerSceneSeconds,
                        PromptText = !string.IsNullOrWhiteSpace(item.MotionPromptDraft) ? item.MotionPromptDraft : null,
                        ImageAssetId = config.WorkflowMode == WorkflowModes.ReadyImage ? config.ReadyImageAssetId :
                                       config.WorkflowMode == WorkflowModes.ProductOnly ? config.ProductAssetId : null,
                        AudioAssetId = config.AudioOption == AudioOptions.CustomAudio ? config.AudioAssetId : null,
                        Status = "Draft",
                        CreatedAtUtc = nowIso,
                        UpdatedAtUtc = nowIso
                    };
                    dbContext.Scenes.Add(newScene);
                    createdScenes.Add(newScene);
                }

                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                // Nếu không bị pause, enqueue các job tiếp theo cho các scene mới tạo
                if (!isPaused && _jobService != null)
                {
                    await EnqueueJobsForExistingScenesAsync(dbContext, job.WorkspaceId, job.ProjectId, config, createdScenes, cancellationToken).ConfigureAwait(false);
                }
            }
            return true;
        }

        if (string.IsNullOrWhiteSpace(job.SceneId))
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        var scene = await dbContext.Scenes.FirstOrDefaultAsync(s => s.Id == job.SceneId, cancellationToken).ConfigureAwait(false);
        if (scene == null)
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        // Lấy metadata setting hiện tại của scene để kiểm tra stale và candidate history
        var sceneKey = GetSceneKey(scene.Id);
        var sceneSetting = await dbContext.AppSettings.FirstOrDefaultAsync(s => s.Key == sceneKey, cancellationToken).ConfigureAwait(false);
        SceneWorkflowDetailDto? sceneMeta = null;
        if (sceneSetting != null && !string.IsNullOrWhiteSpace(sceneSetting.Value))
        {
            try { sceneMeta = JsonSerializer.Deserialize<SceneWorkflowDetailDto>(sceneSetting.Value, JsonOptions); } catch { }
        }

        var candidateHistory = sceneMeta?.VideoCandidateHistory != null
            ? new List<string>(sceneMeta.VideoCandidateHistory)
            : new List<string>();

        bool isUserModified = string.CompareOrdinal(scene.UpdatedAtUtc, job.CreatedAtUtc) > 0;

        // 2. Áp dụng kết quả Image -> Lưu ảnh và kích hoạt VideoPrompt
        if (job.TaskType == JobTaskType.Image)
        {
            // Kiểm tra stale: nếu user đã chỉnh sửa/chọn ảnh khác trong khi job đang chạy
            // Kết quả của job này chuyển vào candidateHistory thay vì ghi đè ảnh hiện tại (PV-R3)
            if (isUserModified && !string.IsNullOrWhiteSpace(scene.ImageAssetId) &&
                !string.Equals(scene.ImageAssetId, job.ResultReference, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(job.ResultReference) && !candidateHistory.Contains(job.ResultReference))
                {
                    candidateHistory.Add(job.ResultReference);
                }
            }
            else
            {
                scene.ImageAssetId = job.ResultReference;
                scene.UpdatedAtUtc = nowIso;
            }

            // Kích hoạt bước tiếp theo: VideoPrompt
            // Chú ý: Kể cả khi scene.PromptText đã có draft từ Script, VẪN enqueue VideoPrompt
            // để AI writer hoàn thiện thành motion prompt đầy đủ với Template và USER_BRIEF (§3.3, PV-R5, PV-R11)
            if (!isPaused)
            {
                var composition = await composer.ComposeVideoPromptAiRequestAsync(
                    job.WorkspaceId,
                    config,
                    scene,
                    cancellationToken).ConfigureAwait(false);

                continuationIntent = new WorkflowContinuationIntent(
                    IntentId: Guid.NewGuid().ToString("D").ToLowerInvariant(),
                    WorkspaceId: job.WorkspaceId,
                    ProjectId: job.ProjectId,
                    SceneId: scene.Id,
                    TaskType: JobTaskType.VideoPrompt,
                    CustomPromptText: composition.ComposedPrompt,
                    MediaSnapshot: BuildMediaSnapshot(config, scene),
                    RunId: config.CurrentRunId ?? string.Empty,
                    CreatedAtUtc: nowIso
                );
            }
        }
        // 3. Áp dụng kết quả VideoPrompt -> Lưu prompt và kích hoạt VideoRender
        else if (job.TaskType == JobTaskType.VideoPrompt)
        {
            // Kiểm tra stale: nếu user đã tự tay sửa PromptText trong khi job VideoPrompt đang chạy
            // Kết quả này không được ghi đè prompt đã sửa của user (PV-R3)
            if (isUserModified && !string.IsNullOrWhiteSpace(scene.PromptText) &&
                !string.Equals(scene.PromptText, job.ResultText, StringComparison.Ordinal))
            {
                // Giữ nguyên prompt của người dùng
            }
            else
            {
                scene.PromptText = job.ResultText;
                scene.UpdatedAtUtc = nowIso;
            }

            if (!isPaused)
            {
                continuationIntent = new WorkflowContinuationIntent(
                    IntentId: Guid.NewGuid().ToString("D").ToLowerInvariant(),
                    WorkspaceId: job.WorkspaceId,
                    ProjectId: job.ProjectId,
                    SceneId: scene.Id,
                    TaskType: JobTaskType.VideoRender,
                    CustomPromptText: scene.PromptText,
                    MediaSnapshot: BuildMediaSnapshot(config, scene),
                    RunId: config.CurrentRunId ?? string.Empty,
                    CreatedAtUtc: nowIso
                );
            }
        }
        // 4. Áp dụng kết quả VideoRender -> Lưu video và kiểm tra hoàn tất run
        else if (job.TaskType == JobTaskType.VideoRender)
        {
            scene.VideoAssetId = job.ResultReference;
            scene.Status = "Completed";
            scene.UpdatedAtUtc = nowIso;

            // Xóa cờ Stale khi đã có video mới khớp cấu hình
            if (sceneMeta != null && sceneMeta.IsStale)
            {
                sceneMeta = sceneMeta with { IsStale = false, StaleReason = null };
            }

            if (!string.IsNullOrWhiteSpace(job.ResultReference) && !candidateHistory.Contains(job.ResultReference))
            {
                candidateHistory.Add(job.ResultReference);
            }

            var remainingScenes = await dbContext.Scenes
                .CountAsync(s => s.ProjectId == job.ProjectId && string.IsNullOrWhiteSpace(s.VideoAssetId), cancellationToken).ConfigureAwait(false);

            if (remainingScenes == 0)
            {
                var updatedConfig = config with
                {
                    RunState = WorkflowRunState.Completed,
                    UpdatedAtUtc = nowIso
                };
                var cfgKey = GetConfigKey(job.ProjectId);
                var cfgSetting = await dbContext.AppSettings.FirstOrDefaultAsync(s => s.Key == cfgKey, cancellationToken).ConfigureAwait(false);
                if (cfgSetting != null)
                {
                    cfgSetting.Value = JsonSerializer.Serialize(updatedConfig, JsonOptions);
                    cfgSetting.UpdatedAtUtc = nowIso;
                }
            }
        }

        // Cập nhật lại Scene AppSetting chứa candidateHistory mới nhất
        var updatedDetail = new SceneWorkflowDetailDto(
            SceneId: scene.Id,
            ProjectId: scene.ProjectId,
            WorkspaceId: scene.WorkspaceId,
            SequenceOrder: scene.SequenceOrder,
            Name: scene.Name,
            TargetDurationSeconds: Convert.ToInt32(scene.TargetDurationSeconds),
            WorkflowStatus: !string.IsNullOrWhiteSpace(scene.VideoAssetId) ? SceneWorkflowStatus.VideoCompleted :
                            !string.IsNullOrWhiteSpace(scene.ImageAssetId) && !string.IsNullOrWhiteSpace(scene.PromptText) ? SceneWorkflowStatus.ReadyForVideo :
                            !string.IsNullOrWhiteSpace(scene.ImageAssetId) ? SceneWorkflowStatus.PreparingPrompt : SceneWorkflowStatus.PreparingImage,
            ImageStrategy: sceneMeta?.ImageStrategy ?? SceneImageStrategy.ReuseFoundation,
            SourceImageAssetId: scene.ImageAssetId,
            SelectedImageAssetId: scene.ImageAssetId,
            MotionPromptText: scene.PromptText,
            AudioOption: scene.AudioAssetId != null ? AudioOptions.CustomAudio : AudioOptions.None,
            AudioAssetId: scene.AudioAssetId,
            VideoAssetId: scene.VideoAssetId,
            IsStale: sceneMeta?.IsStale ?? false,
            StaleReason: sceneMeta?.StaleReason,
            ActiveJobId: null,
            ActiveJobType: null,
            ActiveJobState: null,
            ActiveJobProgressPercent: null,
            VideoCandidateHistory: candidateHistory,
            Revision: nowIso
        );

        var updatedDetailJson = JsonSerializer.Serialize(updatedDetail, JsonOptions);
        if (sceneSetting == null)
        {
            dbContext.AppSettings.Add(new AppSetting { Key = sceneKey, Value = updatedDetailJson, UpdatedAtUtc = nowIso });
        }
        else
        {
            sceneSetting.Value = updatedDetailJson;
            sceneSetting.UpdatedAtUtc = nowIso;
        }

        // Nếu có continuation intent: commit vào AppSettings trong cùng một transaction (PV-R7)
        AppSetting? intentSetting = null;
        if (continuationIntent != null)
        {
            intentSetting = new AppSetting
            {
                Key = $"workflow.continuation.{continuationIntent.IntentId}",
                Value = JsonSerializer.Serialize(continuationIntent, JsonOptions),
                UpdatedAtUtc = nowIso
            };
            dbContext.AppSettings.Add(intentSetting);
        }

        // ATOMIC TRANSACTION: Commit scene changes + applied marker + continuation intent
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Dispatch continuation intent nếu có
        if (continuationIntent != null && _jobService != null)
        {
            try
            {
                await _jobService.EnqueueAsync(new JobEnqueueRequest(
                    WorkspaceId: continuationIntent.WorkspaceId,
                    ProjectId: continuationIntent.ProjectId,
                    SceneId: continuationIntent.SceneId,
                    TaskType: continuationIntent.TaskType,
                    CustomPromptText: continuationIntent.CustomPromptText,
                    WorkflowRunId: continuationIntent.RunId,
                    WorkflowStepKey: $"{continuationIntent.TaskType.ToLowerInvariant()}.{continuationIntent.SceneId}",
                    MediaSnapshot: continuationIntent.MediaSnapshot
                ), cancellationToken).ConfigureAwait(false);

                // Đã dispatch thành công -> xóa intent
                if (intentSetting != null)
                {
                    dbContext.AppSettings.Remove(intentSetting);
                    await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
                // Nếu dispatch bị lỗi/crash, intent vẫn tồn tại trong DB và sẽ được phục hồi bởi RunWorkflowContinuationSweepAsync
            }
        }

        return true;
    }

    /// <summary>
    /// Quét và tiếp tục thực hiện các ý định chuyển tiếp (Continuation Intents) chưa hoàn tất sau khi ứng dụng crash hoặc khởi động lại (PV-R7).
    /// </summary>
    public async Task<int> RunWorkflowContinuationSweepAsync(CancellationToken cancellationToken = default)
    {
        if (_jobService == null) return 0;

        await using var dbContext = new TiToAIDbContext(_dbOptions);
        var pendingIntentSettings = await dbContext.AppSettings
            .Where(s => s.Key.StartsWith("workflow.continuation."))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (pendingIntentSettings.Count == 0) return 0;

        int resumed = 0;
        foreach (var setting in pendingIntentSettings)
        {
            try
            {
                var intent = JsonSerializer.Deserialize<WorkflowContinuationIntent>(setting.Value, JsonOptions);
                if (intent != null)
                {
                    await _jobService.EnqueueAsync(new JobEnqueueRequest(
                        WorkspaceId: intent.WorkspaceId,
                        ProjectId: intent.ProjectId,
                        SceneId: intent.SceneId,
                        TaskType: intent.TaskType,
                        CustomPromptText: intent.CustomPromptText,
                        WorkflowRunId: intent.RunId,
                        WorkflowStepKey: $"{intent.TaskType.ToLowerInvariant()}.{intent.SceneId}",
                        MediaSnapshot: intent.MediaSnapshot
                    ), cancellationToken).ConfigureAwait(false);

                    dbContext.AppSettings.Remove(setting);
                    resumed++;
                }
            }
            catch
            {
                // Giữ lại intent để thử lại lần sau nếu có lỗi tạm thời
            }
        }

        if (resumed > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return resumed;
    }

    private static IReadOnlyList<ScenePlanItemDto> ExtractScenePlanItems(string resultText, int fallbackCount, int fallbackDuration)
    {
        var cleaned = resultText.Trim();
        if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring(7);
        }
        if (cleaned.StartsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring(3);
        }
        if (cleaned.EndsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring(0, cleaned.Length - 3);
        }
        cleaned = cleaned.Trim();

        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            if (doc.RootElement.TryGetProperty("scenes", out var scenesElement) && scenesElement.ValueKind == JsonValueKind.Array)
            {
                var list = new List<ScenePlanItemDto>();
                int seq = 1;
                foreach (var el in scenesElement.EnumerateArray())
                {
                    string purpose = el.TryGetProperty("purpose", out var p) ? p.GetString() ?? $"Cảnh {seq}" : $"Cảnh {seq}";
                    string name = el.TryGetProperty("name", out var n) ? n.GetString() ?? $"Cảnh {seq}: {purpose}" : $"Cảnh {seq}: {purpose}";
                    string motionPrompt = el.TryGetProperty("motionPrompt", out var m) ? m.GetString() ?? string.Empty : string.Empty;
                    int duration = el.TryGetProperty("targetDurationSeconds", out var d) && d.TryGetInt32(out var dVal) ? dVal : fallbackDuration;

                    list.Add(new ScenePlanItemDto(
                        SequenceOrder: seq++,
                        Name: name,
                        Purpose: purpose,
                        TargetDurationSeconds: duration,
                        ImageStrategy: SceneImageStrategy.ReuseFoundation,
                        MotionPromptDraft: motionPrompt
                    ));
                }
                if (list.Count > 0) return list;
            }
        }
        catch
        {
            // Fallback
        }

        return Array.Empty<ScenePlanItemDto>();
    }

    /// <summary>
    /// Thẩm định và tạo bản xem trước yêu cầu gửi AI writer (Preview AI Prompt) theo CLIP_DURATION_STORYBOARD_PLAN §3.1–3.3.
    /// Dùng chung logic composition với quá trình enqueue thật.
    /// </summary>
    public async Task<PreviewAiPromptResponseData> PreviewAiPromptAsync(
        PreviewAiPromptRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalizedWorkspaceId = NormalizeAndValidateGuid(request.WorkspaceId, nameof(request.WorkspaceId));
        var normalizedProjectId = NormalizeAndValidateGuid(request.ProjectId, nameof(request.ProjectId));

        var config = await GetWorkflowConfigAsync(normalizedWorkspaceId, normalizedProjectId, cancellationToken);
        var composer = new VideoWorkflowPromptComposer(_dbOptions);

        await using var dbContext = new TiToAIDbContext(_dbOptions);
        Scene? targetScene = null;
        if (!string.IsNullOrWhiteSpace(request.SceneId))
        {
            var normalizedSceneId = NormalizeAndValidateGuid(request.SceneId, nameof(request.SceneId));
            targetScene = await dbContext.Scenes
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == normalizedSceneId && s.ProjectId == normalizedProjectId, cancellationToken);
        }

        if (targetScene == null)
        {
            targetScene = await dbContext.Scenes
                .AsNoTracking()
                .Where(s => s.ProjectId == normalizedProjectId && s.WorkspaceId == normalizedWorkspaceId)
                .OrderBy(s => s.SequenceOrder)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (targetScene == null)
        {
            targetScene = new Scene
            {
                Id = "preview-scene-1",
                ProjectId = normalizedProjectId,
                WorkspaceId = normalizedWorkspaceId,
                SequenceOrder = 1,
                Name = "Clip 1",
                TargetDurationSeconds = config.TargetDurationPerSceneSeconds > 0 ? config.TargetDurationPerSceneSeconds : 15,
                ImageAssetId = config.ReadyImageAssetId ?? config.ProductAssetId ?? config.ModelAssetId,
                CreatedAtUtc = DateTimeOffset.UtcNow.ToString("o"),
                UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("o")
            };
        }

        var composition = await composer.ComposeVideoPromptAiRequestAsync(
            normalizedWorkspaceId,
            config,
            targetScene,
            cancellationToken);

        return new PreviewAiPromptResponseData(
            TemplateId: composition.TemplateId,
            TemplateName: composition.TemplateName,
            VersionId: composition.VersionId,
            IsOverride: composition.IsOverride,
            UserBrief: composition.UserBrief,
            ComposedPrompt: composition.ComposedPrompt,
            OutputContractVersion: composition.OutputContractVersion
        );
    }

    private static async Task ValidateAssetBelongsToWorkspaceAsync(
        TiToAIDbContext dbContext,
        string workspaceId,
        string? assetId,
        string assetName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(assetId)) return;

        var asset = await dbContext.Assets
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == assetId, cancellationToken);

        if (asset == null)
        {
            throw new ProjectWorkflowValidationException($"Không tìm thấy tệp {assetName} '{assetId}'.");
        }

        if (asset.WorkspaceId != workspaceId)
        {
            throw new UnauthorizedAccessException($"Tệp {assetName} '{assetId}' không thuộc Không gian làm việc '{workspaceId}'.");
        }
    }

    private static string NormalizeAndValidateGuid(string? id, string paramName)
    {
        if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out var parsedGuid))
        {
            throw new ProjectWorkflowValidationException($"Mã định danh '{paramName}' không phải là GUID hợp lệ.");
        }

        return parsedGuid.ToString("D").ToLowerInvariant();
    }
}
