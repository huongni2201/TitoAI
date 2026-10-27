# Tài khoản và định danh kênh

Task T07 baseline DONE. Account/executor delta theo Q1/Q2 sau Q0; [plan 1.2](../QUEUE_EXECUTION_SETTINGS_PLAN.md) là nguồn chuẩn. Account AI là tài nguyên queue, không gắn Workspace hoặc chia shared/private.

- AI account/profile/identity/capacity qua lifecycle thật; mọi thêm/login/probe/enable/archive ở Cài đặt → Tài khoản. API quản lý AI account không nhận Workspace để phân quyền thực thi.
- TikTok identity nếu nghiệp vụ cần liên kết kênh vẫn giữ scope/mapping lịch sử; form nằm trong tab Tài khoản, không trong Workspace card/overview. Chỉ profile đúng provider/session mới link; không chọn SharedAI cho TikTok hoặc MuseAI cho Workspace.
- Workspace delete chỉ ảnh hưởng dữ liệu nguồn và link kênh phù hợp theo guard; không xóa tài nguyên executor hoặc history của job khác.
- Migration giữ provenance/link legacy, không clone cookie/identity. Disable ngăn cấp mới, không làm mất reference/binding đã submit; retry phải kiểm lại enabled/capacity.

Coordinator giữ command/DTO/allow-list/host; persistence một owner; UI Q2 quản lý tất cả đường connect. Đạt A02/A07/A11/A13, backend cross-scope guards và identity fixture thật. Không dùng link Workspace để suy tài khoản AI nào phải chạy job.
