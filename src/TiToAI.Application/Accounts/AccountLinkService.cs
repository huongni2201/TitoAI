using Microsoft.EntityFrameworkCore;
using TiToAI.Application.Workspaces;
using TiToAI.Contracts.Accounts;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;

namespace TiToAI.Application.Accounts;

/// <summary>
/// Dịch vụ nghiệp vụ quản lý liên kết tài khoản MuseAI và TikTok theo Workspace (T07b Account Linking)
/// tuân thủ ranh giới dữ liệu kênh và cơ chế xác thực danh tính hồ sơ trình duyệt.
/// </summary>
public sealed class AccountLinkService
{
    private readonly DbContextOptions<TiToAIDbContext> _dbOptions;
    private readonly IProfileIdentityResolver _identityResolver;

    public AccountLinkService(
        DbContextOptions<TiToAIDbContext> dbOptions,
        IProfileIdentityResolver identityResolver)
    {
        _dbOptions = dbOptions ?? throw new ArgumentNullException(nameof(dbOptions));
        _identityResolver = identityResolver ?? throw new ArgumentNullException(nameof(identityResolver));
    }

    /// <summary>
    /// Liệt kê toàn bộ tài khoản đang liên kết (Status != "Unlinked") của một Không gian làm việc.
    /// </summary>
    public async Task<IReadOnlyList<AccountDto>> ListByWorkspaceAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        var normalizedWorkspaceId = NormalizeAndValidateGuid(workspaceId, nameof(workspaceId));

        await using var dbContext = new TiToAIDbContext(_dbOptions);

        var workspaceExists = await dbContext.Workspaces
            .AsNoTracking()
            .AnyAsync(w => w.Id == normalizedWorkspaceId, cancellationToken);

        if (!workspaceExists)
        {
            throw new WorkspaceNotFoundException(normalizedWorkspaceId);
        }

