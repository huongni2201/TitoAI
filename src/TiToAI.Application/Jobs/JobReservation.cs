using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TiToAI.Contracts.Jobs;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;

namespace TiToAI.Application.Jobs;

/// <summary>
/// Kết quả phản hồi của một nỗ lực cấp quyền thuê độc quyền (Reservation)
/// kèm theo ExecutionBinding bất biến theo execution-contract-v1.1 §3.1.
/// </summary>
public sealed record JobReservationResult(
    bool Acquired,
    Job? Job,
    ExecutionBinding? Binding = null,
    string? Reason = null
)
{
    public static JobReservationResult Success(Job job, ExecutionBinding? binding = null) => new(true, job, binding);
    public static JobReservationResult Failure(string reason) => new(false, null, null, reason);
}

/// <summary>
/// Quản lý việc cấp phát độc quyền (Atomic Reservation), thời hạn thuê (Lease),
/// nhịp tim định kỳ (Heartbeat), lựa chọn ProviderAccount từ Pool toàn ứng dụng
/// và bảo vệ chống Zombie Worker bằng Fencing Token đơn điệu theo kiến trúc v1.1.
/// </summary>
public sealed class JobReservation
{
    private readonly DbContextOptions<TiToAIDbContext> _dbOptions;
    private readonly Func<DateTimeOffset>? _clock;

    public JobReservation(
        DbContextOptions<TiToAIDbContext> dbOptions,
        Func<DateTimeOffset>? clock = null)
    {
        _dbOptions = dbOptions ?? throw new ArgumentNullException(nameof(dbOptions));
        _clock = clock;
    }

    private DateTimeOffset UtcNow => _clock?.Invoke() ?? DateTimeOffset.UtcNow;

