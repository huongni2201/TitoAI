using TiToAI.Application.Workspaces;

namespace TiToAI.Application.Accounts;

public class AccountValidationException : Exception
{
    public AccountValidationException(string message) : base(message) { }
    public AccountValidationException(string message, Exception innerException) : base(message, innerException) { }
}

public class AccountNotFoundException : Exception
{
    public AccountNotFoundException(string accountId)
        : base($"Không tìm thấy tài khoản với mã '{accountId}'.") { }
    public AccountNotFoundException(string message, Exception innerException) : base(message, innerException) { }
}

public class AccountConcurrencyConflictException : Exception
{
    public AccountConcurrencyConflictException(string message) : base(message) { }
    public AccountConcurrencyConflictException(string message, Exception innerException) : base(message, innerException) { }
}
