# Điều phối tạo video

Phạm vi theo [kiến trúc video-first](../VIDEO_FIRST_ARCHITECTURE_PLAN.md); trạng thái, dependency và evidence hiện hành xem [TASKS](../../agents/TASKS.md), [STATUS](../../agents/STATUS.md). Tên task là định danh lịch sử, không tự xác nhận tính năng đã nghiệm thu.

## Phạm vi

Workflow tự động điều phối các job từ brief và media đến video; scene chạy khi đủ input, áp output với revision guard. Không manual handoff, không tự đăng TikTok.

## Kiểm tra và nghiệm thu

Kiểm tra dependencies, snapshot, stale output, cancel/retry và một video cuối; live provider ghi NOT_RUN khi chưa thử.
