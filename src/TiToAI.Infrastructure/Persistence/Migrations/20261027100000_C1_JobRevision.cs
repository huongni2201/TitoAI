using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TiToAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class C1_JobRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "revision",
                table: "Job",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "revision",
                table: "Job");
        }
    }
}
