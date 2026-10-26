using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TiToAI.Contracts.Workspaces;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;

namespace TiToAI.Application.Workspaces;

/// <summary>
/// Dịch vụ nghiệp vụ quản lý Không gian làm việc (Workspace CRUD & Pause Semantics)
/// tuân thủ ranh giới dữ liệu kênh TikTok theo contracts.md v1.0.4 và G2-SCOPE.md.
/// </summary>
public sealed class WorkspaceService
{
    private readonly DbContextOptions<TiToAIDbContext> _dbOptions;
    private readonly string? _baseStoragePath;

    public WorkspaceService(DbContextOptions<TiToAIDbContext> dbOptions, string? baseStoragePath = null)
    {
        _dbOptions = dbOptions ?? throw new ArgumentNullException(nameof(dbOptions));
        _baseStoragePath = baseStoragePath;
    }

    /// <summary>
    /// Lấy danh sách toàn bộ Không gian làm việc (Read-only projection cho Topbar và màn hình quản lý).
    /// </summary>
    public async Task<IReadOnlyList<WorkspaceSummaryDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = new TiToAIDbContext(_dbOptions);

        var list = await dbContext.Workspaces
            .AsNoTracking()
            .OrderBy(w => w.Name)
            .Select(w => new WorkspaceSummaryDto(
                w.Id,
                w.Name,
                w.ChannelHandle,
                w.Topic,
                w.Status))
            .ToListAsync(cancellationToken);

