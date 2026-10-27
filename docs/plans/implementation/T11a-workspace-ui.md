# Màn hình danh sách Workspace và form quản lý

> **Delta hiện hành:** [plan queue/Settings 1.2](../QUEUE_EXECUTION_SETTINGS_PLAN.md) thay popup Settings, nhóm account shared/private, connect tại Workspace và round-robin Workspace. Dùng [TASKS](../../agents/TASKS.md) cho trạng thái/gate; Baseline/checkbox dưới không là evidence đã chạy.

- Task gốc: **T11 (Workspace UI)** · Ưu tiên: **P0 / G2**.
- Điều kiện mở: T07,T08 DONE.
- Baseline 2026-10-26: App.tsx đã có danh sách khung; chưa có CRUD UI thật.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Màn hình và hành vi

Theo panel 01: card avatar/tên/handle/chủ đề/trạng thái, tìm kiếm, lọc Active/Paused, sort và tạo/mở/chỉnh sửa/xóa. Liên kết account và session là hai nhãn riêng; count chỉ xuất hiện khi backend có dữ liệu.

## File và contract

- Tạo đề xuất: `src/TiToAI.Web/src/features/workspaces/{WorkspaceListPage.tsx,WorkspaceForm.tsx,AccountLinkDialog.tsx}`.
- Integrator nối App.tsx/navigation; dùng typed client và workspace/account DTO T07. Ảnh avatar chưa có schema thì dùng initials, không ảnh demo.
- Sửa contract count/avatar chỉ qua coordinator; UI không tự mở provider browser.

## Các bước triển khai

- [ ] Tách view khỏi App.tsx, dùng route/context hiện có; tạo Workspace rồi cập nhật selector từ response thật.
- [ ] Thêm tìm/lọc/sort theo contract; lưu bộ lọc khi mở kênh rồi Back.
- [ ] Form validation cùng backend, hiển thị fieldErrors, lỗi save giữ dữ liệu và không đóng form.
- [ ] Menu pause/resume phản ánh dispatch semantics; xóa có xác nhận tên/phạm vi và lỗi active job dễ hiểu.
- [ ] Account dialog Workspace chỉ link TikTok; không yêu cầu MuseAI riêng kênh. Link tới trang account AI dùng chung để xem pool/capacity, dữ liệu thiếu không báo thiếu liên kết MuseAI. Chuyển UI baseline này theo T13a, không overwrite worker đang giữ file.
- [ ] Loading/empty/error/retry riêng biệt; bỏ response cũ khi đổi context, không đổ card kênh khác.
- [ ] Dùng tokens hai theme, thao tác menu bằng keyboard và focus return.

## Kiểm thử và nghiệm thu

Luồng thật: tạo 3 kênh, edit, lọc/sort, pause/resume, thử xóa kênh có job; backend lỗi thì UI giữ form. Mở A→B nhanh khi response A chậm không thay B. So ảnh 1280×800/1440×900 và DPI 125/150; card co 3/2/1 cột.

Link account thật và trạng thái login kiểm chứng chéo T14; không đánh dấu xong chỉ dựa screenshot card.

