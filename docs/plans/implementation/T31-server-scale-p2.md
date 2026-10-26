# Đánh giá mở rộng server hoặc nhiều máy khi có nhu cầu

- Task gốc: **T31** · Ưu tiên: **P2 / chưa mở**.
- Điều kiện mở: Chỉ mở theo yêu cầu multi-machine/server cụ thể của người dùng.
- Baseline 2026-10-26: Chưa có yêu cầu server; giữ local-first SQLite trong MVP.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Phạm vi của plan

Đây là plan khảo sát có điều kiện để bao phủ T31 trong backlog; chưa đề xuất triển khai server. Không thêm PostgreSQL/service/auth/sync vào MVP hiện tại.

## Đầu vào và file khi task được mở

Tải đo từ T30, số máy/user/Workspace, yêu cầu chia sẻ dữ liệu và ownership browser/account. Deliverable đề xuất `docs/decisions/server-scale.md`; không tạo mã hoặc migration cho tới khi quyết định kiến trúc được chấp nhận.

## Các bước khảo sát

- [ ] Xác định vấn đề thực tế: giới hạn SQLite single writer, cần remote jobs, nhiều user, hay chỉ cần nhiều tab cùng máy.
- [ ] Đo bottleneck CPU/I/O/browser quota/DB; phân biệt chậm vì website với chậm vì persistence.
- [ ] So ba phương án giữ SQLite tối ưu local, service trung tâm, và server PostgreSQL theo dữ liệu đo.
- [ ] Thiết kế lại quyền truy cập/tenant, profile ownership một máy, secrets, lease fencing, storage/download và backup nếu có server.
- [ ] Đề xuất migration/sync/rollback và chi phí vận hành; không coi việc đổi connection string là đủ.
- [ ] ADR ghi quyết định, tiêu chí kích hoạt và bằng chứng, rồi mới chia task implementation riêng nếu được yêu cầu.

## Nghiệm thu

Báo cáo trả lời nhu cầu cụ thể bằng số liệu và trade-offs, có quyết định làm hoặc hoãn. Không chặn P0/G4; việc plan này tồn tại không mở task T31 và không đổi model vận hành Windows local-first.

