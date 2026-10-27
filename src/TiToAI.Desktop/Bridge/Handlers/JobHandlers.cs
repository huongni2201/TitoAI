using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TiToAI.Application.Jobs;
using TiToAI.Application.Providers;
using TiToAI.Contracts.Providers;
using TiToAI.Contracts.Bridge;
using TiToAI.Contracts.Jobs;

namespace TiToAI.Desktop.Bridge.Handlers;

/// <summary>
/// Bridge Handlers tiếp nhận và điều phối 5 lệnh hàng đợi tác vụ bền vững (job.*) qua WebView2 Bridge
/// theo đặc tả contracts.md v1.0.4 và kiến trúc T15.
/// </summary>
public sealed class JobHandlers
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly JobService _jobService;
    private readonly ManualHandoffService? _manualHandoffService;
    private readonly Func<EventEnvelope, Task>? _eventSink;
    private long _eventSequenceCounter;

    public JobHandlers(
        JobService jobService,
        Func<EventEnvelope, Task>? eventSink = null,
        ManualHandoffService? manualHandoffService = null)
    {
        _jobService = jobService ?? throw new ArgumentNullException(nameof(jobService));
        _eventSink = eventSink;
        _manualHandoffService = manualHandoffService;
    }

    private static T? ParsePayload<T>(RequestEnvelope request) where T : class
    {
        var element = request.Payload;
        if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        return JsonSerializer.Deserialize<T>(element.GetRawText(), JsonOptions);
    }

    /// <summary>
    /// Đăng ký toàn bộ 5 lệnh job.* vào Bridge Dispatcher và mở Allow-list.
    /// </summary>
    public void Register(IBridgeDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        dispatcher.AllowCommand("job.enqueue");
        dispatcher.AllowCommand("job.get_status");
        dispatcher.AllowCommand("job.list_active");
        dispatcher.AllowCommand("job.cancel");
        dispatcher.AllowCommand("job.reconcile");
        dispatcher.AllowCommand("job.resume");
        dispatcher.AllowCommand("job.get_handoff");
        dispatcher.AllowCommand("job.import_result");

        dispatcher.RegisterHandler("job.enqueue", HandleEnqueueAsync);
        dispatcher.RegisterHandler("job.get_status", HandleGetStatusAsync);
        dispatcher.RegisterHandler("job.list_active", HandleListActiveAsync);
        dispatcher.RegisterHandler("job.cancel", HandleCancelAsync);
        dispatcher.RegisterHandler("job.reconcile", HandleReconcileAsync);
        dispatcher.RegisterHandler("job.resume", HandleResumeAsync);
        dispatcher.RegisterHandler("job.get_handoff", HandleGetHandoffAsync);
        dispatcher.RegisterHandler("job.import_result", HandleImportResultAsync);
    }

    /// <summary>
    /// Hủy đăng ký danh mục lệnh job.* khỏi Bridge Dispatcher.
    /// </summary>
    public void Unregister(IBridgeDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        dispatcher.UnregisterHandler("job.enqueue");
        dispatcher.UnregisterHandler("job.get_status");
        dispatcher.UnregisterHandler("job.list_active");
        dispatcher.UnregisterHandler("job.cancel");
        dispatcher.UnregisterHandler("job.reconcile");
        dispatcher.UnregisterHandler("job.resume");
        dispatcher.UnregisterHandler("job.get_handoff");
        dispatcher.UnregisterHandler("job.import_result");
    }

    public async Task<ResponseEnvelope> HandleEnqueueAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var payload = ParsePayload<JobEnqueueRequest>(request);
            if (payload is null)
            {
                return ResponseEnvelopeHelper.CreateError(
                    request.Id,
                    "INVALID_ARGUMENT",
                    "Payload yêu cầu xếp hàng tác vụ (job.enqueue) không hợp lệ.",
                    retryable: false);
            }

            var result = await _jobService.EnqueueAsync(payload, cancellationToken).ConfigureAwait(false);

            // Phát Event thông báo trạng thái Queued ban đầu
            await EmitEventAsync(result.WorkspaceId, "job.state_changed", new JobStateChangedPayload(
                JobId: result.JobId,
                WorkspaceId: result.WorkspaceId,
                ProjectId: payload.ProjectId,
                PreviousState: "",
                NewState: JobState.Queued,
                ReasonCode: null,
                ResultReference: null,
                TimestampUtc: result.CreatedAtUtc
            )).ConfigureAwait(false);

            return ResponseEnvelopeHelper.CreateSuccess(request.Id, result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return MapExceptionToError(request.Id, ex);
        }
    }

    public async Task<ResponseEnvelope> HandleGetStatusAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var payload = ParsePayload<JobGetStatusRequest>(request);
            if (payload is null || string.IsNullOrWhiteSpace(payload.JobId) || string.IsNullOrWhiteSpace(payload.WorkspaceId))
            {
                return ResponseEnvelopeHelper.CreateError(
                    request.Id,
                    "INVALID_ARGUMENT",
                    "Payload yêu cầu kiểm tra trạng thái thiếu 'workspaceId' hoặc 'jobId'.",
                    retryable: false);
            }

            var result = await _jobService.GetStatusAsync(payload.WorkspaceId, payload.JobId, cancellationToken).ConfigureAwait(false);
            return ResponseEnvelopeHelper.CreateSuccess(request.Id, result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return MapExceptionToError(request.Id, ex);
        }
    }

    public async Task<ResponseEnvelope> HandleListActiveAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var payload = ParsePayload<JobListActiveRequest>(request);
            var result = await _jobService.ListActiveAsync(payload?.WorkspaceId, cancellationToken).ConfigureAwait(false);
            return ResponseEnvelopeHelper.CreateSuccess(request.Id, result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return MapExceptionToError(request.Id, ex);
        }
    }

    public async Task<ResponseEnvelope> HandleCancelAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var payload = ParsePayload<JobCancelRequest>(request);
            if (payload is null || string.IsNullOrWhiteSpace(payload.JobId) || string.IsNullOrWhiteSpace(payload.WorkspaceId))
            {
                return ResponseEnvelopeHelper.CreateError(
                    request.Id,
                    "INVALID_ARGUMENT",
                    "Payload yêu cầu hủy tác vụ thiếu 'workspaceId' hoặc 'jobId'.",
                    retryable: false);
            }

            var result = await _jobService.CancelAsync(payload.WorkspaceId, payload.JobId, cancellationToken).ConfigureAwait(false);

            if (result.State == JobState.Canceled)
            {
                await EmitEventAsync(payload.WorkspaceId, "job.state_changed", new JobStateChangedPayload(
                    JobId: result.JobId,
                    WorkspaceId: payload.WorkspaceId,
                    ProjectId: null,
                    PreviousState: result.PreviousState ?? JobState.Running,
                    NewState: JobState.Canceled,
                    ReasonCode: JobReasonCode.UserCanceled,
                    ResultReference: null,
                    TimestampUtc: DateTimeOffset.UtcNow.ToString("o")
                )).ConfigureAwait(false);
            }

            return ResponseEnvelopeHelper.CreateSuccess(request.Id, result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return MapExceptionToError(request.Id, ex);
        }
    }

    public async Task<ResponseEnvelope> HandleReconcileAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var payload = ParsePayload<JobReconcileRequest>(request);
            if (payload is null || string.IsNullOrWhiteSpace(payload.JobId) || string.IsNullOrWhiteSpace(payload.WorkspaceId) || string.IsNullOrWhiteSpace(payload.Action))
            {
                return ResponseEnvelopeHelper.CreateError(
                    request.Id,
                    "INVALID_ARGUMENT",
                    "Payload yêu cầu đối soát thiếu 'workspaceId', 'jobId' hoặc 'action'.",
                    retryable: false);
            }

            var result = await _jobService.ReconcileAsync(payload, cancellationToken).ConfigureAwait(false);

            await EmitEventAsync(payload.WorkspaceId, "job.state_changed", new JobStateChangedPayload(
                JobId: result.JobId,
                WorkspaceId: payload.WorkspaceId,
                ProjectId: null,
                PreviousState: result.PreviousState,
                NewState: result.CurrentState,
                ReasonCode: null,
                ResultReference: result.ResultReference,
                TimestampUtc: DateTimeOffset.UtcNow.ToString("o")
            )).ConfigureAwait(false);

            return ResponseEnvelopeHelper.CreateSuccess(request.Id, result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return MapExceptionToError(request.Id, ex);
        }
    }

    public async Task<ResponseEnvelope> HandleResumeAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var payload = ParsePayload<JobResumeRequest>(request);
            if (payload is null || string.IsNullOrWhiteSpace(payload.JobId) || string.IsNullOrWhiteSpace(payload.WorkspaceId))
            {
                return ResponseEnvelopeHelper.CreateError(
                    request.Id,
                    "INVALID_ARGUMENT",
                    "Payload yêu cầu tiếp tục tác vụ thiếu 'workspaceId' hoặc 'jobId'.",
                    retryable: false);
            }

            var result = await _jobService.ResumeAsync(payload.WorkspaceId, payload.JobId, cancellationToken).ConfigureAwait(false);

            await EmitEventAsync(payload.WorkspaceId, "job.state_changed", new JobStateChangedPayload(
                JobId: result.JobId,
                WorkspaceId: payload.WorkspaceId,
                ProjectId: null,
                PreviousState: JobState.AwaitingUser,
                NewState: result.State,
                ReasonCode: null,
                ResultReference: null,
                TimestampUtc: DateTimeOffset.UtcNow.ToString("o")
            )).ConfigureAwait(false);

            return ResponseEnvelopeHelper.CreateSuccess(request.Id, result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return MapExceptionToError(request.Id, ex);
        }
    }

    public async Task<ResponseEnvelope> HandleGetHandoffAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_manualHandoffService is null)
        {
            return ResponseEnvelopeHelper.CreateError(
                request.Id,
                "NOT_IMPLEMENTED",
                "Dịch vụ Manual Handoff chưa được cấu hình.",
                retryable: false);
        }

        try
        {
            var payload = ParsePayload<JobGetHandoffRequest>(request);
            if (payload is null || string.IsNullOrWhiteSpace(payload.JobId))
            {
                return ResponseEnvelopeHelper.CreateError(
                    request.Id,
                    "INVALID_ARGUMENT",
                    "Payload thiếu thông tin 'jobId'.",
                    retryable: false);
            }

            var handoff = await _manualHandoffService.GetHandoffAsync(payload, cancellationToken).ConfigureAwait(false);
            return ResponseEnvelopeHelper.CreateSuccess(request.Id, handoff);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return MapExceptionToError(request.Id, ex);
        }
    }

    public async Task<ResponseEnvelope> HandleImportResultAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_manualHandoffService is null)
        {
            return ResponseEnvelopeHelper.CreateError(
                request.Id,
                "NOT_IMPLEMENTED",
                "Dịch vụ Manual Handoff chưa được cấu hình.",
                retryable: false);
        }

        try
        {
            var payload = ParsePayload<JobImportResultRequest>(request);
            if (payload is null || string.IsNullOrWhiteSpace(payload.JobId))
            {
                return ResponseEnvelopeHelper.CreateError(
                    request.Id,
                    "INVALID_ARGUMENT",
                    "Payload thiếu thông tin 'jobId'.",
                    retryable: false);
            }

            var result = await _manualHandoffService.ImportResultAsync(payload, cancellationToken).ConfigureAwait(false);
            return ResponseEnvelopeHelper.CreateSuccess(request.Id, result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return MapExceptionToError(request.Id, ex);
        }
    }

    private async Task EmitEventAsync<TPayload>(string? workspaceId, string eventName, TPayload payload)
        where TPayload : notnull
    {
        if (_eventSink is null)
        {
            return;
        }

        try
        {
            var seq = Interlocked.Increment(ref _eventSequenceCounter);
            var eventEnvelope = ResponseEnvelopeHelper.CreateEvent(
                eventName,
                workspaceId,
                seq,
                payload);

            await _eventSink(eventEnvelope).ConfigureAwait(false);
        }
        catch
        {
            // Nuốt lỗi phát event không đồng bộ để không chặn luồng chính
        }
    }

    private static ResponseEnvelope MapExceptionToError(Guid requestId, Exception ex)
    {
        return ex switch
        {
            JobNotFoundException notFound =>
                ResponseEnvelopeHelper.CreateError(requestId, notFound.ErrorCode, notFound.Message, retryable: false),

            JobValidationException val =>
                ResponseEnvelopeHelper.CreateError(requestId, val.ErrorCode, val.Message, retryable: false),

            ProviderNotConfiguredException prov =>
                ResponseEnvelopeHelper.CreateError(requestId, prov.ErrorCode, prov.Message, retryable: false),

            StorageRootUnavailableException root =>
                ResponseEnvelopeHelper.CreateError(requestId, root.ErrorCode, root.Message, retryable: false),

            FencingTokenStaleException fence =>
                ResponseEnvelopeHelper.CreateError(requestId, fence.ErrorCode, fence.Message, retryable: false),

            JobConcurrencyConflictException conflict =>
                ResponseEnvelopeHelper.CreateError(requestId, conflict.ErrorCode, conflict.Message, retryable: false),

            ArgumentException arg =>
                ResponseEnvelopeHelper.CreateError(requestId, "INVALID_ARGUMENT", arg.Message, retryable: false),

            _ =>
                ResponseEnvelopeHelper.CreateError(requestId, "INTERNAL_ERROR", $"Lỗi hệ thống: {ex.Message}", retryable: false)
        };
    }
}
