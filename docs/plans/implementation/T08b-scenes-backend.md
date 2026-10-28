# Scene và thứ tự

Phạm vi theo [kiến trúc video-first](../VIDEO_FIRST_ARCHITECTURE_PLAN.md); trạng thái, dependency và evidence hiện hành xem [TASKS](../../agents/TASKS.md), [STATUS](../../agents/STATUS.md). Tên task là định danh lịch sử, không tự xác nhận tính năng đã nghiệm thu.

## Phạm vi

Scene thuộc Project và là chi tiết sản xuất; kiểm ProjectId, revision và thứ tự, không xem clip trung gian là video cuối.

## Kiểm tra và nghiệm thu

Kiểm tra CRUD, reorder, conflict, quan hệ Project/Scene và xóa an toàn trên SQLite fixture.
