# ADR: Project là ranh giới video, bỏ Workspace

**Trạng thái:** Accepted — yêu cầu sản phẩm ngày 2026-10-27  
**Quyết định:** Dùng một Project cho đúng một video TikTok đích; bỏ Workspace khỏi domain/UI/schema hoạt động.  
**Chi tiết:** [VIDEO_FIRST_ARCHITECTURE_PLAN.md](../plans/VIDEO_FIRST_ARCHITECTURE_PLAN.md)

## Bối cảnh

TiToAI hiện mô hình hóa Workspace như kênh TikTok/ranh giới sở hữu rồi đặt nhiều Project trong đó. Điều này làm người dùng phải quản lý thêm một lớp khái niệm dù mục tiêu chính là tạo video và đăng video lên TikTok. Workspace lan sang ownership checks, project list, prompt scope, media paths, queue/progress, account links và analytics.

## Yếu tố quyết định

- Luồng chính phải bắt đầu từ việc tạo video, không phải tạo kênh/Workspace.
- Một Project biểu diễn một video đích, dễ tìm lại và theo dõi từ brief đến trạng thái đăng.
- Tài khoản AI là tài nguyên thực thi toàn ứng dụng; TikTok identity không cần làm container nội dung.
- Dữ liệu và lịch sử hiện có phải được bảo toàn; gỡ Workspace không đồng nghĩa xóa Project hoặc file.
- TiToAI là tiện ích local-first một người dùng. TikTok Content Sharing Guidelines không chấp nhận utility chỉ upload cho tài khoản do cùng cá nhân/đội quản lý; Direct Post API không phải lựa chọn MVP cho hình thức sản phẩm này.

## Các phương án đã cân nhắc

1. **Giữ Workspace và cải thiện navigation.** Ít migration hơn nhưng duy trì lớp domain/UI mà người dùng muốn loại bỏ.
2. **Bỏ Workspace; Project là aggregate root cho một video.** Đơn giản hóa luồng chính, queries và storage scope; yêu cầu migration cùng xử lý xung đột setting cũ.
3. **Project là một TikTok channel và chứa nhiều video.** Không khớp yêu cầu một Project cho một video, vẫn lẫn nội dung với danh tính tài khoản.

## Quyết định

Chọn phương án 2. `Project` là công việc tạo một logical video. Scene/clip chỉ là chi tiết nội bộ nếu provider cần. TiToAI chuẩn bị video/caption để người dùng tự xem lại và tải lên TikTok. App không tự upload, không lưu OAuth token đăng và không ghi `Published`; trạng thái chỉ phản ánh export. Direct Post API cần ADR mới nếu đối tượng/sản phẩm chuyển thành ứng dụng công khai cho nhiều nhà sáng tạo.

## Hệ quả

### Tích cực

- Người dùng có đường đi trực tiếp từ brief đến một video và thao tác đăng.
- Không cần Workspace CRUD/selector, account-to-workspace links hoặc cross-workspace authorization.
- Project search, queue links, asset references và publish history có một chủ thể rõ ràng.

### Chi phí và rủi ro

- Cần schema/data migration và thay nhiều route, bridge DTO, UI, constraints, paths, prompt scopes, analytics.
- Project cũ có thể chia sẻ setting ở Workspace scope; các conflict phải giữ nguyên và đưa ra migration report, không tự gộp.
- Provider hiện tại có thể chỉ xuất scene clips; khi chưa có một video cuối hoàn chỉnh, publish phải bị chặn thay vì ghép cục bộ ngoài phạm vi.
- Người dùng cần tự hoàn tất upload trên TikTok sau khi TiToAI mở thư mục video/copy caption.
- API Direct Post không được thêm vào tiện ích nội bộ một người dùng; phạm vi sản phẩm phải đổi và được đánh giá lại trước khi tích hợp.

## Ranh giới quyết định

ADR này chốt mô hình sản phẩm, không xác nhận schema đã được migrate, không pin TikTok API/SDK, và không cấp quyền gửi bài đăng thật. Tiến độ triển khai xem [STATUS](../agents/STATUS.md).
