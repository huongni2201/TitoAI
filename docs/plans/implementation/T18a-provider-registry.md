# Provider registry

Phạm vi theo [kiến trúc video-first](../VIDEO_FIRST_ARCHITECTURE_PLAN.md); trạng thái, dependency và evidence hiện hành xem [TASKS](../../agents/TASKS.md), [STATUS](../../agents/STATUS.md). Tên task là định danh lịch sử, không tự xác nhận tính năng đã nghiệm thu.

## Phạm vi

Định nghĩa capability và trạng thái xác minh từng provider/task; chặn capability chưa hỗ trợ hoặc chưa xác minh. Không chuyển sang manual handoff.

## Kiểm tra và nghiệm thu

Kiểm tra validation, lựa chọn provider, trạng thái không tương thích và snapshot config.
