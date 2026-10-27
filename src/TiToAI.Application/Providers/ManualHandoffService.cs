using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TiToAI.Application.Jobs;
using TiToAI.Contracts.Jobs;
using TiToAI.Contracts.Providers;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;

namespace TiToAI.Application.Providers;

/// <summary>
/// Dịch vụ Handoff thủ công (Manual Handoff Service) cho tác vụ AI theo execution-contract-v1.1 §4, §5:
/// - Cung cấp prompt snapshot bất biến, cấu hình và hướng dẫn thao tác ra dịch vụ AI bên ngoài (GetHandoffAsync).
/// - Nhập kết quả thủ công (ImportResultAsync) với kiểm định cấu hình chặt chẽ theo từng loại tác vụ (Script, VideoPrompt, Image, VideoRender).
/// - Đảm bảo idempotency an toàn qua ClientRequestId, kiểm soát optimistic concurrency,
///   và tuân thủ quy tắc bảo toàn late result trên các tác vụ đã hủy (Unlinked Asset, giữ nguyên Canceled).
/// </summary>
public sealed class ManualHandoffService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly DbContextOptions<TiToAIDbContext> _dbOptions;
    private readonly Func<string, CancellationToken, Task<bool>>? _onJobSucceeded;
    private readonly Func<DateTimeOffset>? _clock;

    public ManualHandoffService(
        DbContextOptions<TiToAIDbContext> dbOptions,
        Func<string, CancellationToken, Task<bool>>? onJobSucceeded = null,
        Func<DateTimeOffset>? clock = null)
    {
        _dbOptions = dbOptions ?? throw new ArgumentNullException(nameof(dbOptions));
        _onJobSucceeded = onJobSucceeded;
        _clock = clock;
    }

    public ManualHandoffService(
        DbContextOptions<TiToAIDbContext> dbOptions,
        Func<DateTimeOffset>? clock)
        : this(dbOptions, null, clock)
    {
    }

    private DateTimeOffset UtcNow => _clock?.Invoke() ?? DateTimeOffset.UtcNow;

    /// <summary>
    /// Lấy thông tin Handoff cho tác vụ (job.get_handoff):
    /// Trích xuất prompt snapshot, schema mong đợi và hướng dẫn thao tác tương ứng với Provider và TaskType.
    /// </summary>
    public async Task<JobGetHandoffResponseData> GetHandoffAsync(
        JobGetHandoffRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.JobId))
        {
            throw new JobValidationException("Trường 'jobId' không được để trống.", nameof(request.JobId));
        }

        if (string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            throw new JobValidationException("Trường 'workspaceId' không được để trống.", nameof(request.WorkspaceId));
        }

        await using var dbContext = new TiToAIDbContext(_dbOptions);

        var job = await dbContext.Jobs
            .FirstOrDefaultAsync(j => j.Id == request.JobId && j.WorkspaceId == request.WorkspaceId, cancellationToken)
            .ConfigureAwait(false);

        if (job is null)
        {
            throw new JobNotFoundException(request.JobId);
        }

        // Trích xuất prompt sạch từ immutable snapshot
        string promptText = ExtractPromptText(job.PromptSnapshotText);

        // Trích xuất workflowVersion từ config snapshot
        string? workflowVersion = ExtractWorkflowVersion(job.ConfigSnapshotJson);

        // Xác định ExpectedOutputSchema theo loại tác vụ
        string expectedOutputSchema = job.TaskType switch
        {
            JobTaskType.Script => "script_json_v1",
            JobTaskType.VideoPrompt => "text_v1",
            JobTaskType.Image => "image_asset",
            JobTaskType.VideoRender => "video_asset",
            _ => "text_v1"
        };

        // Hướng dẫn người dùng thao tác từng bước
        string instructions = GenerateInstructions(job.TaskType, job.ProviderKey);

        // Kiểm tra xem tác vụ có side-effect chưa được xác nhận giải quyết không
        bool hasUnresolvedSideEffect = job.SideEffectSubmitted == 1
            && string.IsNullOrWhiteSpace(job.ResultReference)
            && string.IsNullOrWhiteSpace(job.ResultText);

        var handoffDto = new JobHandoffDto(
            JobId: job.Id,
            WorkspaceId: job.WorkspaceId,
            ProjectId: job.ProjectId,
            SceneId: job.SceneId,
            TaskType: job.TaskType,
            ProviderKey: job.ProviderKey,
            PromptText: promptText,
            ConfigSnapshotJson: job.ConfigSnapshotJson,
            WorkflowVersion: workflowVersion,
            Revision: job.FencingToken,
            ReasonCode: job.ReasonCode ?? string.Empty,
            Instructions: instructions,
            ExpectedOutputSchema: expectedOutputSchema,
            HasUnresolvedSideEffect: hasUnresolvedSideEffect,
            ResultReference: job.ResultReference
        );

        return new JobGetHandoffResponseData(handoffDto);
    }

    /// <summary>
    /// Nhập kết quả thủ công cho tác vụ (job.import_result) theo execution-contract-v1.1 §4, §5:
    /// - Kiểm tra ranh giới Workspace và kiểm soát xung đột lạc quan (ExpectedRevision).
    /// - Bảo đảm Idempotency qua ClientRequestId: lặp lại request cũ trả lại kết quả trước đó mà không tạo trùng lặp.
    /// - Xác thực nghiêm ngặt cấu trúc kết quả theo từng TaskType.
    /// - Nếu Job đã bị Canceled: lưu kết quả dạng Unlinked Asset (nếu là media), giữ nguyên Canceled.
    /// - Nếu Job chưa hủy: lưu JobResult, giải phóng ActiveSlotOccupancy và chuyển sang Succeeded trong SQLite transaction atomic.
    /// </summary>
    public async Task<JobImportResultResponseData> ImportResultAsync(
        JobImportResultRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.JobId))
        {
            throw new JobValidationException("Trường 'jobId' không được để trống.", nameof(request.JobId));
        }

        if (string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            throw new JobValidationException("Trường 'workspaceId' không được để trống.", nameof(request.WorkspaceId));
        }

        if (string.IsNullOrWhiteSpace(request.ClientRequestId))
        {
            throw new JobValidationException("Trường 'clientRequestId' không được để trống.", nameof(request.ClientRequestId));
        }

        await using var dbContext = new TiToAIDbContext(_dbOptions);

        // 1. Kiểm tra Idempotency thông qua ClientRequestId đã lưu
        string idempotencyKey = $"idempotency:handoff:{request.ClientRequestId.Trim()}";
        var existingSetting = await dbContext.AppSettings
            .FirstOrDefaultAsync(s => s.Key == idempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        if (existingSetting != null)
        {
            try
            {
                var record = JsonSerializer.Deserialize<HandoffIdempotencyRecord>(existingSetting.Value, JsonOptions);
                if (record != null)
                {
                    bool isSamePayload = string.Equals(record.JobId, request.JobId, StringComparison.Ordinal)
                        && string.Equals(record.TextResult, request.TextResult, StringComparison.Ordinal)
                        && string.Equals(record.AssetId, request.AssetId, StringComparison.Ordinal);

                    if (!isSamePayload)
                    {
                        throw new JobConcurrencyConflictException(
                            $"Mã yêu cầu ClientRequestId '{request.ClientRequestId}' đã được sử dụng với dữ liệu đầu vào khác.",
                            request.JobId);
                    }

                    return record.Response with { IsDuplicate = true };
                }
            }
            catch (JobConcurrencyConflictException)
            {
                throw;
            }
            catch
            {
                // Nếu bản ghi lỗi định dạng, bỏ qua và tiếp tục xử lý
            }
        }

        // 2. Kiểm tra tồn tại Job và ranh giới Workspace
        var job = await dbContext.Jobs
            .FirstOrDefaultAsync(j => j.Id == request.JobId && j.WorkspaceId == request.WorkspaceId, cancellationToken)
            .ConfigureAwait(false);

        if (job is null)
        {
            throw new JobNotFoundException(request.JobId);
        }

        // 3. Kiểm tra Optimistic Concurrency Token (FencingToken / Revision)
        if (job.FencingToken != request.ExpectedRevision)
        {
            throw new JobConcurrencyConflictException(
                $"Revision của tác vụ ({job.FencingToken}) không khớp với expectedRevision ({request.ExpectedRevision}).",
                job.Id);
        }

        // 4. Nếu Job đã ở trạng thái kết thúc Succeeded trước đó mà không trùng ClientRequestId -> Xung đột
        if (job.State == JobState.Succeeded)
        {
            throw new JobConcurrencyConflictException(
                $"Tác vụ '{job.Id}' đã hoàn thành thành công trước đó, không thể ghi đè kết quả.",
                job.Id);
        }

        // 5. Xác thực nội dung đầu vào theo loại tác vụ (TaskType)
        string? resultText = null;
        string? resultReference = null;
        string resultId = $"res_{Guid.NewGuid():N}";

        switch (job.TaskType)
        {
            case JobTaskType.Script:
                resultText = ValidateAndFormatScriptResult(request.TextResult);
                break;

            case JobTaskType.VideoPrompt:
                resultText = ValidateAndFormatVideoPromptResult(request.TextResult);
                break;

            case JobTaskType.Image:
                resultReference = await ValidateAndResolveAssetAsync(
                    dbContext,
                    request.AssetId,
                    job.WorkspaceId,
                    "Image",
                    cancellationToken).ConfigureAwait(false);
                break;

            case JobTaskType.VideoRender:
                resultReference = await ValidateAndResolveAssetAsync(
                    dbContext,
                    request.AssetId,
                    job.WorkspaceId,
                    "Video",
                    cancellationToken).ConfigureAwait(false);
                break;

            default:
                if (string.IsNullOrWhiteSpace(request.TextResult) && string.IsNullOrWhiteSpace(request.AssetId))
                {
                    throw new JobValidationException("Vui lòng cung cấp TextResult hoặc AssetId cho tác vụ.", nameof(request.TextResult));
                }
                resultText = request.TextResult;
                resultReference = request.AssetId;
                break;
        }

        // 6. Xử lý tác vụ đã hủy (wasCanceled) vs tác vụ hợp lệ bình thường
        bool wasCanceled = job.State == JobState.Canceled || job.CancelRequested == 1;
        var nowIso = UtcNow.ToString("o");

        if (wasCanceled)
        {
            // Tác vụ đã bị Canceled:
            // - Nếu là media: Đánh dấu Asset tương ứng là "Unlinked" để bảo đảm an toàn dữ liệu
            if (!string.IsNullOrWhiteSpace(resultReference))
            {
                var asset = await dbContext.Assets
                    .FirstOrDefaultAsync(a => a.Id == resultReference, cancellationToken)
                    .ConfigureAwait(false);
                if (asset != null)
                {
                    asset.Status = "Unlinked";
                }
            }

            // - Giữ nguyên trạng thái Canceled, không chuyển sang Succeeded, không áp kết quả vào Scene
            job.State = JobState.Canceled;
            job.CancelRequested = 1;
            job.ReasonCode = JobReasonCode.UserCanceled;
            job.ResultText = resultText;
            job.ResultReference = resultReference;
            job.LeaseExpiresAtUtc = null;
            job.LeaseOwnerId = null;
            job.UpdatedAtUtc = nowIso;
            job.FencingToken += 1;
        }
        else
        {
            // Tác vụ chưa bị Canceled:
            job.State = JobState.Succeeded;
            job.ReasonCode = null;
            job.ResultText = resultText;
            job.ResultReference = resultReference;
            job.LeaseExpiresAtUtc = null;
            job.LeaseOwnerId = null;
            job.UpdatedAtUtc = nowIso;
            job.FencingToken += 1;
        }

        // 7. Giải phóng ActiveSlotOccupancy trong cùng transaction SQLite
        var occupancy = await dbContext.ActiveSlotOccupancies
            .FirstOrDefaultAsync(o => o.JobId == job.Id, cancellationToken)
            .ConfigureAwait(false);

        if (occupancy != null)
        {
            dbContext.ActiveSlotOccupancies.Remove(occupancy);
        }

        var responseData = new JobImportResultResponseData(
            JobId: job.Id,
            State: job.State,
            Revision: job.FencingToken,
            ResultId: resultId,
            AssetId: resultReference,
            IsDuplicate: false,
            WasCanceled: wasCanceled
        );

        // 8. Lưu Idempotency record vào AppSettings để chặn gửi lặp an toàn
        var idempotencyRecord = new HandoffIdempotencyRecord(
            JobId: job.Id,
            TextResult: request.TextResult,
            AssetId: request.AssetId,
            Response: responseData
        );

        dbContext.AppSettings.Add(new AppSetting
        {
            Key = idempotencyKey,
            Value = JsonSerializer.Serialize(idempotencyRecord, JsonOptions),
            UpdatedAtUtc = nowIso
        });

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Kích hoạt áp dụng kết quả và chuyển tiếp quy trình tự động sau khi nhập kết quả thủ công (PV-R2)
        if (!wasCanceled && _onJobSucceeded != null)
        {
            try
            {
                await _onJobSucceeded(job.Id, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Lỗi áp dụng kết quả không làm fail import result đã commit an toàn vào DB
            }
        }

        return responseData;
    }

    /// <summary>
    /// Trích xuất rendered prompt text từ snapshot raw hoặc json object
    /// </summary>
    private static string ExtractPromptText(string? rawSnapshot)
    {
        if (string.IsNullOrWhiteSpace(rawSnapshot))
        {
            return string.Empty;
        }

        string trimmed = rawSnapshot.Trim();
        if (trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.TryGetProperty("renderedText", out var renderedProp) ||
                    doc.RootElement.TryGetProperty("RenderedText", out renderedProp))
                {
                    return renderedProp.GetString() ?? trimmed;
                }
            }
            catch
            {
                // Bỏ qua lỗi parse và trả raw snapshot
            }
        }

        return trimmed;
    }

    /// <summary>
    /// Trích xuất workflowVersion từ configSnapshot json nếu có
    /// </summary>
    private static string? ExtractWorkflowVersion(string? configSnapshotJson)
    {
        if (string.IsNullOrWhiteSpace(configSnapshotJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(configSnapshotJson);
            if (doc.RootElement.TryGetProperty("workflowVersion", out var wfProp) ||
                doc.RootElement.TryGetProperty("WorkflowVersion", out wfProp))
            {
                return wfProp.ToString();
            }
        }
        catch
        {
            // Bỏ qua lỗi parse
        }

        return null;
    }

    /// <summary>
    /// Tạo hướng dẫn thao tác từng bước thân thiện bằng tiếng Việt
    /// </summary>
    private static string GenerateInstructions(string taskType, string providerKey)
    {
        return taskType switch
        {
            JobTaskType.Script =>
                $"Bước 1: Sao chép prompt kịch bản bên dưới.\n" +
                $"Bước 2: Mở dịch vụ AI ({providerKey}), dán prompt và yêu cầu trả về định dạng JSON (gồm tiêu đề và danh sách phân cảnh).\n" +
                $"Bước 3: Sao chép toàn bộ chuỗi JSON kịch bản thu được, dán vào ô bên dưới và nhấn 'Gửi kết quả thủ công'.",

            JobTaskType.VideoPrompt =>
                $"Bước 1: Sao chép nội dung prompt mô tả video bên dưới.\n" +
                $"Bước 2: Dán vào dịch vụ AI ({providerKey}) để tinh chỉnh hoặc tạo prompt chuyển động camera.\n" +
                $"Bước 3: Dán đoạn text kết quả vào ô bên dưới và nhấn 'Gửi kết quả thủ công'.",

            JobTaskType.Image =>
                $"Bước 1: Sao chép prompt tạo hình ảnh bên dưới.\n" +
                $"Bước 2: Tạo ảnh trên dịch vụ AI ({providerKey}) và tải file ảnh thành phẩm về máy tính.\n" +
                $"Bước 3: Thêm file ảnh vào thư viện Workspace của dự án, sau đó nhập Asset ID vào ô bên dưới và nhấn 'Gửi kết quả thủ công'.",

            JobTaskType.VideoRender =>
                $"Bước 1: Sao chép prompt/thông số render video bên dưới.\n" +
                $"Bước 2: Tạo video trên dịch vụ AI ({providerKey}) và tải file video thành phẩm về máy.\n" +
                $"Bước 3: Thêm file video vào thư viện Workspace, sau đó nhập Asset ID vào ô bên dưới và nhấn 'Gửi kết quả thủ công'.",

            _ =>
                $"Bước 1: Sao chép prompt bên dưới.\n" +
                $"Bước 2: Dán vào dịch vụ AI ({providerKey}) và lấy kết quả về.\n" +
                $"Bước 3: Nhập kết quả vào ô bên dưới và nhấn 'Gửi kết quả thủ công'."
        };
    }

    /// <summary>
    /// Xác thực và chuẩn hóa kịch bản Script JSON theo schema script_json_v1
    /// </summary>
    private static string ValidateAndFormatScriptResult(string? textResult)
    {
        if (string.IsNullOrWhiteSpace(textResult))
        {
            throw new JobValidationException("Nội dung kịch bản (TextResult) không được để trống.", nameof(textResult));
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(textResult);
        }
        catch (Exception ex)
        {
            throw new JobValidationException($"Dữ liệu kịch bản không phải là chuỗi JSON hợp lệ: {ex.Message}", nameof(textResult));
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new JobValidationException("Dữ liệu kịch bản phải là một đối tượng JSON (JSON object).", nameof(textResult));
            }

            // Kiểm tra title
            string? title = null;
            if (doc.RootElement.TryGetProperty("title", out var titleProp) ||
                doc.RootElement.TryGetProperty("Title", out titleProp))
            {
                title = titleProp.GetString();
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                throw new JobValidationException("Kịch bản phải có trường 'title' không được để trống.", "title");
            }

            if (title.Length > 200)
            {
                throw new JobValidationException("Tiêu đề kịch bản ('title') không được vượt quá 200 ký tự.", "title");
            }

            // Kiểm tra mảng scenes
            JsonElement scenesProp;
            bool hasScenes = doc.RootElement.TryGetProperty("scenes", out scenesProp) ||
                             doc.RootElement.TryGetProperty("Scenes", out scenesProp);

            if (!hasScenes || scenesProp.ValueKind != JsonValueKind.Array)
            {
                throw new JobValidationException("Kịch bản phải có mảng 'scenes' chứa danh sách các phân cảnh.", "scenes");
            }

            int sceneCount = scenesProp.GetArrayLength();
            if (sceneCount < 1)
            {
                throw new JobValidationException("Mảng 'scenes' phải chứa ít nhất 1 phân cảnh.", "scenes");
            }

            if (sceneCount > 100)
            {
                throw new JobValidationException("Mảng 'scenes' không được vượt quá 100 phân cảnh.", "scenes");
            }

            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            var validatedScenes = new List<object>(sceneCount);

            int expectedOrder = 1;
            foreach (var sceneElem in scenesProp.EnumerateArray())
            {
                if (sceneElem.ValueKind != JsonValueKind.Object)
                {
                    throw new JobValidationException($"Phân cảnh thứ {expectedOrder} không phải là đối tượng JSON.", "scenes");
                }

                // key
                string? key = null;
                if (sceneElem.TryGetProperty("key", out var keyProp) || sceneElem.TryGetProperty("Key", out keyProp))
                {
                    key = keyProp.GetString();
                }
                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new JobValidationException($"Phân cảnh thứ {expectedOrder} thiếu trường 'key' hoặc để trống.", "key");
                }
                if (!seenKeys.Add(key))
                {
                    throw new JobValidationException($"Mã scene key '{key}' bị trùng lặp trong kịch bản.", "key");
                }

                // order
                int order = 0;
                if (sceneElem.TryGetProperty("order", out var orderProp) || sceneElem.TryGetProperty("Order", out orderProp))
                {
                    if (!orderProp.TryGetInt32(out order))
                    {
                        throw new JobValidationException($"Trường 'order' của scene '{key}' phải là số nguyên.", "order");
                    }
                }
                else
                {
                    throw new JobValidationException($"Phân cảnh '{key}' thiếu trường 'order'.", "order");
                }

                if (order != expectedOrder)
                {
                    throw new JobValidationException(
                        $"Phân cảnh thứ {expectedOrder} có 'order' = {order}, yêu cầu phải là {expectedOrder} (thứ tự bắt đầu từ 1 và liên tục).",
                        "order");
                }

                // name
                string? name = null;
                if (sceneElem.TryGetProperty("name", out var nameProp) || sceneElem.TryGetProperty("Name", out nameProp))
                {
                    name = nameProp.GetString();
                }
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new JobValidationException($"Phân cảnh '{key}' thiếu trường 'name' hoặc để trống.", "name");
                }
                if (name.Length > 200)
                {
                    throw new JobValidationException($"Tên phân cảnh '{key}' không được vượt quá 200 ký tự.", "name");
                }

                // description
                string? description = string.Empty;
                if (sceneElem.TryGetProperty("description", out var descProp) || sceneElem.TryGetProperty("Description", out descProp))
                {
                    description = descProp.GetString() ?? string.Empty;
                }
                if (description.Length > 20000)
                {
                    throw new JobValidationException($"Mô tả của phân cảnh '{key}' không được vượt quá 20.000 ký tự.", "description");
                }

                // targetDurationSeconds
                double targetDuration = 0;
                if (sceneElem.TryGetProperty("targetDurationSeconds", out var durProp) || sceneElem.TryGetProperty("TargetDurationSeconds", out durProp))
                {
                    if (!durProp.TryGetDouble(out targetDuration))
                    {
                        throw new JobValidationException($"Trường 'targetDurationSeconds' của scene '{key}' phải là số hợp lệ.", "targetDurationSeconds");
                    }
                }
                else
                {
                    throw new JobValidationException($"Phân cảnh '{key}' thiếu trường 'targetDurationSeconds'.", "targetDurationSeconds");
                }

                if (targetDuration <= 0 || targetDuration > 600)
                {
                    throw new JobValidationException(
                        $"Thời lượng 'targetDurationSeconds' của phân cảnh '{key}' ({targetDuration}s) phải lớn hơn 0 và không quá 600 giây.",
                        "targetDurationSeconds");
                }

                // imagePrompt
                string? imagePrompt = string.Empty;
                if (sceneElem.TryGetProperty("imagePrompt", out var ipProp) || sceneElem.TryGetProperty("ImagePrompt", out ipProp))
                {
                    imagePrompt = ipProp.GetString() ?? string.Empty;
                }
                if (imagePrompt.Length > 20000)
                {
                    throw new JobValidationException($"Prompt hình ảnh của phân cảnh '{key}' không được vượt quá 20.000 ký tự.", "imagePrompt");
                }

                validatedScenes.Add(new
                {
                    key,
                    order,
                    name,
                    description,
                    targetDurationSeconds = targetDuration,
                    imagePrompt
                });

                expectedOrder++;
            }

            var standardizedPayload = new
            {
                schemaVersion = 1,
                title,
                scenes = validatedScenes
            };

            return JsonSerializer.Serialize(standardizedPayload, JsonOptions);
        }
    }

    /// <summary>
    /// Xác thực và chuẩn hóa prompt video theo schema text_v1
    /// </summary>
    private static string ValidateAndFormatVideoPromptResult(string? textResult)
    {
        if (string.IsNullOrWhiteSpace(textResult))
        {
            throw new JobValidationException("Nội dung prompt video (TextResult) không được để trống.", nameof(textResult));
        }

        string trimmed = textResult.Trim();
        if (trimmed.Length > 20000)
        {
            throw new JobValidationException(
                $"Nội dung prompt video vượt quá giới hạn 20.000 ký tự (nhận được {trimmed.Length} ký tự).",
                nameof(textResult));
        }

        var standardizedPayload = new
        {
            schemaVersion = 1,
            text = trimmed
        };

        return JsonSerializer.Serialize(standardizedPayload, JsonOptions);
    }

    /// <summary>
    /// Kiểm tra và phân giải tài sản media (Image hoặc Video) trong cơ sở dữ liệu
    /// </summary>
    private static async Task<string> ValidateAndResolveAssetAsync(
        TiToAIDbContext dbContext,
        string? assetId,
        string workspaceId,
        string expectedAssetType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(assetId))
        {
            throw new JobValidationException($"Tác vụ yêu cầu cung cấp mã định danh tài sản (AssetId).", nameof(assetId));
        }

        var asset = await dbContext.Assets
            .FirstOrDefaultAsync(a => a.Id == assetId, cancellationToken)
            .ConfigureAwait(false);

        if (asset is null)
        {
            throw new JobValidationException($"Không tìm thấy tài sản với mã định danh '{assetId}'.", nameof(assetId));
        }

        if (!string.Equals(asset.WorkspaceId, workspaceId, StringComparison.Ordinal))
        {
            throw new JobValidationException(
                $"Tài sản '{assetId}' thuộc Workspace '{asset.WorkspaceId}', không khớp với Workspace của tác vụ ('{workspaceId}').",
                nameof(assetId));
        }

        if (!string.Equals(asset.AssetType, expectedAssetType, StringComparison.OrdinalIgnoreCase))
        {
            throw new JobValidationException(
                $"Tài sản '{assetId}' có loại là '{asset.AssetType}', nhưng tác vụ yêu cầu loại '{expectedAssetType}'.",
                nameof(assetId));
        }

        return asset.Id;
    }

    private sealed record HandoffIdempotencyRecord(
        string JobId,
        string? TextResult,
        string? AssetId,
        JobImportResultResponseData Response
    );
}
