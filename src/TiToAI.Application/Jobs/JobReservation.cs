using System;
using System.Data;
using System.Data.Common;
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
/// Kết quả phản hồi của một nỗ lực cấp quyền thuê độc quyền (Reservation).
/// </summary>
public sealed record JobReservationResult(
    bool Acquired,
    Job? Job,
    string? Reason = null
)
{
    public static JobReservationResult Success(Job job) => new(true, job);
    public static JobReservationResult Failure(string reason) => new(false, null, reason);
}

/// <summary>
/// Quản lý việc cấp phát độc quyền (Atomic Reservation), thời hạn thuê (Lease),
/// nhịp tim định kỳ (Heartbeat) và bảo vệ chống Zombie Worker bằng Fencing Token đơn điệu.
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
    /// Trả về Acquired = true khi và chỉ khi đúng 1 dòng được cập nhật từ 'Queued' sang 'Reserved'.
    /// </summary>
    public async Task<JobReservationResult> TryReserveJobAsync(
        string jobId,
        string workerId,
        TimeSpan leaseDuration,
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

                // Câu lệnh Compare-and-Set (CAS) atomic: chỉ cập nhật nếu state == 'Queued'
                const string updateSql = @"
                    UPDATE Job
                    SET state = 'Reserved',
                        attempts = attempts + 1,
                        lease_owner_id = @workerId,
                        fencing_token = fencing_token + 1,
                        lease_expires_at_utc = @leaseExpiresAt,
                        last_heartbeat_at_utc = @now,
                        updated_at_utc = @now
                    WHERE id = @jobId
                      AND state = 'Queued';";

                var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
                    updateSql,
                    new object[]
                    {
                        new SqliteParameter("@workerId", workerId),
                        new SqliteParameter("@leaseExpiresAt", leaseExpiresIso),
                        new SqliteParameter("@now", nowIso),
                        new SqliteParameter("@jobId", jobId)
                    },
                    cancellationToken).ConfigureAwait(false);

                if (rowsAffected == 1)
                {
                    // Lấy bản ghi Job vừa được cập nhật đầy đủ thông tin
                    var reservedJob = await dbContext.Jobs
                        .AsNoTracking()
                        .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken).ConfigureAwait(false);

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                    if (reservedJob is not null)
                    {
                        return JobReservationResult.Success(reservedJob);
                    }
                }

                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return JobReservationResult.Failure("Job đã được chiếm bởi tiến trình khác hoặc không còn ở trạng thái Queued.");
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
              AND state IN ('Reserved', 'Running');";

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
              AND state IN ('Reserved', 'Running');";

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
    /// </summary>
    public async Task CommitSuccessAsync(
        string jobId,
        long fencingToken,
        string? resultReference,
        string? resultText,
        CancellationToken cancellationToken = default)
    {
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
}
