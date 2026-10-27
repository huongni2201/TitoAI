using System.Text.Json;
using TiToAI.Application.Workspaces;
using TiToAI.Contracts.Bridge;
using TiToAI.Contracts.Workspaces;

namespace TiToAI.Desktop.Bridge.Handlers;

/// <summary>
/// Bridge Handlers tiếp nhận và điều phối các lệnh quản lý không gian làm việc (workspace.*)
/// theo contracts.md v1.0.4 §1.5 B và §1.6 G.
/// </summary>
public sealed class WorkspaceHandlers
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly WorkspaceService _workspaceService;
    private readonly Func<EventEnvelope, Task>? _eventSink;
    private long _eventSequenceCounter;

    public WorkspaceHandlers(
        WorkspaceService workspaceService,
        Func<EventEnvelope, Task>? eventSink = null)
    {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
        _eventSink = eventSink;
    }

    /// <summary>
    /// Đăng ký toàn bộ danh mục lệnh workspace.* vào BridgeDispatcher và mở Allow-list.
    /// </summary>
    public void Register(IBridgeDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        dispatcher.AllowCommand("workspace.list");
        dispatcher.AllowCommand("workspace.get");
        dispatcher.AllowCommand("workspace.create");
        dispatcher.AllowCommand("workspace.update");
        dispatcher.AllowCommand("workspace.delete");
        dispatcher.AllowCommand("workspace.set_status");
        dispatcher.AllowCommand("workspace.pause");
        dispatcher.AllowCommand("workspace.resume");

        dispatcher.RegisterHandler("workspace.list", HandleListAsync);
        dispatcher.RegisterHandler("workspace.get", HandleGetAsync);
        dispatcher.RegisterHandler("workspace.create", HandleCreateAsync);
        dispatcher.RegisterHandler("workspace.update", HandleUpdateAsync);
        dispatcher.RegisterHandler("workspace.delete", HandleDeleteAsync);
        dispatcher.RegisterHandler("workspace.set_status", HandleSetStatusAsync);
        dispatcher.RegisterHandler("workspace.pause", HandlePauseAsync);
        dispatcher.RegisterHandler("workspace.resume", HandleResumeAsync);
    }

    /// <summary>
    /// Hủy đăng ký toàn bộ danh mục lệnh workspace.* khỏi BridgeDispatcher.
    /// </summary>
    public void Unregister(IBridgeDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        dispatcher.UnregisterHandler("workspace.list");
        dispatcher.UnregisterHandler("workspace.get");
        dispatcher.UnregisterHandler("workspace.create");
        dispatcher.UnregisterHandler("workspace.update");
        dispatcher.UnregisterHandler("workspace.delete");
        dispatcher.UnregisterHandler("workspace.set_status");
        dispatcher.UnregisterHandler("workspace.pause");
        dispatcher.UnregisterHandler("workspace.resume");
    }

    public async Task<ResponseEnvelope> HandleListAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var workspaces = await _workspaceService.ListAsync(cancellationToken);
            var data = new TiToAI.Contracts.Workspaces.WorkspaceListResponseData(workspaces);
            return ResponseEnvelopeHelper.CreateSuccess(request.Id, data);
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

    public async Task<ResponseEnvelope> HandleGetAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var payload = ParsePayload<WorkspaceGetRequest>(request);
            if (payload is null || string.IsNullOrWhiteSpace(payload.WorkspaceId))
            {
                return ResponseEnvelopeHelper.CreateError(
                    request.Id,
                    "INVALID_ARGUMENT",
                    "Payload thiếu trường 'workspaceId' bắt buộc.",
                    retryable: false);
            }

            var workspace = await _workspaceService.GetAsync(payload.WorkspaceId, cancellationToken);
            var data = new WorkspaceGetResponseData(workspace);
            return ResponseEnvelopeHelper.CreateSuccess(request.Id, data);
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

    public async Task<ResponseEnvelope> HandleCreateAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var payload = ParsePayload<WorkspaceCreateRequest>(request);
            if (payload is null)
            {
                return ResponseEnvelopeHelper.CreateError(
                    request.Id,
                    "INVALID_ARGUMENT",
                    "Payload yêu cầu tạo không gian làm việc không hợp lệ.",
                    retryable: false);
            }

            var result = await _workspaceService.CreateAsync(payload, cancellationToken);
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

    public async Task<ResponseEnvelope> HandleUpdateAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var payload = ParsePayload<WorkspaceUpdateRequest>(request);
            if (payload is null || string.IsNullOrWhiteSpace(payload.WorkspaceId))
            {
                return ResponseEnvelopeHelper.CreateError(
                    request.Id,
                    "INVALID_ARGUMENT",
                    "Payload yêu cầu cập nhật thiếu trường 'workspaceId'.",
                    retryable: false);
            }

            var detail = await _workspaceService.UpdateAsync(payload, cancellationToken);
            var data = new WorkspaceUpdateResponseData(detail);
            return ResponseEnvelopeHelper.CreateSuccess(request.Id, data);
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

    public async Task<ResponseEnvelope> HandleSetStatusAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var payload = ParsePayload<WorkspaceSetStatusRequest>(request);
            if (payload is null || string.IsNullOrWhiteSpace(payload.WorkspaceId) || string.IsNullOrWhiteSpace(payload.Status))
            {
                return ResponseEnvelopeHelper.CreateError(
                    request.Id,
                    "INVALID_ARGUMENT",
                    "Payload yêu cầu thay đổi trạng thái thiếu 'workspaceId' hoặc 'status'.",
                    retryable: false);
            }

            var result = await _workspaceService.SetStatusAsync(payload, cancellationToken);

            // Phát Event workspace.status_changed đơn phương tới Web UI
            if (_eventSink is not null)
            {
                var seq = Interlocked.Increment(ref _eventSequenceCounter);
                var eventPayload = new WorkspaceStatusChangedPayload(
                    result.WorkspaceId,
                    result.Status,
                    result.UpdatedAtUtc);

                var eventEnvelope = ResponseEnvelopeHelper.CreateEvent(
                    "workspace.status_changed",
                    result.WorkspaceId,
                    seq,
                    eventPayload);

                try
                {
                    await _eventSink(eventEnvelope);
                }
                catch (Exception sinkEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[WorkspaceHandlers] Lỗi gửi event workspace.status_changed: {sinkEx.Message}");
                }
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

    public async Task<ResponseEnvelope> HandleDeleteAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var payload = ParsePayload<WorkspaceDeleteRequest>(request);
            if (payload is null || string.IsNullOrWhiteSpace(payload.WorkspaceId))
            {
                return ResponseEnvelopeHelper.CreateError(
                    request.Id,
                    "INVALID_ARGUMENT",
                    "Payload yêu cầu xóa thiếu 'workspaceId'.",
                    retryable: false);
            }

            var result = await _workspaceService.DeleteAsync(payload.WorkspaceId, cancellationToken);
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

    public async Task<ResponseEnvelope> HandlePauseAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var payload = ParsePayload<WorkspaceGetRequest>(request);
            if (payload is null || string.IsNullOrWhiteSpace(payload.WorkspaceId))
            {
                return ResponseEnvelopeHelper.CreateError(
                    request.Id,
                    "INVALID_ARGUMENT",
                    "Payload yêu cầu tạm dừng workspace thiếu 'workspaceId'.",
                    retryable: false);
            }

            var result = await _workspaceService.SetStatusAsync(
                new WorkspaceSetStatusRequest(payload.WorkspaceId, "Paused"),
                cancellationToken);

            if (_eventSink is not null)
            {
                var seq = Interlocked.Increment(ref _eventSequenceCounter);
                var eventPayload = new WorkspaceStatusChangedPayload(
                    result.WorkspaceId,
                    result.Status,
                    result.UpdatedAtUtc);

                var eventEnvelope = ResponseEnvelopeHelper.CreateEvent(
                    "workspace.status_changed",
                    result.WorkspaceId,
                    seq,
                    eventPayload);

                try
                {
                    await _eventSink(eventEnvelope);
                }
                catch (Exception sinkEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[WorkspaceHandlers] Lỗi gửi event workspace.status_changed: {sinkEx.Message}");
                }
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

    public async Task<ResponseEnvelope> HandleResumeAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var payload = ParsePayload<WorkspaceGetRequest>(request);
            if (payload is null || string.IsNullOrWhiteSpace(payload.WorkspaceId))
            {
                return ResponseEnvelopeHelper.CreateError(
                    request.Id,
                    "INVALID_ARGUMENT",
                    "Payload yêu cầu tiếp tục workspace thiếu 'workspaceId'.",
                    retryable: false);
            }

            var result = await _workspaceService.SetStatusAsync(
                new WorkspaceSetStatusRequest(payload.WorkspaceId, "Active"),
                cancellationToken);

            if (_eventSink is not null)
            {
                var seq = Interlocked.Increment(ref _eventSequenceCounter);
                var eventPayload = new WorkspaceStatusChangedPayload(
                    result.WorkspaceId,
                    result.Status,
                    result.UpdatedAtUtc);

                var eventEnvelope = ResponseEnvelopeHelper.CreateEvent(
                    "workspace.status_changed",
                    result.WorkspaceId,
                    seq,
                    eventPayload);

                try
                {
                    await _eventSink(eventEnvelope);
                }
                catch (Exception sinkEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[WorkspaceHandlers] Lỗi gửi event workspace.status_changed: {sinkEx.Message}");
                }
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

    private static T? ParsePayload<T>(RequestEnvelope request) where T : class
    {
        var element = request.Payload;
        if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        return JsonSerializer.Deserialize<T>(element.GetRawText(), JsonOptions);
    }

    private static ResponseEnvelope MapExceptionToError(Guid requestId, Exception ex)
    {
        return ex switch
        {
            WorkspaceNotFoundException notFound =>
                ResponseEnvelopeHelper.CreateError(requestId, "ENTITY_NOT_FOUND", notFound.Message, retryable: false),

            WorkspaceValidationException validation =>
                ResponseEnvelopeHelper.CreateError(requestId, "VALIDATION_FAILED", validation.Message, retryable: false),

            WorkspaceConcurrencyConflictException conflict =>
                ResponseEnvelopeHelper.CreateError(requestId, "CONCURRENCY_CONFLICT", conflict.Message, retryable: false),

            UnauthorizedWorkspaceException unauthorized =>
                ResponseEnvelopeHelper.CreateError(requestId, "UNAUTHORIZED_WORKSPACE", unauthorized.Message, retryable: false),

            ArgumentException arg =>
                ResponseEnvelopeHelper.CreateError(requestId, "INVALID_ARGUMENT", arg.Message, retryable: false),

            _ =>
                ResponseEnvelopeHelper.CreateError(requestId, "INTERNAL_ERROR", $"Lỗi xử lý nội bộ: {ex.Message}", retryable: false)
        };
    }
}
