using System.Text.Json;
using TiToAI.Application.Assets;
using TiToAI.Application.Projects;
using TiToAI.Application.Scenes;
using TiToAI.Application.Workspaces;
using TiToAI.Contracts.Assets;
using TiToAI.Contracts.Bridge;

namespace TiToAI.Desktop.Bridge.Handlers;

/// <summary>
/// Bridge Handlers tiếp nhận và điều phối các lệnh quản lý tài nguyên media (asset.*)
/// theo contracts.md v1.0.4 và implementation/T09b-media-assets.md.
/// </summary>
public sealed class AssetHandlers
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AssetService _assetService;

    public AssetHandlers(AssetService assetService)
    {
        _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
    }

    /// <summary>
    /// Đăng ký toàn bộ danh mục lệnh asset.* vào BridgeDispatcher và mở Allow-list.
    /// </summary>
    public void Register(IBridgeDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        dispatcher.AllowCommand("asset.import_audio");
        dispatcher.AllowCommand("asset.select_reference_image");
        dispatcher.AllowCommand("asset.get_metadata");
        dispatcher.AllowCommand("asset.open_folder");

        dispatcher.RegisterHandler("asset.import_audio", HandleImportAudioAsync);
        dispatcher.RegisterHandler("asset.select_reference_image", HandleSelectReferenceImageAsync);
        dispatcher.RegisterHandler("asset.get_metadata", HandleGetMetadataAsync);
        dispatcher.RegisterHandler("asset.open_folder", HandleOpenFolderAsync);
    }

    /// <summary>
    /// Hủy đăng ký danh mục lệnh asset.* khỏi BridgeDispatcher.
    /// </summary>
    public void Unregister(IBridgeDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        dispatcher.UnregisterHandler("asset.import_audio");
        dispatcher.UnregisterHandler("asset.select_reference_image");
        dispatcher.UnregisterHandler("asset.get_metadata");
        dispatcher.UnregisterHandler("asset.open_folder");
    }

    public async Task<ResponseEnvelope> HandleImportAudioAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var payload = ParsePayload<ImportAudioRequest>(request);
            if (payload is null ||
                string.IsNullOrWhiteSpace(payload.WorkspaceId) ||
                string.IsNullOrWhiteSpace(payload.ProjectId) ||
                string.IsNullOrWhiteSpace(payload.SceneId))
            {
                return ResponseEnvelopeHelper.CreateError(
                    request.Id,
                    "INVALID_ARGUMENT",
                    "Payload thiếu các trường bắt buộc (workspaceId, projectId, sceneId).",
                    retryable: false);
            }

            var result = await _assetService.ImportAudioAsync(payload, cancellationToken);
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

    public async Task<ResponseEnvelope> HandleSelectReferenceImageAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var payload = ParsePayload<SelectReferenceImageRequest>(request);
            if (payload is null ||
                string.IsNullOrWhiteSpace(payload.WorkspaceId) ||
                string.IsNullOrWhiteSpace(payload.ProjectId) ||
                string.IsNullOrWhiteSpace(payload.SceneId))
            {
                return ResponseEnvelopeHelper.CreateError(
                    request.Id,
                    "INVALID_ARGUMENT",
                    "Payload thiếu các trường bắt buộc (workspaceId, projectId, sceneId).",
                    retryable: false);
            }

            var result = await _assetService.SelectReferenceImageAsync(payload, cancellationToken);
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

    public async Task<ResponseEnvelope> HandleGetMetadataAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var payload = ParsePayload<GetAssetMetadataRequest>(request);
            if (payload is null ||
                string.IsNullOrWhiteSpace(payload.AssetId) ||
                string.IsNullOrWhiteSpace(payload.WorkspaceId))
            {
                return ResponseEnvelopeHelper.CreateError(
                    request.Id,
                    "INVALID_ARGUMENT",
                    "Payload thiếu trường 'assetId' hoặc 'workspaceId'.",
                    retryable: false);
            }

            var result = await _assetService.GetMetadataAsync(payload, cancellationToken);
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

    public async Task<ResponseEnvelope> HandleOpenFolderAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var payload = ParsePayload<OpenFolderRequest>(request);
            if (payload is null ||
                string.IsNullOrWhiteSpace(payload.AssetId) ||
                string.IsNullOrWhiteSpace(payload.WorkspaceId))
            {
                return ResponseEnvelopeHelper.CreateError(
                    request.Id,
                    "INVALID_ARGUMENT",
                    "Payload thiếu trường 'assetId' hoặc 'workspaceId'.",
                    retryable: false);
            }

            var result = await _assetService.OpenFolderAsync(payload, cancellationToken);
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
            AssetValidationException validation =>
                ResponseEnvelopeHelper.CreateError(requestId, "VALIDATION_FAILED", validation.Message, retryable: false),

            AssetNotFoundException notFound =>
                ResponseEnvelopeHelper.CreateError(requestId, "ENTITY_NOT_FOUND", notFound.Message, retryable: false),

            SceneNotFoundException sceneNotFound =>
                ResponseEnvelopeHelper.CreateError(requestId, "ENTITY_NOT_FOUND", sceneNotFound.Message, retryable: false),

            ProjectNotFoundException prjNotFound =>
                ResponseEnvelopeHelper.CreateError(requestId, "ENTITY_NOT_FOUND", prjNotFound.Message, retryable: false),

            WorkspaceNotFoundException wsNotFound =>
                ResponseEnvelopeHelper.CreateError(requestId, "ENTITY_NOT_FOUND", wsNotFound.Message, retryable: false),

            ArgumentException arg =>
                ResponseEnvelopeHelper.CreateError(requestId, "INVALID_ARGUMENT", arg.Message, retryable: false),

            _ =>
                ResponseEnvelopeHelper.CreateError(requestId, "INTERNAL_ERROR", $"Lỗi xử lý tài nguyên: {ex.Message}", retryable: false)
        };
    }
}
