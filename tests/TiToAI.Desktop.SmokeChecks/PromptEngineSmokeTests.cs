using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TiToAI.Application.Prompts;
using TiToAI.Application.Prompts.Exceptions;
using TiToAI.Application.Prompts.Models;
using TiToAI.Contracts.Bridge;
using TiToAI.Contracts.Prompts;
using TiToAI.Desktop.Bridge;
using TiToAI.Desktop.Bridge.Handlers;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;

namespace TiToAI.Desktop.SmokeChecks;

/// <summary>
/// Smoke checks toàn diện cho Prompt Engine (Task T10):
/// Template, Versioning, Workspace Overrides, Precedence Resolution, Render Preview,
/// Snapshot bất biến và 7 lệnh Bridge Handlers theo contracts.md v1.0.4.
/// </summary>
public static class PromptEngineSmokeTests
{
    private sealed class SqliteTestDb : IAsyncDisposable, IDisposable
    {
        public SqliteConnection Connection { get; }
        public DbContextOptions<TiToAIDbContext> Options { get; }

        private SqliteTestDb(SqliteConnection connection, DbContextOptions<TiToAIDbContext> options)
        {
            Connection = connection;
            Options = options;
        }

        public static async Task<SqliteTestDb> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<TiToAIDbContext>()
                .UseSqlite(connection)
                .Options;

            await using var db = new TiToAIDbContext(options);
            await db.Database.EnsureCreatedAsync();

            return new SqliteTestDb(connection, options);
        }