    /// <summary>
    /// Thực hiện cấp quyền thuê độc quyền (Atomic Compare-and-Set) thông qua SQLite BEGIN IMMEDIATE.
    /// Lựa chọn ProviderAccount eligible từ pool toàn ứng dụng, tạo ExecutionBinding bất biến
    /// và chuyển trạng thái từ 'Queued' sang 'Reserved'.
    /// Nếu pool đầy, giữ nguyên 'Queued', không tăng attempts và trả Acquired = false.
    /// </summary>
    public async Task<JobReservationResult> TryReserveJobAsync(
        string jobId,
        string workerId,
        TimeSpan leaseDuration,
        Func<string, bool>? isProfileAvailable = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        await using var dbContext = new TiToAIDbContext(_dbOptions);
        var dbConn = dbContext.Database.GetDbConnection();

        if (dbConn.State != ConnectionState.Open)
        {
            await dbConn.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        IDbContextTransaction transaction;
        if (dbConn is SqliteConnection sqliteConn)
        {
            // BEGIN IMMEDIATE: Chiếm Exclusive Write Lock trên SQLite ngay lập tức
            var sqliteTx = sqliteConn.BeginTransaction(deferred: false);
            transaction = await dbContext.Database.UseTransactionAsync(sqliteTx, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Không thể liên kết DbContextTransaction từ SqliteTransaction.");
        }
        else
        {
            transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        }

        await using (transaction)
        {
            try
            {
                var nowIso = UtcNow.ToString("o");
                var leaseExpiresIso = UtcNow.Add(leaseDuration).ToString("o");

                // 1. Đọc bản ghi Job cần reserve
                var job = await dbContext.Jobs
                    .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken).ConfigureAwait(false);

                if (job == null || job.State != JobState.Queued)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    return JobReservationResult.Failure("Job đã được chiếm bởi tiến trình khác hoặc không còn ở trạng thái Queued.");
                }

                // BẢO VỆ CHỐNG RESEND MÙ: Tuyệt đối không reserve lại job đã submit side effect mà chưa đối soát.
                if (job.SideEffectSubmitted == 1 && string.IsNullOrWhiteSpace(job.ResultReference))
                {
                    job.State = JobState.AwaitingUser;
                    job.ReasonCode = JobReasonCode.ReconcileRequired;
                    job.LeaseExpiresAtUtc = null;
                    job.LeaseOwnerId = null;
                    job.UpdatedAtUtc = nowIso;
                    await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                    return JobReservationResult.Failure("reconcile_required");
                }

                // 2. Kiểm tra xem Job đã có ExecutionBinding chưa (trường hợp retry / reconcile)
                var existingBinding = await dbContext.ExecutionBindings
                    .Include(b => b.ProviderAccount)
                    .Include(b => b.Profile)
                    .FirstOrDefaultAsync(b => b.JobId == jobId, cancellationToken).ConfigureAwait(false);

                if (existingBinding != null)
                {
                    if (isProfileAvailable != null && !isProfileAvailable(existingBinding.ProfileId))
                    {
                        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                        return JobReservationResult.Failure("profile_unavailable");
                    }

                    // v1.1 §3.1: Giữ nguyên binding đã cấp cho các lần retry/reconcile, không đổi account ngầm
                    job.State = JobState.Reserved;
                    job.Attempts += 1;
                    job.LeaseOwnerId = workerId;
                    job.FencingToken += 1;
                    job.LeaseExpiresAtUtc = leaseExpiresIso;
                    job.LastHeartbeatAtUtc = nowIso;
                    job.UpdatedAtUtc = nowIso;
                    job.ReasonCode = null;

                    existingBinding.LeaseOwnerId = workerId;
                    existingBinding.LeaseToken = job.FencingToken;

                    await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                    return JobReservationResult.Success(job, existingBinding);
                }

                // 3. Lần reserve đầu tiên: Tìm ProviderAccount eligible từ pool toàn ứng dụng
                var eligibleAccounts = await dbContext.ProviderAccounts
                    .Where(a => a.ProviderKey == job.ProviderKey &&
                                a.Enabled &&
                                a.Lifecycle == "Active")
                    .ToListAsync(cancellationToken).ConfigureAwait(false);

                if (eligibleAccounts.Count == 0)
                {
                    // Chưa có tài khoản nào được cấu hình cho provider này trong pool
                    job.ReasonCode = "provider_account_required";
                    job.UpdatedAtUtc = nowIso;
                    await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                    return JobReservationResult.Failure("provider_account_required");
                }

                // Tìm các job đang chiếm slot (Reserved hoặc Running) và loại tác vụ của chúng (tránh N+1 query)
                var activeJobsInfo = await dbContext.Jobs
                    .Where(j => j.State == JobState.Reserved || j.State == JobState.Running)
                    .Select(j => new { j.Id, j.TaskType })
                    .ToListAsync(cancellationToken).ConfigureAwait(false);

                var activeJobIds = activeJobsInfo.Select(j => j.Id).ToList();
                var activeJobTaskTypes = activeJobsInfo.ToDictionary(j => j.Id, j => j.TaskType);

                var activeBindings = await dbContext.ExecutionBindings
                    .Where(b => activeJobIds.Contains(b.JobId))
                    .ToListAsync(cancellationToken).ConfigureAwait(false);

                // Lọc các account còn slot trống và đáp ứng perTaskLimits
                var candidatePool = new List<(ProviderAccount Account, int ActiveCount)>();

                foreach (var account in eligibleAccounts)
                {
                    if (isProfileAvailable != null && !isProfileAvailable(account.ProfileId))
                    {
                        continue;
                    }

                    var accountBindings = activeBindings.Where(b => b.ProviderAccountId == account.Id).ToList();
                    int activeCount = accountBindings.Count;

                    if (activeCount >= account.MaxConcurrentJobs)
                    {
                        continue; // Hết capacity
                    }

                    // Kiểm tra perTaskLimitsJson nếu có
                    if (!string.IsNullOrWhiteSpace(account.PerTaskLimitsJson))
                    {
                        try
                        {
                            var limits = JsonSerializer.Deserialize<Dictionary<string, int>>(account.PerTaskLimitsJson);
                            if (limits != null && limits.TryGetValue(job.TaskType, out int taskLimit))
                            {
                                int currentRoleCount = accountBindings
                                    .Count(b => activeJobTaskTypes.TryGetValue(b.JobId, out var tt) && tt == job.TaskType);

                                if (currentRoleCount >= taskLimit)
                                {
                                    continue; // Vượt giới hạn task type này
                                }
                            }
                        }
                        catch
                        {
                            // Bỏ qua lỗi parse giới hạn, dùng MaxConcurrentJobs
                        }
                    }

                    candidatePool.Add((account, activeCount));
                }

                if (candidatePool.Count == 0)
                {
                    // Toàn bộ pool đã đầy: Giữ nguyên trạng thái Queued, KHÔNG tăng attempts
                    job.ReasonCode = "waiting_for_capacity";
                    job.UpdatedAtUtc = nowIso;
                    await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                    return JobReservationResult.Failure("pool_capacity_reached");
                }

                // Phân phối công bằng: Chọn account có ít active job nhất, rồi theo UpdatedAt, rồi Id
                var selectedCandidate = candidatePool
                    .OrderBy(c => c.ActiveCount)
                    .ThenBy(c => c.Account.UpdatedAtUtc)
                    .ThenBy(c => c.Account.Id)
                    .First();

                var chosenAccount = selectedCandidate.Account;
                int slotNumber = selectedCandidate.ActiveCount + 1;

                // 4. Tạo ExecutionBinding bất biến
                var newBinding = new ExecutionBinding
                {
                    JobId = job.Id,
                    ProviderAccountId = chosenAccount.Id,
                    ProfileId = chosenAccount.ProfileId,
                    Identity = chosenAccount.VerifiedIdentity,
                    CapacityConfigRevision = chosenAccount.Revision,
                    TabSlotId = $"slot-{slotNumber}",
                    AssignedAtUtc = nowIso,
                    LeaseOwnerId = workerId,
                    LeaseToken = job.FencingToken + 1
                };

                dbContext.ExecutionBindings.Add(newBinding);

                // 5. Cập nhật Job sang trạng thái Reserved
                job.State = JobState.Reserved;
                job.Attempts += 1;
                job.LeaseOwnerId = workerId;
                job.FencingToken += 1;
                job.LeaseExpiresAtUtc = leaseExpiresIso;
                job.LastHeartbeatAtUtc = nowIso;
                job.UpdatedAtUtc = nowIso;
                job.ReasonCode = null;

                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                return JobReservationResult.Success(job, newBinding);
            }
            catch (Exception ex)
            {
                try
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Nuốt lỗi rollback nếu kết nối đã đóng
                }

                return JobReservationResult.Failure($"Lỗi giao dịch reservation: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Chuyển trạng thái từ Reserved sang Running trước khi tiến hành chuẩn bị dữ liệu và mở tab.
    /// Bắt buộc fencing_token phải khớp chính xác.
    /// </summary>
    public async Task TransitionToRunningAsync(
        string jobId,
        long fencingToken,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = new TiToAIDbContext(_dbOptions);
        var nowIso = UtcNow.ToString("o");

        const string updateSql = @"
            UPDATE Job
            SET state = 'Running',
                updated_at_utc = @now
            WHERE id = @jobId
              AND fencing_token = @token
              AND state = 'Reserved';";

        var rows = await dbContext.Database.ExecuteSqlRawAsync(
            updateSql,
            new object[]
            {
                new SqliteParameter("@now", nowIso),
                new SqliteParameter("@jobId", jobId),
                new SqliteParameter("@token", fencingToken)
            },
            cancellationToken).ConfigureAwait(false);

        if (rows == 0)
        {
            throw new FencingTokenStaleException(jobId, fencingToken);
        }
    }

    /// <summary>
    /// Gửi nhịp tim định kỳ (Heartbeat) để gia hạn lease_expires_at_utc.
    /// Kiểm tra nghiêm ngặt lease_owner_id và fencing_token.
    /// Nếu rowsAffected == 0, worker đã bị loại bỏ (fenced out), ném ngay FencingTokenStaleException.
    /// </summary>
    public async Task SendHeartbeatAsync(
        string jobId,
        string workerId,
        long fencingToken,
        TimeSpan extension,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = new TiToAIDbContext(_dbOptions);
        var nowIso = UtcNow.ToString("o");
        var newLeaseExpiresIso = UtcNow.Add(extension).ToString("o");

        const string updateSql = @"
            UPDATE Job
            SET lease_expires_at_utc = @newLeaseExpires,
                last_heartbeat_at_utc = @now,
                updated_at_utc = @now
            WHERE id = @jobId
              AND lease_owner_id = @workerId
              AND fencing_token = @token
              AND state IN ('Reserved', 'Running')
              AND (lease_expires_at_utc IS NULL OR lease_expires_at_utc >= @now);";

        var rows = await dbContext.Database.ExecuteSqlRawAsync(
            updateSql,
            new object[]
            {
                new SqliteParameter("@newLeaseExpires", newLeaseExpiresIso),
                new SqliteParameter("@now", nowIso),
                new SqliteParameter("@jobId", jobId),
                new SqliteParameter("@workerId", workerId),
                new SqliteParameter("@token", fencingToken)
            },
            cancellationToken).ConfigureAwait(false);

        if (rows == 0)
        {
            throw new FencingTokenStaleException(jobId, fencingToken);
        }
    }

    /// <summary>
    /// CRITICAL BOUNDARY: Cam kết cờ side_effect_submitted = 1 vào SQLite TRƯỚC KHI thực thi thao tác ngoài.
    /// Nếu rowsAffected == 0, ném ngay FencingTokenStaleException để ngừng ngay lập tức trước khi gọi browser.
    /// </summary>
    public async Task CommitSideEffectAsync(
        string jobId,
        long fencingToken,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = new TiToAIDbContext(_dbOptions);
        var nowIso = UtcNow.ToString("o");

        const string updateSql = @"
            UPDATE Job
            SET side_effect_submitted = 1,
                side_effect_at_utc = @now,
                updated_at_utc = @now
            WHERE id = @jobId
              AND fencing_token = @token
              AND state IN ('Reserved', 'Running')
              AND (lease_expires_at_utc IS NULL OR lease_expires_at_utc >= @now);";

        var rows = await dbContext.Database.ExecuteSqlRawAsync(
            updateSql,
            new object[]
            {
                new SqliteParameter("@now", nowIso),
                new SqliteParameter("@jobId", jobId),
                new SqliteParameter("@token", fencingToken)
            },
            cancellationToken).ConfigureAwait(false);

        if (rows == 0)
        {
            throw new FencingTokenStaleException(jobId, fencingToken);
        }
    }

    /// <summary>
    /// Cam kết kết quả hoàn thành thành công (Succeeded), xóa lease và lưu kết quả đầu ra (Asset ID hoặc Result Text).
    /// Bắt buộc phải có ít nhất ResultReference hoặc ResultText hợp lệ theo v1.1.
    /// </summary>
    public async Task CommitSuccessAsync(
        string jobId,
        long fencingToken,
        string? resultReference,
        string? resultText,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resultReference) && string.IsNullOrWhiteSpace(resultText))
        {
            throw new InvalidOperationException("Không thể đánh dấu thành công (Succeeded) khi không có ResultReference hoặc ResultText hợp lệ.");
        }

        await using var dbContext = new TiToAIDbContext(_dbOptions);
        var nowIso = UtcNow.ToString("o");

        const string updateSql = @"
            UPDATE Job
            SET state = 'Succeeded',
                result_reference = @resultRef,
                result_text = @resultText,
                lease_expires_at_utc = NULL,
                updated_at_utc = @now
            WHERE id = @jobId
              AND fencing_token = @token
              AND state IN ('Reserved', 'Running');";

        var rows = await dbContext.Database.ExecuteSqlRawAsync(
            updateSql,
            new object[]
            {
                new SqliteParameter("@resultRef", (object?)resultReference ?? DBNull.Value),
                new SqliteParameter("@resultText", (object?)resultText ?? DBNull.Value),
                new SqliteParameter("@now", nowIso),
                new SqliteParameter("@jobId", jobId),
                new SqliteParameter("@token", fencingToken)
            },
            cancellationToken).ConfigureAwait(false);

        if (rows == 0)
        {
            throw new FencingTokenStaleException(jobId, fencingToken);
        }
    }

    /// <summary>
    /// Xử lý thất bại khi thực thi tác vụ:
    /// Nếu canRetry == true (chưa đạt max_attempts và side_effect_submitted == 0): trả về Queued kèm retry_after_utc.
    /// Ngược lại: đánh dấu Failed kèm reason_code.
    /// </summary>
    public async Task CommitFailureAsync(
        string jobId,
        long fencingToken,
        string reasonCode,
        bool canRetry,
        TimeSpan? retryDelay,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = new TiToAIDbContext(_dbOptions);
        var nowIso = UtcNow.ToString("o");

        if (canRetry)
        {
            var retryAfterIso = UtcNow.Add(retryDelay ?? TimeSpan.FromSeconds(5)).ToString("o");

            const string retrySql = @"
                UPDATE Job
                SET state = 'Queued',
                    reason_code = @reason,
                    lease_expires_at_utc = NULL,
                    retry_after_utc = @retryAfter,
                    updated_at_utc = @now
                WHERE id = @jobId
                  AND fencing_token = @token
                  AND state IN ('Reserved', 'Running');";

            var rows = await dbContext.Database.ExecuteSqlRawAsync(
                retrySql,
                new object[]
                {
                    new SqliteParameter("@reason", reasonCode),
                    new SqliteParameter("@retryAfter", retryAfterIso),
                    new SqliteParameter("@now", nowIso),
                    new SqliteParameter("@jobId", jobId),
                    new SqliteParameter("@token", fencingToken)
                },
                cancellationToken).ConfigureAwait(false);

            if (rows == 0)
            {
                throw new FencingTokenStaleException(jobId, fencingToken);
            }
        }
        else
        {
            const string failSql = @"
                UPDATE Job
                SET state = 'Failed',
                    reason_code = @reason,
                    lease_expires_at_utc = NULL,
                    updated_at_utc = @now
                WHERE id = @jobId
                  AND fencing_token = @token
                  AND state IN ('Reserved', 'Running');";

            var rows = await dbContext.Database.ExecuteSqlRawAsync(
                failSql,
                new object[]
                {
                    new SqliteParameter("@reason", reasonCode),
                    new SqliteParameter("@now", nowIso),
                    new SqliteParameter("@jobId", jobId),
                    new SqliteParameter("@token", fencingToken)
                },
                cancellationToken).ConfigureAwait(false);

            if (rows == 0)
            {
                throw new FencingTokenStaleException(jobId, fencingToken);
            }
        }
    }

    /// <summary>
    /// Hoàn trả Job về trạng thái Queued khi không chiếm được tab lease (ví dụ role tab đang bận).
    /// Giảm attempts đi 1 vì tác vụ chưa từng thực sự được chạy trên worker.
    /// </summary>
    public async Task ReleaseReservationDueToNoCapacityAsync(
        string jobId,
        long fencingToken,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = new TiToAIDbContext(_dbOptions);
        var nowIso = UtcNow.ToString("o");

        const string updateSql = @"
            UPDATE Job
            SET state = 'Queued',
                attempts = MAX(0, attempts - 1),
                lease_owner_id = NULL,
                lease_expires_at_utc = NULL,
                reason_code = 'tab_capacity_unavailable',
                updated_at_utc = @now
            WHERE id = @jobId
              AND fencing_token = @token
              AND state IN ('Reserved', 'Running');";

        await dbContext.Database.ExecuteSqlRawAsync(
            updateSql,
            new object[]
            {
                new SqliteParameter("@now", nowIso),
                new SqliteParameter("@jobId", jobId),
                new SqliteParameter("@token", fencingToken)
            },
            cancellationToken).ConfigureAwait(false);
    }
}
