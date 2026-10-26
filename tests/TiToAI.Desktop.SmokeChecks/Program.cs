using System;
using System.IO;
using System.Threading.Tasks;

namespace TiToAI.Desktop.SmokeChecks;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=================================================================");
        Console.WriteLine("        TiToAI — WPF WebView2 Bridge Smoke Checks (T05)          ");
        Console.WriteLine("=================================================================");

        int passed = 0;
        int failed = 0;

        async Task RunCheckAsync(string testName, Func<Task> testAction)
        {
            Console.Write($"[RUN] {testName} ... ");
            try
            {
                await testAction();
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

        void RunSyncCheck(string testName, Action testAction)
        {
            Console.Write($"[RUN] {testName} ... ");
            try
            {
                testAction();
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

        Console.WriteLine("\n[1] Bridge Message Router & Envelope Smoke Checks:");
        await RunCheckAsync(
            "Test 1: JSON envelope parser and dispatcher smoke check",
            BridgeSmokeTests.Test_JsonEnvelopeParserAndDispatcher_Success);

        await RunCheckAsync(
            "Test 2: Cancel message version mismatch does not cancel in-flight",
            BridgeSmokeTests.Test_CancelMessage_VersionMismatch_DoesNotCancelInFlight);

        await RunCheckAsync(
            "Test 3: Duplicate request ID rejected with CONCURRENCY_CONFLICT",
            BridgeSmokeTests.Test_DuplicateRequestId_RejectedWithConcurrencyConflict);

        await RunCheckAsync(
            "Test 4: Prohibited fields in nested arrays and allowed paths",
            BridgeSmokeTests.Test_ProhibitedFields_NestedArraysAndAllowedFields);

        await RunCheckAsync(
            "Test 8: Internal exception does not leak raw exception message or paths",
            BridgeSmokeTests.Test_BridgeMessageRouter_InternalException_DoesNotLeakRawMessageOrPaths);

        await RunCheckAsync(
            "Test 12: Router dispose cancels in-flight request within bounded timeout",
            BridgeSmokeTests.Test_Router_Dispose_CancelsInFlightRequest_Bounded);

        await RunCheckAsync(
            "Test 13: Router calls after dispose return null and do not throw",
            BridgeSmokeTests.Test_Router_CallsAfterDispose_ReturnNull);

        await RunCheckAsync(
            "Test 14: Concurrent router start and dispose does not throw ObjectDisposedException",
            BridgeSmokeTests.Test_Router_ConcurrentStartAndDispose_NoObjectDisposedException);

        await RunCheckAsync(
            "Test 15: Valid cancel message cancels in-flight request and cleans up registration",
            BridgeSmokeTests.Test_CancelMessage_ValidCancel_CancelsInFlightRequest_AndCleansUp);

        await RunCheckAsync(
            "Test 16: Untrusted origin rejected immediately and returns null",
            BridgeSmokeTests.Test_Router_UntrustedOrigin_RejectedAndReturnsNull);

        await RunCheckAsync(
            "Test 17: In-flight registration cleaned up on exception allowing subsequent request with same ID",
            BridgeSmokeTests.Test_Router_InFlightCleanup_OnException_AllowsSubsequentRequestWithSameId);

        Console.WriteLine("\n[2] Host Theme Service Smoke Checks:");
        RunSyncCheck(
            "Test 5: System theme default and fallback resolution",
            BridgeSmokeTests.Test_SystemTheme_DefaultAndFallback);

        Console.WriteLine("\n[3] Bridge Dispatcher Smoke Checks:");
        await RunCheckAsync(
            "Test 6: Allowed command without handler returns neutral message without task IDs",
            BridgeSmokeTests.Test_BridgeDispatcher_AllowedCommandWithoutHandler_ReturnsNeutralMessageWithoutTaskIds);

        await RunCheckAsync(
            "Test 7: Unknown command rejected with UNKNOWN_COMMAND",
            BridgeSmokeTests.Test_BridgeDispatcher_UnknownCommand_Rejected);

        Console.WriteLine("\n[4] Workspace Query Handler Smoke Checks:");
        await RunCheckAsync(
            "Test 9: Workspace query handler returns empty list when DB is empty",
            BridgeSmokeTests.Test_WorkspaceQueryHandler_ReturnsEmptyWhenDbEmpty);

        await RunCheckAsync(
            "Test 10: Workspace query handler projects workspace from real database",
            BridgeSmokeTests.Test_WorkspaceQueryHandler_ProjectsWorkspaceFromRealDatabase);

        await RunCheckAsync(
            "Test 11: Workspace query handler unregister removes handler cleanly",
            BridgeSmokeTests.Test_WorkspaceQueryHandler_Unregister_RemovesHandlerCleanly);

        Console.WriteLine("\n[5] Desktop Runtime Paths & Isolation Smoke Checks:");
        RunSyncCheck(
            "Test 18: Default paths resolve to production LocalAppData and remain unopened",
            DesktopRuntimePathsSmokeTests.Test_DefaultPaths_UsesLocalAppDataAndProductionDefaults);

        RunSyncCheck(
            "Test 19: Valid test pair inside temp normalized and accepted in both CLI formats",
            DesktopRuntimePathsSmokeTests.Test_ValidTestPair_NormalizedAndAccepted);

        RunSyncCheck(
            "Test 20: Missing one test parameter in pair rejected with ArgumentException",
            DesktopRuntimePathsSmokeTests.Test_MissingPair_RejectedWithArgumentException);

        RunSyncCheck(
            "Test 21: Relative paths rejected with ArgumentException",
            DesktopRuntimePathsSmokeTests.Test_RelativePath_RejectedWithArgumentException);

        RunSyncCheck(
            "Test 22: Paths outside temp directory rejected with ArgumentException",
            DesktopRuntimePathsSmokeTests.Test_OutsideTemp_RejectedWithArgumentException);

        RunSyncCheck(
            "Test 23: Production default paths rejected with ArgumentException",
            DesktopRuntimePathsSmokeTests.Test_ProductionCollision_RejectedWithArgumentException);

        RunSyncCheck(
            "Test 24: Identical database and profile paths rejected with ArgumentException",
            DesktopRuntimePathsSmokeTests.Test_IdenticalPaths_RejectedWithArgumentException);

        Console.WriteLine("\n[6] Task T07 Workspace & Account Bridge Smoke Checks:");
        var tempG2Dir = Path.Combine(Path.GetTempPath(), "titoai_g2_desktop_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempG2Dir);
        var tempG2DbPath = Path.Combine(tempG2Dir, "titoai_g2.db");
        try
        {
            var bootstrapper = new Infrastructure.Persistence.SqliteDatabaseBootstrapper();
            await bootstrapper.BootstrapAsync(tempG2DbPath);
            await RunCheckAsync(
                "Task T07: Workspace CRUD & Account Linking Bridge Handlers (9 commands)",
                async () => await WorkspaceAndAccountBridgeSmokeTests.RunAllTestsAsync(tempG2DbPath));
        }
        finally
        {
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(tempG2Dir))
                {
                    Directory.Delete(tempG2Dir, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup error in test runner
            }
        }

        Console.WriteLine("\n[7] Task T10 Prompt Engine Smoke Checks:");
        await RunCheckAsync(
            "Task T10: Prompt Templates, Immutable Versions, Overrides, Preview, Rollback & Snapshot",
            async () =>
            {
                int exitCode = await PromptEngineSmokeTests.RunAllTestsAsync();
                if (exitCode != 0)
                {
                    throw new InvalidOperationException($"PromptEngineSmokeTests failed with exit code {exitCode}");
                }
            });

        Console.WriteLine("\n[8] Task T13 Browser Ownership & Registry Smoke Checks:");
        await RunCheckAsync(
            "Task T13: Browser Profile Registry, Single Owner Lock, Tab Pool, Login Queue & Quiescing",
            async () => await BrowserSmokeTests.RunAllAsync());

        Console.WriteLine("\n[9] Task R1 Desktop Browser Bridge Handlers Smoke Checks:");
        await RunCheckAsync(
            "Task R1: Browser Handlers (list_profiles, request_login, cancel_login, check_session, errors)",
            async () => await BrowserBridgeSmokeTests.RunAllAsync());

        Console.WriteLine("\n[10] Task T15 Job Queue & Scheduler Smoke Checks:");
        await RunCheckAsync(
            "Task T15: Durable Job Queue, Atomic Reservation, Fair Scheduler & Crash Recovery",
            async () =>
            {
                int exitCode = await JobQueueSmokeTests.RunAllAsync();
                if (exitCode != 0)
                {
                    throw new InvalidOperationException($"JobQueueSmokeTests failed with exit code {exitCode}");
                }
            });

        Console.WriteLine("\n[11] Global AI Account Pool v1.1 Smoke Checks:");
        await RunCheckAsync(
            "Task T13a: Global AI Pool (10 Workspaces / 1 ChatGPT, 10 Videos / 4 MuseAI, Isolation & 8 Combinations)",
            async () =>
            {
                int exitCode = await GlobalAiPoolSmokeTests.RunAllAsync();
                if (exitCode != 0)
                {
                    throw new InvalidOperationException($"GlobalAiPoolSmokeTests failed with exit code {exitCode}");
                }
            });

        Console.WriteLine("\n=================================================================");
        if (failed == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"             ALL T05 SMOKE CHECKS PASSED ({passed}/{passed})                    ");
            Console.ResetColor();
            Console.WriteLine("=================================================================");
            return 0;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"             SMOKE CHECKS FAILED: {failed} failed, {passed} passed            ");
            Console.ResetColor();
            Console.WriteLine("=================================================================");
            return 1;
        }
    }
}
