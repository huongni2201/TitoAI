using Microsoft.EntityFrameworkCore;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;

namespace TiToAI.Application.Jobs;

public sealed record JobActivityEntry(string AtUtc, string Step, string Message);

/// <summary>Small, durable user-facing trace. Messages must not contain prompts, cookies or file paths.</summary>
public sealed class JobActivityLog
{
    private readonly DbContextOptions<TiToAIDbContext> _dbOptions;
    public JobActivityLog(DbContextOptions<TiToAIDbContext> dbOptions) => _dbOptions = dbOptions;

    public async Task WriteAsync(string jobId, string step, string message, CancellationToken ct = default)
    {
        var at = DateTimeOffset.UtcNow;
        await using var db = new TiToAIDbContext(_dbOptions);
        db.AppSettings.Add(new AppSetting
        {
            Key = $"job.activity.{jobId}.{at.UtcTicks:D19}.{Guid.NewGuid():N}",
            Value = System.Text.Json.JsonSerializer.Serialize(new JobActivityEntry(at.ToString("O"), step, message)),
            UpdatedAtUtc = at.ToString("O")
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<JobActivityEntry>> ReadAsync(string jobId, CancellationToken ct = default)
    {
        await using var db = new TiToAIDbContext(_dbOptions);
        var values = await db.AppSettings.AsNoTracking()
            .Where(item => item.Key.StartsWith($"job.activity.{jobId}."))
            .OrderByDescending(item => item.Key).Take(100)
            .Select(item => item.Value).ToListAsync(ct).ConfigureAwait(false);
        return values.AsEnumerable().Reverse()
            .Select(value => System.Text.Json.JsonSerializer.Deserialize<JobActivityEntry>(value)!)
            .ToArray();
    }
}
