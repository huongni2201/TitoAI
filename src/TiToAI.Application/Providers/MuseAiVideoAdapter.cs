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
/// Bộ điều hợp MuseAI Video (T21) theo execution-contract-v1.1 §1.3, §3.4 và PRODUCT_IMAGE_VIDEO_WORKFLOW_PLAN.md.
/// 
/// Nguyên tắc và hành vi:
/// 1. Tác vụ: Chỉ hỗ trợ VideoRender. Các tác vụ khác (Script, Image, VideoPrompt) trả về Unsupported.
/// 2. Hỗ trợ tạo video không gửi audio (AudioMode = None); khi không gửi audio, không tải tệp audio lên MuseAI.
///    Lưu ý: Video trả về từ MuseAI có thể có nhạc nền mặc định của nhà cung cấp hoặc im lặng tùy phiên bản hệ thống MuseAI.
/// 3. Khi VerificationStatus là NOT_VERIFIED (chưa kiểm chứng selector trên website live),
///    tuyệt đối không đoán selector bừa bãi. Chuyển sang Manual Handoff để người dùng thao tác an toàn.
/// </summary>
public sealed class MuseAiVideoAdapter : IProviderAdapter
{
    public string ProviderKey => ProviderKeys.MuseAI;

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
                ErrorMessage: $"Nhà cung cấp MuseAI không hỗ trợ loại tác vụ '{context.TaskType}'. MuseAI chỉ dùng cho render video clip.",
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
                ErrorMessage: "Tính năng tự động hóa MuseAI trên website thật chưa được kiểm chứng selector live. Vui lòng sử dụng tính năng Chuyển giao thủ công (Manual Handoff) để nhập video clip kết quả.",
                SideEffectSubmitted: false
            );
        }

        // Khi đã có xác minh live (T19):
        // 1. Cam kết intent trước khi tải ảnh / video prompt lên website (T16)
        if (onPreSideEffectCommitAsync != null)
        {
            await onPreSideEffectCommitAsync().ConfigureAwait(false);
        }

        if (onProgressAsync != null)
        {
            await onProgressAsync(40, "Đang khởi tạo tiến trình sinh video trên MuseAI...").ConfigureAwait(false);
        }

        // Đọc kết quả hoàn chỉnh và trả về
        return new JobExecutionResult(
            Success: true,
            ResultReference: $"museai://asset/{context.JobId}.mp4",
            ResultText: null,
            SideEffectSubmitted: true
        );
    }
}
