using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TiToAI.Application.Jobs;
using TiToAI.Contracts.Content;
using TiToAI.Contracts.Jobs;
using TiToAI.Contracts.Scenes;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;

namespace TiToAI.Application.Content;

public sealed partial class ProjectWorkflowService
{
    /// <summary>
    /// Áp dụng kết quả hợp lệ của một Job hoàn tất thành công (Succeeded) và kích hoạt bước tiếp theo (§3.3).
    /// </summary>
    public async Task<bool> ApplyJobOutputAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId)) return false;

        await using var dbContext = new TiToAIDbContext(_dbOptions);
        var job = await dbContext.Jobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken).ConfigureAwait(false);
        if (job == null || job.State != JobState.Succeeded)
        {
            return false;
        }

        // Idempotency guard: kiểm tra đã áp dụng kết quả của job này chưa (PV-R3)
        var appliedKey = $"workflow.applied.{job.Id}";
        var appliedSetting = await dbContext.AppSettings
            .FirstOrDefaultAsync(s => s.Key == appliedKey, cancellationToken).ConfigureAwait(false);
        if (appliedSetting != null)
        {
            return true;
        }

        var nowIso = DateTimeOffset.UtcNow.ToString("o");
        var config = await GetWorkflowConfigAsync(job.ProjectId, cancellationToken).ConfigureAwait(false);
        var jobRunId = GetWorkflowRunId(job.ConfigSnapshotJson);
        if (jobRunId != null && !string.Equals(jobRunId, config.CurrentRunId, StringComparison.Ordinal))
        {
            dbContext.AppSettings.Add(new AppSetting
            {
                Key = appliedKey,
                Value = JsonSerializer.Serialize(new { JobId = job.Id, AppliedAtUtc = nowIso, IgnoredAsStaleRun = true }, JsonOptions),
                UpdatedAtUtc = nowIso
            });
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        // Đánh dấu job đã được apply trong transaction này
        dbContext.AppSettings.Add(new AppSetting
        {
            Key = appliedKey,
            Value = JsonSerializer.Serialize(new { JobId = job.Id, AppliedAtUtc = nowIso }, JsonOptions),
            UpdatedAtUtc = nowIso
        });

        bool isPaused = config.RunState == WorkflowRunState.Paused;
        WorkflowContinuationIntent? continuationIntent = null;
        var composer = new VideoWorkflowPromptComposer(_dbOptions);

        // 1. Áp dụng kết quả Script -> Tự động tạo các Scene trong SQLite
        if (job.TaskType == JobTaskType.Script)
        {
            if (string.IsNullOrWhiteSpace(job.ResultText))
            {
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            var existingScenes = await dbContext.Scenes
                .Where(s => s.ProjectId == job.ProjectId)
                .OrderBy(s => s.SequenceOrder)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            if (existingScenes.Count > 0)
            {
                // Cảnh đã tồn tại (do người dùng tạo hoặc đã sinh trước đó), KHÔNG nhân bản thêm cảnh mới (PV-R3)
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }

            var storyboard = TryParseStoryboard(job.ResultText, config.TargetDurationPerSceneSeconds);
            if (storyboard != null)
            {
                await new StoryboardContentService(_dbOptions).SaveAsync(job.ProjectId, job.ResultText, config.TargetDurationPerSceneSeconds, cancellationToken).ConfigureAwait(false);
            }
            var sceneItems = storyboard != null
                ? StoryboardScenePlan(storyboard)
                : ExtractScenePlanItems(job.ResultText, config.TargetSceneCount, config.TargetDurationPerSceneSeconds);
            if (sceneItems.Count == 0)
            {
                var cfgSetting = await dbContext.AppSettings.FirstOrDefaultAsync(
                    setting => setting.Key == GetConfigKey(job.ProjectId), cancellationToken).ConfigureAwait(false);
                if (cfgSetting != null)
                {
                    cfgSetting.Value = JsonSerializer.Serialize(config with
                    {
                        RunState = WorkflowRunState.Failed,
                        UpdatedAtUtc = nowIso
                    }, JsonOptions);
                    cfgSetting.UpdatedAtUtc = nowIso;
                }
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }
            if (sceneItems.Count > 0)
            {
                var createdScenes = new List<Scene>();
                for (int i = 0; i < sceneItems.Count; i++)
                {
                    var item = sceneItems[i];
                    var newScene = new Scene
                    {
                        Id = Guid.NewGuid().ToString("D").ToLowerInvariant(),
                        ProjectId = job.ProjectId,
                        SequenceOrder = i + 1,
                        Name = item.Name,
                        TargetDurationSeconds = item.TargetDurationSeconds > 0 ? item.TargetDurationSeconds : config.TargetDurationPerSceneSeconds,
                        PromptText = null,
                        ImageAssetId = config.WorkflowMode == WorkflowModes.ReadyImage ? config.ReadyImageAssetId :
                                       config.WorkflowMode == WorkflowModes.ProductOnly ? config.ProductAssetId : null,
                        AudioAssetId = config.AudioOption == AudioOptions.CustomAudio ? config.AudioAssetId : null,
                        Status = "Draft",
                        CreatedAtUtc = nowIso,
                        UpdatedAtUtc = nowIso
                    };
                    dbContext.Scenes.Add(newScene);
                    dbContext.AppSettings.Add(new AppSetting
                    {
                        Key = $"scene.plan.{newScene.Id}",
                        Value = item.Purpose,
                        UpdatedAtUtc = nowIso
                    });
                    createdScenes.Add(newScene);
                }

                // Persist scenes and continuation intents in one commit. A restart can safely replay each intent.
                if (!isPaused && _jobService != null)
                {
                    await EnqueueJobsForExistingScenesAsync(
                        dbContext, job.ProjectId, config, createdScenes, cancellationToken,
                        deferDispatch: true).ConfigureAwait(false);
                    await RunWorkflowContinuationSweepAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            return true;
        }

        if (string.IsNullOrWhiteSpace(job.SceneId))
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        var scene = await dbContext.Scenes.FirstOrDefaultAsync(s => s.Id == job.SceneId, cancellationToken).ConfigureAwait(false);
        if (scene == null)
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        // Lấy metadata setting hiện tại của scene để kiểm tra stale và candidate history
        var sceneKey = GetSceneKey(scene.Id);
        var sceneSetting = await dbContext.AppSettings.FirstOrDefaultAsync(s => s.Key == sceneKey, cancellationToken).ConfigureAwait(false);
        SceneWorkflowDetailDto? sceneMeta = null;
        if (sceneSetting != null && !string.IsNullOrWhiteSpace(sceneSetting.Value))
        {
            try { sceneMeta = JsonSerializer.Deserialize<SceneWorkflowDetailDto>(sceneSetting.Value, JsonOptions); } catch { }
        }

        var candidateHistory = sceneMeta?.VideoCandidateHistory != null
            ? new List<string>(sceneMeta.VideoCandidateHistory)
            : new List<string>();
        var staleTaskTypes = new HashSet<string>(sceneMeta?.StaleTaskTypes ?? Array.Empty<string>(), StringComparer.Ordinal);

        bool isUserModified = string.CompareOrdinal(scene.UpdatedAtUtc, job.CreatedAtUtc) > 0;
        if (isUserModified)
        {
            if (job.TaskType == JobTaskType.VideoRender && !string.IsNullOrWhiteSpace(job.ResultReference) &&
                !candidateHistory.Contains(job.ResultReference))
            {
                candidateHistory.Add(job.ResultReference);
            }

            var staleDetail = new SceneWorkflowDetailDto(
                SceneId: scene.Id,
                ProjectId: scene.ProjectId,
                SequenceOrder: scene.SequenceOrder,
                Name: scene.Name,
                TargetDurationSeconds: Convert.ToInt32(scene.TargetDurationSeconds),
                WorkflowStatus: SceneWorkflowStatus.NeedsReview,
                ImageStrategy: sceneMeta?.ImageStrategy ?? SceneImageStrategy.ReuseFoundation,
                SourceImageAssetId: scene.ImageAssetId,
                SelectedImageAssetId: scene.ImageAssetId,
                MotionPromptText: scene.PromptText,
                AudioOption: scene.AudioAssetId != null ? AudioOptions.CustomAudio : AudioOptions.None,
                AudioAssetId: scene.AudioAssetId,
                VideoAssetId: scene.VideoAssetId,
                IsStale: true,
                StaleReason: "Đầu vào đã thay đổi khi tác vụ đang chạy. Kết quả được giữ trong lịch sử để người dùng quyết định.",
                ActiveJobId: null,
                ActiveJobType: null,
                ActiveJobState: null,
                ActiveJobProgressPercent: null,
                VideoCandidateHistory: candidateHistory,
                Revision: nowIso,
                StaleTaskTypes: staleTaskTypes.ToArray());
            var staleJson = JsonSerializer.Serialize(staleDetail, JsonOptions);
            if (sceneSetting == null)
            {
                dbContext.AppSettings.Add(new AppSetting { Key = sceneKey, Value = staleJson, UpdatedAtUtc = nowIso });
            }
            else
            {
                sceneSetting.Value = staleJson;
                sceneSetting.UpdatedAtUtc = nowIso;
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        // 2. Áp dụng kết quả Image -> Lưu ảnh và kích hoạt VideoPrompt
        if (job.TaskType == JobTaskType.Image)
        {
            // Kiểm tra stale: nếu user đã chỉnh sửa/chọn ảnh khác trong khi job đang chạy
            // Kết quả của job này chuyển vào candidateHistory thay vì ghi đè ảnh hiện tại (PV-R3)
            if (isUserModified && !string.IsNullOrWhiteSpace(scene.ImageAssetId) &&
                !string.Equals(scene.ImageAssetId, job.ResultReference, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(job.ResultReference) && !candidateHistory.Contains(job.ResultReference))
                {
                    candidateHistory.Add(job.ResultReference);
                }
            }
            else
            {
                scene.ImageAssetId = job.ResultReference;
                scene.UpdatedAtUtc = nowIso;
            }
            staleTaskTypes.Remove(JobTaskType.Image);

            // Kích hoạt bước tiếp theo: VideoPrompt
            // Chú ý: Kể cả khi scene.PromptText đã có draft từ Script, VẪN enqueue VideoPrompt
            // để AI writer hoàn thiện thành motion prompt đầy đủ với template đã render (§3.3, PV-R5, PV-R11)
            if (!isPaused)
            {
                var composition = await composer.ComposeVideoPromptAiRequestAsync(
                    config,
                    scene,
                    cancellationToken).ConfigureAwait(false);

                continuationIntent = new WorkflowContinuationIntent(
                    IntentId: Guid.NewGuid().ToString("D").ToLowerInvariant(),
                    ProjectId: job.ProjectId,
                    SceneId: scene.Id,
                    TaskType: JobTaskType.VideoPrompt,
                    CustomPromptText: composition.ComposedPrompt,
                    MediaSnapshot: BuildMediaSnapshot(config, scene),
                    RunId: config.CurrentRunId ?? string.Empty,
                    CreatedAtUtc: nowIso,
                    WorkflowStepKey: $"prompt.{scene.Id}.{scene.UpdatedAtUtc}"
                );
            }
        }
        // 3. Áp dụng kết quả VideoPrompt -> Lưu prompt và kích hoạt VideoRender
        else if (job.TaskType == JobTaskType.VideoPrompt)
        {
            var contentParsed = ContentCandidateParser.ParseVideoContent(
                job.ResultText ?? string.Empty,
                job.ProjectId,
                scene.Id,
                scene.SequenceOrder,
                scene.ImageAssetId);

            var contentItem = contentParsed.Candidate;

            // Kiểm tra stale: nếu user đã tự tay sửa PromptText trong khi job VideoPrompt đang chạy
            // Kết quả này không được ghi đè prompt đã sửa của user (PV-R3)
            if (isUserModified && !string.IsNullOrWhiteSpace(scene.PromptText) &&
                !string.Equals(scene.PromptText, job.ResultText, StringComparison.Ordinal))
            {
                // Giữ nguyên prompt của người dùng
            }
            else
            {
                scene.PromptText = contentItem?.VideoPrompt ?? job.ResultText;
                if (contentItem != null && !string.IsNullOrWhiteSpace(contentItem.Idea) && scene.Name.StartsWith("Cảnh "))
                {
                    scene.Name = contentItem.Idea;
                }
                scene.UpdatedAtUtc = nowIso;
            }
            staleTaskTypes.Remove(JobTaskType.VideoPrompt);

            // Lưu bản ghi VideoContentItem vào AppSettings để hiển thị dạng bảng
            if (contentItem != null)
            {
                await SaveOrUpdateContentItemAsync(dbContext, job.ProjectId, contentItem with
                {
                    ImageAssetId = scene.ImageAssetId,
                    VideoPrompt = scene.PromptText ?? contentItem.VideoPrompt,
                    Status = "ReadyForVideo",
                    UpdatedAtUtc = nowIso
                }, cancellationToken).ConfigureAwait(false);
            }

            // Pause at ReadyForVideo until the real video adapter has been verified.
            // The selected image and motion prompt remain persisted for a later render job.
        }
        // 4. Áp dụng kết quả VideoRender -> Lưu video và kiểm tra hoàn tất run
        else if (job.TaskType == JobTaskType.VideoRender)
        {
            scene.VideoAssetId = job.ResultReference;
            scene.Status = "Completed";
            scene.UpdatedAtUtc = nowIso;

            // Publish only a complete, linked video Asset as the Project's selected revision.
            // Chỉ render cho cả Project (ảnh của mọi phân cảnh) mới thành video cuối khi có nhiều cảnh; clip một cảnh thì không.
            var sceneCount = await dbContext.Scenes.CountAsync(s => s.ProjectId == job.ProjectId, cancellationToken).ConfigureAwait(false);
            var hasFinalVideo = false;
            if ((sceneCount == 1 || CountSnapshotImages(job.ConfigSnapshotJson) == sceneCount) && !string.IsNullOrWhiteSpace(job.ResultReference))
            {
                var outputAsset = await dbContext.Assets.FirstOrDefaultAsync(
                    a => a.Id == job.ResultReference && a.AssetType == "Video" && a.Status == "Linked",
                    cancellationToken).ConfigureAwait(false);
                var project = await dbContext.Projects.FirstOrDefaultAsync(
                    p => p.Id == job.ProjectId, cancellationToken).ConfigureAwait(false);
                if (outputAsset != null && project != null)
                {
                    hasFinalVideo = true;
                    var revisionNumber = await dbContext.VideoRevisions
                        .Where(v => v.ProjectId == project.Id)
                        .Select(v => (int?)v.Revision)
                        .MaxAsync(cancellationToken).ConfigureAwait(false) ?? 0;
                    var videoRevision = new VideoRevision
                    {
                        Id = Guid.NewGuid().ToString("D").ToLowerInvariant(),
                        ProjectId = project.Id,
                        Revision = revisionNumber + 1,
                        State = "Ready",
                        AssetId = outputAsset.Id,
                        ContentHash = outputAsset.Sha256Hash,
                        CreatedAtUtc = nowIso
                    };
                    dbContext.VideoRevisions.Add(videoRevision);
                    project.CurrentVideoRevisionId = videoRevision.Id;
                    project.Revision += 1;
                    project.UpdatedAtUtc = nowIso;
                }
            }

            // Cập nhật trạng thái hoàn tất vào VideoContentItem
            await UpdateContentItemVideoStatusAsync(dbContext, job.ProjectId, scene.Id, job.ResultReference, "Completed", nowIso, cancellationToken).ConfigureAwait(false);

            // Xóa cờ Stale khi đã có video mới khớp cấu hình
            if (sceneMeta != null && sceneMeta.IsStale)
            {
                sceneMeta = sceneMeta with { IsStale = false, StaleReason = null, StaleTaskTypes = Array.Empty<string>() };
            }
            staleTaskTypes.Clear();

            if (!string.IsNullOrWhiteSpace(job.ResultReference) && !candidateHistory.Contains(job.ResultReference))
            {
                candidateHistory.Add(job.ResultReference);
            }

            // Một render cho cả Project gắn video với phân cảnh neo; các phân cảnh khác chỉ giữ ảnh.
            if (hasFinalVideo)
            {
                var updatedConfig = config with
                {
                    RunState = WorkflowRunState.Completed,
                    UpdatedAtUtc = nowIso
                };
                var cfgKey = GetConfigKey(job.ProjectId);
                var cfgSetting = await dbContext.AppSettings.FirstOrDefaultAsync(s => s.Key == cfgKey, cancellationToken).ConfigureAwait(false);
                if (cfgSetting != null)
                {
                    cfgSetting.Value = JsonSerializer.Serialize(updatedConfig, JsonOptions);
                    cfgSetting.UpdatedAtUtc = nowIso;
                }
            }
        }

        // Cập nhật lại Scene AppSetting chứa candidateHistory mới nhất
        var updatedDetail = new SceneWorkflowDetailDto(
            SceneId: scene.Id,
            ProjectId: scene.ProjectId,
            SequenceOrder: scene.SequenceOrder,
            Name: scene.Name,
            TargetDurationSeconds: Convert.ToInt32(scene.TargetDurationSeconds),
            WorkflowStatus: staleTaskTypes.Count > 0 ? SceneWorkflowStatus.NeedsReview :
                            !string.IsNullOrWhiteSpace(scene.VideoAssetId) ? SceneWorkflowStatus.VideoCompleted :
                            !string.IsNullOrWhiteSpace(scene.ImageAssetId) && !string.IsNullOrWhiteSpace(scene.PromptText) ? SceneWorkflowStatus.ReadyForVideo :
                            !string.IsNullOrWhiteSpace(scene.ImageAssetId) ? SceneWorkflowStatus.PreparingPrompt : SceneWorkflowStatus.PreparingImage,
            ImageStrategy: sceneMeta?.ImageStrategy ?? SceneImageStrategy.ReuseFoundation,
            SourceImageAssetId: scene.ImageAssetId,
            SelectedImageAssetId: scene.ImageAssetId,
            MotionPromptText: scene.PromptText,
            AudioOption: scene.AudioAssetId != null ? AudioOptions.CustomAudio : AudioOptions.None,
            AudioAssetId: scene.AudioAssetId,
            VideoAssetId: scene.VideoAssetId,
            IsStale: staleTaskTypes.Count > 0,
            StaleReason: staleTaskTypes.Count > 0 ? sceneMeta?.StaleReason : null,
            ActiveJobId: null,
            ActiveJobType: null,
            ActiveJobState: null,
            ActiveJobProgressPercent: null,
            VideoCandidateHistory: candidateHistory,
            Revision: nowIso,
            StaleTaskTypes: staleTaskTypes.ToArray()
        );

        var updatedDetailJson = JsonSerializer.Serialize(updatedDetail, JsonOptions);
        if (sceneSetting == null)
        {
            dbContext.AppSettings.Add(new AppSetting { Key = sceneKey, Value = updatedDetailJson, UpdatedAtUtc = nowIso });
        }
        else
        {
            sceneSetting.Value = updatedDetailJson;
            sceneSetting.UpdatedAtUtc = nowIso;
        }

        // Nếu có continuation intent: commit vào AppSettings trong cùng một transaction (PV-R7)
        AppSetting? intentSetting = null;
        if (continuationIntent != null)
        {
            intentSetting = new AppSetting
            {
                Key = $"workflow.continuation.{continuationIntent.IntentId}",
                Value = JsonSerializer.Serialize(continuationIntent, JsonOptions),
                UpdatedAtUtc = nowIso
            };
            dbContext.AppSettings.Add(intentSetting);
        }

        // ATOMIC TRANSACTION: Commit scene changes + applied marker + continuation intent
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Dispatch continuation intent nếu có
        if (continuationIntent != null && _jobService != null)
        {
            try
            {
                await _jobService.EnqueueAsync(new JobEnqueueRequest(
                    ProjectId: continuationIntent.ProjectId,
                    SceneId: continuationIntent.SceneId,
                    TaskType: continuationIntent.TaskType,
                    CustomPromptText: continuationIntent.CustomPromptText,
                    WorkflowRunId: continuationIntent.RunId,
                    WorkflowStepKey: continuationIntent.WorkflowStepKey ?? GetWorkflowStepKey(continuationIntent.TaskType, continuationIntent.SceneId),
                    MediaSnapshot: continuationIntent.MediaSnapshot
                ), cancellationToken).ConfigureAwait(false);

                // Đã dispatch thành công -> xóa intent
                if (intentSetting != null)
                {
                    dbContext.AppSettings.Remove(intentSetting);
                    await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
                // Nếu dispatch bị lỗi/crash, intent vẫn tồn tại trong DB và sẽ được phục hồi bởi RunWorkflowContinuationSweepAsync
            }
        }

        return true;
    }

    /// <summary>
    /// Quét và tiếp tục thực hiện các ý định chuyển tiếp (Continuation Intents) chưa hoàn tất sau khi ứng dụng crash hoặc khởi động lại (PV-R7).
    /// </summary>
    public async Task<int> RunWorkflowContinuationSweepAsync(CancellationToken cancellationToken = default)
    {
        if (_jobService == null) return 0;

        await using var dbContext = new TiToAIDbContext(_dbOptions);
        var pendingIntentSettings = await dbContext.AppSettings
            .Where(s => s.Key.StartsWith("workflow.continuation."))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (pendingIntentSettings.Count == 0) return 0;

        int resumed = 0;
        foreach (var setting in pendingIntentSettings)
        {
            try
            {
                var intent = JsonSerializer.Deserialize<WorkflowContinuationIntent>(setting.Value, JsonOptions);
                if (intent != null)
                {
                    var config = await GetWorkflowConfigAsync(intent.ProjectId, cancellationToken).ConfigureAwait(false);
                    if (!string.Equals(config.CurrentRunId, intent.RunId, StringComparison.Ordinal) ||
                        config.RunState is WorkflowRunState.Idle or WorkflowRunState.Failed or WorkflowRunState.Completed)
                    {
                        dbContext.AppSettings.Remove(setting);
                        resumed++;
                        continue;
                    }

                    if (config.RunState == WorkflowRunState.Paused)
                    {
                        continue;
                    }

                    await _jobService.EnqueueAsync(new JobEnqueueRequest(
                        ProjectId: intent.ProjectId,
                        SceneId: intent.SceneId,
                        TaskType: intent.TaskType,
                        CustomPromptText: intent.CustomPromptText,
                        WorkflowRunId: intent.RunId,
                        WorkflowStepKey: intent.WorkflowStepKey ?? GetWorkflowStepKey(intent.TaskType, intent.SceneId),
                        MediaSnapshot: intent.MediaSnapshot
                    ), cancellationToken).ConfigureAwait(false);

                    dbContext.AppSettings.Remove(setting);
                    resumed++;
                }
            }
            catch
            {
                // Giữ lại intent để thử lại lần sau nếu có lỗi tạm thời
            }
        }

        if (resumed > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return resumed;
    }

    public async Task<int> RunWorkflowApplySweepAsync(CancellationToken cancellationToken = default)
    {
        if (_jobService == null) return 0;

        await using var dbContext = new TiToAIDbContext(_dbOptions);
        var succeededJobs = await dbContext.Jobs.AsNoTracking()
            .Where(job => job.State == JobState.Succeeded && job.ConfigSnapshotJson != null)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        int applied = 0;
        foreach (var job in succeededJobs)
        {
            if (GetWorkflowRunId(job.ConfigSnapshotJson) == null) continue;
            bool alreadyApplied = await dbContext.AppSettings.AsNoTracking()
                .AnyAsync(setting => setting.Key == $"workflow.applied.{job.Id}", cancellationToken)
                .ConfigureAwait(false);
            if (!alreadyApplied && await ApplyJobOutputAsync(job.Id, cancellationToken).ConfigureAwait(false))
            {
                applied++;
            }
        }

        return applied;
    }

    private static string? GetWorkflowRunId(string? configSnapshotJson)
    {
        if (string.IsNullOrWhiteSpace(configSnapshotJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(configSnapshotJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("WorkflowRunId", out var runId) && !root.TryGetProperty("workflowRunId", out runId))
            {
                return null;
            }
            return runId.ValueKind == JsonValueKind.String ? runId.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetWorkflowStepKey(string taskType, string? sceneId) => taskType switch
    {
        JobTaskType.Script => "script.planning",
        JobTaskType.Image => $"image.{sceneId}",
        JobTaskType.VideoPrompt => $"prompt.{sceneId}",
        JobTaskType.VideoRender => $"render.{sceneId}",
        _ => $"{taskType.ToLowerInvariant()}.{sceneId}"
    };

    private static int CountSnapshotImages(string? configSnapshotJson)
    {
        if (string.IsNullOrWhiteSpace(configSnapshotJson)) return 0;
        using var doc = JsonDocument.Parse(configSnapshotJson);
        return doc.RootElement.TryGetProperty("MediaSnapshot", out var media) &&
               media.TryGetProperty("ImageAssetIds", out var ids) && ids.ValueKind == JsonValueKind.Array
            ? ids.GetArrayLength()
            : 0;
    }

    /// <summary>Storyboard hợp lệ thì trả về; kết quả Script không phải storyboard sẽ đi theo đường scenes cũ.</summary>
    private static StoryboardContent? TryParseStoryboard(string resultText, int durationSeconds)
    {
        try
        {
            return StoryboardContentService.Parse(resultText, durationSeconds);
        }
        catch (ProjectWorkflowValidationException)
        {
            return null;
        }
    }

    /// <summary>Mỗi shot thành một phân cảnh theo thứ tự; imagePrompt được giữ làm purpose để tạo ảnh.</summary>
    private static IReadOnlyList<ScenePlanItemDto> StoryboardScenePlan(StoryboardContent storyboard) =>
        storyboard.Shots.Select((shot, index) => new ScenePlanItemDto(
            SequenceOrder: index + 1,
            Name: shot.Description,
            Purpose: shot.ImagePrompt,
            TargetDurationSeconds: shot.EndSeconds - shot.StartSeconds,
            ImageStrategy: SceneImageStrategy.ReuseFoundation,
            MotionPromptDraft: string.Empty)).ToArray();

    private static IReadOnlyList<ScenePlanItemDto> ExtractScenePlanItems(string resultText, int fallbackCount, int fallbackDuration)
    {
        var cleaned = resultText.Trim();
        if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring(7);
        }
        if (cleaned.StartsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring(3);
        }
        if (cleaned.EndsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring(0, cleaned.Length - 3);
        }
        cleaned = cleaned.Trim();

        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            if (doc.RootElement.TryGetProperty("scenes", out var scenesElement) && scenesElement.ValueKind == JsonValueKind.Array)
            {
                var list = new List<ScenePlanItemDto>();
                int seq = 1;
                foreach (var el in scenesElement.EnumerateArray())
                {
                    string purpose = el.TryGetProperty("purpose", out var p) ? p.GetString() ?? $"Cảnh {seq}" : $"Cảnh {seq}";
                    string name = el.TryGetProperty("name", out var n) ? n.GetString() ?? $"Cảnh {seq}: {purpose}" : $"Cảnh {seq}: {purpose}";
                    string motionPrompt = el.TryGetProperty("motionPrompt", out var m) ? m.GetString() ?? string.Empty : string.Empty;
                    int duration = el.TryGetProperty("targetDurationSeconds", out var d) && d.TryGetInt32(out var dVal) ? dVal : fallbackDuration;

                    list.Add(new ScenePlanItemDto(
                        SequenceOrder: seq++,
                        Name: name,
                        Purpose: purpose,
                        TargetDurationSeconds: duration,
                        ImageStrategy: SceneImageStrategy.ReuseFoundation,
                        MotionPromptDraft: motionPrompt
                    ));
                }
                if (list.Count == Math.Max(1, fallbackCount))
                {
                    return list;
                }
            }

            if (doc.RootElement.TryGetProperty("idea", out var ideaEl) || doc.RootElement.TryGetProperty("video_prompt", out _))
            {
                string idea = doc.RootElement.TryGetProperty("idea", out var ie) ? ie.GetString() ?? "Cảnh 1" : "Cảnh 1";
                string hook = doc.RootElement.TryGetProperty("hook", out var he) ? he.GetString() ?? "" : "";
                string prompt = doc.RootElement.TryGetProperty("video_prompt", out var vpe) ? vpe.GetString() ?? "" : "";
                return new[]
                {
                    new ScenePlanItemDto(
                        SequenceOrder: 1,
                        Name: idea,
                        Purpose: hook,
                        TargetDurationSeconds: fallbackDuration,
                        ImageStrategy: SceneImageStrategy.ReuseFoundation,
                        MotionPromptDraft: prompt
                    )
                };
            }
        }
        catch
        {
            // Fallback
        }

        return Array.Empty<ScenePlanItemDto>();
    }
}
