# Quản lý Workspace và tạm dừng kênh

- Task gốc: **T07 (phần Workspace)** · Ưu tiên: **P0 / G2**.
- Điều kiện mở: T04,T05,T06,T06a DONE.
- Baseline 2026-10-26: Đã có entity/config SQLite; chưa có service CRUD và handlers.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Hành vi và phạm vi

Tạo/sửa/list/get/xóa Workspace, tìm kiếm/lọc/sắp xếp theo contract đã chốt; Active/Paused là trạng thái dispatch của kênh. Tạo kênh không bắt buộc liên kết account. Account có plan riêng [T07b](T07b-account-linking.md).

## File và contract

- Tạo đề xuất: `src/TiToAI.Application/Workspaces/WorkspaceService.cs`, `src/TiToAI.Desktop/Bridge/Handlers/WorkspaceHandlers.cs`, `src/TiToAI.Contracts/Workspaces/WorkspaceDtos.cs`.
- Dùng entity/config Workspace hiện có; project Application chỉ tạo một lần qua integrator.
- workspace.list/get/create/update/delete/set_status theo contracts §1.5,§1.6 G; coordinator bổ sung DTO create/update/get, validation, pagination và event CRUD trước khi frontend phụ thuộc.

## Các bước triển khai

- [ ] Chốt trường bắt buộc, giới hạn độ dài và quy tắc handle; normalize GUID/UTC, backend từ chối trạng thái lạ.
- [ ] Dùng projection DB thật cho selector/card; counts nếu bổ sung phải từ query thật, không đếm mẫu.
- [ ] CRUD trong unit of work ngắn; trả ID và dữ liệu đã commit, không trả entity EF trực tiếp.
- [ ] set_status phát workspace.status_changed sau commit; Paused vẫn cho enqueue và chạy nốt request đã submit; Active phát wakeup khi T15 nối vào.
- [ ] Xóa serialize với reserve/enqueue, áp guard jobs và thứ tự detach Asset theo contracts §2.3 C. Queued/unresolved side effect còn mâu thuẫn phải được chốt trước thao tác xóa.
- [ ] Dọn file sau DB commit chỉ trong subtree Workspace thuộc root đã cấp; lỗi cleanup có thể đối soát, không xóa cả shared root.
- [ ] Đăng ký đúng từng command qua integrator, giữ command ngoài allow-list bị từ chối.

## Kiểm thử và nghiệm thu

SQLite thật: CRUD/restart, GUID sai, tên rỗng, lọc Active/Paused; xóa với Asset–Project NO ACTION ở cả tracked/untracked; race reserve–delete; rollback không phát event. Fake scheduler chứng minh pause ngăn dispatch mới nhưng không hủy running; integration wakeup kiểm tra lại ở T15.

Kênh A không thể đọc/xóa đối tượng B qua các handlers liên quan. Hoàn tất backend không đồng nghĩa [UI Workspace](T11a-workspace-ui.md) đã làm.

