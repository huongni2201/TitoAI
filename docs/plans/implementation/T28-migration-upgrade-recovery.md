# Schema và phục hồi

Phạm vi theo [kiến trúc video-first](../VIDEO_FIRST_ARCHITECTURE_PLAN.md); trạng thái, dependency và evidence hiện hành xem [TASKS](../../agents/TASKS.md), [STATUS](../../agents/STATUS.md). Tên task là định danh lịch sử, không tự xác nhận tính năng đã nghiệm thu.

## Phạm vi

Schema hiện hành tại final-schema.sql, user_version=1. DB legacy chưa version được backup bằng SQLite Backup API trước reset; không chuyển dữ liệu thử. Schema version không hỗ trợ phải dừng.

## Kiểm tra và nghiệm thu

Kiểm tra fixture DB mới, backup/reset, parity model/schema, backup mở được và lỗi khởi tạo; không suy ra policy upgrade tương lai đã hoàn tất.
