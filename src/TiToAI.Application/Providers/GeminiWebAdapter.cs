using TiToAI.Application.Jobs;
using TiToAI.Contracts.Jobs;
using TiToAI.Contracts.Providers;

namespace TiToAI.Application.Providers;

/// <summary>Năng lực Gemini Web; chặn thực thi khi automation chưa sẵn sàng.</summary>
public sealed class GeminiWebAdapter : IProviderAdapter
{
    public string ProviderKey => ProviderKeys.GeminiWeb;

    public IReadOnlyList<ProviderTaskCapabilityDto> GetCapabilities() =>
        ProviderCapabilities.GetDefaultCapabilities(ProviderKey);

    public Task<ProviderTaskCapabilityDto?> GetCapabilityForTaskAsync(string taskType, CancellationToken ct = default) =>
        Task.FromResult(ProviderCapabilities.GetDefaultCapability(ProviderKey, taskType));

    public Task<JobExecutionResult> ExecuteAsync(
        JobExecutionContext context,
        Func<Task> onPreSideEffectCommitAsync,
        Func<int, string, Task>? onProgressAsync,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var capability = ProviderCapabilities.GetDefaultCapability(ProviderKey, context.TaskType);
        if (capability is null || capability.VerificationStatus == ProviderVerificationStatus.Unsupported)
        {
            return Task.FromResult(new JobExecutionResult(
                Success: false,
                ReasonCode: "CAPABILITY_UNSUPPORTED",
                ErrorMessage: $"Gemini Web không hỗ trợ tác vụ '{context.TaskType}'."));
        }

        return Task.FromResult(new JobExecutionResult(
            Success: false,
            ReasonCode: "automation_blocked",
            ErrorMessage: "Tự động hóa Gemini Web chưa được xác minh. Không thể thực thi cho đến khi bộ điều hợp tự động hóa sẵn sàng."));
    }
}
