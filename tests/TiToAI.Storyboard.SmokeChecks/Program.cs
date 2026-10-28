using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TiToAI.Application.Content;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;

const string valid = """
{"durationSeconds":15,"shots":[
  {"startSeconds":0,"endSeconds":5,"description":"Giới thiệu","imagePrompt":"Toàn thân"},
  {"startSeconds":5,"endSeconds":15,"description":"Chi tiết","imagePrompt":"Cận áo"}
]}
""";

var storyboard = StoryboardContentService.Parse(valid, 15);
if (storyboard.Shots.Count != 2) throw new Exception("Thiếu phân cảnh");
foreach (var invalid in new[] { valid.Replace("\"startSeconds\":5", "\"startSeconds\":6"), valid.Replace("\"endSeconds\":15", "\"endSeconds\":14"), valid.Replace("Cận áo", "") })
{
    try { StoryboardContentService.Parse(invalid, 15); throw new Exception("Đã chấp nhận storyboard sai"); }
    catch (ProjectWorkflowValidationException) { }
}

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var options = new DbContextOptionsBuilder<TiToAIDbContext>().UseSqlite(connection).Options;
await using (var db = new TiToAIDbContext(options))
{
    await db.Database.EnsureCreatedAsync();
    db.Projects.Add(new Project { Id = "26fd4c1a-e393-4e52-bc3c-7339a2ef9abd", Name = "Test", CreatedAtUtc = "2026-01-01", UpdatedAtUtc = "2026-01-01" });
    await db.SaveChangesAsync();
}
var service = new StoryboardContentService(options);
await service.SaveAsync("26fd4c1a-e393-4e52-bc3c-7339a2ef9abd", valid, 15);
if ((await service.GetAsync("26fd4c1a-e393-4e52-bc3c-7339a2ef9abd"))?.Shots.Count != 2)
    throw new Exception("Không tải được storyboard");
try { await service.SaveAsync("26fd4c1a-e393-4e52-bc3c-7339a2ef9abd", valid.Replace("Cận áo", ""), 15); throw new Exception("Đã lưu nội dung sai"); }
catch (ProjectWorkflowValidationException) { }
var missingImage = valid.Replace("\"imagePrompt\":\"Cận áo\"", "\"imagePrompt\":\"Cận áo\",\"imageAssetId\":\"missing\"");
try { await service.SaveAsync("26fd4c1a-e393-4e52-bc3c-7339a2ef9abd", missingImage, 15); throw new Exception("Đã lưu ảnh không tồn tại"); }
catch (ProjectWorkflowValidationException) { }
await using (var db = new TiToAIDbContext(options))
{
    var saved = await db.AppSettings.SingleAsync(x => x.Key == "project.storyboard.26fd4c1a-e393-4e52-bc3c-7339a2ef9abd");
    if (StoryboardContentService.Parse(saved.Value, 15).Shots.Count != 2) throw new Exception("Không đọc lại được storyboard");
}
Console.WriteLine("Storyboard content checks passed");
