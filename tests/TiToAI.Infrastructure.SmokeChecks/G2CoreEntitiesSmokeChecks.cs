using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TiToAI.Infrastructure.Persistence;
using TiToAI.Infrastructure.Persistence.Entities;

namespace TiToAI.Infrastructure.SmokeChecks;

public static class G2CoreEntitiesSmokeChecks
{
    public static async Task VerifyAsync(string connectionString, DbContextOptions<TiToAIDbContext> dbOptions)
    {
        Console.WriteLine("\n[5j] Verifying G2 Core Entities, Unique Indexes & Constraints...");
        string nowUtc = DateTime.UtcNow.ToString("o");

        // 1. Setup a test workspace
        string ws1Id = "w-" + Guid.NewGuid().ToString("N");
        string ws2Id = "w-" + Guid.NewGuid().ToString("N");

        await using (var db = new TiToAIDbContext(dbOptions))
        {
            db.Workspaces.Add(new Workspace
            {
                Id = ws1Id,
                Name = "Workspace G2 One",
                ChannelHandle = "@ws_g2_one",
                Topic = "Topic One",
                Status = "Active",
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            });

            db.Workspaces.Add(new Workspace
            {
                Id = ws2Id,
                Name = "Workspace G2 Two",
                ChannelHandle = "@ws_g2_two",
                Topic = "Topic Two",
                Status = "Active",
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            });

            await db.SaveChangesAsync();
        }

        // 2. PromptTemplate Global Unique Index
        string globalTmpl1Id = "pt-" + Guid.NewGuid().ToString("N");
        string globalTmpl2Id = "pt-" + Guid.NewGuid().ToString("N");

        await using (var db = new TiToAIDbContext(dbOptions))
        {
            db.PromptTemplates.Add(new PromptTemplate
            {
                Id = globalTmpl1Id,
                Purpose = "Script",
                WorkspaceId = null, // Global
                CurrentVersionId = null,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            });
            await db.SaveChangesAsync();
            Console.WriteLine("    -> Global PromptTemplate created: PASS");
        }

        // Attempt second global template with same purpose "Script" -> Must throw DbUpdateException
        await using (var db = new TiToAIDbContext(dbOptions))
        {
            db.PromptTemplates.Add(new PromptTemplate
            {
                Id = globalTmpl2Id,
                Purpose = "Script", // Duplicate global purpose!
                WorkspaceId = null,
                CurrentVersionId = null,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            });

            bool rejected = false;
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqliteException sqlEx && sqlEx.SqliteErrorCode == 19)
            {
                rejected = true;
            }

            Assert(rejected, "Duplicate global PromptTemplate with purpose 'Script' must be rejected by uq_prompt_template_global_purpose!");
            Console.WriteLine("    -> Duplicate global PromptTemplate constraint rejection: PASS");
        }

        // 3. Workspace Override PromptTemplate Unique Index
        string ws1OverrideId = "pt-" + Guid.NewGuid().ToString("N");
        string ws1DuplicateOverrideId = "pt-" + Guid.NewGuid().ToString("N");
        string ws2OverrideId = "pt-" + Guid.NewGuid().ToString("N");

        await using (var db = new TiToAIDbContext(dbOptions))
        {
            // Same purpose "Script" for Workspace 1 override -> ALLOWED
            db.PromptTemplates.Add(new PromptTemplate
            {
                Id = ws1OverrideId,
                Purpose = "Script",
                WorkspaceId = ws1Id,
                CurrentVersionId = null,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            });

            // Same purpose "Script" for Workspace 2 override -> ALLOWED
            db.PromptTemplates.Add(new PromptTemplate
            {
                Id = ws2OverrideId,
                Purpose = "Script",
                WorkspaceId = ws2Id,
                CurrentVersionId = null,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            });

            await db.SaveChangesAsync();
            Console.WriteLine("    -> Workspace Override PromptTemplates for separate workspaces: PASS");
        }

        // Attempt duplicate override for Workspace 1 with same purpose "Script" -> Must FAIL
        await using (var db = new TiToAIDbContext(dbOptions))
        {
            db.PromptTemplates.Add(new PromptTemplate
            {
                Id = ws1DuplicateOverrideId,
                Purpose = "Script",
                WorkspaceId = ws1Id,
                CurrentVersionId = null,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            });

            bool rejected = false;
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqliteException sqlEx && sqlEx.SqliteErrorCode == 19)
            {
                rejected = true;
            }

