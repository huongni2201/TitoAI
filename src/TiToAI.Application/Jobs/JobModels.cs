using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace TiToAI.Application.Jobs;

/// <summary>
/// Tín hiệu đánh thức gửi tới JobScheduler thông qua In-memory Channel
/// khi có sự kiện enqueue mới, unpause workspace hoặc hoàn tất đăng nhập browser.
/// </summary>
public sealed record JobWakeupSignal(
    string Source,
    string? WorkspaceId = null,
    string? JobId = null
);

/// <summary>
/// Giao diện phát tín hiệu đánh thức tới Scheduler.
/// </summary>
public interface IJobWakeupNotifier
{
    void NotifyWakeup(JobWakeupSignal signal);
}

/// <summary>
/// Kết quả chụp bản sao bất biến của các tham số đầu vào khi Enqueue.
/// </summary>
public sealed record JobSnapshotResult(
    string ProviderKey,
    string StorageRootId,
    string? PromptSnapshotText,
    string? ConfigSnapshotJson
);

/// <summary>
/// Ngữ cảnh thực thi cung cấp cho Worker khi tiến hành xử lý một Job.
/// </summary>
public sealed record JobExecutionContext(
    string JobId,
    string WorkspaceId,
    string ProjectId,
    string? SceneId,
    string TaskType,
    string ProviderKey,
    string StorageRootId,
    string? PromptSnapshotText,
    string? ConfigSnapshotJson,
    long FencingToken,
    int Attempts
);

/// <summary>
/// Kết quả trả về sau khi Worker hoàn tất thực thi tác vụ.
/// </summary>
public sealed record JobExecutionResult(
    bool Success,
    string? ResultReference = null,
    string? ResultText = null,
    string? ReasonCode = null,
    string? ErrorMessage = null,
    bool SideEffectSubmitted = false
);

/// <summary>
/// Abstraction điều phối thực thi tác vụ AI.
/// Được thiết kế độc lập với Playwright để hỗ trợ kiểm thử định lượng và cách ly module.
/// </summary>
public interface IJobExecutor
{
    Task<JobExecutionResult> ExecuteAsync(
        JobExecutionContext context,
        Func<Task> onPreSideEffectCommitAsync,
        Func<int, string, Task>? onProgressAsync,
        CancellationToken cancellationToken);
}

/// <summary>
/// Cổng kiểm soát (Gatekeeper) trạng thái trình duyệt và hạn ngạch tab.
/// Scheduler sử dụng giao diện này để tự động bỏ qua Workspace Paused,
/// Profile đang chờ đăng nhập hoặc Role Tab đang bận.
/// </summary>
public interface IBrowserGatekeeper
{
    bool IsDispatchPaused(string profileId);
    bool IsProfileBusyWithLogin(string profileId);
    bool IsRoleActive(string profileId, string role);
    Task<IDisposable?> TryAcquireTabLeaseAsync(string profileId, string role, string jobId, CancellationToken cancellationToken = default);
    void ReleaseTabLease(string profileId, string role, string jobId);
}

/// <summary>
/// Triển khai mặc định của IBrowserGatekeeper dựa trên bộ nhớ trong.
/// Phục vụ điều phối cơ bản và cho phép test can thiệp trạng thái nhanh chóng.
/// </summary>
public class DefaultBrowserGatekeeper : IBrowserGatekeeper
{
    private readonly ConcurrentDictionary<string, bool> _pausedProfiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _loginBusyProfiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _activeTabRoles = new(StringComparer.OrdinalIgnoreCase);

    public void SetDispatchPaused(string profileId, bool paused) => _pausedProfiles[profileId] = paused;
    public void SetProfileBusyWithLogin(string profileId, bool busy) => _loginBusyProfiles[profileId] = busy;

    public bool IsDispatchPaused(string profileId) =>
        _pausedProfiles.TryGetValue(profileId, out var paused) && paused;

    public bool IsProfileBusyWithLogin(string profileId) =>
        _loginBusyProfiles.TryGetValue(profileId, out var busy) && busy;

    public bool IsRoleActive(string profileId, string role) =>
        _activeTabRoles.ContainsKey(MakeKey(profileId, role));

