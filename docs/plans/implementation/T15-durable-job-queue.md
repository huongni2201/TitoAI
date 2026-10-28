# Queue bền vững

Phạm vi theo [kiến trúc video-first](../VIDEO_FIRST_ARCHITECTURE_PLAN.md); trạng thái, dependency và evidence hiện hành xem [TASKS](../../agents/TASKS.md), [STATUS](../../agents/STATUS.md). Tên task là định danh lịch sử, không tự xác nhận tính năng đã nghiệm thu.

## Phạm vi

Một queue SQLite toàn app; job có ProjectId và SceneId tùy chọn, snapshot bất biến, reservation theo provider/capacity và lease/fencing.

## Kiểm tra và nghiệm thu

Kiểm tra FIFO job đủ điều kiện, cạnh tranh slot, idempotency và recovery trên SQLite thật.
