# Hàng đợi, tiến độ và thao tác cần người dùng

> **Delta hiện hành:** [plan queue/Settings 1.2](../QUEUE_EXECUTION_SETTINGS_PLAN.md) thay popup Settings, nhóm account shared/private, connect tại Workspace và round-robin Workspace. Dùng [TASKS](../../agents/TASKS.md) cho trạng thái/gate; Baseline/checkbox dưới không là evidence đã chạy.

- Task gốc: **T17** · Ưu tiên: **P0 / G2**.
- Điều kiện mở: T16 DONE.
- Baseline 2026-10-26: QueueDrawer hiện chỉ hiển thị chưa kết nối.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Hành vi cần đạt

Xem đủ Queued/Reserved/Running/AwaitingUser/Succeeded/Failed/Canceled, reason và CTA đúng hoàn cảnh. Queue nền không tự đổi Workspace; click job dẫn tới đúng kênh/project/scene.

## File và contract

- Sửa `src/TiToAI.Web/src/components/QueueDrawer.tsx`; tạo đề xuất `features/jobs/{JobList.tsx,JobDetailsPanel.tsx,JobActionPanel.tsx}`.
- Dùng job.list_active/list_history/get_status/cancel/reconcile/retry/resume và event theo [v1.1 §5](../../decisions/execution-contract-v1.1.md); history phân trang riêng, list_active gồm Canceled unresolved nhưng không là toàn bộ lịch sử.
- Manual panel job.get_handoff/import_result thuộc T18c; trước khi T18 DONE hiển thị cần tính năng manual, không đặt nút hoàn tất giả. Mọi CTA mang expectedRevision, conflict reload status và giữ nội dung đang nhập.

## Các bước triển khai

- [ ] Load snapshot thật, phân biệt chưa tải/lỗi/rỗng; badges/counts từ backend.
- [ ] Progress từ milestone/bytes/đầu ra; provider không có tỷ lệ thì chỉ hiện bước và elapsed time có nhãn, không đoán %.
- [ ] Route reason login_required sang CTA login đúng profile; automation_blocked/UI đổi sang manual trong app.
- [ ] Cancel hiện rõ “đã yêu cầu hủy, đang nhận kết quả” sau submit; kết quả Unlinked vẫn tìm lại được.
- [ ] Retry/reconcile theo safety state từ backend, disable thao tác trùng khi request đang xử lý.
- [ ] Scope events, loại seq cũ và reload khi gap/reconnect; tránh response A cập nhật danh sách B.
- [ ] Notification dẫn đúng scene nhưng không chuyển ngữ cảnh khi event tự đến.
- [ ] Script project-level dẫn về project/run review, không route tới scene null. Job hiển thị input/provider snapshot, account/profile ExecutionBinding sau reserve; trước reserve Chưa phân tài khoản/Chờ slot. profile_migration_required giải thích giữ legacy, không có nút đổi profile job cũ.
- [ ] Pool capacity global dùng metadata event riêng, không đưa prompt/media/job detail kênh khác vào event global. Hết slot hoặc chưa có account không là generation failed; có CTA xem pool/thêm account tùy lý do, không bắt mỗi kênh có account riêng.

## Kiểm thử và nghiệm thu

Fake event stream out-of-order/duplicate/mất event, đổi Workspace nhanh, cancel hai lần, reconcile vẫn đang chạy, lỗi root có CTA lưu lại. Backend state là nguồn cuối, UI không tự đổi job thành thành công.

Keyboard trong drawer/details, focus return và hai theme phải đạt. Các form cảnh đang gõ không mất khi mở queue hoặc nhận event.

