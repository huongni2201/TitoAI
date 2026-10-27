using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TiToAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Q1_ActiveSlotOccupancyAndCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // H4 Data cleanup: Đảm bảo các job Script legacy không vi phạm check constraint CK_Job_TaskType_Scene
            migrationBuilder.Sql("UPDATE \"Job\" SET \"scene_id\" = NULL WHERE \"task_type\" = 'Script' AND \"scene_id\" IS NOT NULL;");

            // H4 Data fix: Cập nhật enabled và identity_verified_at_utc cho các ProviderAccount có BrowserProfile Connected/Verified
            migrationBuilder.Sql(
                "UPDATE \"ProviderAccount\" " +
                "SET \"identity_verified_at_utc\" = COALESCE(\"identity_verified_at_utc\", \"updated_at_utc\"), \"enabled\" = 1 " +
                "WHERE \"enabled\" = 0 AND \"verified_identity\" IS NOT NULL " +
                "AND EXISTS (SELECT 1 FROM \"BrowserProfile\" bp WHERE bp.\"id\" = \"ProviderAccount\".\"profile_id\" AND bp.\"session_status\" IN ('Connected', 'Verified'));");

            // H2: Tạo bảng ActiveSlotOccupancy quản lý slot DB với PK composite ngăn trùng lặp
            migrationBuilder.CreateTable(
                name: "ActiveSlotOccupancy",
                columns: table => new
                {
                    provider_account_id = table.Column<string>(type: "TEXT", nullable: false),
                    tab_slot_id = table.Column<string>(type: "TEXT", nullable: false),
                    job_id = table.Column<string>(type: "TEXT", nullable: false),
                    lease_owner_id = table.Column<string>(type: "TEXT", nullable: false),
                    lease_token = table.Column<long>(type: "INTEGER", nullable: false),
                    acquired_at_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActiveSlotOccupancy", x => new { x.provider_account_id, x.tab_slot_id });
                    table.ForeignKey(
                        name: "FK_ActiveSlotOccupancy_Job_job_id",
                        column: x => x.job_id,
                        principalTable: "Job",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActiveSlotOccupancy_ProviderAccount_provider_account_id",
                        column: x => x.provider_account_id,
                        principalTable: "ProviderAccount",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "uq_active_slot_occupancy_job",
                table: "ActiveSlotOccupancy",
                column: "job_id",
                unique: true);

            // H2 Conflict Detection & Quarantine:
            // Phát hiện các ExecutionBinding active/uncertain bị trùng slot (lỗi legacy H2)
            // và cô lập (quarantine) chúng với slot ID độc nhất kèm reason_code rõ ràng,
            // tuyệt đối không dùng INSERT OR IGNORE làm biến mất tài nguyên!

            // Bước 1: Remap các ExecutionBinding bị trùng slot thành slot-conflict riêng biệt
            migrationBuilder.Sql(@"
                UPDATE ""ExecutionBinding""
                SET ""tab_slot_id"" = ""tab_slot_id"" || '-conflict-' || substr(""job_id"", 1, 8)
                WHERE ""job_id"" IN (
                    SELECT b1.""job_id""
                    FROM ""ExecutionBinding"" b1
                    JOIN ""Job"" j1 ON b1.""job_id"" = j1.""id""
                    JOIN ""ExecutionBinding"" b2 ON b1.""provider_account_id"" = b2.""provider_account_id""
                                                AND b1.""tab_slot_id"" = b2.""tab_slot_id""
                                                AND b1.""job_id"" != b2.""job_id""
                    JOIN ""Job"" j2 ON b2.""job_id"" = j2.""id""
                    WHERE j1.""state"" IN ('Reserved', 'Running', 'AwaitingUser')
                      AND j2.""state"" IN ('Reserved', 'Running', 'AwaitingUser')
                      AND (
                            (j1.""state"" = 'AwaitingUser' AND j2.""state"" = 'Running')
                            OR (j1.""state"" = j2.""state"" AND (b1.""assigned_at_utc"" > b2.""assigned_at_utc"" OR (b1.""assigned_at_utc"" = b2.""assigned_at_utc"" AND b1.""job_id"" > b2.""job_id"")))
                          )
                );
            ");

            // Bước 2: Chuyển các Job bị conflict sang AwaitingUser với reason rõ ràng để chờ đối soát an toàn
            migrationBuilder.Sql(@"
                UPDATE ""Job""
                SET ""state"" = 'AwaitingUser',
                    ""reason_code"" = 'slot_conflict_quarantined',
                    ""updated_at_utc"" = datetime('now')
                WHERE ""id"" IN (
                    SELECT ""job_id"" FROM ""ExecutionBinding"" WHERE ""tab_slot_id"" LIKE '%-conflict-%'
                ) AND ""state"" IN ('Reserved', 'Running', 'AwaitingUser');
            ");

            // Bước 3: Backfill toàn bộ slot occupancy từ ExecutionBinding (bây giờ đã 100% unique per account/slot)
            migrationBuilder.Sql(@"
                INSERT INTO ""ActiveSlotOccupancy"" (""provider_account_id"", ""tab_slot_id"", ""job_id"", ""lease_owner_id"", ""lease_token"", ""acquired_at_utc"")
                SELECT b.""provider_account_id"", b.""tab_slot_id"", b.""job_id"", COALESCE(b.""lease_owner_id"", 'migrated'), b.""lease_token"", b.""assigned_at_utc""
                FROM ""ExecutionBinding"" b
                JOIN ""Job"" j ON b.""job_id"" = j.""id""
                WHERE j.""state"" IN ('Reserved', 'Running', 'AwaitingUser');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActiveSlotOccupancy");
        }
    }
}
