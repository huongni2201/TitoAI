using System.Text.Json;
using TiToAI.Application.Accounts;
using TiToAI.Application.Workspaces;
using TiToAI.Contracts.Accounts;
using TiToAI.Contracts.Bridge;

namespace TiToAI.Desktop.Bridge.Handlers;

/// <summary>
/// Bridge Handlers tiếp nhận và điều phối các lệnh quản lý liên kết tài khoản (account.*)
/// theo contracts.md v1.0.4 §1.5 B và §1.6 A.
/// </summary>
public sealed class AccountHandlers
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AccountLinkService _accountLinkService;

    public AccountHandlers(AccountLinkService accountLinkService)
    {
        _accountLinkService = accountLinkService ?? throw new ArgumentNullException(nameof(accountLinkService));
    }

    /// <summary>
    /// Đăng ký toàn bộ danh mục lệnh account.* vào BridgeDispatcher và mở Allow-list.
    /// </summary>
    public void Register(IBridgeDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        dispatcher.AllowCommand("account.list_by_workspace");
        dispatcher.AllowCommand("account.link");
        dispatcher.AllowCommand("account.unlink");

        dispatcher.RegisterHandler("account.list_by_workspace", HandleListByWorkspaceAsync);
        dispatcher.RegisterHandler("account.link", HandleLinkAsync);
        dispatcher.RegisterHandler("account.unlink", HandleUnlinkAsync);
    }

    /// <summary>
    /// Hủy đăng ký toàn bộ danh mục lệnh account.* khỏi BridgeDispatcher.
    /// </summary>
    public void Unregister(IBridgeDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        dispatcher.UnregisterHandler("account.list_by_workspace");
        dispatcher.UnregisterHandler("account.link");
        dispatcher.UnregisterHandler("account.unlink");
    }

    public async Task<ResponseEnvelope> HandleListByWorkspaceAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var payload = ParsePayload<AccountListByWorkspaceRequest>(request);
            if (payload is null || string.IsNullOrWhiteSpace(payload.WorkspaceId))
            {
                return ResponseEnvelopeHelper.CreateError(
                    request.Id,
                    "INVALID_ARGUMENT",
                    "Payload thiếu trường 'workspaceId' bắt buộc.",
                    retryable: false);
            }

            var accounts = await _accountLinkService.ListByWorkspaceAsync(payload.WorkspaceId, cancellationToken);
            var data = new AccountListByWorkspaceResponseData(accounts);
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

    public async Task<ResponseEnvelope> HandleLinkAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var payload = ParsePayload<AccountLinkRequest>(request);
            if (payload is null ||
                string.IsNullOrWhiteSpace(payload.WorkspaceId) ||
                string.IsNullOrWhiteSpace(payload.Provider) ||
                string.IsNullOrWhiteSpace(payload.ProfileId))
            {
                return ResponseEnvelopeHelper.CreateError(
                    request.Id,
                    "INVALID_ARGUMENT",
                    "Payload yêu cầu liên kết tài khoản thiếu các trường bắt buộc (workspaceId, provider, profileId).",
                    retryable: false);
            }

            var account = await _accountLinkService.LinkAsync(payload, cancellationToken);
            var data = new AccountLinkResponseData(account);
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

    public async Task<ResponseEnvelope> HandleUnlinkAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var payload = ParsePayload<AccountUnlinkRequest>(request);
            if (payload is null ||
                string.IsNullOrWhiteSpace(payload.WorkspaceId) ||
                string.IsNullOrWhiteSpace(payload.AccountId))
            {
                return ResponseEnvelopeHelper.CreateError(
                    request.Id,
                    "INVALID_ARGUMENT",
                    "Payload yêu cầu hủy liên kết thiếu 'workspaceId' hoặc 'accountId'.",
                    retryable: false);
            }

            var result = await _accountLinkService.UnlinkAsync(payload, cancellationToken);
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
            AccountNotFoundException notFound =>
                ResponseEnvelopeHelper.CreateError(requestId, "ENTITY_NOT_FOUND", notFound.Message, retryable: false),

            WorkspaceNotFoundException wsNotFound =>
                ResponseEnvelopeHelper.CreateError(requestId, "ENTITY_NOT_FOUND", wsNotFound.Message, retryable: false),

            AccountValidationException validation =>
                ResponseEnvelopeHelper.CreateError(requestId, "VALIDATION_FAILED", validation.Message, retryable: false),

            WorkspaceValidationException wsValidation =>
                ResponseEnvelopeHelper.CreateError(requestId, "VALIDATION_FAILED", wsValidation.Message, retryable: false),

            AccountConcurrencyConflictException conflict =>
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
