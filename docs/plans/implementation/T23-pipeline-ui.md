# Giao diện tạo video

Phạm vi theo [kiến trúc video-first](../VIDEO_FIRST_ARCHITECTURE_PLAN.md); trạng thái, dependency và evidence hiện hành xem [TASKS](../../agents/TASKS.md), [STATUS](../../agents/STATUS.md). Tên task là định danh lịch sử, không tự xác nhận tính năng đã nghiệm thu.

## Phạm vi

Luồng Chuẩn bị → Đang chạy → Kết quả theo trạng thái thật; input cấp Project, scene là chi tiết. CTA đăng chỉ xuất hiện khi có video cuối hợp lệ.

## Kiểm tra và nghiệm thu

Kiểm tra validation, progress, lỗi, cancel, output và CTA; không giả kết quả.