            Assert(rejected, "Duplicate Workspace Override PromptTemplate must be rejected by uq_prompt_template_workspace_purpose!");
            Console.WriteLine("    -> Duplicate Workspace Override PromptTemplate constraint rejection: PASS");
        }

        // 4. PromptVersion Unique (template_id, version_number)
        string v1Id = "pv-" + Guid.NewGuid().ToString("N");
        string v2Id = "pv-" + Guid.NewGuid().ToString("N");
        string vDuplicateVersionId = "pv-" + Guid.NewGuid().ToString("N");

        await using (var db = new TiToAIDbContext(dbOptions))
        {
            db.PromptVersions.Add(new PromptVersion
            {
                Id = v1Id,
                TemplateId = globalTmpl1Id,
                VersionNumber = 1,
                TemplateText = "Template text version 1 with {topic}",
                VariableDefinitionsJson = "[{\"name\":\"topic\",\"required\":true}]",
                ChangeDescription = "Initial version",
                CreatedAtUtc = nowUtc
            });

            db.PromptVersions.Add(new PromptVersion
            {
                Id = v2Id,
                TemplateId = globalTmpl1Id,
                VersionNumber = 2,
                TemplateText = "Template text version 2 with {topic} and {style}",
                VariableDefinitionsJson = "[{\"name\":\"topic\",\"required\":true},{\"name\":\"style\",\"required\":false}]",
                ChangeDescription = "Added style variable",
                CreatedAtUtc = nowUtc
            });

            await db.SaveChangesAsync();
            Console.WriteLine("    -> PromptVersions created: PASS");
        }

        // Attempt duplicate version number 1 for globalTmpl1 -> Must FAIL
        await using (var db = new TiToAIDbContext(dbOptions))
        {
            db.PromptVersions.Add(new PromptVersion
            {
                Id = vDuplicateVersionId,
                TemplateId = globalTmpl1Id,
                VersionNumber = 1, // Duplicate version number!
                TemplateText = "Conflicting version 1 text",
                VariableDefinitionsJson = "[]",
                CreatedAtUtc = nowUtc
            });

            bool rejected = false;
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqliteException sqlEx && sqlEx.SqliteErrorCode == 19)
            {
                rejected = true;
            }

            Assert(rejected, "Duplicate version number for same template must be rejected by uq_prompt_version_template_version!");
            Console.WriteLine("    -> Duplicate PromptVersion constraint rejection: PASS");
        }

        // 5. Composite Foreign Key Constraint: CurrentVersionId must belong to this template
        // Valid pointer: globalTmpl1Id.CurrentVersionId -> v1Id (belongs to globalTmpl1Id) -> PASS
        await using (var db = new TiToAIDbContext(dbOptions))
        {
            var tmpl = await db.PromptTemplates.FindAsync(globalTmpl1Id);
            tmpl!.CurrentVersionId = v1Id;
            await db.SaveChangesAsync();
            Console.WriteLine("    -> Setting CurrentVersionId to valid version of same template: PASS");
        }

        // Create a version for ws1OverrideId
        string ws1V1Id = "pv-" + Guid.NewGuid().ToString("N");
        await using (var db = new TiToAIDbContext(dbOptions))
        {
            db.PromptVersions.Add(new PromptVersion
            {
                Id = ws1V1Id,
                TemplateId = ws1OverrideId,
                VersionNumber = 1,
                TemplateText = "Workspace override text version 1",
                VariableDefinitionsJson = "[]",
                CreatedAtUtc = nowUtc
            });
            await db.SaveChangesAsync();
        }

        // Invalid pointer attempt: Set globalTmpl1Id.CurrentVersionId to ws1V1Id (which belongs to ws1OverrideId, not globalTmpl1Id)
        // With SQLite foreign_keys = ON, composite FK (current_version_id, id) -> PromptVersion(id, template_id) MUST REJECT!
        await using (var db = new TiToAIDbContext(dbOptions))
        {
            var tmpl = await db.PromptTemplates.FindAsync(globalTmpl1Id);
            tmpl!.CurrentVersionId = ws1V1Id; // Version belonging to a different template!

            bool rejected = false;
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqliteException sqlEx && sqlEx.SqliteErrorCode == 19)
            {
                rejected = true;
            }

            Assert(rejected, "CurrentVersionId pointing to a version of a DIFFERENT template must be rejected by composite FK!");
            Console.WriteLine("    -> Composite Ownership Constraint (CurrentVersionId must belong to same template): PASS");
        }

        // 6. BrowserProfile and BrowserHandoffCheckpoint
        string profileId = "shared_ai";
        string checkpointId = "chk-" + Guid.NewGuid().ToString("N");

        await using (var db = new TiToAIDbContext(dbOptions))
        {
            db.BrowserProfiles.Add(new BrowserProfile
            {
                Id = profileId,
                Kind = "SharedAI",
                WorkspaceId = null,
                AccountIdentity = null,
                VisibilityState = "Hidden",
                SessionStatus = "Connected",
                OperationalState = "Idle",
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            });

            db.BrowserHandoffCheckpoints.Add(new BrowserHandoffCheckpoint
            {
                Id = checkpointId,
                ProfileId = profileId,
                ActiveJobId = null,
                PreviousState = "Running",
                TargetVisibilityState = "VisibleWaiting",
                ReasonCode = "login_required",
                CreatedAtUtc = nowUtc,
                ResolvedAtUtc = null
            });

            await db.SaveChangesAsync();
            Console.WriteLine("    -> BrowserProfile and BrowserHandoffCheckpoint created: PASS");
        }

        // Verify cascading delete of checkpoint on profile deletion
        await using (var db = new TiToAIDbContext(dbOptions))
        {
            var profile = await db.BrowserProfiles.FindAsync(profileId);
            db.BrowserProfiles.Remove(profile!);
            await db.SaveChangesAsync();

            var chk = await db.BrowserHandoffCheckpoints.FindAsync(checkpointId);
            Assert(chk == null, "BrowserHandoffCheckpoint must cascade delete when BrowserProfile is deleted!");
            Console.WriteLine("    -> BrowserProfile cascade deletion of checkpoints: PASS");
        }

        // 7. Account Relinking after Unlink (Partial Unique Index)
        string acc1Id = "acc-" + Guid.NewGuid().ToString("N");
        string acc2Id = "acc-" + Guid.NewGuid().ToString("N");

        await using (var db = new TiToAIDbContext(dbOptions))
        {
            db.Accounts.Add(new Account
            {
                Id = acc1Id,
                WorkspaceId = ws1Id,
                Provider = "MuseAI",
                AccountIdentity = "channel_muse_01",
                ProfileId = "museai_profile_01",
                Status = "Connected",
                CreatedAtUtc = nowUtc
            });
            await db.SaveChangesAsync();
            Console.WriteLine("    -> Account linked: PASS");
        }

        // Attempt to link a second MuseAI account to ws1 while first is Connected -> Must FAIL
        await using (var db = new TiToAIDbContext(dbOptions))
        {
            db.Accounts.Add(new Account
            {
                Id = acc2Id,
                WorkspaceId = ws1Id,
                Provider = "MuseAI",
                AccountIdentity = "channel_muse_02",
                ProfileId = "museai_profile_02",
                Status = "Connected",
                CreatedAtUtc = nowUtc
            });

            bool rejected = false;
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqliteException sqlEx && sqlEx.SqliteErrorCode == 19)
            {
                rejected = true;
            }

            Assert(rejected, "Second connected MuseAI account in same workspace must be rejected by uq_account_workspace_provider!");
            Console.WriteLine("    -> Second active account in same workspace rejected: PASS");
        }

        // Unlink acc1
        await using (var db = new TiToAIDbContext(dbOptions))
        {
            var acc = await db.Accounts.FindAsync(acc1Id);
            acc!.Status = "Unlinked";
            await db.SaveChangesAsync();
            Console.WriteLine("    -> Account unlinked (status='Unlinked'): PASS");
        }

        // Now relink acc2 with provider "MuseAI" in ws1 -> Must SUCCEED because acc1 is "Unlinked" (partial index filter)
        await using (var db = new TiToAIDbContext(dbOptions))
        {
            db.Accounts.Add(new Account
            {
                Id = acc2Id,
                WorkspaceId = ws1Id,
                Provider = "MuseAI",
                AccountIdentity = "channel_muse_02",
                ProfileId = "museai_profile_02",
                Status = "Connected",
                CreatedAtUtc = nowUtc
            });

            await db.SaveChangesAsync();
            Console.WriteLine("    -> Relink account in same workspace after previous account unlinked: PASS");
        }

        // Verify history preserved
        await using (var db = new TiToAIDbContext(dbOptions))
        {
            var allAccounts = await db.Accounts
                .Where(a => a.WorkspaceId == ws1Id)
                .OrderBy(a => a.CreatedAtUtc)
                .ToListAsync();

            Assert(allAccounts.Count == 2, "Both unlinked and connected accounts must be preserved in SQLite history!");
            Assert(allAccounts[0].Status == "Unlinked", "First account must be Unlinked");
            Assert(allAccounts[1].Status == "Connected", "Second account must be Connected");
            Console.WriteLine("    -> Account link/unlink history fully preserved: PASS");
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