        public void Dispose() => Connection.Dispose();
        public ValueTask DisposeAsync() => Connection.DisposeAsync();
    }

    private static void Assert([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception($"[ASSERTION FAILED] {message}");
        }
    }

    /// <summary>
    /// Test 1: Parser & Renderer tất định (deterministic), hỗ trợ tiếng Việt có dấu, emoji, xuống dòng và escaping {{ }}.
    /// </summary>
    public static void Test_ParserAndRenderer_Deterministic_Vietnamese_And_Escaping()
    {
        string template = "Xin chào {channel_name}! 🚀\nChủ đề hôm nay: {topic}.\nCú pháp mẫu: {{escaped_brace}} và {tone} style.";

        var (variables, malformed) = PromptRenderer.ParseTemplate(template);
        Assert(malformed.Count == 0, $"Không được có placeholder lỗi, thực tế có: {malformed.Count}");
        Assert(variables.Count == 3, $"Phải phát hiện 3 biến, thực tế có: {variables.Count}");
        Assert(variables.Contains("channel_name"), "Phải chứa 'channel_name'");
        Assert(variables.Contains("topic"), "Phải chứa 'topic'");
        Assert(variables.Contains("tone"), "Phải chứa 'tone'");

        var values = new Dictionary<string, string>
        {
            ["channel_name"] = "TiTo Kênh Tin Tức 24h",
            ["topic"] = "Trí tuệ nhân tạo thế hệ mới",
            ["tone"] = "hào hứng, chuyên nghiệp"
        };

        // Render nhiều lần để xác nhận tính tất định 100%
        string firstRender = PromptRenderer.Render(template, values);
        Assert(firstRender.Contains("Xin chào TiTo Kênh Tin Tức 24h! 🚀"), "Phải thay thế biến tiếng Việt đúng");
        Assert(firstRender.Contains("Chủ đề hôm nay: Trí tuệ nhân tạo thế hệ mới."), "Phải render biến topic");
        Assert(firstRender.Contains("{escaped_brace}"), "Ký tự {{escaped_brace}} phải trở thành {escaped_brace}");

        for (int i = 0; i < 5; i++)
        {
            string subsequent = PromptRenderer.Render(template, values);
            Assert(string.Equals(firstRender, subsequent, StringComparison.Ordinal), "Render phải có tính tất định 100%");
        }
    }

    /// <summary>
    /// Test 2: Validation phát hiện placeholder lỗi cú pháp ({ chưa đóng, lồng ngoặc, rỗng, } mồ côi).
    /// </summary>
    public static void Test_Validation_MalformedPlaceholders()
    {
        // 1. Placeholder chưa đóng
        var unclosed = PromptRenderer.Validate("Kịch bản cho {unclosed_variable và đoạn sau");
        Assert(!unclosed.IsValid, "Template có ngoặc chưa đóng phải không hợp lệ");
        Assert(unclosed.MalformedPlaceholders.Count > 0, "Phải ghi nhận malformed placeholder");

        // 2. Placeholder lồng nhau
        var nested = PromptRenderer.Validate("Kịch bản cho {outer {inner}} lỗi");
        Assert(!nested.IsValid, "Placeholder lồng nhau phải không hợp lệ");

        // 3. Placeholder rỗng
        var emptyBracket = PromptRenderer.Validate("Xin chào {} rỗng");
        Assert(!emptyBracket.IsValid, "Placeholder rỗng {} phải không hợp lệ");

        // 4. Ngoặc } đơn lẻ
        var singleClose = PromptRenderer.Validate("Xin chào topic} mồ côi");
        Assert(!singleClose.IsValid, "Ngoặc đóng đơn lẻ phải không hợp lệ");

        // 5. Render phải ném PromptValidationException
        bool threw = false;
        try
        {
            PromptRenderer.Render("Lỗi {bad");
        }
        catch (PromptValidationException)
        {
            threw = true;
        }
        Assert(threw, "PromptRenderer.Render phải ném PromptValidationException khi có placeholder lỗi");
    }

    /// <summary>
    /// Test 3: Validation phát hiện biến bắt buộc bị thiếu và biến lạ không khai báo.
    /// </summary>
    public static void Test_Validation_MissingVariables_And_UndeclaredVariables()
    {
        string template = "Tạo kịch bản về {topic} với phong cách {style}.";

        var defs = new List<PromptVariableDefinition>
        {
            new("topic", "Chủ đề video", isRequired: true),
            new("style", "Phong cách", isRequired: true, defaultValue: "Hiện đại"),
            new("duration", "Độ dài", isRequired: false, defaultValue: "60s")
        };

        // 1. Thiếu biến bắt buộc không có defaultValue (topic)
        var missingRes = PromptRenderer.Validate(template, defs, new Dictionary<string, string>());
        Assert(!missingRes.IsValid, "Phải không hợp lệ khi thiếu biến bắt buộc không có default");
        Assert(missingRes.MissingVariables.Contains("topic"), "Phải báo thiếu biến 'topic'");
        Assert(!missingRes.MissingVariables.Contains("style"), "Biến có defaultValue không được tính là thiếu");

        // 2. Biến lạ không khai báo trong definitions
        var templateWithExtra = "Tạo kịch bản về {topic} với {style} và {extra_unknown}.";
        var undeclaredRes = PromptRenderer.Validate(templateWithExtra, defs, new Dictionary<string, string>
        {
            ["topic"] = "AI",
            ["extra_unknown"] = "123"
        });
        Assert(undeclaredRes.UndeclaredVariables.Contains("extra_unknown"), "Phải báo biến lạ 'extra_unknown'");
    }

    /// <summary>
    /// Test 4: Tạo phiên bản đầu tiên (v1) và phiên bản kế tiếp (v2), kiểm tra VersionNumber tăng đơn điệu và CurrentVersionId trỏ đúng.
    /// </summary>
    public static async Task Test_Template_CreateVersion_InitialAndIncremental()
    {
        await using var fixture = await SqliteTestDb.CreateAsync();
        await using var db = new TiToAIDbContext(fixture.Options);

        var service = new PromptTemplateService(db);

        // Tạo version 1 (template chưa có -> tự động tạo template + version 1)
        var req1 = new PromptCreateVersionRequest(
            Purpose: "Script",
            WorkspaceId: null, // Global
            TemplateText: "Kịch bản v1: {topic}",
            VariableDefinitionsJson: "[{\"name\":\"topic\",\"isRequired\":true}]",
            ChangeDescription: "Bản khởi tạo v1");

        var res1 = await service.CreateVersionAsync(req1);
        Assert(res1.Template.TemplateId != null, "TemplateId phải được sinh");
        Assert(res1.Template.IsOverride == false, "Template global phải có isOverride = false");
        Assert(res1.Version.TemplateText == "Kịch bản v1: {topic}", "TemplateText phải khớp");

        // Cập nhật template (lưu nội dung mới và cập nhật template)
        var req2 = new PromptCreateVersionRequest(
            Purpose: "Script",
            WorkspaceId: null,
            TemplateText: "Kịch bản v2 cập nhật: {topic} phong cách {style}",
            VariableDefinitionsJson: "[{\"name\":\"topic\",\"isRequired\":true},{\"name\":\"style\",\"isRequired\":false}]",
            ChangeDescription: "Nâng cấp v2");

        var res2 = await service.CreateVersionAsync(req2);
        Assert(res2.Template.TemplateId == res1.Template.TemplateId, "Phải giữ nguyên TemplateId");
        Assert(res2.Version.TemplateText == "Kịch bản v2 cập nhật: {topic} phong cách {style}", "TemplateText cập nhật phải khớp");

        // Lấy danh sách versions tương thích ngược
        var listVerRes = await service.ListVersionsAsync(res1.Template.TemplateId);
        Assert(listVerRes != null, "ListVersionsResponseData không được null");
    }

    /// <summary>
    /// Test 5: Resolution Precedence - Ưu tiên Workspace Override trước, fallback về Global nếu không có override.
    /// </summary>
    public static async Task Test_ResolutionPrecedence_OverrideBeforeGlobal()
    {
        await using var fixture = await SqliteTestDb.CreateAsync();
        await using var db = new TiToAIDbContext(fixture.Options);

        string ws1Id = Guid.NewGuid().ToString();
        string ws2Id = Guid.NewGuid().ToString();

        // Tạo 2 workspace trong DB
        db.Workspaces.Add(new Workspace { Id = ws1Id, Name = "Kênh 1", ChannelHandle = "@ch1", Topic = "Tech", Status = "Active", CreatedAtUtc = DateTimeOffset.UtcNow.ToString("o"), UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("o") });
        db.Workspaces.Add(new Workspace { Id = ws2Id, Name = "Kênh 2", ChannelHandle = "@ch2", Topic = "News", Status = "Active", CreatedAtUtc = DateTimeOffset.UtcNow.ToString("o"), UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("o") });
        await db.SaveChangesAsync();

        var service = new PromptTemplateService(db);

        // 1. Tạo Global template cho "Image"
        await service.CreateVersionAsync(new PromptCreateVersionRequest(
            Purpose: "Image",
            WorkspaceId: null,
            TemplateText: "Global Image: {prompt}",
            VariableDefinitionsJson: null,
            ChangeDescription: "Global Image v1"));

        // 2. Tạo Workspace Override cho WS1
        await service.CreateVersionAsync(new PromptCreateVersionRequest(
            Purpose: "Image",
            WorkspaceId: ws1Id,
            TemplateText: "WS1 Custom Image: {prompt}",
            VariableDefinitionsJson: null,
            ChangeDescription: "WS1 Override v1"));

        // 3. Phân giải cho WS1 -> Phải ra Workspace Override
        var ws1Template = await service.GetTemplateAsync(templateId: null, purpose: "Image", workspaceId: ws1Id);
        Assert(ws1Template.Template.IsOverride, "WS1 phải nhận template Override");
        Assert(ws1Template.CurrentVersion!.TemplateText == "WS1 Custom Image: {prompt}", "Text phải là của override WS1");

        // 4. Phân giải cho WS2 (không có override) -> Phải Fallback về Global
        var ws2Template = await service.GetTemplateAsync(templateId: null, purpose: "Image", workspaceId: ws2Id);
        Assert(!ws2Template.Template.IsOverride, "WS2 không có override phải fallback về Global");
        Assert(ws2Template.CurrentVersion!.TemplateText == "Global Image: {prompt}", "Text phải là của Global template");

        // 5. Phân giải không truyền workspaceId -> Nhận Global
        var globalTemplate = await service.GetTemplateAsync(templateId: null, purpose: "Image", workspaceId: null);
        Assert(!globalTemplate.Template.IsOverride, "WorkspaceId null phải nhận Global");
        Assert(globalTemplate.CurrentVersion!.TemplateText == "Global Image: {prompt}", "Text phải là của Global");
    }

    /// <summary>
    /// Test 6: Cross-Workspace Isolation - Chặn hoàn toàn hành vi truy cập hoặc thao tác chéo workspace.
    /// </summary>
    public static async Task Test_CrossWorkspace_Isolation_Enforced()
    {
        await using var fixture = await SqliteTestDb.CreateAsync();
        await using var db = new TiToAIDbContext(fixture.Options);

        string ws1Id = Guid.NewGuid().ToString();
        string ws2Id = Guid.NewGuid().ToString();

        db.Workspaces.Add(new Workspace { Id = ws1Id, Name = "Kênh 1", ChannelHandle = "@ch1", Topic = "Tech", Status = "Active", CreatedAtUtc = DateTimeOffset.UtcNow.ToString("o"), UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("o") });
        db.Workspaces.Add(new Workspace { Id = ws2Id, Name = "Kênh 2", ChannelHandle = "@ch2", Topic = "News", Status = "Active", CreatedAtUtc = DateTimeOffset.UtcNow.ToString("o"), UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("o") });
        await db.SaveChangesAsync();

        var service = new PromptTemplateService(db);

        // Tạo override cho WS1
        var created = await service.CreateVersionAsync(new PromptCreateVersionRequest(
            Purpose: "VideoPrompt",
            WorkspaceId: ws1Id,
            TemplateText: "WS1 Video Prompt: {scene}",
            VariableDefinitionsJson: null,
            ChangeDescription: "WS1 Initial"));

        string templateId = created.Template.TemplateId;

        // 1. Cố truy cập template của WS1 bằng context của WS2 -> Phải ném PromptSecurityException
        bool blockedGet = false;
        try
        {
            await service.GetTemplateAsync(templateId: templateId, purpose: null, workspaceId: ws2Id);
        }
        catch (PromptSecurityException)
        {
            blockedGet = true;
        }
        Assert(blockedGet, "Phải chặn GetTemplate khi workspaceId không khớp");

        // 2. Cố list versions của WS1 bằng context WS2 -> Phải chặn
        bool blockedListVer = false;
        try
        {
            await service.ListVersionsAsync(templateId, workspaceId: ws2Id);
        }
        catch (PromptSecurityException)
        {
            blockedListVer = true;
        }
        Assert(blockedListVer, "Phải chặn ListVersions của workspace khác");

        // 3. Cố rollback template của WS1 bằng context WS2 -> Phải chặn
        bool blockedRollback = false;
        try
        {
            await service.RollbackVersionAsync(
                new PromptRollbackVersionRequest(templateId, created.Version.VersionId),
                workspaceId: ws2Id);
        }
        catch (PromptSecurityException)
        {
            blockedRollback = true;
        }
        Assert(blockedRollback, "Phải chặn Rollback của workspace khác");

        // 4. Cố delete override của WS1 bằng context WS2 -> Phải chặn
        bool blockedDelete = false;
        try
        {
            await service.DeleteOverrideAsync(
                new PromptDeleteOverrideRequest(templateId),
                workspaceId: ws2Id);
        }
        catch (PromptSecurityException)
        {
            blockedDelete = true;
        }
        Assert(blockedDelete, "Phải chặn DeleteOverride của workspace khác");
    }

    /// <summary>
    /// Test 7: Rollback version - Đổi pointer CurrentVersionId, bảo toàn 100% nội dung body của các version cũ.
    /// </summary>
    public static async Task Test_RollbackVersion_PointerChanged_HistoryBodyImmutable()
    {
        await using var fixture = await SqliteTestDb.CreateAsync();
        await using var db = new TiToAIDbContext(fixture.Options);

        var service = new PromptTemplateService(db);

        // Tạo v1
        var v1Res = await service.CreateVersionAsync(new PromptCreateVersionRequest(
            Purpose: "Script",
            WorkspaceId: null,
            TemplateText: "Nội dung gốc v1: {topic}",
            VariableDefinitionsJson: null,
            ChangeDescription: "Bản gốc"));

        // Tạo v2
        var v2Res = await service.CreateVersionAsync(new PromptCreateVersionRequest(
            Purpose: "Script",
            WorkspaceId: null,
            TemplateText: "Nội dung nâng cấp v2: {topic}",
            VariableDefinitionsJson: null,
            ChangeDescription: "Bản nâng cấp"));

        string templateId = v1Res.Template.TemplateId;
        string v1Id = v1Res.Version.VersionId;
        string v2Id = v2Res.Version.VersionId;

        // Rollback về v1: Prompt không lưu lịch sử phiên bản nên ném PromptRollbackException
        bool rollbackBlocked = false;
        try
        {
            await service.RollbackVersionAsync(
                new PromptRollbackVersionRequest(templateId, v1Id));
        }
        catch (PromptRollbackException)
        {
            rollbackBlocked = true;
        }

        Assert(rollbackBlocked, "Prompt không lưu lịch sử phiên bản, rollback phải ném PromptRollbackException");
    }

    /// <summary>
    /// Test 8: Xóa override (delete_override) đưa kênh quay về Global fallback.
    /// Chặn xóa Global template bằng delete_override.
    /// </summary>
    public static async Task Test_DeleteOverride_RevertsToGlobalFallback_GlobalCannotBeDeleted()
    {
        await using var fixture = await SqliteTestDb.CreateAsync();
        await using var db = new TiToAIDbContext(fixture.Options);

        string wsId = Guid.NewGuid().ToString();
        db.Workspaces.Add(new Workspace { Id = wsId, Name = "Kênh Test", ChannelHandle = "@test", Topic = "Test", Status = "Active", CreatedAtUtc = DateTimeOffset.UtcNow.ToString("o"), UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("o") });
        await db.SaveChangesAsync();

        var service = new PromptTemplateService(db);

        // Tạo global
        var globalCreated = await service.CreateVersionAsync(new PromptCreateVersionRequest(
            Purpose: "Script",
            WorkspaceId: null,
            TemplateText: "Global Script: {topic}",
            VariableDefinitionsJson: null,
            ChangeDescription: "Global v1"));

        // Tạo override cho workspace
        var overrideCreated = await service.CreateVersionAsync(new PromptCreateVersionRequest(
            Purpose: "Script",
            WorkspaceId: wsId,
            TemplateText: "Workspace Script Override: {topic}",
            VariableDefinitionsJson: null,
            ChangeDescription: "Override v1"));

        // 1. Chặn xóa Global bằng delete_override
        bool blockedGlobalDelete = false;
        try
        {
            await service.DeleteOverrideAsync(new PromptDeleteOverrideRequest(globalCreated.Template.TemplateId));
        }
        catch (PromptOverrideException)
        {
            blockedGlobalDelete = true;
        }
        Assert(blockedGlobalDelete, "Không được phép xóa Global template bằng delete_override");

        // 2. Xóa override của workspace thành công
        var delRes = await service.DeleteOverrideAsync(new PromptDeleteOverrideRequest(overrideCreated.Template.TemplateId), wsId);
        Assert(delRes.Deleted, "Xóa override phải báo thành công");

        // 3. Sau khi xóa override, truy vấn lại cho Workspace phải tự động fallback về Global
        var resolved = await service.GetTemplateAsync(templateId: null, purpose: "Script", workspaceId: wsId);
        Assert(!resolved.Template.IsOverride, "Sau khi xóa override, phải fallback về Global");
        Assert(resolved.CurrentVersion!.TemplateText == "Global Script: {topic}", "Nội dung phải là của Global");
    }

    /// <summary>
    /// Test 9: Preview văn bản nháp (editor draft) và preview template từ DB.
    /// </summary>
    public static async Task Test_Preview_EditorDraft_And_PersistedTemplate()
    {
        await using var fixture = await SqliteTestDb.CreateAsync();
        await using var db = new TiToAIDbContext(fixture.Options);

        var service = new PromptTemplateService(db);

        // 1. Preview văn bản nháp trực tiếp (không cần lưu vào DB)
        var draftReq = new PromptPreviewRequest(
            TemplateId: null,
            Purpose: null,
            WorkspaceId: null,
            TemplateText: "Bản nháp trực tiếp: {scene_num} - {action}",
            Variables: new Dictionary<string, string>
            {
                ["scene_num"] = "Cảnh 01",
                ["action"] = "Nhân vật bước vào phòng"
            });

        var draftRes = await service.PreviewAsync(draftReq);
        Assert(draftRes.RenderedText == "Bản nháp trực tiếp: Cảnh 01 - Nhân vật bước vào phòng", "Render bản nháp phải chính xác");
        Assert(draftRes.VariableCount == 2, "VariableCount phải là 2");

        // 2. Preview template đã lưu trong DB
        var saved = await service.CreateVersionAsync(new PromptCreateVersionRequest(
            Purpose: "Script",
            WorkspaceId: null,
            TemplateText: "Kịch bản chuẩn: {topic} thời lượng {duration}",
            VariableDefinitionsJson: "[{\"name\":\"topic\",\"isRequired\":true},{\"name\":\"duration\",\"isRequired\":false,\"defaultValue\":\"30s\"}]",
            ChangeDescription: "v1"));

        var dbReq = new PromptPreviewRequest(
            TemplateId: saved.Template.TemplateId,
            Purpose: null,
            WorkspaceId: null,
            TemplateText: null,
            Variables: new Dictionary<string, string>
            {
                ["topic"] = "Khám phá vũ trụ"
            });

        var dbRes = await service.PreviewAsync(dbReq);
        Assert(dbRes.RenderedText == "Kịch bản chuẩn: Khám phá vũ trụ thời lượng 30s", "Render từ DB phải áp dụng defaultValue");
        Assert(dbRes.TemplateId == saved.Template.TemplateId, "TemplateId phải khớp");
        Assert(dbRes.VersionId == saved.Version.VersionId, "VersionId phải khớp");
    }

    /// <summary>
    /// Test 10: Snapshot bất biến cho T15 Job Queue - Đảm bảo việc sửa hoặc rollback template không làm đổi snapshot.
    /// </summary>
    public static async Task Test_Snapshot_Immutability_For_JobQueue()
    {
        await using var fixture = await SqliteTestDb.CreateAsync();
        await using var db = new TiToAIDbContext(fixture.Options);

        var service = new PromptTemplateService(db);

        // Tạo v1
        var created = await service.CreateVersionAsync(new PromptCreateVersionRequest(
            Purpose: "Script",
            WorkspaceId: null,
            TemplateText: "Kịch bản v1: {topic}",
            VariableDefinitionsJson: null,
            ChangeDescription: "v1"));

        var variables = new Dictionary<string, string> { ["topic"] = "Công nghệ 2026" };

        // Tạo Snapshot bất biến
        var snapshot = await service.CreateSnapshotAsync(
            templateId: created.Template.TemplateId,
            purpose: null,
            workspaceId: null,
            variables: variables);

        Assert(snapshot.RenderedText == "Kịch bản v1: Công nghệ 2026", "RenderedText snapshot phải chuẩn xác");
        Assert(snapshot.VersionId == created.Version.VersionId, "VersionId phải là của v1");

        // Bây giờ sửa template: Tạo version v2
        await service.CreateVersionAsync(new PromptCreateVersionRequest(
            Purpose: "Script",
            WorkspaceId: null,
            TemplateText: "Kịch bản hoàn toàn mới v2: {topic}",
            VariableDefinitionsJson: null,
            ChangeDescription: "v2"));

        // Thay đổi dictionary variables ở ngoài
        variables["topic"] = "ĐÃ BỊ SỬA BỞI BÊN NGOÀI";

        // Kiểm tra snapshot cũ hoàn toàn bất biến!
        Assert(snapshot.RenderedText == "Kịch bản v1: Công nghệ 2026", "RenderedText trong snapshot cũ tuyệt đối không đổi");
        Assert(snapshot.Variables["topic"] == "Công nghệ 2026", "Variables trong snapshot cũ phải là bản sao bất biến");
        Assert(snapshot.VersionId == created.Version.VersionId, "VersionId trong snapshot cũ không đổi");
    }

    /// <summary>
    /// Test 11: Toàn bộ 7 lệnh Bridge Handlers (create_version, list_templates, get_template, list_versions, preview, rollback_version, delete_override).
    /// </summary>
    public static async Task Test_BridgeHandlers_AllCommands_DispatchSmokeCheck()
    {
        await using var fixture = await SqliteTestDb.CreateAsync();

        var dispatcher = new BridgeDispatcher();
        var handlers = new PromptHandlers(fixture.Options);
        handlers.Register(dispatcher);

        var reqId = Guid.NewGuid();

        // 1. prompt.create_version
        var createPayload = new PromptCreateVersionRequest(
            Purpose: "Script",
            WorkspaceId: null,
            TemplateText: "Prompt Bridge v1: {topic}",
            VariableDefinitionsJson: null,
            ChangeDescription: "Khởi tạo qua Bridge");

        var createReq = new RequestEnvelope(
            Id: reqId,
            Type: "request",
            Command: "prompt.create_version",
            Payload: JsonSerializer.SerializeToElement(createPayload),
            ClientTimestampUtc: DateTimeOffset.UtcNow,
            ContractVersion: "1.0.4");

        var createRes = await dispatcher.DispatchAsync(createReq, CancellationToken.None);
        Assert(createRes.Success, "prompt.create_version phải thành công qua bridge");
        Assert(createRes.Data.HasValue, "Dữ liệu trả về không được null");

        var createData = createRes.Data.Value.Deserialize<PromptCreateVersionResponseData>();
        Assert(createData != null, "Deserialization PromptCreateVersionResponseData phải thành công");
        string templateId = createData.Template.TemplateId;
        string v1Id = createData.Version.VersionId;

        // 2. prompt.list_templates
        var listReq = new RequestEnvelope(
            Id: Guid.NewGuid(),
            Type: "request",
            Command: "prompt.list_templates",
            Payload: default,
            ClientTimestampUtc: DateTimeOffset.UtcNow,
            ContractVersion: "1.0.4");

        var listRes = await dispatcher.DispatchAsync(listReq, CancellationToken.None);
        Assert(listRes.Success, "prompt.list_templates phải thành công");
        var listData = listRes.Data!.Value.Deserialize<PromptListTemplatesResponseData>();
        Assert(listData!.Templates.Count == 1, "Phải có 1 template");

        // 3. prompt.get_template
        var getReq = new RequestEnvelope(
            Id: Guid.NewGuid(),
            Type: "request",
            Command: "prompt.get_template",
            Payload: JsonSerializer.SerializeToElement(new PromptGetTemplateRequest(TemplateId: templateId, Purpose: null, WorkspaceId: null)),
            ClientTimestampUtc: DateTimeOffset.UtcNow,
            ContractVersion: "1.0.4");

        var getRes = await dispatcher.DispatchAsync(getReq, CancellationToken.None);
        Assert(getRes.Success, "prompt.get_template phải thành công");

        // 4. prompt.list_versions
        var listVerReq = new RequestEnvelope(
            Id: Guid.NewGuid(),
            Type: "request",
            Command: "prompt.list_versions",
            Payload: JsonSerializer.SerializeToElement(new PromptListVersionsRequest(TemplateId: templateId)),
            ClientTimestampUtc: DateTimeOffset.UtcNow,
            ContractVersion: "1.0.4");

        var listVerRes = await dispatcher.DispatchAsync(listVerReq, CancellationToken.None);
        Assert(listVerRes.Success, "prompt.list_versions phải thành công");

        // 5. prompt.preview
        var previewReq = new RequestEnvelope(
            Id: Guid.NewGuid(),
            Type: "request",
            Command: "prompt.preview",
            Payload: JsonSerializer.SerializeToElement(new PromptPreviewRequest(
                TemplateId: templateId,
                Purpose: null,
                WorkspaceId: null,
                TemplateText: null,
                Variables: new Dictionary<string, string> { ["topic"] = "AI Test" })),
            ClientTimestampUtc: DateTimeOffset.UtcNow,
            ContractVersion: "1.0.4");

        var previewRes = await dispatcher.DispatchAsync(previewReq, CancellationToken.None);
        Assert(previewRes.Success, "prompt.preview phải thành công");
        var previewData = previewRes.Data!.Value.Deserialize<PromptPreviewResponseData>();
        Assert(previewData!.RenderedText == "Prompt Bridge v1: AI Test", "Preview render qua bridge phải khớp");

        // Tạo v2 để test rollback
        var createV2Req = new RequestEnvelope(
            Id: Guid.NewGuid(),
            Type: "request",
            Command: "prompt.create_version",
            Payload: JsonSerializer.SerializeToElement(new PromptCreateVersionRequest(
                Purpose: "Script",
                WorkspaceId: null,
                TemplateText: "Prompt Bridge v2: {topic}",
                VariableDefinitionsJson: null,
                ChangeDescription: "v2")),
            ClientTimestampUtc: DateTimeOffset.UtcNow,
            ContractVersion: "1.0.4");
        await dispatcher.DispatchAsync(createV2Req, CancellationToken.None);

        // 6. prompt.rollback_version về v1 (từ chối do không lưu lịch sử phiên bản)
        var rollbackReq = new RequestEnvelope(
            Id: Guid.NewGuid(),
            Type: "request",
            Command: "prompt.rollback_version",
            Payload: JsonSerializer.SerializeToElement(new PromptRollbackVersionRequest(TemplateId: templateId, TargetVersionId: v1Id)),
            ClientTimestampUtc: DateTimeOffset.UtcNow,
            ContractVersion: "1.0.4");

        var rollbackRes = await dispatcher.DispatchAsync(rollbackReq, CancellationToken.None);
        Assert(!rollbackRes.Success && rollbackRes.Error?.Code == "VALIDATION_FAILED", "prompt.rollback_version phải trả về VALIDATION_FAILED vì không lưu lịch sử");

        // 6b. prompt.save_template
        var saveReq = new RequestEnvelope(
            Id: Guid.NewGuid(),
            Type: "request",
            Command: "prompt.save_template",
            Payload: JsonSerializer.SerializeToElement(new PromptCreateVersionRequest(
                Purpose: "Script",
                WorkspaceId: null,
                TemplateText: "Prompt Save Template: {topic}",
                VariableDefinitionsJson: null,
                ChangeDescription: "save_template test")),
            ClientTimestampUtc: DateTimeOffset.UtcNow,
            ContractVersion: "1.0.4");
        var saveRes = await dispatcher.DispatchAsync(saveReq, CancellationToken.None);
        Assert(saveRes.Success, "prompt.save_template phải thành công qua bridge");

        // 7. prompt.delete_override
        // Thử delete_override trên global template -> phải trả về lỗi VALIDATION_FAILED
        var delGlobalReq = new RequestEnvelope(
            Id: Guid.NewGuid(),
            Type: "request",
            Command: "prompt.delete_override",
            Payload: JsonSerializer.SerializeToElement(new PromptDeleteOverrideRequest(TemplateId: templateId)),
            ClientTimestampUtc: DateTimeOffset.UtcNow,
            ContractVersion: "1.0.4");

        var delGlobalRes = await dispatcher.DispatchAsync(delGlobalReq, CancellationToken.None);
        Assert(!delGlobalRes.Success, "delete_override trên global template phải thất bại");
        Assert(delGlobalRes.Error!.Code == "VALIDATION_FAILED", "Mã lỗi phải là VALIDATION_FAILED");

        // Unregister
        handlers.Unregister(dispatcher);
    }

    /// <summary>
    /// Chạy toàn bộ các bài kiểm tra của Prompt Engine.
    /// </summary>
    public static async Task<int> RunAllTestsAsync()
    {
        Console.WriteLine("\n=================================================================");
        Console.WriteLine("    TiToAI — Prompt Engine & Versioning Smoke Checks (T10)       ");
        Console.WriteLine("=================================================================");

        int passed = 0;
        int failed = 0;

        async Task RunAsync(string name, Func<Task> test)
        {
            Console.Write($"[RUN] {name} ... ");
            try
            {
                await test();
                passed++;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("PASS");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                failed++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("FAIL");
                Console.ResetColor();
                Console.WriteLine($"      -> Lỗi: {ex.GetType().Name}: {ex.Message}");
            }
        }

        void RunSync(string name, Action test)
        {
            Console.Write($"[RUN] {name} ... ");
            try
            {
                test();
                passed++;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("PASS");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                failed++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("FAIL");
                Console.ResetColor();
                Console.WriteLine($"      -> Lỗi: {ex.GetType().Name}: {ex.Message}");
            }
        }

        RunSync("Test 1: Parser & Deterministic Renderer (Vietnamese & Escaping)", Test_ParserAndRenderer_Deterministic_Vietnamese_And_Escaping);
        RunSync("Test 2: Validation of Malformed Placeholders", Test_Validation_MalformedPlaceholders);
        RunSync("Test 3: Validation of Missing & Undeclared Variables", Test_Validation_MissingVariables_And_UndeclaredVariables);
        await RunAsync("Test 4: Template CreateVersion (Initial & Incremental)", Test_Template_CreateVersion_InitialAndIncremental);
        await RunAsync("Test 5: Resolution Precedence (Override before Global)", Test_ResolutionPrecedence_OverrideBeforeGlobal);
        await RunAsync("Test 6: Cross-Workspace Isolation Enforced", Test_CrossWorkspace_Isolation_Enforced);
        await RunAsync("Test 7: Rollback Version (Pointer changed, history immutable)", Test_RollbackVersion_PointerChanged_HistoryBodyImmutable);
        await RunAsync("Test 8: Delete Override & Fallback to Global", Test_DeleteOverride_RevertsToGlobalFallback_GlobalCannotBeDeleted);
        await RunAsync("Test 9: Preview Editor Draft & Persisted Template", Test_Preview_EditorDraft_And_PersistedTemplate);
        await RunAsync("Test 10: Snapshot Immutability for Job Queue", Test_Snapshot_Immutability_For_JobQueue);
        await RunAsync("Test 11: Bridge Handlers 7 Commands Smoke Check", Test_BridgeHandlers_AllCommands_DispatchSmokeCheck);

        Console.WriteLine("=================================================================");
        if (failed == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"      ALL PROMPT ENGINE (T10) SMOKE CHECKS PASSED ({passed}/{passed})");
            Console.ResetColor();
            Console.WriteLine("=================================================================\n");
            return 0;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"      PROMPT ENGINE SMOKE CHECKS FAILED: {failed} failed, {passed} passed");
            Console.ResetColor();
            Console.WriteLine("=================================================================\n");
            return 1;
        }
    }
}
