using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TiToAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalyticsTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalyticsSnapshot",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    workspace_id = table.Column<string>(type: "TEXT", nullable: false),
                    account_id = table.Column<string>(type: "TEXT", nullable: true),
                    source = table.Column<string>(type: "TEXT", nullable: false),
                    period_start = table.Column<string>(type: "TEXT", nullable: true),
                    period_end = table.Column<string>(type: "TEXT", nullable: true),
                    record_count = table.Column<int>(type: "INTEGER", nullable: false),
                    raw_manifest = table.Column<string>(type: "TEXT", nullable: true),
                    created_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalyticsSnapshot", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "VideoAnalyticsRecord",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    snapshot_id = table.Column<string>(type: "TEXT", nullable: false),
                    workspace_id = table.Column<string>(type: "TEXT", nullable: false),
                    account_id = table.Column<string>(type: "TEXT", nullable: true),
                    video_key = table.Column<string>(type: "TEXT", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    views = table.Column<long>(type: "INTEGER", nullable: true),
                    likes = table.Column<long>(type: "INTEGER", nullable: true),
                    comments = table.Column<long>(type: "INTEGER", nullable: true),
                    shares = table.Column<long>(type: "INTEGER", nullable: true),
                    watch_time_avg_sec = table.Column<double>(type: "REAL", nullable: true),
                    gmv_amount = table.Column<string>(type: "TEXT", nullable: true),
                    affiliate_clicks = table.Column<long>(type: "INTEGER", nullable: true),
                    published_at_utc = table.Column<string>(type: "TEXT", nullable: true),
                    captured_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoAnalyticsRecord", x => x.id);
                    table.ForeignKey(
                        name: "FK_VideoAnalyticsRecord_AnalyticsSnapshot_snapshot_id",
                        column: x => x.snapshot_id,
                        principalTable: "AnalyticsSnapshot",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_analytics_snapshot_workspace_created",
                table: "AnalyticsSnapshot",
                columns: new[] { "workspace_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_video_analytics_workspace_video",
                table: "VideoAnalyticsRecord",
                columns: new[] { "workspace_id", "video_key" });

            migrationBuilder.CreateIndex(
                name: "ix_video_analytics_workspace_captured",
                table: "VideoAnalyticsRecord",
                columns: new[] { "workspace_id", "captured_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_video_analytics_snapshot_id",
                table: "VideoAnalyticsRecord",
                column: "snapshot_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VideoAnalyticsRecord");

            migrationBuilder.DropTable(
                name: "AnalyticsSnapshot");
        }
    }
}
