namespace TiToAI.Application.Workspaces;

public class WorkspaceValidationException : Exception
{
    public WorkspaceValidationException(string message) : base(message) { }
    public WorkspaceValidationException(string message, Exception innerException) : base(message, innerException) { }
}

public class WorkspaceNotFoundException : Exception
{
    public WorkspaceNotFoundException(string workspaceId) 
        : base($"Không tìm thấy không gian làm việc với mã '{workspaceId}'.") { }
    public WorkspaceNotFoundException(string message, Exception innerException) : base(message, innerException) { }
}

public class WorkspaceConcurrencyConflictException : Exception
{
    public WorkspaceConcurrencyConflictException(string message) : base(message) { }
    public WorkspaceConcurrencyConflictException(string message, Exception innerException) : base(message, innerException) { }
}

public class UnauthorizedWorkspaceException : Exception
{
    public UnauthorizedWorkspaceException(string message) : base(message) { }
    public UnauthorizedWorkspaceException(string message, Exception innerException) : base(message, innerException) { }
}
