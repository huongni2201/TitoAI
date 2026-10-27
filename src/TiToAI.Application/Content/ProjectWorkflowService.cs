using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TiToAI.Application.Jobs;
using TiToAI.Contracts.Content;
using TiToAI.Contracts.Jobs;
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
    string CreatedAtUtc,
    string? WorkflowStepKey = null
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
public sealed partial class ProjectWorkflowService
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
