using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TiToAI.Application.Accounts;
using TiToAI.Application.Workspaces;
using TiToAI.Contracts.Accounts;
using TiToAI.Contracts.Bridge;
using TiToAI.Contracts.Workspaces;
using TiToAI.Desktop.Bridge;
using TiToAI.Desktop.Bridge.Handlers;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;
using TiToAI.Infrastructure.Persistence.Interceptors;

namespace TiToAI.Desktop.SmokeChecks;

/// <summary>
/// Bộ kiểm thử khói kiểm chứng Bridge Handlers cho Workspace và Account theo contract v1.0.4.
/// Thử nghiệm tương tác RequestEnvelope -> BridgeDispatcher -> Handlers -> ResponseEnvelope.
/// </summary>
public static class WorkspaceAndAccountBridgeSmokeTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public sealed class MockProfileIdentityResolver : IProfileIdentityResolver
    {
        private readonly Dictionary<(string ProfileId, string Provider), ProfileIdentityResolutionResult> _dict = new();

        public void Setup(string profileId, string provider, ProfileIdentityResolutionResult result)
        {
            _dict[(profileId, provider)] = result;
        }

        public Task<ProfileIdentityResolutionResult> ResolveProfileIdentityAsync(
            string profileId,
            string provider,
            CancellationToken cancellationToken = default)
        {
            if (_dict.TryGetValue((profileId, provider), out var res))
            {
                return Task.FromResult(res);
            }

            return Task.FromResult(ProfileIdentityResolutionResult.Failed($"Profile '{profileId}' không tồn tại."));
        }
    }

    public static async Task RunAllTestsAsync(string databasePath)
    {
        Console.WriteLine("\n=================================================================");
        Console.WriteLine("  BẮT ĐẦU KIỂM THỬ KHÓI T07: WORKSPACE & ACCOUNT BRIDGE HANDLERS ");
        Console.WriteLine("=================================================================");

        var connectionString = DatabasePathHelper.BuildConnectionString(databasePath);
        var optionsBuilder = new DbContextOptionsBuilder<TiToAIDbContext>();
        optionsBuilder.UseSqlite(connectionString)
                      .AddInterceptors(new SqlitePragmaInterceptor());
        var dbOptions = optionsBuilder.Options;

        var tempStorage = Path.Combine(Path.GetTempPath(), "titoai_bridge_storage_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempStorage);

        try
        {
            var workspaceService = new WorkspaceService(dbOptions, tempStorage);
            var mockResolver = new MockProfileIdentityResolver();
            var accountService = new AccountLinkService(dbOptions, mockResolver);

            var capturedEvents = new List<EventEnvelope>();
            var workspaceHandlers = new WorkspaceHandlers(workspaceService, evt =>
            {
                capturedEvents.Add(evt);
                return Task.CompletedTask;
            });
            var accountHandlers = new AccountHandlers(accountService);

            var dispatcher = new BridgeDispatcher();
            workspaceHandlers.Register(dispatcher);
            accountHandlers.Register(dispatcher);

            // Chạy các bài test
            await TestWorkspaceBridgeHandlersAsync(dispatcher, capturedEvents, dbOptions);
            await TestAccountBridgeHandlersAsync(dispatcher, mockResolver, dbOptions);

            Console.WriteLine("\n>>> TOÀN BỘ KIỂM THỬ KHÓI BRIDGE CHO T07 ĐÃ VƯỢT QUA (PASS)! <<<");
        }
        finally
        {
            if (Directory.Exists(tempStorage))
            {
                try { Directory.Delete(tempStorage, true); } catch { }
            }
        }
    }

    private static async Task TestWorkspaceBridgeHandlersAsync(
        BridgeDispatcher dispatcher,
        List<EventEnvelope> capturedEvents,
        DbContextOptions<TiToAIDbContext> dbOptions)
    {
        Console.WriteLine("\n[Bridge.Workspace] Kiểm tra các lệnh workspace.*...");

        // 1. workspace.create thành công
        var createPayload = new WorkspaceCreateRequest("Kênh Công Nghệ", "congnghe_channel", "Tin công nghệ AI");
        var createReq = CreateRequest("workspace.create", createPayload);
        var createRes = await dispatcher.DispatchAsync(createReq, CancellationToken.None);

        Assert(createRes.Success, "Tạo Workspace phải thành công");
        var createData = DeserializeData<WorkspaceCreateResponseData>(createRes);
        Assert(!string.IsNullOrWhiteSpace(createData.WorkspaceId), "WorkspaceId phải có giá trị");
        var wsId = createData.WorkspaceId;
        Console.WriteLine($"    -> workspace.create thành công: ID = {wsId}");

        // 2. workspace.create với payload thiếu trường -> INVALID_ARGUMENT hoặc VALIDATION_FAILED
        var invalidCreateReq = CreateRequest("workspace.create", new { name = "", channelHandle = "" });
        var invalidCreateRes = await dispatcher.DispatchAsync(invalidCreateReq, CancellationToken.None);
        Assert(!invalidCreateRes.Success, "Tạo với tên rỗng phải thất bại");
        Assert(invalidCreateRes.Error?.Code == "VALIDATION_FAILED" || invalidCreateRes.Error?.Code == "INVALID_ARGUMENT",
            "Mã lỗi phải là VALIDATION_FAILED hoặc INVALID_ARGUMENT");
        Console.WriteLine($"    -> workspace.create validation guard: PASS (Code={invalidCreateRes.Error?.Code})");

        // 3. workspace.get
        var getReq = CreateRequest("workspace.get", new WorkspaceGetRequest(wsId));
        var getRes = await dispatcher.DispatchAsync(getReq, CancellationToken.None);
        Assert(getRes.Success, "workspace.get phải thành công");
        var getData = DeserializeData<WorkspaceGetResponseData>(getRes);
        Assert(getData.Workspace.Name == "Kênh Công Nghệ", "Tên Workspace không khớp");
        Console.WriteLine("    -> workspace.get thành công: PASS");

        // 4. workspace.update
        var updatePayload = new WorkspaceUpdateRequest(wsId, "Kênh Công Nghệ Đổi Tên", "@congnghe_new", "AI 2026");
        var updateReq = CreateRequest("workspace.update", updatePayload);
        var updateRes = await dispatcher.DispatchAsync(updateReq, CancellationToken.None);
        Assert(updateRes.Success, "workspace.update phải thành công");
        var updateData = DeserializeData<WorkspaceUpdateResponseData>(updateRes);
        Assert(updateData.Workspace.Name == "Kênh Công Nghệ Đổi Tên", "Tên sau update không khớp");
        Console.WriteLine("    -> workspace.update thành công: PASS");

        // 5. workspace.set_status sang Paused và kiểm tra Event
        capturedEvents.Clear();
        var pauseReq = CreateRequest("workspace.set_status", new WorkspaceSetStatusRequest(wsId, "Paused"));
        var pauseRes = await dispatcher.DispatchAsync(pauseReq, CancellationToken.None);
        Assert(pauseRes.Success, "set_status Paused phải thành công");
        var pauseData = DeserializeData<WorkspaceSetStatusResponseData>(pauseRes);
        Assert(pauseData.Status == "Paused", "Trạng thái phải là Paused");
        Assert(capturedEvents.Count == 1, "Phải bắt được 1 event workspace.status_changed");
        Assert(capturedEvents[0].EventName == "workspace.status_changed", "Tên event không khớp");
        Console.WriteLine("    -> workspace.set_status và phát event workspace.status_changed: PASS");

        // 6. workspace.list
        var listReq = CreateRequest("workspace.list", new { });
        var listRes = await dispatcher.DispatchAsync(listReq, CancellationToken.None);
        Assert(listRes.Success, "workspace.list phải thành công");
        var listData = DeserializeData<TiToAI.Contracts.Workspaces.WorkspaceListResponseData>(listRes);
        Assert(listData.Workspaces.Any(w => w.WorkspaceId == wsId), "List phải chứa workspace vừa tạo");
        Console.WriteLine($"    -> workspace.list trả về {listData.Workspaces.Count} không gian làm việc: PASS");

        // 7. workspace.delete - Guard khi có Job active
        await using (var db = new TiToAIDbContext(dbOptions))
        {
            var p = new Project { Id = Guid.NewGuid().ToString("D").ToLowerInvariant(), WorkspaceId = wsId, Name = "P1", CreatedAtUtc = "2026-01-01T00:00:00Z", UpdatedAtUtc = "2026-01-01T00:00:00Z" };
            db.Projects.Add(p);
            var s = new Scene { Id = Guid.NewGuid().ToString("D").ToLowerInvariant(), ProjectId = p.Id, WorkspaceId = wsId, Name = "S1", SequenceOrder = 1, TargetDurationSeconds = 10, CreatedAtUtc = "2026-01-01T00:00:00Z", UpdatedAtUtc = "2026-01-01T00:00:00Z" };
            db.Scenes.Add(s);
            var j = new Job { Id = Guid.NewGuid().ToString("D").ToLowerInvariant(), WorkspaceId = wsId, ProjectId = p.Id, SceneId = s.Id, TaskType = "Image", State = "Running", ProviderKey = "test", StorageRootId = "test", CreatedAtUtc = "2026-01-01T00:00:00Z", UpdatedAtUtc = "2026-01-01T00:00:00Z" };
            db.Jobs.Add(j);
            await db.SaveChangesAsync();
        }

        var deleteReq = CreateRequest("workspace.delete", new WorkspaceDeleteRequest(wsId));
        var deleteBlockedRes = await dispatcher.DispatchAsync(deleteReq, CancellationToken.None);
        Assert(!deleteBlockedRes.Success, "Xóa workspace có job Running phải thất bại");
        Assert(deleteBlockedRes.Error?.Code == "CONCURRENCY_CONFLICT", "Mã lỗi phải là CONCURRENCY_CONFLICT");
        Console.WriteLine("    -> workspace.delete chặn khi có job Running (CONCURRENCY_CONFLICT): PASS");

        // Giải phóng Job và xóa thành công
        await using (var db = new TiToAIDbContext(dbOptions))
        {
            var job = await db.Jobs.FirstAsync(j => j.WorkspaceId == wsId);
            job.State = "Succeeded";
            await db.SaveChangesAsync();
        }

        var deleteSuccessRes = await dispatcher.DispatchAsync(deleteReq, CancellationToken.None);
        Assert(deleteSuccessRes.Success, "workspace.delete phải thành công sau khi job hoàn tất");
        Console.WriteLine("    -> workspace.delete thành công: PASS");
    }

    private static async Task TestAccountBridgeHandlersAsync(
        BridgeDispatcher dispatcher,
        MockProfileIdentityResolver mockResolver,
        DbContextOptions<TiToAIDbContext> dbOptions)
    {
        Console.WriteLine("\n[Bridge.Account] Kiểm tra các lệnh account.*...");

        // Chuẩn bị 2 Workspace
        string ws1, ws2;
        await using (var db = new TiToAIDbContext(dbOptions))
        {
            var w1 = new Workspace { Id = Guid.NewGuid().ToString("D").ToLowerInvariant(), Name = "WS1", ChannelHandle = "@ws1", Topic = "T1", CreatedAtUtc = "2026-01-01T00:00:00Z", UpdatedAtUtc = "2026-01-01T00:00:00Z" };
            var w2 = new Workspace { Id = Guid.NewGuid().ToString("D").ToLowerInvariant(), Name = "WS2", ChannelHandle = "@ws2", Topic = "T2", CreatedAtUtc = "2026-01-01T00:00:00Z", UpdatedAtUtc = "2026-01-01T00:00:00Z" };
            db.Workspaces.AddRange(w1, w2);
            await db.SaveChangesAsync();
            ws1 = w1.Id;
            ws2 = w2.Id;
        }

        var profileId = "profile_bridge_muse";
        mockResolver.Setup(profileId, "MuseAI", ProfileIdentityResolutionResult.Success(
            profileId, "MuseAI", "muse_identity_01", "Connected"));

        // 1. account.link thành công
        var linkReq = CreateRequest("account.link", new AccountLinkRequest(ws1, "MuseAI", profileId));
        var linkRes = await dispatcher.DispatchAsync(linkReq, CancellationToken.None);
        Assert(linkRes.Success, "account.link phải thành công");
        var linkData = DeserializeData<AccountLinkResponseData>(linkRes);
        Assert(linkData.Account.AccountIdentity == "muse_identity_01", "Identity không khớp");
        var accountId = linkData.Account.AccountId;
        Console.WriteLine($"    -> account.link thành công: ID = {accountId}");

        // 2. account.link đúp cho cùng provider -> CONCURRENCY_CONFLICT
        var secondProfile = "profile_bridge_muse_2";
        mockResolver.Setup(secondProfile, "MuseAI", ProfileIdentityResolutionResult.Success(
            secondProfile, "MuseAI", "muse_identity_02", "Connected"));

        var dupLinkReq = CreateRequest("account.link", new AccountLinkRequest(ws1, "MuseAI", secondProfile));
        var dupLinkRes = await dispatcher.DispatchAsync(dupLinkReq, CancellationToken.None);
        Assert(!dupLinkRes.Success, "Không được link tài khoản thứ 2");
        Assert(dupLinkRes.Error?.Code == "CONCURRENCY_CONFLICT", "Mã lỗi phải là CONCURRENCY_CONFLICT");
        Console.WriteLine("    -> account.link chặn liên kết thứ 2 (CONCURRENCY_CONFLICT): PASS");

        // 3. account.list_by_workspace
        var listReq = CreateRequest("account.list_by_workspace", new AccountListByWorkspaceRequest(ws1));
        var listRes = await dispatcher.DispatchAsync(listReq, CancellationToken.None);
        Assert(listRes.Success, "account.list_by_workspace phải thành công");
        var listData = DeserializeData<AccountListByWorkspaceResponseData>(listRes);
        Assert(listData.Accounts.Count == 1, "Chỉ có 1 tài khoản active");
        Assert(listData.Accounts[0].AccountId == accountId, "AccountId không khớp");
        Console.WriteLine("    -> account.list_by_workspace trả về đúng tài khoản: PASS");

        // 4. account.unlink từ Workspace khác -> UNAUTHORIZED_WORKSPACE
        var crossUnlinkReq = CreateRequest("account.unlink", new AccountUnlinkRequest(ws2, accountId));
        var crossUnlinkRes = await dispatcher.DispatchAsync(crossUnlinkReq, CancellationToken.None);
        Assert(!crossUnlinkRes.Success, "Không được unlink tài khoản của workspace khác");
        Assert(crossUnlinkRes.Error?.Code == "UNAUTHORIZED_WORKSPACE", "Mã lỗi phải là UNAUTHORIZED_WORKSPACE");
        Console.WriteLine("    -> account.unlink chặn cross-workspace (UNAUTHORIZED_WORKSPACE): PASS");

        // 5. account.unlink hợp lệ
        var validUnlinkReq = CreateRequest("account.unlink", new AccountUnlinkRequest(ws1, accountId));
        var validUnlinkRes = await dispatcher.DispatchAsync(validUnlinkReq, CancellationToken.None);
        Assert(validUnlinkRes.Success, "account.unlink hợp lệ phải thành công");
        var unlinkData = DeserializeData<AccountUnlinkResponseData>(validUnlinkRes);
        Assert(unlinkData.Status == "Unlinked", "Status phải là Unlinked");
        Console.WriteLine("    -> account.unlink hợp lệ thành công: PASS");
    }

    private static RequestEnvelope CreateRequest(string command, object payload)
    {
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        using var doc = JsonDocument.Parse(jsonBytes);
        var payloadElement = doc.RootElement.Clone();

        return new RequestEnvelope(
            Id: Guid.NewGuid(),
            Type: "request",
            Command: command,
            Payload: payloadElement,
            ClientTimestampUtc: DateTimeOffset.UtcNow,
            ContractVersion: BridgeContract.Version
        );
    }

    private static T DeserializeData<T>(ResponseEnvelope response)
    {
        Assert(response.Data.HasValue, "Response.Data phải có giá trị");
        var json = response.Data!.Value.GetRawText();
        var obj = JsonSerializer.Deserialize<T>(json, JsonOptions);
        Assert(obj is not null, "Deserialization kết quả trả về null");
        return obj!;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Khẳng định thất bại (Assertion Failed): {message}");
        }
    }
}
