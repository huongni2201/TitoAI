# Tài khoản và profile của executor

Task sản phẩm T13a. Hiện **REVIEW**, baseline cũ từng báo DONE; xem [review H1–H4](../../reviews/2026-10-26-handoff-recheck.md). Thực hiện phần backend trong Q1 và UI trong Q2, sau Q0 theo [plan 1.2](../QUEUE_EXECUTION_SETTINGS_PLAN.md).

## Phạm vi còn phải đạt

- Một danh sách account trong Cài đặt; thêm/login/probe/enable/disable/capacity qua host commands thật. Không shared/private groups hoặc mapping account AI–Workspace.
- Một account một profile/userDataDir/identity; unique identity theo provider, owner lock duy nhất; browser chỉ visible cho login. Login/quiesce đúng profile, không ảnh hưởng tài nguyên độc lập khác.
- DTO/allow-list/handler/client cùng contract. Profile/account lifecycle không suy ra từ nhãn hoặc cookie cũ; fresh app đi qua commands, không chỉ seed DB test.
- Slot occupancy/budget/uncertain/Closing thực thi ở queue, close/reopen context có kết quả xác nhận thật. Giữ binding job để retry/reconcile; giữ scope dữ liệu theo job.
- Upgrade legacy bảo toàn identity/profile/history/checkpoint, xử lý duplicate và Script scope bằng fixture; identity chưa đủ chứng cứ không bật automation. Không clone cookie hoặc sửa job snapshot.

## Ownership và nghiệm thu

Coordinator giữ DTO/host/client chung; một persistence owner giữ migrations/model snapshot; Browser owner giữ registry/lock/tab/login/probe. UI owner Q2 giữ trang account. Freeze exact paths/hash trước dispatch.

Đạt A02–A07/A09/A11/A13 của plan 1.2, kết quả source/DB/runtime tách rõ; fake không chứng minh login/hidden thật. Giữ evidence Chromium baseline; bổ sung account/test profile thật khi đủ điều kiện, thiếu ghi NOT_RUN.
