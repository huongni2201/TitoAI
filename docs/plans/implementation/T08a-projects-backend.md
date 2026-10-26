# Quản lý dự án và xóa an toàn

- Task gốc: **T08 (phần project)** · Ưu tiên: **P0 / G2**.
- Điều kiện mở: T07 DONE.
- Baseline 2026-10-26: Có Project entity/config; chưa có CRUD application.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Hành vi cần đạt

List/get/create/update/delete project trong một Workspace; mô tả và trạng thái dự án được lưu thật. Xóa dự án giữ file và Asset thuộc Workspace, detach ProjectId trước cascade scene/jobs đã được xử lý hợp lệ.

## File và contract

- Tạo đề xuất: `src/TiToAI.Application/Projects/ProjectService.cs`, `src/TiToAI.Desktop/Bridge/Handlers/ProjectHandlers.cs`, `src/TiToAI.Contracts/Projects/ProjectDtos.cs`.
- Dùng Project/Asset/Job hiện có. Spec contracts §2.3 C và SOURCE §5,§7.
- project.list_by_workspace/get/create/update/delete đã có tên trong allow-list spec nhưng thiếu DTO chi tiết. Chốt payload, sorting/paging, status và conflict semantics trước code UI.

## Các bước triển khai

- [ ] Kiểm tra Workspace tồn tại, validation tên/mô tả/status, tạo GUID và UTC chuẩn.
- [ ] List/get filter workspaceId ở backend; count scene/status phải là projection có nguồn.
- [ ] Update không tự biến trạng thái job thành trạng thái project; chốt cách suy ra tiến độ với T22.
- [ ] Delete mở write transaction serialize với scheduler; kiểm tra guard, detach toàn bộ Asset.ProjectId nhưng giữ WorkspaceId, rồi xóa project.
- [ ] Không xóa file media khi detach; rollback giữ nguyên scene/job/asset nếu bất cứ bước nào lỗi.
- [ ] Phát response/event sau commit, tránh EF change tracker ghi ngược giá trị đã bulk-update.

## Kiểm thử và nghiệm thu

SQLite file tạm: project CRUD/restart; projectId hợp lệ thuộc kênh khác; xóa có nhiều scene/job/asset ở trạng thái kết thúc; active hoặc chưa đối soát bị chặn; tracked/untracked không vi phạm NO ACTION; lỗi giữa detach và delete rollback đầy đủ.

Guard Queued/terminal có side effect chưa giải quyết phải được coordinator chốt thống nhất như mục “Contract cần bổ sung” trong README trước mở delete production.