    public Task<IDisposable?> TryAcquireTabLeaseAsync(string profileId, string role, string jobId, CancellationToken cancellationToken = default)
    {
        var key = MakeKey(profileId, role);
        if (_activeTabRoles.TryAdd(key, jobId))
        {
            var lease = new TabLeaseDisposable(() => _activeTabRoles.TryRemove(key, out _));
            return Task.FromResult<IDisposable?>(lease);
        }

        return Task.FromResult<IDisposable?>(null);
    }

    public void ReleaseTabLease(string profileId, string role, string jobId)
    {
        var key = MakeKey(profileId, role);
        _activeTabRoles.TryRemove(key, out _);
    }

    private static string MakeKey(string profileId, string role) => $"{profileId}:{role}";

    private sealed class TabLeaseDisposable : IDisposable
    {
        private Action? _releaseAction;
        public TabLeaseDisposable(Action releaseAction) => _releaseAction = releaseAction;

        public void Dispose()
        {
            Interlocked.Exchange(ref _releaseAction, null)?.Invoke();
        }
    }
}

/// <summary>
/// Executor giả lập có tính chất tất định (Deterministic Fake Job Executor) dành riêng cho test suite.
/// Cho phép lập trình viên mô phỏng chính xác các kịch bản crash, network failure, trễ tiến trình và zombie worker.
/// </summary>
public sealed class DeterministicFakeJobExecutor : IJobExecutor
{
    public TimeSpan ExecutionDelay { get; set; } = TimeSpan.Zero;
    public bool SimulateNetworkErrorBeforeSideEffect { get; set; }
    public bool SimulateCrashBeforeSideEffectCommit { get; set; }
    public bool SimulateCrashAfterSideEffectCommit { get; set; }
    public bool SimulateHangWithoutHeartbeat { get; set; }
    public string? ResultReferenceToReturn { get; set; } = "asset-fake-ref-001";
    public string? ResultTextToReturn { get; set; } = "Script generated successfully by deterministic fake executor.";

    public List<JobExecutionContext> RecordedExecutions { get; } = new();

    public async Task<JobExecutionResult> ExecuteAsync(
        JobExecutionContext context,
        Func<Task> onPreSideEffectCommitAsync,
        Func<int, string, Task>? onProgressAsync,
        CancellationToken cancellationToken)
    {
        lock (RecordedExecutions)
        {
            RecordedExecutions.Add(context);
        }

        if (onProgressAsync is not null)
        {
            await onProgressAsync(10, "Chuẩn bị tham số prompt").ConfigureAwait(false);
        }

        if (SimulateNetworkErrorBeforeSideEffect)
        {
            throw new InvalidOperationException("Mạng bị ngắt kết nối trước khi gửi yêu cầu.");
        }

        if (SimulateCrashBeforeSideEffectCommit)
        {
            throw new ApplicationException("Giả lập sự cố crash ứng dụng trước khi commit cờ side effect.");
        }

        if (ExecutionDelay > TimeSpan.Zero)
        {
            await Task.Delay(ExecutionDelay, cancellationToken).ConfigureAwait(false);
        }

        // CRITICAL BOUNDARY: Commit cờ side_effect_submitted = 1 vào SQLite TRƯỚC KHI thao tác ngoài
        await onPreSideEffectCommitAsync().ConfigureAwait(false);

        if (onProgressAsync is not null)
        {
            await onProgressAsync(50, "Đã gửi yêu cầu AI, đang chờ phản hồi").ConfigureAwait(false);
        }

        if (SimulateCrashAfterSideEffectCommit)
        {
            throw new ApplicationException("Giả lập sự cố crash ứng dụng ngay sau khi commit cờ side effect.");
        }

        if (SimulateHangWithoutHeartbeat)
        {
            // Treo tiến trình và không gửi heartbeat để lease quá hạn
            await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken).ConfigureAwait(false);
        }

        if (onProgressAsync is not null)
        {
            await onProgressAsync(100, "Hoàn tất xử lý tác vụ").ConfigureAwait(false);
        }

        return new JobExecutionResult(
            Success: true,
            ResultReference: ResultReferenceToReturn,
            ResultText: ResultTextToReturn,
            SideEffectSubmitted: true
        );
    }
}
