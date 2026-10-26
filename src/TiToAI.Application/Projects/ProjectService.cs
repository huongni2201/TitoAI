using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TiToAI.Application.Workspaces;
using TiToAI.Contracts.Projects;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;

namespace TiToAI.Application.Projects;

/// <summary>
/// Dịch vụ nghiệp vụ quản lý Dự án (Project CRUD, Safe Detach &amp; Active Job Guard)
/// tuân thủ ranh giới dữ liệu kênh TikTok theo contracts.md v1.0.4 và T08a.
/// </summary>
public sealed class ProjectService
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Draft",
        "InProgress",
        "Completed"
    };

    private readonly DbContextOptions<TiToAIDbContext> _dbOptions;

    public ProjectService(DbContextOptions<TiToAIDbContext> dbOptions)
    {
        _dbOptions = dbOptions ?? throw new ArgumentNullException(nameof(dbOptions));
    }

    /// <summary>
    /// Lấy danh sách Dự án theo Không gian làm việc kèm số lượng Cảnh (sceneCount) được tính toán chính xác.
    /// </summary>
    public async Task<IReadOnlyList<ProjectSummaryDto>> ListByWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
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

        var list = await dbContext.Projects
            .AsNoTracking()
            .Where(p => p.WorkspaceId == normalizedWorkspaceId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .Select(p => new ProjectSummaryDto(
                p.Id,
                p.WorkspaceId,
                p.Name,
                p.Description,
                p.Status,
                dbContext.Scenes.Count(s => s.ProjectId == p.Id && s.WorkspaceId == normalizedWorkspaceId),
                p.CreatedAtUtc,
                p.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return list;
    }

    /// <summary>
    /// Lấy thông tin chi tiết một Dự án theo Workspace và Project ID.
    /// </summary>
    public async Task<ProjectDetailDto> GetAsync(string workspaceId, string projectId, CancellationToken cancellationToken = default)
    {
        var normalizedWorkspaceId = NormalizeAndValidateGuid(workspaceId, nameof(workspaceId));
        var normalizedProjectId = NormalizeAndValidateGuid(projectId, nameof(projectId));

        await using var dbContext = new TiToAIDbContext(_dbOptions);

        var project = await dbContext.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == normalizedProjectId, cancellationToken);

        if (project is null)
        {
            throw new ProjectNotFoundException(normalizedProjectId);
        }

        if (project.WorkspaceId != normalizedWorkspaceId)
        {
            throw new UnauthorizedWorkspaceException($"Dự án '{normalizedProjectId}' không thuộc không gian làm việc '{normalizedWorkspaceId}'.");
        }

        return new ProjectDetailDto(
            project.Id,
            project.WorkspaceId,
            project.Name,
            project.Description,
            project.Status,
            project.CreatedAtUtc,
            project.UpdatedAtUtc);
    }

    /// <summary>
    /// Tạo mới một Dự án thuộc Không gian làm việc với trạng thái ban đầu là "Draft".
    /// </summary>
    public async Task<ProjectCreateResponseData> CreateAsync(ProjectCreateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedWorkspaceId = NormalizeAndValidateGuid(request.WorkspaceId, nameof(request.WorkspaceId));
        var (name, description) = ValidateProjectFields(request.Name, request.Description);

        await using var dbContext = new TiToAIDbContext(_dbOptions);

        var workspaceExists = await dbContext.Workspaces
            .AsNoTracking()
            .AnyAsync(w => w.Id == normalizedWorkspaceId, cancellationToken);

        if (!workspaceExists)
        {
            throw new WorkspaceNotFoundException(normalizedWorkspaceId);
        }

        var newId = Guid.NewGuid().ToString("D").ToLowerInvariant();
        var nowUtc = DateTime.UtcNow.ToString("o");

        var project = new Project
        {
            Id = newId,
            WorkspaceId = normalizedWorkspaceId,
            Name = name,
            Description = description,
            Status = "Draft",
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ProjectCreateResponseData(project.Id);
    }

    /// <summary>
    /// Cập nhật thông tin Dự án (tên, mô tả, trạng thái).
    /// </summary>
    public async Task<ProjectDetailDto> UpdateAsync(ProjectUpdateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedWorkspaceId = NormalizeAndValidateGuid(request.WorkspaceId, nameof(request.WorkspaceId));
        var normalizedProjectId = NormalizeAndValidateGuid(request.ProjectId, nameof(request.ProjectId));

        await using var dbContext = new TiToAIDbContext(_dbOptions);

        var project = await dbContext.Projects
            .FirstOrDefaultAsync(p => p.Id == normalizedProjectId, cancellationToken);

        if (project is null)
        {
            throw new ProjectNotFoundException(normalizedProjectId);
        }

        if (project.WorkspaceId != normalizedWorkspaceId)
        {
            throw new UnauthorizedWorkspaceException($"Dự án '{normalizedProjectId}' không thuộc không gian làm việc '{normalizedWorkspaceId}'.");
        }

        bool isModified = false;

        if (request.Name is not null)
        {
            var trimmedName = request.Name.Trim();
            if (string.IsNullOrWhiteSpace(trimmedName))
            {
                throw new ProjectValidationException("Tên dự án không được để trống.");
            }
            if (trimmedName.Length > 100)
            {
                throw new ProjectValidationException("Tên dự án không được vượt quá 100 ký tự.");
            }
            project.Name = trimmedName;
            isModified = true;
        }

        if (request.Description is not null)
        {
            var trimmedDesc = request.Description.Trim();
            if (trimmedDesc.Length > 500)
            {
                throw new ProjectValidationException("Mô tả dự án không được vượt quá 500 ký tự.");
            }
            project.Description = string.IsNullOrWhiteSpace(trimmedDesc) ? null : trimmedDesc;
            isModified = true;
        }

        if (request.Status is not null)
        {
            var trimmedStatus = request.Status.Trim();
            if (!AllowedStatuses.Contains(trimmedStatus))
            {
                throw new ProjectValidationException($"Trạng thái dự án '{trimmedStatus}' không hợp lệ. Chỉ chấp nhận Draft, InProgress, hoặc Completed.");
            }
            project.Status = trimmedStatus;
            isModified = true;
        }

        if (isModified)
        {
            project.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new ProjectDetailDto(
            project.Id,
            project.WorkspaceId,
            project.Name,
            project.Description,
            project.Status,
            project.CreatedAtUtc,
            project.UpdatedAtUtc);
    }

    /// <summary>
    /// Xóa Dự án với Strict Active Job Guard và Safe Asset Detach trong cùng một write transaction serialize.
    /// </summary>
    public async Task<ProjectDeleteResponseData> DeleteAsync(string workspaceId, string projectId, CancellationToken cancellationToken = default)
    {
        var normalizedWorkspaceId = NormalizeAndValidateGuid(workspaceId, nameof(workspaceId));
        var normalizedProjectId = NormalizeAndValidateGuid(projectId, nameof(projectId));

        await using var dbContext = new TiToAIDbContext(_dbOptions);

        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        var dbConn = dbContext.Database.GetDbConnection();

        IDbContextTransaction transaction;
        if (dbConn is SqliteConnection sqliteConn)
        {
            var sqliteTx = sqliteConn.BeginTransaction(deferred: false);
            transaction = await dbContext.Database.UseTransactionAsync(sqliteTx, cancellationToken)
                ?? throw new InvalidOperationException("Không thể khởi tạo DbContextTransaction từ SqliteTransaction.");
        }
        else
        {
            transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        }

        await using (transaction)
        {
            try
            {
                var project = await dbContext.Projects
                    .FirstOrDefaultAsync(p => p.Id == normalizedProjectId, cancellationToken);

                if (project is null)
                {
                    throw new ProjectNotFoundException(normalizedProjectId);
                }

                if (project.WorkspaceId != normalizedWorkspaceId)
                {
                    throw new UnauthorizedWorkspaceException($"Dự án '{normalizedProjectId}' không thuộc không gian làm việc '{normalizedWorkspaceId}'.");
                }

                // 1. Strict Nonterminal & Unreconciled Side-Effect Job Guard:
                // Chặn xóa nếu bất kỳ cảnh nào thuộc dự án có Job chưa kết thúc (Queued, Reserved, Running, AwaitingUser)
                // hoặc job có SideEffectSubmitted == 1 mà chưa đối soát xong (ResultReference null/rỗng hoặc đang ở AwaitingUser).
                bool hasActiveOrUnreconciledJobs = await (
                    from s in dbContext.Scenes
                    where s.ProjectId == normalizedProjectId && s.WorkspaceId == normalizedWorkspaceId
                    join j in dbContext.Jobs on new { s.Id, s.WorkspaceId } equals new { Id = j.SceneId, j.WorkspaceId }
                    where (
                        j.State == "Queued" ||
                        j.State == "Reserved" ||
                        j.State == "Running" ||
                        j.State == "AwaitingUser" ||
                        (j.SideEffectSubmitted == 1 && (j.ResultReference == null || j.ResultReference.Trim() == "" || j.State == "AwaitingUser"))
                    )
                    select j.Id
                ).AnyAsync(cancellationToken);

                if (hasActiveOrUnreconciledJobs)
                {
                    throw new WorkspaceConcurrencyConflictException(
                        "Không thể xóa dự án trong khi có tác vụ thuộc các cảnh đang chờ, đang chạy hoặc tác vụ có tác dụng phụ (side-effect) chưa đối soát xong.");
                }

                // 2. Safe Asset Detach:
                // Cập nhật UPDATE Asset SET project_id = NULL WHERE project_id = @id AND workspace_id = @workspaceId
                // để giải phóng composite foreign key ON DELETE NO ACTION trước khi cascade xóa Scene.
                await dbContext.Assets
                    .Where(a => a.ProjectId == normalizedProjectId && a.WorkspaceId == normalizedWorkspaceId)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.ProjectId, (string?)null), cancellationToken);

                // 3. Xóa Project:
                // ON DELETE CASCADE của SQLite sẽ tự động xóa các Scene và Job đã kết thúc thuộc Scene của Project.
                dbContext.Projects.Remove(project);
                await dbContext.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                try
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                catch
                {
                    // Giữ nguyên ngoại lệ gốc
                }
                throw;
            }
        }

        return new ProjectDeleteResponseData(normalizedProjectId, true);
    }

    private static (string Name, string? Description) ValidateProjectFields(string? name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ProjectValidationException("Tên dự án không được để trống.");
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length > 100)
        {
            throw new ProjectValidationException("Tên dự án không được vượt quá 100 ký tự.");
        }

        string? trimmedDesc = null;
        if (!string.IsNullOrWhiteSpace(description))
        {
            trimmedDesc = description.Trim();
            if (trimmedDesc.Length > 500)
            {
                throw new ProjectValidationException("Mô tả dự án không được vượt quá 500 ký tự.");
            }
        }

        return (trimmedName, trimmedDesc);
    }

    private static string NormalizeAndValidateGuid(string? id, string paramName)
    {
        if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out var parsedGuid))
        {
            throw new ProjectValidationException($"Mã định danh '{paramName}' không phải là GUID hợp lệ.");
        }

        return parsedGuid.ToString("D").ToLowerInvariant();
    }
}
