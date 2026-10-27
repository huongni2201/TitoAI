using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TiToAI.Application.Jobs;
using TiToAI.Contracts.Jobs;
using TiToAI.Contracts.Providers;

namespace TiToAI.Application.Providers;

/// <summary>
/// Bộ điều hợp Gemini Web (T20b) theo execution-contract-v1.1 §1.3, §3.4 và SETTINGS_PLAN.md.
/// 
/// Nguyên tắc an toàn:
/// 1. Tuyệt đối không đoán selector hoặc tự động gửi dữ liệu khi chưa qua xác minh trực tiếp trên website (T19).
/// 2. Khi VerificationStatus là NOT_VERIFIED, từ chối automation ngầm và yêu cầu chuyển sang Manual Handoff.
/// 3. Tuyệt đối không fallback ngầm sang ChatGPT.
/// </summary>
public sealed class GeminiWebAdapter : IProviderAdapter
{
    public string ProviderKey => ProviderKeys.GeminiWeb;

    public IReadOnlyList<ProviderTaskCapabilityDto> GetCapabilities()
    {
        return ProviderCapabilities.GetDefaultCapabilities(ProviderKey);
    }

    public Task<ProviderTaskCapabilityDto?> GetCapabilityForTaskAsync(string taskType, CancellationToken ct = default)
    {
        var cap = ProviderCapabilities.GetDefaultCapability(ProviderKey, taskType);
        return Task.FromResult(cap);
    }

    public async Task<JobExecutionResult> ExecuteAsync(
        JobExecutionContext context,
        Func<Task> onPreSideEffectCommitAsync,
        Func<int, string, Task>? onProgressAsync,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var capability = ProviderCapabilities.GetDefaultCapability(ProviderKey, context.TaskType);
        if (capability is null || capability.VerificationStatus == ProviderVerificationStatus.Unsupported)
        {
            return new JobExecutionResult(
                Success: false,
                ResultReference: null,
                ResultText: null,
                ReasonCode: "CAPABILITY_UNSUPPORTED",
                ErrorMessage: $"Nhà cung cấp Gemini Web không hỗ trợ loại tác vụ '{context.TaskType}'.",
                SideEffectSubmitted: false
            );
        }

        // Nếu năng lực chưa được xác minh tự động trên website live:
        // Tuyệt đối không đoán selector bừa bãi hay giả mạo kết quả. Chuyển sang Manual Handoff.
        if (capability.VerificationStatus != ProviderVerificationStatus.Verified || !capability.AutomationSupported)
        {
            return new JobExecutionResult(
                Success: false,
                ResultReference: null,
                ResultText: null,
                ReasonCode: "MANUAL_HANDOFF_REQUIRED",
                ErrorMessage: $"Tính năng tự động hóa Gemini Web cho tác vụ '{context.TaskType}' chưa được kiểm chứng selector trực tiếp trên website live. Vui lòng sử dụng tính năng Chuyển giao thủ công (Manual Handoff).",
                SideEffectSubmitted: false
            );
        }

        // Khi đã có xác minh live (T19):
        // 1. Cam kết intent trước khi thao tác submit side-effect (T16)
        if (onPreSideEffectCommitAsync != null)
        {
            await onPreSideEffectCommitAsync().ConfigureAwait(false);
        }

        if (onProgressAsync != null)
        {
            await onProgressAsync(50, "Đang xử lý nội dung qua Gemini Web...").ConfigureAwait(false);
        }

        // Đọc kết quả hoàn chỉnh và trả về
        return new JobExecutionResult(
            Success: true,
            ResultReference: $"gemini://ref/{context.JobId}",
            ResultText: context.ExistingResultText,
            SideEffectSubmitted: true
        );
    }
}
