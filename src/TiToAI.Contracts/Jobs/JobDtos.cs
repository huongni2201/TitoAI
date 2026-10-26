using System;
using System.Collections.Generic;
using System.Linq;

namespace TiToAI.Contracts.Jobs;

public static class JobTaskType
{
    public const string Script = "Script";
    public const string Image = "Image";
    public const string VideoPrompt = "VideoPrompt";
    public const string VideoRender = "VideoRender";

    public static readonly string[] All = [Script, Image, VideoPrompt, VideoRender];
    public static bool IsValid(string type) => All.Contains(type, StringComparer.OrdinalIgnoreCase);
}

public static class JobState
{
    public const string Queued = "Queued";
    public const string Reserved = "Reserved";
    public const string Running = "Running";
    public const string AwaitingUser = "AwaitingUser";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Canceled = "Canceled";

    public static readonly string[] ActiveStates = [Queued, Reserved, Running, AwaitingUser];
    public static readonly string[] TerminalStates = [Succeeded, Failed, Canceled];
    public static bool IsTerminal(string state) => TerminalStates.Contains(state, StringComparer.OrdinalIgnoreCase);
}

public static class JobReasonCode
{
    public const string LoginRequired = "login_required";
    public const string SessionExpired = "session_expired";
    public const string WrongAccount = "wrong_account";
    public const string AutomationBlocked = "automation_blocked";
    public const string NetworkError = "network_error";
    public const string AuthClosed = "auth_closed";
    public const string ReconcileRequired = "reconcile_required";
    public const string MaxAttemptsExceeded = "max_attempts_exceeded";
    public const string LeaseExpiredMaxAttempts = "lease_expired_max_attempts";
    public const string StorageRootUnavailable = "storage_root_unavailable";
    public const string ProviderRejected = "provider_rejected";
    public const string UserCanceled = "user_canceled";
}

public sealed record JobSummaryDto(
    string Id,
    string WorkspaceId,
    string ProjectId,
    string? SceneId,
    string TaskType,
    string State,
    string? ReasonCode,
    int Attempts,
    int? ProgressPercent,
    string? CurrentStep,
    string CreatedAtUtc
);

public sealed record JobDetailDto(
    string Id,
    string WorkspaceId,
    string ProjectId,
    string? SceneId,
    string TaskType,
    string State,
    string? ReasonCode,
    string ProviderKey,
    string StorageRootId,
    int Attempts,
    int MaxAttempts,
    int SideEffectSubmitted,
    string? ResultReference,
    string? ResultText,
    bool CancelRequested,
    string CreatedAtUtc,
    string UpdatedAtUtc
);

public sealed record JobEnqueueRequest(
    string WorkspaceId,
    string ProjectId,
    string? SceneId,
    string TaskType,
    string? PromptTemplateId = null,
    IReadOnlyDictionary<string, string>? PromptVariables = null,
    string? CustomPromptText = null
);

public sealed record JobEnqueueResponseData(
    string JobId,
    string State,
    string WorkspaceId,
    string TaskType,
    string ProviderKey,
    string StorageRootId,
    string CreatedAtUtc
);

public sealed record JobGetStatusRequest(string WorkspaceId, string JobId);
public sealed record JobGetStatusResponseData(JobDetailDto Job);

public sealed record JobListActiveRequest(string? WorkspaceId = null);
public sealed record JobListActiveResponseData(IReadOnlyList<JobSummaryDto> Jobs);

public sealed record JobCancelRequest(string WorkspaceId, string JobId);
public sealed record JobCancelResponseData(string JobId, string State, bool CancelRequested, string Message, string? PreviousState = null);

public sealed record JobReconcileRequest(
    string WorkspaceId,
    string JobId,
    string Action,
    bool? ConfirmedNotReceived = null,
    string? ResultText = null,
    string? ResultReference = null
);
public sealed record JobReconcileResponseData(
    string JobId,
    string PreviousState,
    string CurrentState,
    string ReconcileResult,
    string? ResultReference
);

public sealed record JobProgressUpdatedPayload(
    string JobId,
    string WorkspaceId,
    string ProjectId,
    string? SceneId,
    string State,
    int ProgressPercent,
    string CurrentStep
);

public sealed record JobStateChangedPayload(
    string JobId,
    string WorkspaceId,
    string? ProjectId,
    string PreviousState,
    string NewState,
    string? ReasonCode,
    string? ResultReference,
    string TimestampUtc
);
