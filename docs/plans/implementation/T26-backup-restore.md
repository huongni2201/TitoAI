# Backup và restore

Phạm vi theo [kiến trúc video-first](../VIDEO_FIRST_ARCHITECTURE_PLAN.md); trạng thái, dependency và evidence hiện hành xem [TASKS](../../agents/TASKS.md), [STATUS](../../agents/STATUS.md). Tên task là định danh lịch sử, không tự xác nhận tính năng đã nghiệm thu.

## Phạm vi

Backup SQLite nhất quán kể cả WAL và các asset được tham chiếu trên root cấu hình; restore kiểm tra manifest, đường dẫn và tính đầy đủ.

## Kiểm tra và nghiệm thu

Kiểm tra fixture nhiều root, file thiếu, quick_check, phục hồi và lỗi giữa chừng.
