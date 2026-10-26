using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TiToAI.Application.Prompts;
using TiToAI.Application.Prompts.Exceptions;
using TiToAI.Contracts.Bridge;
using TiToAI.Contracts.Prompts;
using TiToAI.Infrastructure.Persistence;

namespace TiToAI.Desktop.Bridge.Handlers;

/// <summary>
/// Bridge Handlers tiếp nhận và điều phối 7 lệnh Prompt Engine qua WebView2 Bridge
/// theo đặc tả contracts.md v1.0.4 và G2-SCOPE §T10.
/// </summary>
public sealed class PromptHandlers
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly DbContextOptions<TiToAIDbContext>? _dbOptions;
    private readonly Func<TiToAIDbContext>? _dbContextFactory;

    public PromptHandlers(DbContextOptions<TiToAIDbContext> dbOptions)
    {
        _dbOptions = dbOptions ?? throw new ArgumentNullException(nameof(dbOptions));
    }

    public PromptHandlers(Func<TiToAIDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    private TiToAIDbContext CreateDbContext()
    {
        if (_dbContextFactory != null)
        {
            return _dbContextFactory();
        }

        if (_dbOptions != null)
        {
            return new TiToAIDbContext(_dbOptions);
        }

        throw new InvalidOperationException("Không có cấu hình khởi tạo TiToAIDbContext.");
    }

    private static bool IsEmptyPayload(JsonElement element)
    {
        return element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;
    }

    /// <summary>
    /// Đăng ký toàn bộ 7 lệnh prompt vào Bridge Dispatcher.
    /// </summary>
    public void Register(IBridgeDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        dispatcher.AllowCommand("prompt.list_templates");
        dispatcher.RegisterHandler("prompt.list_templates", HandleListTemplatesAsync);

        dispatcher.AllowCommand("prompt.get_template");
        dispatcher.RegisterHandler("prompt.get_template", HandleGetTemplateAsync);

        dispatcher.AllowCommand("prompt.list_versions");
        dispatcher.RegisterHandler("prompt.list_versions", HandleListVersionsAsync);

        dispatcher.AllowCommand("prompt.create_version");
        dispatcher.RegisterHandler("prompt.create_version", HandleCreateVersionAsync);

        dispatcher.AllowCommand("prompt.preview");
        dispatcher.RegisterHandler("prompt.preview", HandlePreviewAsync);

        dispatcher.AllowCommand("prompt.rollback_version");
        dispatcher.RegisterHandler("prompt.rollback_version", HandleRollbackVersionAsync);

        dispatcher.AllowCommand("prompt.delete_override");
        dispatcher.RegisterHandler("prompt.delete_override", HandleDeleteOverrideAsync);
    }

    /// <summary>
    /// Hủy đăng ký các lệnh prompt khỏi Bridge Dispatcher.
    /// </summary>
    public void Unregister(IBridgeDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        dispatcher.UnregisterHandler("prompt.list_templates");
        dispatcher.UnregisterHandler("prompt.get_template");
        dispatcher.UnregisterHandler("prompt.list_versions");
        dispatcher.UnregisterHandler("prompt.create_version");
        dispatcher.UnregisterHandler("prompt.preview");
        dispatcher.UnregisterHandler("prompt.rollback_version");
        dispatcher.UnregisterHandler("prompt.delete_override");
    }

    public async Task<ResponseEnvelope> HandleListTemplatesAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            PromptListTemplatesRequest? payload = null;
            if (!IsEmptyPayload(request.Payload))
            {
                payload = request.Payload.Deserialize<PromptListTemplatesRequest>(JsonOptions);
            }

            await using var db = CreateDbContext();
            var service = new PromptTemplateService(db);
            var result = await service.ListTemplatesAsync(payload?.WorkspaceId, cancellationToken);

            return ResponseEnvelopeHelper.CreateSuccess(request.Id, result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PromptSecurityException ex)
        {
            return ResponseEnvelopeHelper.CreateError(request.Id, "UNAUTHORIZED_WORKSPACE", ex.Message);
        }
        catch (PromptValidationException ex)
        {
            return ResponseEnvelopeHelper.CreateError(request.Id, "VALIDATION_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            return ResponseEnvelopeHelper.CreateError(request.Id, "INTERNAL_ERROR", $"Lỗi hệ thống khi lấy danh sách template: {ex.Message}");
        }
    }

    public async Task<ResponseEnvelope> HandleGetTemplateAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            if (IsEmptyPayload(request.Payload))
            {
                return ResponseEnvelopeHelper.CreateError(request.Id, "INVALID_ARGUMENT", "Payload không được để trống cho lệnh prompt.get_template.");
            }

            var payload = request.Payload.Deserialize<PromptGetTemplateRequest>(JsonOptions);
            if (payload == null)
            {
                return ResponseEnvelopeHelper.CreateError(request.Id, "INVALID_ARGUMENT", "Dữ liệu yêu cầu prompt.get_template không hợp lệ.");
            }

            await using var db = CreateDbContext();
            var service = new PromptTemplateService(db);
            var result = await service.GetTemplateAsync(payload.TemplateId, payload.Purpose, payload.WorkspaceId, cancellationToken);

            return ResponseEnvelopeHelper.CreateSuccess(request.Id, result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PromptTemplateNotFoundException ex)
        {
            return ResponseEnvelopeHelper.CreateError(request.Id, "ENTITY_NOT_FOUND", ex.Message);
        }
        catch (PromptSecurityException ex)
        {
            return ResponseEnvelopeHelper.CreateError(request.Id, "UNAUTHORIZED_WORKSPACE", ex.Message);
        }
        catch (PromptValidationException ex)
        {
            return ResponseEnvelopeHelper.CreateError(request.Id, "VALIDATION_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            return ResponseEnvelopeHelper.CreateError(request.Id, "INTERNAL_ERROR", $"Lỗi hệ thống khi lấy template: {ex.Message}");
        }
    }

    public async Task<ResponseEnvelope> HandleListVersionsAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            if (IsEmptyPayload(request.Payload))
            {
                return ResponseEnvelopeHelper.CreateError(request.Id, "INVALID_ARGUMENT", "Payload không được để trống cho lệnh prompt.list_versions.");
            }

            var payload = request.Payload.Deserialize<PromptListVersionsRequest>(JsonOptions);
            if (payload == null || string.IsNullOrWhiteSpace(payload.TemplateId))
            {
                return ResponseEnvelopeHelper.CreateError(request.Id, "INVALID_ARGUMENT", "templateId là bắt buộc cho lệnh prompt.list_versions.");
            }

            await using var db = CreateDbContext();
            var service = new PromptTemplateService(db);
            var result = await service.ListVersionsAsync(payload.TemplateId, workspaceId: null, cancellationToken);

            return ResponseEnvelopeHelper.CreateSuccess(request.Id, result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PromptTemplateNotFoundException ex)
        {
            return ResponseEnvelopeHelper.CreateError(request.Id, "ENTITY_NOT_FOUND", ex.Message);
        }
        catch (PromptSecurityException ex)
        {
            return ResponseEnvelopeHelper.CreateError(request.Id, "UNAUTHORIZED_WORKSPACE", ex.Message);
        }
        catch (Exception ex)
        {
            return ResponseEnvelopeHelper.CreateError(request.Id, "INTERNAL_ERROR", $"Lỗi hệ thống khi lấy danh sách version: {ex.Message}");
        }
    }

    public async Task<ResponseEnvelope> HandleCreateVersionAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            if (IsEmptyPayload(request.Payload))
            {
                return ResponseEnvelopeHelper.CreateError(request.Id, "INVALID_ARGUMENT", "Payload không được để trống cho lệnh prompt.create_version.");
            }

            var payload = request.Payload.Deserialize<PromptCreateVersionRequest>(JsonOptions);
            if (payload == null)
            {
                return ResponseEnvelopeHelper.CreateError(request.Id, "INVALID_ARGUMENT", "Dữ liệu yêu cầu prompt.create_version không hợp lệ.");
            }

            await using var db = CreateDbContext();
            var service = new PromptTemplateService(db);
            var result = await service.CreateVersionAsync(payload, cancellationToken);

            return ResponseEnvelopeHelper.CreateSuccess(request.Id, result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PromptSecurityException ex)
        {
            return ResponseEnvelopeHelper.CreateError(request.Id, "UNAUTHORIZED_WORKSPACE", ex.Message);
        }
        catch (PromptValidationException ex)
        {
            return ResponseEnvelopeHelper.CreateError(request.Id, "VALIDATION_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            return ResponseEnvelopeHelper.CreateError(request.Id, "INTERNAL_ERROR", $"Lỗi hệ thống khi tạo version: {ex.Message}");
        }
    }

    public async Task<ResponseEnvelope> HandlePreviewAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            if (IsEmptyPayload(request.Payload))
            {
                return ResponseEnvelopeHelper.CreateError(request.Id, "INVALID_ARGUMENT", "Payload không được để trống cho lệnh prompt.preview.");
            }

            var payload = request.Payload.Deserialize<PromptPreviewRequest>(JsonOptions);
            if (payload == null)
            {
                return ResponseEnvelopeHelper.CreateError(request.Id, "INVALID_ARGUMENT", "Dữ liệu yêu cầu prompt.preview không hợp lệ.");
            }

            await using var db = CreateDbContext();
            var service = new PromptTemplateService(db);
            var result = await service.PreviewAsync(payload, cancellationToken);

            return ResponseEnvelopeHelper.CreateSuccess(request.Id, result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PromptTemplateNotFoundException ex)
        {
            return ResponseEnvelopeHelper.CreateError(request.Id, "ENTITY_NOT_FOUND", ex.Message);
        }
        catch (PromptSecurityException ex)
        {
            return ResponseEnvelopeHelper.CreateError(request.Id, "UNAUTHORIZED_WORKSPACE", ex.Message);
        }
        catch (PromptValidationException ex)
        {
            return ResponseEnvelopeHelper.CreateError(request.Id, "VALIDATION_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            return ResponseEnvelopeHelper.CreateError(request.Id, "INTERNAL_ERROR", $"Lỗi hệ thống khi render preview: {ex.Message}");
        }
    }

    public async Task<ResponseEnvelope> HandleRollbackVersionAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            if (IsEmptyPayload(request.Payload))
            {
                return ResponseEnvelopeHelper.CreateError(request.Id, "INVALID_ARGUMENT", "Payload không được để trống cho lệnh prompt.rollback_version.");
            }

            var payload = request.Payload.Deserialize<PromptRollbackVersionRequest>(JsonOptions);
            if (payload == null || string.IsNullOrWhiteSpace(payload.TemplateId) || string.IsNullOrWhiteSpace(payload.TargetVersionId))
            {
                return ResponseEnvelopeHelper.CreateError(request.Id, "INVALID_ARGUMENT", "templateId và targetVersionId là bắt buộc cho lệnh prompt.rollback_version.");
            }

            await using var db = CreateDbContext();
            var service = new PromptTemplateService(db);
            var result = await service.RollbackVersionAsync(payload, workspaceId: null, cancellationToken);

            return ResponseEnvelopeHelper.CreateSuccess(request.Id, result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PromptTemplateNotFoundException ex)
        {
            return ResponseEnvelopeHelper.CreateError(request.Id, "ENTITY_NOT_FOUND", ex.Message);
        }
        catch (PromptVersionNotFoundException ex)
        {
            return ResponseEnvelopeHelper.CreateError(request.Id, "ENTITY_NOT_FOUND", ex.Message);
        }
        catch (PromptRollbackException ex)
        {
            return ResponseEnvelopeHelper.CreateError(request.Id, "VALIDATION_FAILED", ex.Message);
        }
        catch (PromptSecurityException ex)
        {
            return ResponseEnvelopeHelper.CreateError(request.Id, "UNAUTHORIZED_WORKSPACE", ex.Message);
        }
        catch (Exception ex)
        {
            return ResponseEnvelopeHelper.CreateError(request.Id, "INTERNAL_ERROR", $"Lỗi hệ thống khi rollback version: {ex.Message}");
        }
    }

    public async Task<ResponseEnvelope> HandleDeleteOverrideAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            if (IsEmptyPayload(request.Payload))
            {
                return ResponseEnvelopeHelper.CreateError(request.Id, "INVALID_ARGUMENT", "Payload không được để trống cho lệnh prompt.delete_override.");
            }

            var payload = request.Payload.Deserialize<PromptDeleteOverrideRequest>(JsonOptions);
            if (payload == null || string.IsNullOrWhiteSpace(payload.TemplateId))
            {
                return ResponseEnvelopeHelper.CreateError(request.Id, "INVALID_ARGUMENT", "templateId là bắt buộc cho lệnh prompt.delete_override.");
            }

            await using var db = CreateDbContext();
            var service = new PromptTemplateService(db);
            var result = await service.DeleteOverrideAsync(payload, workspaceId: null, cancellationToken);

            return ResponseEnvelopeHelper.CreateSuccess(request.Id, result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PromptTemplateNotFoundException ex)
        {
            return ResponseEnvelopeHelper.CreateError(request.Id, "ENTITY_NOT_FOUND", ex.Message);
        }
        catch (PromptOverrideException ex)
        {
            return ResponseEnvelopeHelper.CreateError(request.Id, "VALIDATION_FAILED", ex.Message);
        }
        catch (PromptSecurityException ex)
        {
            return ResponseEnvelopeHelper.CreateError(request.Id, "UNAUTHORIZED_WORKSPACE", ex.Message);
        }
        catch (Exception ex)
        {
            return ResponseEnvelopeHelper.CreateError(request.Id, "INTERNAL_ERROR", $"Lỗi hệ thống khi xóa override: {ex.Message}");
        }
    }
}
