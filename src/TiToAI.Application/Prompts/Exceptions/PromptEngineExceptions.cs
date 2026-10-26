namespace TiToAI.Application.Prompts.Exceptions;

/// <summary>
/// Ngoại lệ cơ sở cho các lỗi trong Prompt Engine.
/// </summary>
public abstract class PromptEngineException : Exception
{
    protected PromptEngineException(string message) : base(message) { }
    protected PromptEngineException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Ngoại lệ khi không tìm thấy template.
/// </summary>
public class PromptTemplateNotFoundException : PromptEngineException
{
    public string? TemplateId { get; }
    public string? Purpose { get; }
    public string? WorkspaceId { get; }

    public PromptTemplateNotFoundException(string templateId)
        : base($"Không tìm thấy Prompt Template với ID '{templateId}'.")
    {
        TemplateId = templateId;
    }

    public PromptTemplateNotFoundException(string purpose, string? workspaceId)
        : base($"Không tìm thấy Prompt Template cho mục đích '{purpose}' trong phạm vi workspace '{workspaceId ?? "Global"}'.")
    {
        Purpose = purpose;
        WorkspaceId = workspaceId;
    }
}

/// <summary>
/// Ngoại lệ khi không tìm thấy phiên bản prompt hoặc phiên bản không thuộc template.
/// </summary>
public class PromptVersionNotFoundException : PromptEngineException
{
    public string VersionId { get; }
    public string? TemplateId { get; }

    public PromptVersionNotFoundException(string versionId, string? templateId = null)
        : base(templateId != null
            ? $"Không tìm thấy phiên bản '{versionId}' thuộc template '{templateId}'."
            : $"Không tìm thấy phiên bản prompt '{versionId}'.")
    {
        VersionId = versionId;
        TemplateId = templateId;
    }
}

/// <summary>
/// Ngoại lệ khi template text hoặc biến số vi phạm quy tắc thẩm định cú pháp hoặc dữ liệu.
/// </summary>
public class PromptValidationException : PromptEngineException
{
    public IReadOnlyList<string> Errors { get; }

    public PromptValidationException(string message, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        Errors = errors ?? Array.Empty<string>();
    }
}

/// <summary>
/// Ngoại lệ khi thao tác vi phạm ranh giới workspace (truy cập hoặc override chéo kênh).
/// </summary>
public class PromptSecurityException : PromptEngineException
{
    public string? TargetWorkspaceId { get; }
    public string? CurrentWorkspaceId { get; }

    public PromptSecurityException(string message, string? targetWorkspaceId = null, string? currentWorkspaceId = null)
        : base(message)
    {
        TargetWorkspaceId = targetWorkspaceId;
        CurrentWorkspaceId = currentWorkspaceId;
    }
}

/// <summary>
/// Ngoại lệ khi rollback thất bại (ví dụ version không thuộc template).
/// </summary>
public class PromptRollbackException : PromptEngineException
{
    public string TemplateId { get; }
    public string TargetVersionId { get; }

    public PromptRollbackException(string templateId, string targetVersionId, string message)
        : base(message)
    {
        TemplateId = templateId;
        TargetVersionId = targetVersionId;
    }
}

/// <summary>
/// Ngoại lệ khi thao tác override không hợp lệ (ví dụ xóa override trên global template).
/// </summary>
public class PromptOverrideException : PromptEngineException
{
    public string TemplateId { get; }

    public PromptOverrideException(string templateId, string message)
        : base(message)
    {
        TemplateId = templateId;
    }
}
