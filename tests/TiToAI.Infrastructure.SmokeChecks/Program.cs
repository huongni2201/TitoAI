using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;
using TiToAI.Infrastructure.Persistence.Interceptors;

namespace TiToAI.Infrastructure.SmokeChecks;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=================================================================");
        Console.WriteLine("        TiToAI — SQLite Bootstrap & Schema Smoke Checks          ");
        Console.WriteLine("=================================================================");

        var tempDir = Path.Combine(Path.GetTempPath(), "titoai_smoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempDbPath = Path.Combine(tempDir, "smoke.db");

        Console.WriteLine($"[1] Target Temp Database: {tempDbPath}");
        Console.WriteLine($"    Default App Database (Untouched): {DatabasePathHelper.GetDefaultDatabasePath()}");

        try
        {
            // Step 1: Bootstrap SQLite using SqliteDatabaseBootstrapper
            Console.WriteLine("\n[2] Executing SqliteDatabaseBootstrapper.BootstrapAsync...");
            var bootstrapper = new SqliteDatabaseBootstrapper();
            var bootstrapResult = await bootstrapper.BootstrapAsync(tempDbPath);

            if (!bootstrapResult.IsBootstrapSuccessful)
            {
                throw new InvalidOperationException("Bootstrap failed: initial verification check did not pass.");
            }

            Console.WriteLine("    -> Bootstrap status: PASS");
            Console.WriteLine($"    -> Engine SQLite Version: {bootstrapResult.SqliteVersion}");
            Console.WriteLine($"    -> WAL Journal Mode: {bootstrapResult.JournalMode}");
            Console.WriteLine($"    -> Foreign Keys Enabled: {bootstrapResult.ForeignKeysEnabled}");
            Console.WriteLine($"    -> Busy Timeout: {bootstrapResult.BusyTimeout}ms");
            Console.WriteLine($"    -> Synchronous Mode: {bootstrapResult.SynchronousMode}");
            Console.WriteLine($"    -> Applied Migrations: {string.Join(", ", bootstrapResult.AppliedMigrations)}");

            if (bootstrapResult.AppliedMigrations.Count == 0)
            {
                throw new InvalidOperationException("Expected at least one migration to be applied.");
            }

            Console.WriteLine("\n[2a] Verifying persisted theme preference...");
            await ThemePreferenceStoreSmokeChecks.VerifyAsync(tempDbPath);
            Console.WriteLine("    -> Theme preference read/upsert/validation: PASS");

            var connectionString = DatabasePathHelper.BuildConnectionString(tempDbPath);

            // Step 2: PRAGMA Verification on Fresh Connection via TiToAIDbContext & SqlitePragmaInterceptor
            Console.WriteLine("\n[3] Verifying PRAGMA on a fresh connection via TiToAIDbContext...");
            var verifyOptions = new DbContextOptionsBuilder<TiToAIDbContext>()
                .UseSqlite(connectionString)
                .AddInterceptors(new SqlitePragmaInterceptor())
                .Options;

            await using (var verifyContext = new TiToAIDbContext(verifyOptions))
            {
                await verifyContext.Database.OpenConnectionAsync();
                var verifyConn = verifyContext.Database.GetDbConnection();

                // Note: NO PRAGMA setter commands are run here.
                // Interceptor must have applied foreign_keys, busy_timeout, synchronous upon ConnectionOpened.
                // WAL mode persists from bootstrap in the DB file header.

                // Verify WAL persists across connections
                using (var cmd = verifyConn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA journal_mode;";
                    var jm = (await cmd.ExecuteScalarAsync())?.ToString();
                    Assert(string.Equals(jm, "wal", StringComparison.OrdinalIgnoreCase), $"Expected WAL journal mode, got '{jm}'");
                }

                // Verify FK
                using (var cmd = verifyConn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA foreign_keys;";
                    var fk = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    Assert(fk == 1, $"Expected foreign_keys=1, got {fk}");
                }

                // Verify busy_timeout
                using (var cmd = verifyConn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA busy_timeout;";
                    var bt = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    Assert(bt == 5000, $"Expected busy_timeout=5000, got {bt}");
                }

                // Verify synchronous
                using (var cmd = verifyConn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA synchronous;";
                    var syn = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    Assert(syn == 1, $"Expected synchronous=1 (NORMAL), got {syn}");
                }

                await verifyContext.Database.CloseConnectionAsync();
                Console.WriteLine("    -> Fresh Connection PRAGMA assertions: PASS (production interceptor & persistent WAL verified without manual setters)");
            }

            // Step 3: Schema & Master Table Inspection
            Console.WriteLine("\n[4] Inspecting SQLite schema and sqlite_master...");
            await using (var schemaConn = new SqliteConnection(connectionString))
            {
                await schemaConn.OpenAsync();

                var expectedTables = new[]
                {
                    "Workspace", "Account", "Project", "Scene", "Asset", "Job", "AppSettings",
                    "PromptTemplate", "BrowserProfile", "BrowserHandoffCheckpoint"
                };
                foreach (var table in expectedTables)
                {
                    using var cmd = schemaConn.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name;";
                    cmd.Parameters.AddWithValue("@name", table);
                    var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    Assert(count == 1, $"Table '{table}' not found in sqlite_master!");
                    Console.WriteLine($"    -> Table '{table}': EXISTS");
                }

                // Verify partial indexes
                var expectedIndexes = new[]
                {
                    "uq_account_workspace_provider",
                    "uq_account_provider_identity",
                    "uq_asset_id_workspace",
                    "ix_asset_project_workspace",
                    "ix_job_state_lease_retry",
                    "ix_job_workspace",
                    "uq_prompt_template_global_purpose",
                    "uq_prompt_template_workspace_purpose",
                    "ix_browser_profile_workspace",
                    "ix_browser_handoff_checkpoint_profile"
                };

                foreach (var idx in expectedIndexes)
                {
                    using var cmd = schemaConn.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name=@name;";
                    cmd.Parameters.AddWithValue("@name", idx);
                    var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    Assert(count == 1, $"Index '{idx}' not found in sqlite_master!");
                    Console.WriteLine($"    -> Index '{idx}': EXISTS");
                }
            }

            // Step 4: EF Core Functional & Constraint Verification
            Console.WriteLine("\n[5] Verifying EF Core Unit of Work, Composite Keys & Constraints...");
            var optionsBuilder = new DbContextOptionsBuilder<TiToAIDbContext>();
            optionsBuilder.UseSqlite(connectionString);
            optionsBuilder.AddInterceptors(new SqlitePragmaInterceptor());

            var w1Id = Guid.NewGuid().ToString("D");
            var w2Id = Guid.NewGuid().ToString("D");
            var p1Id = Guid.NewGuid().ToString("D");
            var s1Id = Guid.NewGuid().ToString("D");
            var a1Id = Guid.NewGuid().ToString("D");
            var j1Id = Guid.NewGuid().ToString("D");
            var nowUtc = DateTime.UtcNow.ToString("O");

            // Verify all IDs adhere strictly to contract §2.1 (36-character lowercase GUID)
            var fixtureIds = new[] { w1Id, w2Id, p1Id, s1Id, a1Id, j1Id };
            foreach (var id in fixtureIds)
            {
                Assert(Guid.TryParse(id, out _) && id.Length == 36 && id == id.ToLowerInvariant(),
                    $"Fixture ID '{id}' must be a 36-character lowercase standard GUID format.");
            }

            // 5a. Insert valid entities in Workspace 1 and Workspace 2
            await using (var db = new TiToAIDbContext(optionsBuilder.Options))
            {
                db.Workspaces.Add(new Workspace
                {
                    Id = w1Id,
                    Name = "Kênh Tin Tức 24h",
                    ChannelHandle = "@news24h",
                    Topic = "Tin tức công nghệ",
                    Status = "Active",
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc
                });

                db.Workspaces.Add(new Workspace
                {
                    Id = w2Id,
                    Name = "Kênh Thời Trang GenZ",
                    ChannelHandle = "@genz_fashion",
                    Topic = "Thời trang",
                    Status = "Active",
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc
                });

                await db.SaveChangesAsync();
                Console.WriteLine("    -> Insert Workspace 1 & 2: PASS");
            }

            // 5b. Insert Project in Workspace 1
            await using (var db = new TiToAIDbContext(optionsBuilder.Options))
            {
                db.Projects.Add(new Project
                {
                    Id = p1Id,
                    WorkspaceId = w1Id,
                    Name = "Video 01 - AI Thế Hệ Mới",
                    Description = "Dự án video đầu tiên",
                    Status = "Draft",
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc
                });
                await db.SaveChangesAsync();
                Console.WriteLine("    -> Insert Project 1 in Workspace 1: PASS");
            }

            // 5c. Test Composite FK Violation on Scene: project in W1 but scene assigned to W2!
            await using (var db = new TiToAIDbContext(optionsBuilder.Options))
            {
                bool fkCaught = false;
                try
                {
                    db.Scenes.Add(new Scene
                    {
                        Id = s1Id,
                        ProjectId = p1Id,    // belongs to W1
                        WorkspaceId = w2Id,  // mismatched! W2
                        SequenceOrder = 1,
                        Name = "Cảnh 1",
                        TargetDurationSeconds = 5.5m,
                        Status = "Empty",
                        CreatedAtUtc = nowUtc,
                        UpdatedAtUtc = nowUtc
                    });
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateException ex) when (ex.InnerException is SqliteException sqlEx && sqlEx.SqliteErrorCode == 19)
                {
                    fkCaught = true;
                    Console.WriteLine($"    -> Cross-workspace Scene insert rejected by SQLite Composite FK: PASS ({sqlEx.Message})");
                }

                Assert(fkCaught, "Expected composite FK violation when inserting Scene with mismatched (project_id, workspace_id)!");
            }

            // 5d. Insert Scene and Asset in Workspace 1 with correct composite keys
            await using (var db = new TiToAIDbContext(optionsBuilder.Options))
            {
                var asset = new Asset
                {
                    Id = a1Id,
                    WorkspaceId = w1Id,
                    ProjectId = p1Id,
                    StorageRootId = "root-default-images",
                    RelativePath = $"{w1Id}/projects/{p1Id}/images/{a1Id}.png",
                    AssetType = "Image",
                    MimeType = "image/png",
                    ByteSize = 1048576,
                    Sha256Hash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                    Status = "Linked",
                    CreatedAtUtc = nowUtc
                };
                db.Assets.Add(asset);

                var scene = new Scene
                {
                    Id = s1Id,
                    ProjectId = p1Id,
                    WorkspaceId = w1Id,
                    SequenceOrder = 1,
                    Name = "Cảnh 1",
                    TargetDurationSeconds = 4.0m,
                    Status = "Draft",
                    ImageAssetId = a1Id,
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc
                };
                db.Scenes.Add(scene);

                await db.SaveChangesAsync();
                Console.WriteLine("    -> Insert Asset & Scene with valid composite keys: PASS");
            }

            // 5e. Test Job insertion with composite FK to Scene(id, workspace_id)
            await using (var db = new TiToAIDbContext(optionsBuilder.Options))
            {
                var job = new Job
                {
                    Id = j1Id,
                    WorkspaceId = w1Id,
                    ProjectId = p1Id,
                    SceneId = s1Id,
                    TaskType = "Image",
                    State = "Queued",
                    ProviderKey = "gemini-web",
                    StorageRootId = "root-default-images",
                    Attempts = 0,
                    MaxAttempts = 3,
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc
                };
                db.Jobs.Add(job);
                await db.SaveChangesAsync();
                Console.WriteLine("    -> Insert Job linked to Scene & Workspace: PASS");
            }

            // 5f. Test Partial Unique Index on Account
            var acc1Id = Guid.NewGuid().ToString("D");
            var acc2Id = Guid.NewGuid().ToString("D");
            Assert(Guid.TryParse(acc1Id, out _) && acc1Id.Length == 36 && acc1Id == acc1Id.ToLowerInvariant(),
                $"Account 1 ID '{acc1Id}' must be a 36-character lowercase standard GUID format.");
            Assert(Guid.TryParse(acc2Id, out _) && acc2Id.Length == 36 && acc2Id == acc2Id.ToLowerInvariant(),
                $"Account 2 ID '{acc2Id}' must be a 36-character lowercase standard GUID format.");

            await using (var db = new TiToAIDbContext(optionsBuilder.Options))
            {
                db.Accounts.Add(new Account
                {
                    Id = acc1Id,
                    WorkspaceId = w1Id,
                    Provider = "MuseAI",
                    AccountIdentity = "channel_01_muse",
                    ProfileId = "museai_acc1",
                    Status = "Connected",
                    CreatedAtUtc = nowUtc
                });
                await db.SaveChangesAsync();
                Console.WriteLine("    -> Insert Account 1 (Connected): PASS");
            }

            // Inserting another Connected account for same (workspace_id, provider) must fail
            await using (var db = new TiToAIDbContext(optionsBuilder.Options))
            {
                bool uqCaught = false;
                try
                {
                    db.Accounts.Add(new Account
                    {
                        Id = acc2Id,
                        WorkspaceId = w1Id,
                        Provider = "MuseAI",
                        AccountIdentity = "channel_02_muse",
                        ProfileId = "museai_acc2",
                        Status = "Connected",
                        CreatedAtUtc = nowUtc
                    });
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateException ex) when (ex.InnerException is SqliteException sqlEx && sqlEx.SqliteErrorCode == 19)
                {
                    uqCaught = true;
                    Console.WriteLine($"    -> Duplicate Connected Account rejected by Partial Index: PASS ({sqlEx.Message})");
                }
                Assert(uqCaught, "Expected partial unique index to reject duplicate Connected account!");
            }

            // Updating Account 1 to 'Unlinked' allows new 'Connected' account for same provider
            await using (var db = new TiToAIDbContext(optionsBuilder.Options))
            {
                var acc1 = await db.Accounts.FindAsync(acc1Id);
                acc1!.Status = "Unlinked";
                await db.SaveChangesAsync();

                // Now insert second account with Connected
                db.Accounts.Add(new Account
                {
                    Id = acc2Id,
                    WorkspaceId = w1Id,
                    Provider = "MuseAI",
                    AccountIdentity = "channel_02_muse",
                    ProfileId = "museai_acc2",
                    Status = "Connected",
                    CreatedAtUtc = nowUtc
                });
                await db.SaveChangesAsync();
                Console.WriteLine("    -> Partial Index allows relinking when previous account is Unlinked: PASS");
            }

            // 5g. Test ON DELETE NO ACTION on Project with Asset attached (contracts §2.3 C)
            await using (var db = new TiToAIDbContext(optionsBuilder.Options))
            {
                bool deleteBlocked = false;
                try
                {
                    // Trying to delete Project directly while Asset still points to it
                    var project = await db.Projects.FindAsync(p1Id);
                    db.Projects.Remove(project!);
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateException ex) when (ex.InnerException is SqliteException sqlEx && sqlEx.SqliteErrorCode == 19)
                {
                    deleteBlocked = true;
                    Console.WriteLine($"    -> Direct Project deletion blocked by Asset ON DELETE NO ACTION: PASS ({sqlEx.Message})");
                }
                Assert(deleteBlocked, "Expected SQLite FK constraint violation due to ON DELETE NO ACTION on Asset(project_id, workspace_id)!");
            }

            // 5h. Test Safe Project Deletion with Detach (contracts §2.3 C Bước 2 & 3)
            await using (var db = new TiToAIDbContext(optionsBuilder.Options))
            {
                await using var transaction = await db.Database.BeginTransactionAsync();

                // Detach Asset from Project but keep WorkspaceId
                await db.Assets
                    .Where(a => a.ProjectId == p1Id && a.WorkspaceId == w1Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.ProjectId, (string?)null));

                // Now delete Project
                var project = await db.Projects.FindAsync(p1Id);
                db.Projects.Remove(project!);
                await db.SaveChangesAsync();

                await transaction.CommitAsync();
                Console.WriteLine("    -> Safe Project delete with Asset detach (preserving WorkspaceId): PASS");
            }

            // Verify Asset still exists with project_id = null and workspace_id intact
            await using (var db = new TiToAIDbContext(optionsBuilder.Options))
            {
                var asset = await db.Assets.FindAsync(a1Id);
                Assert(asset != null, "Asset must still exist after project deletion!");
                Assert(asset!.ProjectId == null, "Asset.ProjectId must be null after detach!");
                Assert(asset.WorkspaceId == w1Id, "Asset.WorkspaceId must remain intact!");

                // Scene and Job were cascaded
                var scene = await db.Scenes.FindAsync(s1Id);
                Assert(scene == null, "Scene must be cascaded on project deletion!");
                var job = await db.Jobs.FindAsync(j1Id);
                Assert(job == null, "Job must be cascaded on scene deletion!");
                Console.WriteLine("    -> Detached Asset verified & Scene/Job cascade verified: PASS");
            }

            // 5i. Test AppSettings
            await using (var db = new TiToAIDbContext(optionsBuilder.Options))
            {
                db.AppSettings.Add(new AppSetting
                {
                    Key = "ui.theme",
                    Value = "dark",
                    UpdatedAtUtc = nowUtc
                });
                await db.SaveChangesAsync();

                var setting = await db.AppSettings.FindAsync("ui.theme");
                Assert(setting != null && setting.Value == "dark", "AppSettings read/write failed!");
                Console.WriteLine("    -> AppSettings key-value store: PASS");
            }

            // 5j. G2 Core Entities, Unique Indexes & Constraints Verification
            await G2CoreEntitiesSmokeChecks.VerifyAsync(connectionString, optionsBuilder.Options);

            // 5k. Task T07 Workspace & Account Backend Smoke Tests
            await WorkspaceAndAccountSmokeTests.RunTestsAsync(tempDbPath);

            // 5l. Task T08 Project & Scene Backend Smoke Tests
            await ProjectAndSceneSmokeTests.RunTestsAsync(tempDbPath);

            // 5m. Task T09 Storage, Assets & Settings Backend Smoke Tests
            await StorageAndAssetSmokeTests.RunTestsAsync(tempDbPath);

            Console.WriteLine("\n=================================================================");
            Console.WriteLine("        ALL INFRASTRUCTURE & G2-SETUP SMOKE CHECKS PASSED        ");
            Console.WriteLine("=================================================================");
            return 0;
        }
        finally
        {
            // Step 6: Clean up temporary DB
            try
            {
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                    Console.WriteLine($"\n[6] Cleaned up temporary test directory: {tempDir}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Warning] Failed to clean up temp dir: {ex.Message}");
            }
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Assertion failed: {message}");
        }
    }
}
