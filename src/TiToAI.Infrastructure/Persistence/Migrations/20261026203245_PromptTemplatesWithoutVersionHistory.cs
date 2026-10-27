using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TiToAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PromptTemplatesWithoutVersionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "template_text",
                table: "PromptTemplate",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "variable_definitions_json",
                table: "PromptTemplate",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            // Move each template's active prompt into the template row before removing history.
            migrationBuilder.Sql(@"
                UPDATE ""PromptTemplate""
                SET ""template_text"" = COALESCE((
                        SELECT ""template_text"" FROM ""PromptVersion""
                        WHERE ""PromptVersion"".""id"" = ""PromptTemplate"".""current_version_id""
                    ), ""template_text""),
                    ""variable_definitions_json"" = COALESCE((
                        SELECT ""variable_definitions_json"" FROM ""PromptVersion""
                        WHERE ""PromptVersion"".""id"" = ""PromptTemplate"".""current_version_id""
                    ), ""variable_definitions_json"");");

            migrationBuilder.DropForeignKey(
                name: "FK_PromptTemplate_PromptVersion_current_version_id_id",
                table: "PromptTemplate");

            migrationBuilder.DropTable(
                name: "PromptVersion");

            migrationBuilder.DropIndex(
                name: "IX_PromptTemplate_current_version_id_id",
                table: "PromptTemplate");

            migrationBuilder.DropColumn(
                name: "current_version_id",
                table: "PromptTemplate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "current_version_id",
                table: "PromptTemplate",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PromptVersion",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    template_id = table.Column<string>(type: "TEXT", nullable: false),
                    change_description = table.Column<string>(type: "TEXT", nullable: true),
                    created_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    template_text = table.Column<string>(type: "TEXT", nullable: false),
                    variable_definitions_json = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    version_number = table.Column<int>(type: "INTEGER", nullable: false)
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

            migrationBuilder.Sql(@"
                INSERT INTO ""PromptVersion"" (""id"", ""template_id"", ""version_number"", ""template_text"", ""variable_definitions_json"", ""change_description"", ""created_at_utc"")
                SELECT ""id"" || '-restored', ""id"", 1, ""template_text"", ""variable_definitions_json"", NULL, ""updated_at_utc""
                FROM ""PromptTemplate"";
                UPDATE ""PromptTemplate"" SET ""current_version_id"" = ""id"" || '-restored';");

            migrationBuilder.CreateIndex(
                name: "IX_PromptTemplate_current_version_id_id",
                table: "PromptTemplate",
                columns: new[] { "current_version_id", "id" });

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

            migrationBuilder.DropColumn(
                name: "template_text",
                table: "PromptTemplate");

            migrationBuilder.DropColumn(
                name: "variable_definitions_json",
                table: "PromptTemplate");
        }
    }
}
