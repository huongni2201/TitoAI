# Adapter MuseAI

Phạm vi theo [kiến trúc video-first](../VIDEO_FIRST_ARCHITECTURE_PLAN.md); trạng thái, dependency và evidence hiện hành xem [TASKS](../../agents/TASKS.md), [STATUS](../../agents/STATUS.md). Tên task là định danh lịch sử, không tự xác nhận tính năng đã nghiệm thu.

## Phạm vi

Thực hiện video generation theo capability đã xác minh, input và cấu hình thực tế; không giả video khi provider chưa sẵn sàng.

## Kiểm tra và nghiệm thu

Kiểm tra validation, session, output, lỗi và uncertain submit; live provider phải có evidence riêng.
