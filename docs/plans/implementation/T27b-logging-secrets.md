# Log và bảo vệ bí mật

Phạm vi theo [kiến trúc video-first](../VIDEO_FIRST_ARCHITECTURE_PLAN.md); trạng thái, dependency và evidence hiện hành xem [TASKS](../../agents/TASKS.md), [STATUS](../../agents/STATUS.md). Tên task là định danh lịch sử, không tự xác nhận tính năng đã nghiệm thu.

## Phạm vi

Log có correlation và lỗi hữu ích; không ghi cookie, token, mật khẩu hoặc nội dung nhạy cảm không cần thiết.

## Kiểm tra và nghiệm thu

Kiểm tra redaction, lỗi ghi log và khả năng truy nguyên job.
