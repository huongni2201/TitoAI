using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TiToAI.Application.Prompts;
using TiToAI.Application.Prompts.Exceptions;
using TiToAI.Application.Prompts.Models;
using TiToAI.Contracts.Content;
using TiToAI.Contracts.Jobs;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;

namespace TiToAI.Application.Content;

/// <summary>
/// Kết quả tổng hợp yêu cầu gửi AI writer (Prompt Composition Result) theo CLIP_DURATION_STORYBOARD_PLAN §3.1–3.3.
/// </summary>
public sealed record VideoPromptCompositionResult(
    string TemplateId,
    string TemplateName,
    string VersionId,
    bool IsOverride,
    string UserBrief,
    string ComposedPrompt,
    int OutputContractVersion
);

/// <summary>
/// Bộ tổng hợp Prompt chuyên dụng cho quy trình sản xuất video từ ảnh.
/// Thực hiện quy tắc composition §3.1: Dùng template đã render -> Nhiệm vụ và ngữ cảnh cảnh -> OUTPUT_CONTRACT.
/// Bảo đảm tương thích template tùy chỉnh, không lặp render nội dung người dùng, báo lỗi rõ ràng nếu thiếu biến bắt buộc.
/// </summary>
public sealed class VideoWorkflowPromptComposer
{
    private readonly DbContextOptions<TiToAIDbContext> _dbOptions;

    public VideoWorkflowPromptComposer(DbContextOptions<TiToAIDbContext> dbOptions)
    {
        _dbOptions = dbOptions ?? throw new ArgumentNullException(nameof(dbOptions));
    }

    /// <summary>
    /// Tổng hợp yêu cầu gửi AI writer viết prompt chuyển động cho một Cảnh (Clip).
    /// </summary>
    public async Task<VideoPromptCompositionResult> ComposeVideoPromptAiRequestAsync(
        string workspaceId,
        ProjectWorkflowConfigDto config,
        Scene scene,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(scene);

        var brief = (config.GoalPrompt ?? string.Empty).Trim();

        await using var dbContext = new TiToAIDbContext(_dbOptions);

        // 1. Resolve template VideoPrompt: Workspace Override -> Global -> Default Instruction
        PromptTemplate? template = null;
        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            template = await dbContext.PromptTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Purpose == JobTaskType.VideoPrompt && t.WorkspaceId == workspaceId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (template == null)
        {
            template = await dbContext.PromptTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Purpose == JobTaskType.VideoPrompt && t.WorkspaceId == null, cancellationToken)
                .ConfigureAwait(false);
        }

        string templateId = template?.Id ?? "default-video-prompt";
        string templateName = template != null ? $"Mẫu {template.Purpose} ({(template.WorkspaceId != null ? "Kênh" : "Toàn cục")})" : "Mẫu Prompt Video Chuẩn";
        string versionId = template?.Id ?? "default";
        bool isOverride = template?.WorkspaceId != null;
        string templateText = template?.TemplateText ?? PromptTemplateService.DefaultVideoPromptInstruction;

        // Frontend renders dynamic fields before saving. Backend uses the resulting text.
        string renderedTemplate = ResolveRenderedTemplate(config.VideoPromptText, templateText, templateName);

        // 3. Ghép 4 phần canonical theo §3.1:
        // [HƯỚNG DẪN TỪ TEMPLATE VIDEO PROMPT ĐÃ CHỌN]
        // [NHIỆM VỤ]
        // [DỮ LIỆU LẦN TẠO]
        // [OUTPUT_CONTRACT]
        int durationSec = Convert.ToInt32(scene.TargetDurationSeconds);
        var sb = new StringBuilder();
        sb.AppendLine("[HƯỚNG DẪN TỪ TEMPLATE VIDEO PROMPT ĐÃ CHỌN]");
        sb.AppendLine(renderedTemplate.Trim());
        sb.AppendLine();
        sb.AppendLine("[NHIỆM VỤ]");
        sb.AppendLine($"Viết prompt chuyển động bằng tiếng Anh (motion prompt) cho clip_{scene.SequenceOrder} (tên cảnh: '{scene.Name}'), không tạo thêm clip. Mô tả các nhịp phân cảnh/shot bên trong thời lượng {durationSec} giây của clip.");
        sb.AppendLine();
        sb.AppendLine("[DỮ LIỆU LẦN TẠO — do app serialize]");
        if (!string.IsNullOrWhiteSpace(brief))
        {
            sb.AppendLine($"USER_BRIEF: \"{EscapeForPromptData(brief)}\"");
        }
        sb.AppendLine($"CLIP_CONTEXT: clip_{scene.SequenceOrder}, tên: \"{scene.Name}\", thời lượng: {durationSec}s.");
        sb.AppendLine($"REFERENCE_IMAGES: SelectedImage={scene.ImageAssetId ?? "None"}.");
        sb.AppendLine($"PROVIDER_CAPABILITIES: thời lượng clip = {durationSec}s, hỗ trợ multi-shot = Không (chuyển động liên tục).");
        sb.AppendLine();
        sb.AppendLine("[OUTPUT_CONTRACT]");
        sb.AppendLine("Chỉ trả về prompt chuyển động camera và hành động nhân vật bằng tiếng Anh cho một clip duy nhất, khớp với thời lượng clip. Các mốc thời gian (nếu có) đóng vai trò hướng dẫn nhịp điệu nội dung trong clip. Không bao gồm mã JSON hay giải thích phụ.");

