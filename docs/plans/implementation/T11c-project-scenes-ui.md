# Màn hình dự án và bảng danh sách cảnh

- Task gốc: **T11 (Project/Scene UI)** · Ưu tiên: **P0 / G2**.
- Điều kiện mở: T07,T08 DONE.
- Baseline 2026-10-26: Chưa có trang dự án/bảng scene thao tác dữ liệu thật.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Màn hình và hành vi

Theo panel 03: header project, tab Cảnh/Tổng quan/Tài nguyên, thêm cảnh và bảng thứ tự/thumbnail/tên/target duration/ảnh/prompt/audio/video/status. Thao tác batch phải chọn rõ scenes và kiểm input trước enqueue.

## File và contract

- Tạo đề xuất: `src/TiToAI.Web/src/features/projects/{ProjectListPage.tsx,ProjectPage.tsx,ProjectForm.tsx,SceneTable.tsx,SceneForm.tsx}`.
- Dùng project/scene DTO T08; App.tsx route và breadcrumb do UI integrator.
- Batch enqueue chỉ nối khi T15/T22 sẵn sàng; ở T11 không giả tiến độ hoặc chạy cục bộ.

## Các bước triển khai

- [ ] List/create/edit/delete dự án đúng Workspace và lỗi guard; project không thuộc kênh hiện tại không render.
- [ ] Thêm/sửa/xóa scene từ backend, dirty form có bảo vệ khi rời trang.
- [ ] Reorder có nút lên/xuống và keyboard; phản hồi lỗi/concurrency rollback đúng thứ tự đã lưu.
- [ ] Mỗi output có nhãn chưa có/chờ/chạy/cần người dùng/xong/lỗi; audio input là cột độc lập.
- [ ] Click row mở editor đúng scene, Back phục hồi sort/filter/selection; asset có ảnh không được coi video đã hoàn thành.
- [ ] Dùng paging đã chốt; chỉ thêm virtualization nếu đo thấy cần. Cuộn ngang trong bảng, giữ tên/thao tác dễ tìm.
- [ ] Tích hợp library/editor T12 khi sẵn sàng, không tạo bản sao form prompt ở bảng.

## Kiểm thử và nghiệm thu

CRUD project + nhiều scene thật, reorder đồng thời, backend guard delete, lỗi save không mất form. Bảng 100+ cảnh đủ đọc và thao tác; chọn batch có input thiếu chỉ rõ scene nào thiếu. Kiểm tra Windows DPI, hai theme và bàn phím.

Acceptance T11 gồm ba màn: Workspace list, Overview và Project/Scene; trạng thái cả task do coordinator kết luận.

