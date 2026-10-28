# Project và xóa an toàn

Phạm vi theo [kiến trúc video-first](../VIDEO_FIRST_ARCHITECTURE_PLAN.md); trạng thái, dependency và evidence hiện hành xem [TASKS](../../agents/TASKS.md), [STATUS](../../agents/STATUS.md). Tên task là định danh lịch sử, không tự xác nhận tính năng đã nghiệm thu.

## Phạm vi

Project là đơn vị một video đích. CRUD theo ProjectId, kiểm revision và quan hệ Scene/Job/Asset; xóa an toàn không xóa file đang dùng.

## Kiểm tra và nghiệm thu

SQLite fixture kiểm CRUD, conflict, guard job đang chạy hoặc chưa đối soát, rollback và asset dùng chung.