        return new VideoPromptCompositionResult(
            TemplateId: templateId,
            TemplateName: templateName,
            VersionId: versionId,
            IsOverride: isOverride,
            UserBrief: brief,
            ComposedPrompt: sb.ToString().TrimEnd(),
            OutputContractVersion: 1
        );
    }

    /// <summary>
    /// Tổng hợp yêu cầu gửi AI writer cho bước Script lập danh sách cảnh tự động.
    /// </summary>
    public async Task<VideoPromptCompositionResult> ComposeScriptPlanningAiRequestAsync(
        string workspaceId,
        ProjectWorkflowConfigDto config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var brief = (config.GoalPrompt ?? string.Empty).Trim();

        await using var dbContext = new TiToAIDbContext(_dbOptions);

        // 1. Resolve template Script: Workspace Override -> Global -> Default Instruction
        PromptTemplate? template = null;
        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            template = await dbContext.PromptTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Purpose == JobTaskType.Script && t.WorkspaceId == workspaceId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (template == null)
        {
            template = await dbContext.PromptTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Purpose == JobTaskType.Script && t.WorkspaceId == null, cancellationToken)
                .ConfigureAwait(false);
        }

        string templateId = template?.Id ?? "default-script";
        string templateName = template != null ? $"Mẫu {template.Purpose} ({(template.WorkspaceId != null ? "Kênh" : "Toàn cục")})" : "Mẫu Kịch Bản Chuẩn";
        string versionId = template?.Id ?? "default";
        bool isOverride = template?.WorkspaceId != null;
        string templateText = template?.TemplateText ?? PromptTemplateService.DefaultScriptInstruction;

        string renderedTemplate = ResolveRenderedTemplate(config.ScriptPromptText, templateText, templateName);

        var sb = new StringBuilder();
        sb.AppendLine("[HƯỚNG DẪN TỪ TEMPLATE KỊCH BẢN ĐÃ CHỌN]");
        sb.AppendLine(renderedTemplate.Trim());
        sb.AppendLine();
        sb.AppendLine("[NHIỆM VỤ]");
        sb.AppendLine($"Lập kế hoạch nội dung cho đúng {config.TargetSceneCount} clip video riêng biệt cho mục tiêu được cung cấp, mỗi clip dài {config.TargetDurationPerSceneSeconds} giây. AI tự phân chia các phân cảnh/nhịp quay trong từng clip. Không tạo clip ghép dài.");
        sb.AppendLine();
        sb.AppendLine("[DỮ LIỆU LẦN TẠO — do app serialize]");
        if (!string.IsNullOrWhiteSpace(brief))
        {
            sb.AppendLine($"USER_BRIEF: \"{EscapeForPromptData(brief)}\"");
        }
        sb.AppendLine($"TARGET_CLIPS: {config.TargetSceneCount} clip riêng biệt.");
        sb.AppendLine($"DURATION_PER_CLIP: {config.TargetDurationPerSceneSeconds} giây.");
        sb.AppendLine($"WORKFLOW_MODE: {config.WorkflowMode}.");
        sb.AppendLine();
        sb.AppendLine("[OUTPUT_CONTRACT]");
        sb.AppendLine("Trả về danh sách đúng số lượng clip yêu cầu (Clip 1, Clip 2...). Mỗi clip gồm tên clip, mục tiêu nội dung và nhịp phân cảnh mong muốn bên trong clip.");

        return new VideoPromptCompositionResult(
            TemplateId: templateId,
            TemplateName: templateName,
            VersionId: versionId,
            IsOverride: isOverride,
            UserBrief: brief,
            ComposedPrompt: sb.ToString().TrimEnd(),
            OutputContractVersion: 1
        );
    }

    /// <summary>
    /// Tổng hợp yêu cầu gửi AI writer viết content và prompt video theo schema:
    /// { idea, hook, video_script, video_prompt, tiktok_content }.
    /// </summary>
    public async Task<VideoPromptCompositionResult> ComposeContentAndPromptAiRequestAsync(
        string workspaceId,
        ProjectWorkflowConfigDto config,
        Scene? scene = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        var brief = (config.GoalPrompt ?? string.Empty).Trim();

        var sb = new StringBuilder();
        sb.AppendLine("[VAI TRÒ]");
        sb.AppendLine("Bạn là chuyên gia biên kịch video ngắn (TikTok/Reels/Shorts) và chuyên gia tạo prompt AI video chuyên nghiệp.");
        sb.AppendLine();
        sb.AppendLine("[YÊU CẦU NỘI DUNG / MỤC TIÊU]");
        if (!string.IsNullOrWhiteSpace(brief))
        {
            sb.AppendLine($"USER_BRIEF: \"{EscapeForPromptData(brief)}\"");
        }
        sb.AppendLine($"WORKFLOW_MODE: {config.WorkflowMode}");
        sb.AppendLine($"DURATION: {config.TargetDurationPerSceneSeconds} giây");
        if (scene != null)
        {
            sb.AppendLine($"SCENE_ORDER: {scene.SequenceOrder}");
            sb.AppendLine($"SCENE_NAME: {scene.Name}");
        }
        sb.AppendLine();
        sb.AppendLine("[QUY CHUẨN ĐẦU RA - JSON BẮT BUỘC]");
        sb.AppendLine("Hãy trả về DUY NHẤT một khối mã JSON hợp lệ theo đúng schema sau:");
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"idea\": \"Ý tưởng video cốt lõi ngắn gọn và hấp dẫn\",");
        sb.AppendLine("  \"hook\": \"Câu mở đầu gây tò mò giữ chân người xem trong 3 giây đầu tiên\",");
        sb.AppendLine("  \"video_script\": \"Kịch bản chi tiết từng phân cảnh (mô tả hành động, lời thoại/lồng tiếng, chuyển cảnh)\",");
        sb.AppendLine("  \"video_prompt\": \"Prompt tiếng Anh chi tiết cho mô hình AI video (chuyển động camera, góc máy, ánh sáng, chi tiết trang phục/sản phẩm 4K photorealistic)\",");
        sb.AppendLine("  \"tiktok_content\": \"Tiêu đề video cuốn hút, caption đầy đủ kêu gọi hành động kèm 3-5 hashtag thịnh hành\"");
        sb.AppendLine("}");
        sb.AppendLine("```");

        return new VideoPromptCompositionResult(
            TemplateId: "content-schema-v1",
            TemplateName: "Schema Kịch Bản & Prompt Video",
            VersionId: "v1",
            IsOverride: false,
            UserBrief: brief,
            ComposedPrompt: sb.ToString().TrimEnd(),
            OutputContractVersion: 1
        );
    }

    private static string ResolveRenderedTemplate(string? renderedPromptText, string templateText, string templateName)
    {
        var promptText = string.IsNullOrWhiteSpace(renderedPromptText) ? templateText : renderedPromptText.Trim();
        var (detectedVariables, malformed) = PromptRenderer.ParseTemplate(promptText);
        if (malformed.Count > 0)
        {
            throw new ProjectWorkflowValidationException(
                $"Template '{templateName}' chứa placeholder lỗi cú pháp: {string.Join(", ", malformed)}");
        }

        if (detectedVariables.Count > 0)
        {
            throw new ProjectWorkflowValidationException(
                $"Prompt '{templateName}' còn biến chưa được điền: {string.Join(", ", detectedVariables)}. Vui lòng điền biến trong giao diện rồi lưu lại.");
        }

        return promptText;
    }

    private static string EscapeForPromptData(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        // Escape dấu ngoặc kép để bảo đảm serialization rõ ràng, không làm hỏng cấu trúc prompt
        return text.Replace("\"", "\\\"");
    }
}
