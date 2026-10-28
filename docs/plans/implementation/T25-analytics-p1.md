# TikTok Analytics

Phạm vi theo [kiến trúc video-first](../VIDEO_FIRST_ARCHITECTURE_PLAN.md); trạng thái, dependency và evidence hiện hành xem [TASKS](../../agents/TASKS.md), [STATUS](../../agents/STATUS.md). Tên task là định danh lịch sử, không tự xác nhận tính năng đã nghiệm thu.

## Phạm vi

Chọn tài khoản TikTok cấp app; sync/import/query/export theo accountId. Overview, Content và Viewers thuộc cùng snapshot; giữ null, số 0 và nguồn dữ liệu.

## Kiểm tra và nghiệm thu

Kiểm tra parser CSV/ZIP, account isolation, lỗi session và dashboard. Live export trước đây chạy trên DB tạm; nghiệm thu app chính theo STATUS.