        var accounts = await dbContext.Accounts
            .AsNoTracking()
            .Where(a => a.WorkspaceId == normalizedWorkspaceId && a.Status != "Unlinked")
            .OrderBy(a => a.CreatedAtUtc)
            .Select(a => new AccountDto(
                a.Id,
                a.WorkspaceId,
                a.Provider,
                a.AccountIdentity,
                a.ProfileId,
                a.Status,
                a.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return accounts;
    }

    /// <summary>
    /// Liên kết tài khoản MuseAI hoặc TikTok vào Không gian làm việc sau khi đã qua thẩm định danh tính hồ sơ.
    /// Mỗi Workspace chỉ có tối đa 1 liên kết MuseAI/TikTok hiện hành (Active).
    /// </summary>
    public async Task<AccountDto> LinkAsync(
        AccountLinkRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedWorkspaceId = NormalizeAndValidateGuid(request.WorkspaceId, nameof(request.WorkspaceId));
        var normalizedProvider = NormalizeAndValidateProvider(request.Provider);

        if (string.IsNullOrWhiteSpace(request.ProfileId))
        {
            throw new AccountValidationException("Mã hồ sơ trình duyệt (profileId) không được để trống.");
        }

        var profileId = request.ProfileId.Trim();

        await using var dbContext = new TiToAIDbContext(_dbOptions);

        var workspaceExists = await dbContext.Workspaces
            .AsNoTracking()
            .AnyAsync(w => w.Id == normalizedWorkspaceId, cancellationToken);

        if (!workspaceExists)
        {
            throw new WorkspaceNotFoundException(normalizedWorkspaceId);
        }

        // 1. Thẩm định danh tính hồ sơ qua IProfileIdentityResolver
        var resolution = await _identityResolver.ResolveProfileIdentityAsync(profileId, normalizedProvider, cancellationToken);
        if (!resolution.IsSuccess)
        {
            throw new AccountValidationException(
                resolution.ErrorMessage ?? "Không thể xác thực danh tính hồ sơ trình duyệt.");
        }

        if (string.IsNullOrWhiteSpace(resolution.AccountIdentity) ||
            string.Equals(resolution.SessionStatus, "Unverified", StringComparison.OrdinalIgnoreCase))
        {
            throw new AccountValidationException(
                "Hồ sơ trình duyệt chưa được xác minh (Unverified) hoặc không có danh tính hợp lệ.");
        }

        var accountIdentity = resolution.AccountIdentity.Trim();
        var sessionStatus = string.IsNullOrWhiteSpace(resolution.SessionStatus) ? "Connected" : resolution.SessionStatus;

        // 2. Transaction kiểm tra và tạo liên kết
        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Kiểm tra xem identity này đã được liên kết với một Workspace khác chưa
            var linkedInOtherWorkspace = await dbContext.Accounts
                .AsNoTracking()
                .AnyAsync(a => a.Provider == normalizedProvider &&
                               a.AccountIdentity == accountIdentity &&
                               a.Status != "Unlinked" &&
                               a.WorkspaceId != normalizedWorkspaceId,
                    cancellationToken);

            if (linkedInOtherWorkspace)
            {
                throw new AccountConcurrencyConflictException(
                    $"Tài khoản '{accountIdentity}' của {normalizedProvider} đã được liên kết với một không gian làm việc khác.");
            }

            // Kiểm tra xem Workspace hiện tại đã có liên kết active cho provider này chưa
            var existingActiveInThisWorkspace = await dbContext.Accounts
                .FirstOrDefaultAsync(a => a.WorkspaceId == normalizedWorkspaceId &&
                                          a.Provider == normalizedProvider &&
                                          a.Status != "Unlinked",
                    cancellationToken);

            if (existingActiveInThisWorkspace is not null)
            {
                // Nếu cùng account identity và profileId, cập nhật trạng thái session nếu cần và trả về
                if (string.Equals(existingActiveInThisWorkspace.AccountIdentity, accountIdentity, StringComparison.Ordinal) &&
                    string.Equals(existingActiveInThisWorkspace.ProfileId, profileId, StringComparison.Ordinal))
                {
                    existingActiveInThisWorkspace.Status = sessionStatus;
                    await dbContext.SaveChangesAsync(cancellationToken);
                    await tx.CommitAsync(cancellationToken);

                    return new AccountDto(
                        existingActiveInThisWorkspace.Id,
                        existingActiveInThisWorkspace.WorkspaceId,
                        existingActiveInThisWorkspace.Provider,
                        existingActiveInThisWorkspace.AccountIdentity,
                        existingActiveInThisWorkspace.ProfileId,
                        existingActiveInThisWorkspace.Status,
                        existingActiveInThisWorkspace.CreatedAtUtc);
                }

                // Nếu là tài khoản khác, từ chối vì mỗi Workspace chỉ có 1 liên kết active cho provider
                throw new AccountConcurrencyConflictException(
                    $"Không gian làm việc đã có tài khoản {normalizedProvider} đang hoạt động ('{existingActiveInThisWorkspace.AccountIdentity}'). " +
                    "Vui lòng hủy liên kết tài khoản cũ trước khi liên kết tài khoản mới.");
            }

            // Tạo bản ghi Account mới
            var newId = Guid.NewGuid().ToString("D").ToLowerInvariant();
            var nowUtc = DateTime.UtcNow.ToString("o");

            var newAccount = new Account
            {
                Id = newId,
                WorkspaceId = normalizedWorkspaceId,
                Provider = normalizedProvider,
                AccountIdentity = accountIdentity,
                ProfileId = profileId,
                Status = sessionStatus,
                CreatedAtUtc = nowUtc
            };

            dbContext.Accounts.Add(newAccount);
            await dbContext.SaveChangesAsync(cancellationToken);

            await tx.CommitAsync(cancellationToken);

            return new AccountDto(
                newAccount.Id,
                newAccount.WorkspaceId,
                newAccount.Provider,
                newAccount.AccountIdentity,
                newAccount.ProfileId,
                newAccount.Status,
                newAccount.CreatedAtUtc);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Hủy liên kết tài khoản (Đổi status thành "Unlinked", tuyệt đối không xóa cứng khỏi DB).
    /// Có rào chắn Cross-workspace bảo vệ dữ liệu giữa các kênh.
    /// </summary>
    public async Task<AccountUnlinkResponseData> UnlinkAsync(
        AccountUnlinkRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedWorkspaceId = NormalizeAndValidateGuid(request.WorkspaceId, nameof(request.WorkspaceId));
        var normalizedAccountId = NormalizeAndValidateGuid(request.AccountId, nameof(request.AccountId));

        await using var dbContext = new TiToAIDbContext(_dbOptions);

        var account = await dbContext.Accounts
            .FirstOrDefaultAsync(a => a.Id == normalizedAccountId, cancellationToken);

        if (account is null)
        {
            throw new AccountNotFoundException(normalizedAccountId);
        }

        // Cross-workspace guard: Bảo vệ ranh giới kênh
        if (!string.Equals(account.WorkspaceId, normalizedWorkspaceId, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedWorkspaceException("Tài khoản không thuộc không gian làm việc chỉ định.");
        }

        if (account.Status != "Unlinked")
        {
            account.Status = "Unlinked";
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new AccountUnlinkResponseData(account.Id, "Unlinked");
    }

    private static string NormalizeAndValidateProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new AccountValidationException("Nhà cung cấp tài khoản (provider) không được để trống.");
        }

        var trimmed = provider.Trim();
        if (string.Equals(trimmed, "MuseAI", StringComparison.OrdinalIgnoreCase))
        {
            return "MuseAI";
        }

        if (string.Equals(trimmed, "TikTok", StringComparison.OrdinalIgnoreCase))
        {
            return "TikTok";
        }

        throw new AccountValidationException($"Nhà cung cấp tài khoản '{provider}' không được hỗ trợ. Chỉ hỗ trợ 'MuseAI' hoặc 'TikTok'.");
    }

    private static string NormalizeAndValidateGuid(string? id, string paramName)
    {
        if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out var parsedGuid))
        {
            throw new AccountValidationException($"Mã định danh '{paramName}' không phải là GUID hợp lệ.");
        }

        return parsedGuid.ToString("D").ToLowerInvariant();
    }
}
