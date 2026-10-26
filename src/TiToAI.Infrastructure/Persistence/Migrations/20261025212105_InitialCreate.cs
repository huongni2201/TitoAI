using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TiToAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    key = table.Column<string>(type: "TEXT", nullable: false),
                    value = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "Workspace",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    channel_handle = table.Column<string>(type: "TEXT", nullable: false),
                    topic = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "Active"),
                    created_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workspace", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Account",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    workspace_id = table.Column<string>(type: "TEXT", nullable: false),
                    provider = table.Column<string>(type: "TEXT", nullable: false),
                    account_identity = table.Column<string>(type: "TEXT", nullable: false),
                    profile_id = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "Connected"),
                    created_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Account", x => x.id);
                    table.ForeignKey(
                        name: "FK_Account_Workspace_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "Workspace",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Project",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    workspace_id = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "Draft"),
                    created_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Project", x => x.id);
                    table.UniqueConstraint("uq_project_id_workspace", x => new { x.id, x.workspace_id });
                    table.ForeignKey(
                        name: "FK_Project_Workspace_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "Workspace",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Asset",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    workspace_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: true),
                    storage_root_id = table.Column<string>(type: "TEXT", nullable: false),
                    relative_path = table.Column<string>(type: "TEXT", nullable: false),
                    asset_type = table.Column<string>(type: "TEXT", nullable: false),
                    mime_type = table.Column<string>(type: "TEXT", nullable: true),
                    byte_size = table.Column<long>(type: "INTEGER", nullable: false),
                    sha256_hash = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "Linked"),
                    created_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Asset", x => x.id);
                    table.ForeignKey(
                        name: "FK_Asset_Project_project_id_workspace_id",
                        columns: x => new { x.project_id, x.workspace_id },
                        principalTable: "Project",
                        principalColumns: new[] { "id", "workspace_id" });
                    table.ForeignKey(
                        name: "FK_Asset_Workspace_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "Workspace",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Scene",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: false),
                    workspace_id = table.Column<string>(type: "TEXT", nullable: false),
                    sequence_order = table.Column<int>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    target_duration_seconds = table.Column<decimal>(type: "REAL", nullable: false),
                    prompt_text = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "Empty"),
                    audio_asset_id = table.Column<string>(type: "TEXT", nullable: true),
                    image_asset_id = table.Column<string>(type: "TEXT", nullable: true),
                    video_asset_id = table.Column<string>(type: "TEXT", nullable: true),
                    created_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scene", x => x.id);
                    table.UniqueConstraint("uq_scene_id_workspace", x => new { x.id, x.workspace_id });
                    table.ForeignKey(
                        name: "FK_Scene_Asset_audio_asset_id",
                        column: x => x.audio_asset_id,
                        principalTable: "Asset",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Scene_Asset_image_asset_id",
                        column: x => x.image_asset_id,
                        principalTable: "Asset",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Scene_Asset_video_asset_id",
                        column: x => x.video_asset_id,
                        principalTable: "Asset",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Scene_Project_project_id_workspace_id",
                        columns: x => new { x.project_id, x.workspace_id },
                        principalTable: "Project",
                        principalColumns: new[] { "id", "workspace_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Job",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    workspace_id = table.Column<string>(type: "TEXT", nullable: false),
                    scene_id = table.Column<string>(type: "TEXT", nullable: false),
                    task_type = table.Column<string>(type: "TEXT", nullable: false),
                    state = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "Queued"),
                    reason_code = table.Column<string>(type: "TEXT", nullable: true),
                    provider_key = table.Column<string>(type: "TEXT", nullable: false),
                    config_snapshot_json = table.Column<string>(type: "TEXT", nullable: true),
                    prompt_snapshot_text = table.Column<string>(type: "TEXT", nullable: true),
                    storage_root_id = table.Column<string>(type: "TEXT", nullable: false),
                    attempts = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    max_attempts = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 3),
                    lease_expires_at_utc = table.Column<string>(type: "TEXT", nullable: true),
                    retry_after_utc = table.Column<string>(type: "TEXT", nullable: true),
                    side_effect_submitted = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    side_effect_at_utc = table.Column<string>(type: "TEXT", nullable: true),
                    result_reference = table.Column<string>(type: "TEXT", nullable: true),
                    cancel_requested = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    last_heartbeat_at_utc = table.Column<string>(type: "TEXT", nullable: true),
                    created_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Job", x => x.id);
                    table.ForeignKey(
                        name: "FK_Job_Scene_scene_id_workspace_id",
                        columns: x => new { x.scene_id, x.workspace_id },
                        principalTable: "Scene",
                        principalColumns: new[] { "id", "workspace_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uq_account_provider_identity",
                table: "Account",
                columns: new[] { "provider", "account_identity" },
                unique: true,
                filter: "status != 'Unlinked'");

            migrationBuilder.CreateIndex(
                name: "uq_account_workspace_provider",
                table: "Account",
                columns: new[] { "workspace_id", "provider" },
                unique: true,
                filter: "status != 'Unlinked'");

            migrationBuilder.CreateIndex(
                name: "ix_asset_project_workspace",
                table: "Asset",
                columns: new[] { "project_id", "workspace_id" });

            migrationBuilder.CreateIndex(
                name: "IX_Asset_workspace_id",
                table: "Asset",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "uq_asset_id_workspace",
                table: "Asset",
                columns: new[] { "id", "workspace_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Job_scene_id_workspace_id",
                table: "Job",
                columns: new[] { "scene_id", "workspace_id" });

            migrationBuilder.CreateIndex(
                name: "ix_job_state_lease_retry",
                table: "Job",
                columns: new[] { "state", "lease_expires_at_utc", "retry_after_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_job_workspace",
                table: "Job",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "IX_Project_workspace_id",
                table: "Project",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "IX_Scene_audio_asset_id",
                table: "Scene",
                column: "audio_asset_id");

            migrationBuilder.CreateIndex(
                name: "IX_Scene_image_asset_id",
                table: "Scene",
                column: "image_asset_id");

            migrationBuilder.CreateIndex(
                name: "IX_Scene_project_id_workspace_id",
                table: "Scene",
                columns: new[] { "project_id", "workspace_id" });

            migrationBuilder.CreateIndex(
                name: "IX_Scene_video_asset_id",
                table: "Scene",
                column: "video_asset_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Account");

            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "Job");

            migrationBuilder.DropTable(
                name: "Scene");

            migrationBuilder.DropTable(
                name: "Asset");

            migrationBuilder.DropTable(
                name: "Project");

            migrationBuilder.DropTable(
                name: "Workspace");
        }
    }
}