        return list;
    }

    /// <summary>
    /// Lấy thông tin chi tiết của một Không gian làm việc theo ID.
    /// </summary>
    public async Task<WorkspaceDetailDto> GetAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        var normalizedId = NormalizeAndValidateGuid(workspaceId, nameof(workspaceId));

        await using var dbContext = new TiToAIDbContext(_dbOptions);

        var workspace = await dbContext.Workspaces
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == normalizedId, cancellationToken);

        if (workspace is null)
        {
            throw new WorkspaceNotFoundException(normalizedId);
        }

        return new WorkspaceDetailDto(
            workspace.Id,
            workspace.Name,
            workspace.ChannelHandle,
            workspace.Topic,
            workspace.Status,
            workspace.CreatedAtUtc,
            workspace.UpdatedAtUtc);
    }

    /// <summary>
    /// Tạo một Không gian làm việc mới với trạng thái mặc định là "Active".
    /// </summary>
    public async Task<WorkspaceCreateResponseData> CreateAsync(WorkspaceCreateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (name, channelHandle, topic) = ValidateInputFields(request.Name, request.ChannelHandle, request.Topic);

        var newId = Guid.NewGuid().ToString("D").ToLowerInvariant();
        var nowUtc = DateTime.UtcNow.ToString("o");

        var workspace = new Workspace
        {
            Id = newId,
            Name = name,
            ChannelHandle = channelHandle,
            Topic = topic,
            Status = "Active",
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        await using var dbContext = new TiToAIDbContext(_dbOptions);
        dbContext.Workspaces.Add(workspace);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new WorkspaceCreateResponseData(newId);
    }

    /// <summary>
    /// Cập nhật thông tin Không gian làm việc (Tên, Channel handle, Topic).
    /// </summary>
    public async Task<WorkspaceDetailDto> UpdateAsync(WorkspaceUpdateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedId = NormalizeAndValidateGuid(request.WorkspaceId, nameof(request.WorkspaceId));
        var (name, channelHandle, topic) = ValidateInputFields(request.Name, request.ChannelHandle, request.Topic);

        await using var dbContext = new TiToAIDbContext(_dbOptions);

        var workspace = await dbContext.Workspaces.FirstOrDefaultAsync(w => w.Id == normalizedId, cancellationToken);
        if (workspace is null)
        {
            throw new WorkspaceNotFoundException(normalizedId);
        }

        var nowUtc = DateTime.UtcNow.ToString("o");
        workspace.Name = name;
        workspace.ChannelHandle = channelHandle;
        workspace.Topic = topic;
        workspace.UpdatedAtUtc = nowUtc;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new WorkspaceDetailDto(
            workspace.Id,
            workspace.Name,
            workspace.ChannelHandle,
            workspace.Topic,
            workspace.Status,
            workspace.CreatedAtUtc,
            workspace.UpdatedAtUtc);
    }

    /// <summary>
    /// Thay đổi trạng thái điều phối của Không gian làm việc (Active &lt;-&gt; Paused).
    /// Khi Paused, Scheduler sẽ ngừng phân phối job mới nhưng không hủy job đang chạy.
    /// </summary>
    public async Task<WorkspaceSetStatusResponseData> SetStatusAsync(WorkspaceSetStatusRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedId = NormalizeAndValidateGuid(request.WorkspaceId, nameof(request.WorkspaceId));

        if (string.IsNullOrWhiteSpace(request.Status) ||
            (!string.Equals(request.Status, "Active", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(request.Status, "Paused", StringComparison.OrdinalIgnoreCase)))
        {
            throw new WorkspaceValidationException("Trạng thái không hợp lệ. Chỉ chấp nhận 'Active' hoặc 'Paused'.");
        }

        var targetStatus = string.Equals(request.Status, "Active", StringComparison.OrdinalIgnoreCase) ? "Active" : "Paused";

        await using var dbContext = new TiToAIDbContext(_dbOptions);

        var workspace = await dbContext.Workspaces.FirstOrDefaultAsync(w => w.Id == normalizedId, cancellationToken);
        if (workspace is null)
        {
            throw new WorkspaceNotFoundException(normalizedId);
        }

        var nowUtc = DateTime.UtcNow.ToString("o");
        workspace.Status = targetStatus;
        workspace.UpdatedAtUtc = nowUtc;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new WorkspaceSetStatusResponseData(workspace.Id, workspace.Status, workspace.UpdatedAtUtc);
    }

    /// <summary>
    /// Xóa Không gian làm việc với Strict Nonterminal &amp; Unreconciled Side-Effect Guard trong cùng một write transaction serialize.
    /// <para>
    /// <b>Ngữ nghĩa Guard &amp; Serialization (Strict Delete Guard Semantics):</b>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///     <b>Write Lock Serialization (BEGIN IMMEDIATE):</b> Toàn bộ thao tác kiểm tra guard và xóa được thực thi trong một write transaction độc quyền 
    ///     (SQLite IMMEDIATE transaction / Serializable). Cơ chế này serialize với tiến trình Enqueue/Reserve, loại bỏ race condition nơi một job mới 
    ///     có thể được cấp phát giữa lúc kiểm tra và commit.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     <b>Nonterminal Job Guard:</b> Chặn xóa nếu còn bất kỳ Job nào ở các trạng thái chưa kết thúc: <c>Queued</c>, <c>Reserved</c>, <c>Running</c>, hoặc <c>AwaitingUser</c>.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     <b>Conservative Side-Effect Reconciliation Guard:</b> Nếu một Job đã gửi tác dụng phụ ra ngoài provider (<c>SideEffectSubmitted == 1</c>) 
    ///     nhưng chưa hoàn tất đối soát kết quả (kể cả khi trạng thái là <c>Succeeded</c>, <c>Failed</c>, hoặc <c>Canceled</c> mà thiếu <c>ResultReference</c> hợp lệ, 
    ///     hoặc đang ở trạng thái <c>AwaitingUser</c>), hệ thống áp dụng guard bảo thủ: từ chối xóa và ném <see cref="WorkspaceConcurrencyConflictException"/>.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     <b>Safe Asset Detach:</b> Trước khi xóa Workspace (kích hoạt cascade delete của SQLite), cập nhật <c>UPDATE Asset SET project_id = NULL WHERE workspace_id = @id AND project_id IS NOT NULL</c> 
    ///     để bảo toàn ràng buộc <c>ON DELETE NO ACTION</c> giữa <c>Asset</c> và <c>Project</c>.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     <b>Subtree Cleanup:</b> Thư mục lưu trữ vật lý của Workspace chỉ được dọn dẹp sau khi transaction DB đã commit thành công.
    ///     </description>
    ///   </item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="workspaceId">Mã định danh không gian làm việc (GUID chuẩn 36 ký tự).</param>
    /// <param name="cancellationToken">Token hủy thao tác.</param>
    /// <returns>Dữ liệu phản hồi xác nhận xóa không gian làm việc thành công.</returns>
    /// <exception cref="WorkspaceValidationException">Ném ra khi mã định danh không hợp lệ.</exception>
    /// <exception cref="WorkspaceNotFoundException">Ném ra khi không tìm thấy không gian làm việc.</exception>
    /// <exception cref="WorkspaceConcurrencyConflictException">Ném ra khi còn job đang chạy hoặc job có side-effect chưa đối soát.</exception>
    public async Task<WorkspaceDeleteResponseData> DeleteAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        var normalizedId = NormalizeAndValidateGuid(workspaceId, nameof(workspaceId));

        await using var dbContext = new TiToAIDbContext(_dbOptions);

        // Mở kết nối và bắt đầu Write Transaction serialize ngay từ đầu (BEGIN IMMEDIATE trong SQLite)
        // để lập tức acquire write lock, ngăn chặn các luồng khác enqueue/reserve job trong suốt quá trình guard và delete.
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
                var workspace = await dbContext.Workspaces.FirstOrDefaultAsync(w => w.Id == normalizedId, cancellationToken);
                if (workspace is null)
                {
                    throw new WorkspaceNotFoundException(normalizedId);
                }

                // 1. Strict Nonterminal & Conservative Side-Effect Reconciliation Guard trong cùng write transaction:
                // Chặn xóa nếu còn job non-terminal (Queued, Reserved, Running, AwaitingUser)
                // HOẶC job có side effect đã gửi ra ngoài (SideEffectSubmitted == 1) nhưng chưa đối soát hoàn tất
                // (chưa có ResultReference hợp lệ hoặc đang ở AwaitingUser).
                bool hasActiveOrUnreconciledJobs = await dbContext.Jobs.AnyAsync(j =>
                    j.WorkspaceId == normalizedId &&
                    (
                        j.State == "Queued" ||
                        j.State == "Reserved" ||
                        j.State == "Running" ||
                        j.State == "AwaitingUser" ||
                        (j.SideEffectSubmitted == 1 && (j.ResultReference == null || j.ResultReference.Trim() == "" || j.State == "AwaitingUser"))
                    ),
                    cancellationToken);

                if (hasActiveOrUnreconciledJobs)
                {
                    throw new WorkspaceConcurrencyConflictException(
                        "Không thể xóa không gian làm việc trong khi có tác vụ đang chờ, đang chạy hoặc tác vụ có tác dụng phụ (side-effect) chưa đối soát xong.");
                }

                // 2. Safe Detach Asset:
                // Do quan hệ Asset(project_id, workspace_id) mang DeleteBehavior.NoAction,
                // nếu xóa cascade Project trước khi xóa Asset, SQLite sẽ báo lỗi khóa ngoại FK.
                // Do đó, gán project_id = NULL cho toàn bộ Asset của Workspace này có project_id IS NOT NULL.
                await dbContext.Assets
                    .Where(a => a.WorkspaceId == normalizedId && a.ProjectId != null)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.ProjectId, (string?)null), cancellationToken);

                // 3. Xóa Workspace:
                // Cơ chế ON DELETE CASCADE của SQLite sẽ cascade dọn dẹp các thực thể con phụ thuộc.
                dbContext.Workspaces.Remove(workspace);
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
                    // Ưu tiên bảo toàn ngoại lệ gốc
                }
                throw;
            }
        }

        // 4. Dọn dẹp thư mục subtree của Workspace chỉ sau khi transaction DB đã commit thành công
        CleanupWorkspaceSubtree(normalizedId);

        return new WorkspaceDeleteResponseData(normalizedId, true);
    }

    private void CleanupWorkspaceSubtree(string workspaceId)
    {
        if (string.IsNullOrWhiteSpace(_baseStoragePath) || !Directory.Exists(_baseStoragePath))
        {
            return;
        }

        try
        {
            var workspaceDir = Path.Combine(_baseStoragePath, workspaceId);
            var fullWorkspaceDir = Path.GetFullPath(workspaceDir);
            var fullBasePath = Path.GetFullPath(_baseStoragePath);

            // Kiểm tra chống Directory Traversal: Thư mục cần dọn phải là thư mục con nằm trong base storage path
            if (!fullWorkspaceDir.StartsWith(fullBasePath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fullWorkspaceDir, fullBasePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (Directory.Exists(fullWorkspaceDir))
            {
                Directory.Delete(fullWorkspaceDir, recursive: true);
            }
        }
        catch (Exception ex)
        {
            // Lỗi cleanup file không làm hỏng kết quả DB đã commit; log cảnh báo
            System.Diagnostics.Debug.WriteLine($"[WorkspaceService] Cảnh báo dọn dẹp thư mục workspace '{workspaceId}': {ex.Message}");
        }
    }

    private static (string Name, string ChannelHandle, string Topic) ValidateInputFields(string? name, string? channelHandle, string? topic)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new WorkspaceValidationException("Tên không gian làm việc không được để trống.");
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length > 100)
        {
            throw new WorkspaceValidationException("Tên không gian làm việc không được vượt quá 100 ký tự.");
        }

        if (string.IsNullOrWhiteSpace(channelHandle))
        {
            throw new WorkspaceValidationException("Định danh kênh (channelHandle) không được để trống.");
        }

        var trimmedHandle = channelHandle.Trim();
        if (!trimmedHandle.StartsWith('@'))
        {
            trimmedHandle = "@" + trimmedHandle;
        }

        if (trimmedHandle.Length > 100)
        {
            throw new WorkspaceValidationException("Định danh kênh không được vượt quá 100 ký tự.");
        }

        if (string.IsNullOrWhiteSpace(topic))
        {
            throw new WorkspaceValidationException("Chủ đề kênh không được để trống.");
        }

        var trimmedTopic = topic.Trim();
        if (trimmedTopic.Length > 200)
        {
            throw new WorkspaceValidationException("Chủ đề kênh không được vượt quá 200 ký tự.");
        }

        return (trimmedName, trimmedHandle, trimmedTopic);
    }

    private static string NormalizeAndValidateGuid(string? id, string paramName)
    {
        if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out var parsedGuid))
        {
            throw new WorkspaceValidationException($"Mã định danh '{paramName}' không phải là GUID hợp lệ.");
        }

        return parsedGuid.ToString("D").ToLowerInvariant();
    }
}
