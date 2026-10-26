using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TiToAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T13a_GlobalProviderAccountsAndExecutionBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Đảm bảo bảng Scene có Unique Index trước để Foreign Key composite từ Job không bị lỗi foreign key mismatch trên SQLite
            migrationBuilder.Sql("CREATE UNIQUE INDEX IF NOT EXISTS \"uq_scene_id_project_workspace\" ON \"Scene\" (\"id\", \"project_id\", \"workspace_id\");");

            migrationBuilder.DropForeignKey(
                name: "FK_Job_Scene_scene_id_workspace_id",
                table: "Job");

            migrationBuilder.DropIndex(
                name: "IX_Job_scene_id_workspace_id",
                table: "Job");

            migrationBuilder.AddColumn<string>(
                name: "display_name",
                table: "BrowserProfile",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lifecycle",
                table: "BrowserProfile",
                type: "TEXT",
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.AddColumn<string>(
                name: "provider_key",
                table: "BrowserProfile",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "revision",
                table: "BrowserProfile",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1L);

            // Unique constraint đã được đảm bảo bằng unique index ở đầu hàm Up

            migrationBuilder.CreateTable(
                name: "ProviderAccount",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    provider_key = table.Column<string>(type: "TEXT", nullable: false),
                    profile_id = table.Column<string>(type: "TEXT", nullable: false),
                    display_name = table.Column<string>(type: "TEXT", nullable: false),
                    verified_identity = table.Column<string>(type: "TEXT", nullable: true),
                    identity_verified_at_utc = table.Column<string>(type: "TEXT", nullable: true),
                    enabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    lifecycle = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "Active"),
                    max_concurrent_jobs = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 3),
                    max_tabs = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 3),
                    per_task_limits_json = table.Column<string>(type: "TEXT", nullable: true),
                    revision = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 1L),
                    created_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderAccount", x => x.id);
                    table.ForeignKey(
                        name: "FK_ProviderAccount_BrowserProfile_profile_id",
                        column: x => x.profile_id,
                        principalTable: "BrowserProfile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // v1.1 Data migration: Chuyển đổi profile AI cũ sang global pool
            migrationBuilder.Sql("UPDATE \"BrowserProfile\" SET \"lifecycle\" = 'ArchivedLegacy' WHERE \"id\" = 'shared_ai';");
            migrationBuilder.Sql("UPDATE \"BrowserProfile\" SET \"workspace_id\" = NULL WHERE \"kind\" IN ('SharedAI', 'MuseAI');");
            migrationBuilder.Sql(
                "INSERT INTO \"ProviderAccount\" (\"id\", \"provider_key\", \"profile_id\", \"display_name\", \"verified_identity\", \"identity_verified_at_utc\", \"enabled\", \"lifecycle\", \"max_concurrent_jobs\", \"max_tabs\", \"revision\", \"created_at_utc\", \"updated_at_utc\") " +
                "SELECT \"id\", 'museai', \"id\", COALESCE(\"display_name\", 'MuseAI Account'), \"account_identity\", " +
                "CASE WHEN \"session_status\" = 'Verified' THEN \"updated_at_utc\" ELSE NULL END, " +
                "CASE WHEN \"session_status\" = 'Verified' THEN 1 ELSE 0 END, 'Active', 1, 1, 1, \"created_at_utc\", \"updated_at_utc\" " +
                "FROM \"BrowserProfile\" WHERE \"kind\" = 'MuseAI' AND NOT EXISTS (SELECT 1 FROM \"ProviderAccount\" pa WHERE pa.\"profile_id\" = \"BrowserProfile\".\"id\");");

            migrationBuilder.CreateTable(
                name: "ExecutionBinding",
                columns: table => new
                {
                    job_id = table.Column<string>(type: "TEXT", nullable: false),
                    provider_account_id = table.Column<string>(type: "TEXT", nullable: false),
                    profile_id = table.Column<string>(type: "TEXT", nullable: false),
                    identity = table.Column<string>(type: "TEXT", nullable: true),
                    capacity_config_revision = table.Column<long>(type: "INTEGER", nullable: false),
                    tab_slot_id = table.Column<string>(type: "TEXT", nullable: false),
                    assigned_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    lease_owner_id = table.Column<string>(type: "TEXT", nullable: true),
                    lease_token = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionBinding", x => x.job_id);
                    table.ForeignKey(
                        name: "FK_ExecutionBinding_BrowserProfile_profile_id",
                        column: x => x.profile_id,
                        principalTable: "BrowserProfile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExecutionBinding_Job_job_id",
                        column: x => x.job_id,
                        principalTable: "Job",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExecutionBinding_ProviderAccount_provider_account_id",
                        column: x => x.provider_account_id,
                        principalTable: "ProviderAccount",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Job_scene_id_project_id_workspace_id",
                table: "Job",
                columns: new[] { "scene_id", "project_id", "workspace_id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Job_TaskType_Scene",
                table: "Job",
                sql: "(task_type = 'Script' AND scene_id IS NULL) OR (task_type IN ('Image', 'VideoPrompt', 'VideoRender') AND scene_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_execution_binding_account_slot",
                table: "ExecutionBinding",
                columns: new[] { "provider_account_id", "tab_slot_id" });

            migrationBuilder.CreateIndex(
                name: "ix_execution_binding_profile",
                table: "ExecutionBinding",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_provider_account_profile",
                table: "ProviderAccount",
                column: "profile_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_account_provider",
                table: "ProviderAccount",
                column: "provider_key");

            migrationBuilder.CreateIndex(
                name: "ix_provider_account_verified_identity",
                table: "ProviderAccount",
                columns: new[] { "provider_key", "verified_identity" },
                unique: true,
                filter: "\"verified_identity\" IS NOT NULL AND \"lifecycle\" = 'Active'");

            migrationBuilder.AddForeignKey(
                name: "FK_Job_Scene_scene_id_project_id_workspace_id",
                table: "Job",
                columns: new[] { "scene_id", "project_id", "workspace_id" },
                principalTable: "Scene",
                principalColumns: new[] { "id", "project_id", "workspace_id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Job_Scene_scene_id_project_id_workspace_id",
                table: "Job");

            migrationBuilder.DropTable(
                name: "ExecutionBinding");

            migrationBuilder.DropTable(
                name: "ProviderAccount");

            migrationBuilder.DropUniqueConstraint(
                name: "uq_scene_id_project_workspace",
                table: "Scene");

            migrationBuilder.DropIndex(
                name: "IX_Job_scene_id_project_id_workspace_id",
                table: "Job");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Job_TaskType_Scene",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "display_name",
                table: "BrowserProfile");

            migrationBuilder.DropColumn(
                name: "lifecycle",
                table: "BrowserProfile");

            migrationBuilder.DropColumn(
                name: "provider_key",
                table: "BrowserProfile");

            migrationBuilder.DropColumn(
                name: "revision",
                table: "BrowserProfile");

            migrationBuilder.CreateIndex(
                name: "IX_Job_scene_id_workspace_id",
                table: "Job",
                columns: new[] { "scene_id", "workspace_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_Job_Scene_scene_id_workspace_id",
                table: "Job",
                columns: new[] { "scene_id", "workspace_id" },
                principalTable: "Scene",
                principalColumns: new[] { "id", "workspace_id" },
                onDelete: ReferentialAction.Cascade);
        }
    }
}
