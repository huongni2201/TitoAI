using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TiToAI.Contracts.Bridge;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Interceptors;

namespace TiToAI.Desktop.Bridge;

public sealed record WorkspaceSummaryDto(
    [property: JsonPropertyName("workspaceId")] string WorkspaceId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("channelHandle")] string ChannelHandle,
    [property: JsonPropertyName("topic")] string Topic,
    [property: JsonPropertyName("status")] string Status);

public sealed record WorkspaceListResponseData(
    [property: JsonPropertyName("workspaces")] IReadOnlyList<WorkspaceSummaryDto> Workspaces);

/// <summary>
/// Handler xử lý truy vấn đọc danh sách Workspace (projection read-only từ SQLite DB thật)
/// phục vụ Gate G1 Topbar Workspace Selector theo contracts.md v1.0.4 §1.6.G.
/// </summary>
public sealed class WorkspaceQueryHandler
{
    private readonly string _databasePath;
    private readonly DbContextOptions<TiToAIDbContext> _dbOptions;

    public WorkspaceQueryHandler(string? databasePath = null)
    {
        _databasePath = databasePath ?? DatabasePathHelper.GetDefaultDatabasePath();

        var connectionString = DatabasePathHelper.BuildConnectionString(_databasePath);
        var optionsBuilder = new DbContextOptionsBuilder<TiToAIDbContext>();
        optionsBuilder.UseSqlite(connectionString)
                      .AddInterceptors(new SqlitePragmaInterceptor());
        _dbOptions = optionsBuilder.Options;
    }

    public WorkspaceQueryHandler(DbContextOptions<TiToAIDbContext> dbOptions)
    {
        _dbOptions = dbOptions ?? throw new ArgumentNullException(nameof(dbOptions));
        _databasePath = string.Empty;
    }

    public void Register(IBridgeDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        dispatcher.RegisterHandler("workspace.list", HandleWorkspaceListAsync);
    }

    public void Unregister(IBridgeDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        dispatcher.UnregisterHandler("workspace.list");
    }

    public async Task<ResponseEnvelope> HandleWorkspaceListAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            await using var dbContext = new TiToAIDbContext(_dbOptions);

            var workspaces = await dbContext.Workspaces
                .AsNoTracking()
                .OrderBy(w => w.Name)
                .Select(w => new WorkspaceSummaryDto(
                    w.Id,
                    w.Name,
                    w.ChannelHandle,
                    w.Topic,
                    w.Status))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var data = new WorkspaceListResponseData(workspaces);
            return ResponseEnvelopeHelper.CreateSuccess(request.Id, data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WorkspaceQueryHandler] Lỗi truy vấn workspace.list: {ex.GetType().Name}");
            return ResponseEnvelopeHelper.CreateError(
                request.Id,
                "INTERNAL_ERROR",
                "Không thể truy vấn danh sách không gian làm việc từ cơ sở dữ liệu.",
                retryable: true);
        }
    }
}
