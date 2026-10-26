using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TiToAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class G2_CoreEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BrowserProfile",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    kind = table.Column<string>(type: "TEXT", nullable: false),
                    workspace_id = table.Column<string>(type: "TEXT", nullable: true),
                    account_identity = table.Column<string>(type: "TEXT", nullable: true),
                    visibility_state = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "Hidden"),
                    session_status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "Unverified"),
                    operational_state = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "Stopped"),
                    created_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrowserProfile", x => x.id);
                    table.ForeignKey(
                        name: "FK_BrowserProfile_Workspace_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "Workspace",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BrowserHandoffCheckpoint",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    profile_id = table.Column<string>(type: "TEXT", nullable: false),
                    active_job_id = table.Column<string>(type: "TEXT", nullable: true),
                    previous_state = table.Column<string>(type: "TEXT", nullable: false),
                    target_visibility_state = table.Column<string>(type: "TEXT", nullable: false),
                    reason_code = table.Column<string>(type: "TEXT", nullable: false),
                    created_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    resolved_at_utc = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrowserHandoffCheckpoint", x => x.id);
                    table.ForeignKey(
                        name: "FK_BrowserHandoffCheckpoint_BrowserProfile_profile_id",
                        column: x => x.profile_id,
                        principalTable: "BrowserProfile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PromptTemplate",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    purpose = table.Column<string>(type: "TEXT", nullable: false),
                    workspace_id = table.Column<string>(type: "TEXT", nullable: true),
                    current_version_id = table.Column<string>(type: "TEXT", nullable: true),
                    created_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptTemplate", x => x.id);
                    table.ForeignKey(
                        name: "FK_PromptTemplate_Workspace_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "Workspace",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PromptVersion",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    template_id = table.Column<string>(type: "TEXT", nullable: false),
                    version_number = table.Column<int>(type: "INTEGER", nullable: false),
                    template_text = table.Column<string>(type: "TEXT", nullable: false),
                    variable_definitions_json = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    change_description = table.Column<string>(type: "TEXT", nullable: true),
                    created_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptVersion", x => x.id);
                    table.UniqueConstraint("AK_PromptVersion_id_template_id", x => new { x.id, x.template_id });
                    table.ForeignKey(
                        name: "FK_PromptVersion_PromptTemplate_template_id",
                        column: x => x.template_id,
                        principalTable: "PromptTemplate",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_browser_handoff_checkpoint_profile",
                table: "BrowserHandoffCheckpoint",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_browser_profile_workspace",
                table: "BrowserProfile",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "IX_PromptTemplate_current_version_id_id",
                table: "PromptTemplate",
                columns: new[] { "current_version_id", "id" });

            migrationBuilder.CreateIndex(
                name: "uq_prompt_template_global_purpose",
                table: "PromptTemplate",
                column: "purpose",
                unique: true,
                filter: "workspace_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_prompt_template_workspace_purpose",
                table: "PromptTemplate",
                columns: new[] { "workspace_id", "purpose" },
                unique: true,
                filter: "workspace_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_prompt_version_id_template",
                table: "PromptVersion",
                columns: new[] { "id", "template_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_prompt_version_template_version",
                table: "PromptVersion",
                columns: new[] { "template_id", "version_number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PromptTemplate_PromptVersion_current_version_id_id",
                table: "PromptTemplate",
                columns: new[] { "current_version_id", "id" },
                principalTable: "PromptVersion",
                principalColumns: new[] { "id", "template_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PromptTemplate_PromptVersion_current_version_id_id",
                table: "PromptTemplate");

            migrationBuilder.DropTable(
                name: "BrowserHandoffCheckpoint");

            migrationBuilder.DropTable(
                name: "BrowserProfile");

            migrationBuilder.DropTable(
                name: "PromptVersion");

            migrationBuilder.DropTable(
                name: "PromptTemplate");
        }
    }
}
