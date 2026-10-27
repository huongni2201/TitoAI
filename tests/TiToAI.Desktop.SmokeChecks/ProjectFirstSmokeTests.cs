using System.IO;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TiToAI.Application.Projects;
using TiToAI.Contracts.Bridge;
using TiToAI.Contracts.Projects;
using TiToAI.Desktop.Bridge;
using TiToAI.Desktop.Bridge.Handlers;
using TiToAI.Infrastructure.Persistence;

namespace TiToAI.Desktop.SmokeChecks;

public static class ProjectFirstSmokeTests
{
    public static async Task<int> RunAllAsync()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "titoai-project-first-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var databasePath = Path.Combine(tempDirectory, "project-first.db");
        var options = new DbContextOptionsBuilder<TiToAIDbContext>()
            .UseSqlite(DatabasePathHelper.BuildConnectionString(databasePath))
            .Options;

        try
        {
            await using (var db = new TiToAIDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();
            }

            var service = new ProjectService(options);
            var firstId = (await service.CreateAsync(new ProjectCreateRequest(null, "Video đầu tiên", null))).ProjectId;

            string scopeId;
            await using (var db = new TiToAIDbContext(options))
            {
                scopeId = (await db.Projects.SingleAsync(p => p.Id == firstId)).WorkspaceId;
            }

            var secondId = (await service.CreateAsync(new ProjectCreateRequest(scopeId, "Video thứ hai", null))).ProjectId;
            var allProjects = await service.ListAllAsync();

            Assert(allProjects.Count == 2, "project.list phải liệt kê Project thuộc mọi legacy scope.");
            Assert(allProjects.Select(p => p.ProjectId).ToHashSet().SetEquals(new[] { firstId, secondId }),
                "Danh sách toàn cục phải chứa đúng hai Project đã tạo.");
            Assert(allProjects.All(p => !p.HasVideo), "Project mới chưa có video đầu ra.");

            var dispatcher = new BridgeDispatcher();
            new ProjectHandlers(service).Register(dispatcher);
            Assert(dispatcher.IsCommandAllowed("project.list"), "Project-first command phải được đăng ký.");
            Assert(!dispatcher.IsCommandAllowed("workspace.list"), "Workspace list không còn là bridge command hoạt động.");

            using var payloadDocument = JsonDocument.Parse("{}");
            var request = new RequestEnvelope(
                Guid.NewGuid(),
                "request",
                "project.list",
                payloadDocument.RootElement.Clone(),
                DateTimeOffset.UtcNow,
                BridgeContract.Version);
            var response = await dispatcher.DispatchAsync(request, CancellationToken.None);
            Assert(response.Success, "project.list bridge command phải thành công.");
            Assert(response.Data.HasValue, "project.list response phải có dữ liệu.");
            var responseData = JsonSerializer.Deserialize<ProjectListResponseData>(response.Data?.GetRawText() ?? throw new InvalidOperationException("project.list response data missing."), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            Assert(responseData?.Projects.Count == 2, "project.list phải trả đủ hai Project.");

            Console.WriteLine("[PASS] Project-first create/list works without a user-selected Workspace.");
            return 0;
        }
        finally
        {
            try
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
            catch
            {
                // SQLite may still hold a Windows file handle until disposal completes.
            }
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
