using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TiToAI.Infrastructure.Persistence;

#nullable disable

namespace TiToAI.Infrastructure.Persistence.Migrations;

[DbContext(typeof(TiToAIDbContext))]
[Migration("20261027140000_AddTikTokAnalyticsDailyRecords")]
public sealed class AddTikTokAnalyticsDailyRecords : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TikTokAnalyticsDailyRecord",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", nullable: false),
                snapshot_id = table.Column<string>(type: "TEXT", nullable: false),
                workspace_id = table.Column<string>(type: "TEXT", nullable: false),
                account_id = table.Column<string>(type: "TEXT", nullable: true),
                data_type = table.Column<string>(type: "TEXT", nullable: false),
                period_date_utc = table.Column<string>(type: "TEXT", nullable: false),
                video_views = table.Column<long>(type: "INTEGER", nullable: true),
                profile_views = table.Column<long>(type: "INTEGER", nullable: true),
                likes = table.Column<long>(type: "INTEGER", nullable: true),
                comments = table.Column<long>(type: "INTEGER", nullable: true),
                shares = table.Column<long>(type: "INTEGER", nullable: true),
                total_viewers = table.Column<long>(type: "INTEGER", nullable: true),
                new_viewers = table.Column<long>(type: "INTEGER", nullable: true),
                returning_viewers = table.Column<long>(type: "INTEGER", nullable: true),
                captured_at_utc = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TikTokAnalyticsDailyRecord", x => x.id);
                table.ForeignKey(
                    name: "FK_TikTokAnalyticsDailyRecord_AnalyticsSnapshot_snapshot_id",
                    column: x => x.snapshot_id,
                    principalTable: "AnalyticsSnapshot",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_tiktok_analytics_daily_workspace_type_date",
            table: "TikTokAnalyticsDailyRecord",
            columns: new[] { "workspace_id", "data_type", "period_date_utc" });

        migrationBuilder.CreateIndex(
            name: "ix_tiktok_analytics_daily_snapshot_id",
            table: "TikTokAnalyticsDailyRecord",
            column: "snapshot_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "TikTokAnalyticsDailyRecord");
    }
}
