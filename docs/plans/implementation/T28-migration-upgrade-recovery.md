# Nâng schema và phục hồi migration thất bại

- Task gốc: **T28** · Ưu tiên: **P0 / G4**.
- Điều kiện mở: T24,T26,T27 DONE.
- Baseline 2026-10-26: Có migration InitialCreate; chưa có upgrade/fault-recovery qualification.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Hành vi cần đạt

Startup chỉ một tiến trình migrate, có backup trước nâng schema, hỗ trợ upgrade từ fixture bản trước. Migration lỗi không tiếp tục dùng DB nửa nâng; người dùng có đường khôi phục giữ dữ liệu.

## File và contract

- Sửa SqliteDatabaseBootstrapper và bootstrap Desktop qua owner; tạo đề xuất `src/TiToAI.Infrastructure/Persistence/DatabaseUpgradeCoordinator.cs`.
- Persistence owner duy nhất giữ migrations/snapshot; fixtures version cũ và checks trong `tests/TiToAI.Infrastructure.SmokeChecks/UpgradeChecks.cs` (đề xuất).
- Dùng BackupService T26; không copy app DB đang WAL hoạt động.

## Các bước triển khai

- [ ] Gắn app/schema compatibility marker; bản app cũ gặp DB mới chặn ghi, không tự downgrade.
- [ ] Acquire migration single-owner lock có metadata/timeout; xác minh tiến trình owner đã chết trước xử lý abandoned lock, không xóa lock mù.
- [ ] Preflight dung lượng/quyền/health, backup nhất quán, ngừng scheduler/writers và đóng connections liên quan.
- [ ] Apply migrations tuần tự; kiểm quick_check/FKs/ownership/root mappings/snapshot job sau upgrade.
- [ ] Inject lỗi trước, giữa rebuild table và sau DDL; giữ backup/journal và trạng thái để restart khôi phục đúng.
- [ ] Failed upgrade mở recovery UI có thông tin/bản backup, không start scheduler trên schema chưa tương thích.
- [ ] Restore backup schema cũ bằng cơ chế T26 khi cần; không hứa mọi Down migration an toàn.

## Kiểm thử và nghiệm thu

Fixtures InitialCreate và mỗi schema đã phát hành có dữ liệu project/scene/job/root cũ. Upgrade hai app đồng thời, crash lock holder, disk full, corrupt DB và snapshot jobs đã submit. Sau recovery mọi FK/hash/input còn nguyên và không auto-submit.

Thêm fixture v1.0.4→v1.1 theo [§2/§4](../../decisions/execution-contract-v1.1.md): archive shared_ai; MuseAI riêng Workspace chuyển global ProviderAccount giữ profile/session và legacy mapping; trùng identity quarantine, không clone cookie. Input snapshot/binding cũ không reroute; Script project scope backfill và text result giữ evidence. Tái dùng fixtures T13a, không tạo migration engine thứ hai.

Nếu chưa có bản phát hành trước, dùng fixture được freeze ở baseline, ghi rõ đây là upgrade rehearsal thay vì bằng chứng từ khách hàng thật.

