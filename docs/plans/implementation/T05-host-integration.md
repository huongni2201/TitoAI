# Desktop host và WebView2 bridge

Phạm vi theo [kiến trúc video-first](../VIDEO_FIRST_ARCHITECTURE_PLAN.md); trạng thái, dependency và evidence hiện hành xem [TASKS](../../agents/TASKS.md), [STATUS](../../agents/STATUS.md). Tên task là định danh lịch sử, không tự xác nhận tính năng đã nghiệm thu.

## Phạm vi

Đóng gói nội dung React local; kiểm soát navigation và allow-list bridge. Xử lý request, lỗi, hủy và event đúng contract hiện hành; không chặn UI thread.

## Kiểm tra và nghiệm thu

Kiểm tra round-trip, command không hợp lệ, lỗi/hủy và navigation guard; nghiệm thu WebView2 thật thuộc T30.
