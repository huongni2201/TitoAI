using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TiToAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T15_JobQueueExtensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "scene_id",
                table: "Job",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AddColumn<long>(
                name: "fencing_token",
                table: "Job",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "lease_owner_id",
                table: "Job",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "project_id",
                table: "Job",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // Backfill project_id từ bảng Scene cho các Job hiện hữu trước khi siết Foreign Key constraint
            migrationBuilder.Sql(
                "UPDATE \"Job\" SET \"project_id\" = (SELECT s.\"project_id\" FROM \"Scene\" s WHERE s.\"id\" = \"Job\".\"scene_id\") " +
                "WHERE (\"project_id\" = '' OR \"project_id\" IS NULL) AND \"scene_id\" IS NOT NULL;");

            migrationBuilder.AddColumn<string>(
                name: "result_text",
                table: "Job",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_project_workspace",
                table: "Job",
                columns: new[] { "project_id", "workspace_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_Job_Project_project_id_workspace_id",
                table: "Job",
                columns: new[] { "project_id", "workspace_id" },
                principalTable: "Project",
                principalColumns: new[] { "id", "workspace_id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Job_Project_project_id_workspace_id",
                table: "Job");

            migrationBuilder.DropIndex(
                name: "ix_job_project_workspace",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "fencing_token",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "lease_owner_id",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "project_id",
                table: "Job");

            migrationBuilder.DropColumn(
                name: "result_text",
                table: "Job");

            migrationBuilder.AlterColumn<string>(
                name: "scene_id",
                table: "Job",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
