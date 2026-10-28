# Cấu hình workflow

Phạm vi theo [kiến trúc video-first](../VIDEO_FIRST_ARCHITECTURE_PLAN.md); trạng thái, dependency và evidence hiện hành xem [TASKS](../../agents/TASKS.md), [STATUS](../../agents/STATUS.md). Tên task là định danh lịch sử, không tự xác nhận tính năng đã nghiệm thu.

## Phạm vi

Cấu hình generation theo Project và mặc định toàn app; job giữ snapshot tại enqueue, thay đổi sau đó không sửa job đã tạo.

## Kiểm tra và nghiệm thu

Kiểm tra validation, persistence, snapshot và conflict.
