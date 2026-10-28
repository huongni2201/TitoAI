# Recovery, retry, cancel và đối soát

Phạm vi theo [kiến trúc video-first](../VIDEO_FIRST_ARCHITECTURE_PLAN.md); trạng thái, dependency và evidence hiện hành xem [TASKS](../../agents/TASKS.md), [STATUS](../../agents/STATUS.md). Tên task là định danh lịch sử, không tự xác nhận tính năng đã nghiệm thu.

## Phạm vi

Phân biệt lỗi trước và sau submit; giữ binding và snapshot khi retry. Side effect chưa chắc chắn cần reconcile, không tự gửi lại.

## Kiểm tra và nghiệm thu

Kiểm tra crash, cancel, retry, lease, slot và uncertain side effect; không dùng PASS lịch sử cho source mới.
