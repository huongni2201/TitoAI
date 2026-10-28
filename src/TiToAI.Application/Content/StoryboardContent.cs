using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;

namespace TiToAI.Application.Content;

public sealed record StoryboardShot(int StartSeconds, int EndSeconds, string Description, string ImagePrompt, string? ImageAssetId = null);

public sealed record StoryboardContent(int DurationSeconds, IReadOnlyList<StoryboardShot> Shots);

/// <summary>Lưu storyboard của một video sau khi kiểm tra nội dung và thời lượng.</summary>
public sealed class StoryboardContentService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly DbContextOptions<TiToAIDbContext> _dbOptions;

    public StoryboardContentService(DbContextOptions<TiToAIDbContext> dbOptions) => _dbOptions = dbOptions;

    public async Task<StoryboardContent?> GetAsync(string projectId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(projectId, out var projectGuid))
            throw new ProjectWorkflowValidationException("Project không hợp lệ.");
        var key = $"project.storyboard.{projectGuid:D}".ToLowerInvariant();
        await using var db = new TiToAIDbContext(_dbOptions);
        var value = await db.AppSettings.AsNoTracking().Where(item => item.Key == key)
            .Select(item => item.Value).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return value == null ? null : JsonSerializer.Deserialize<StoryboardContent>(value, JsonOptions);
    }

    public static StoryboardContent Parse(string text, int durationSeconds)
    {
        StoryboardContent? storyboard;
        try
        {
            storyboard = JsonSerializer.Deserialize<StoryboardContent>(ContentCandidateParser.ExtractJsonPayload(text), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ProjectWorkflowValidationException($"Storyboard không phải JSON hợp lệ: {ex.Message}");
        }

        if (storyboard == null || durationSeconds <= 0 || storyboard.DurationSeconds != durationSeconds ||
            storyboard.Shots == null || storyboard.Shots.Count == 0 || storyboard.Shots.Count > 30)
            throw new ProjectWorkflowValidationException("Storyboard thiếu phân cảnh hoặc sai thời lượng video.");

        var end = 0;
        foreach (var shot in storyboard.Shots)
        {
            if (shot == null || shot.StartSeconds != end || shot.EndSeconds <= end ||
                shot.EndSeconds > durationSeconds || string.IsNullOrWhiteSpace(shot.Description) ||
                string.IsNullOrWhiteSpace(shot.ImagePrompt))
                throw new ProjectWorkflowValidationException("Phân cảnh phải liên tiếp, có thời lượng, mô tả và prompt ảnh hợp lệ.");
            end = shot.EndSeconds;
        }

        if (end != durationSeconds)
            throw new ProjectWorkflowValidationException("Tổng thời lượng phân cảnh không khớp thời lượng video.");
        return storyboard;
    }

    public async Task<StoryboardContent> SaveAsync(string projectId, string text, int durationSeconds, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(projectId, out var projectGuid))
            throw new ProjectWorkflowValidationException("Project không hợp lệ.");
        projectId = projectGuid.ToString("D").ToLowerInvariant();
        var storyboard = Parse(text, durationSeconds);
        await using var db = new TiToAIDbContext(_dbOptions);
        if (!await db.Projects.AnyAsync(project => project.Id == projectId, cancellationToken).ConfigureAwait(false))
            throw new ProjectWorkflowValidationException("Không tìm thấy Project.");

        var imageIds = storyboard.Shots.Where(shot => !string.IsNullOrWhiteSpace(shot.ImageAssetId))
            .Select(shot => shot.ImageAssetId!).Distinct(StringComparer.Ordinal).ToArray();
        var validImages = await db.Assets.CountAsync(asset => imageIds.Contains(asset.Id) &&
            asset.AssetType == "Image" && asset.Status == "Linked" &&
            (asset.ProjectId == null || asset.ProjectId == projectId), cancellationToken).ConfigureAwait(false);
        if (validImages != imageIds.Length)
            throw new ProjectWorkflowValidationException("Ảnh phân cảnh không tồn tại hoặc không thuộc Project.");

        var key = $"project.storyboard.{projectId}";
        var setting = await db.AppSettings.FirstOrDefaultAsync(item => item.Key == key, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow.ToString("o");
        if (setting == null)
            db.AppSettings.Add(new AppSetting { Key = key, Value = JsonSerializer.Serialize(storyboard), UpdatedAtUtc = now });
        else
        {
            setting.Value = JsonSerializer.Serialize(storyboard);
            setting.UpdatedAtUtc = now;
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return storyboard;
    }
}
