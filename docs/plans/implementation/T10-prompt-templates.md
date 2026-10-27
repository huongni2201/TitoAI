# Prompt template, override và render preview

> Delta 2026-10-27: Prompt chỉ lưu một nội dung hiện hành; bỏ lịch sử/rollback version. Migration mới chép nội dung đang active vào `PromptTemplate` trước khi xóa bảng `PromptVersion`. Job vẫn lưu rendered text snapshot bất biến. [Rà soát database](../../decisions/prompt-database-simplification.md). [Hướng dẫn AI web](../AI_WEB_CONTENT_CREATION_PLAN.md) thay cách yêu cầu người dùng điền biến kỹ thuật; lưu và preview không gọi AI. Xem [hướng dẫn mặc định](../AI_WEB_CONTENT_CREATION_PROMPTS.md).

- Task gốc: **T10** · Ưu tiên: **P0 / G2**.
- Điều kiện mở: T04,T05,T06,T06a DONE.
- Baseline 2026-10-26: Chưa có bảng/service prompt; Scene.PromptText không thay hệ template.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Hành vi cần đạt

Template global có override theo Workspace; lưu trực tiếp nội dung hiện hành cùng khai báo biến, render preview tất định. Job chụp rendered text khi enqueue; sửa prompt không làm thay nội dung job cũ.

## File và contract

- Persistence: `PromptTemplate` lưu `template_text` và `variable_definitions_json`; không còn entity/table `PromptVersion`.
- Bridge cung cấp list/get/save/preview/delete_override; không có API lịch sử hoặc rollback version.
- Persistence owner tạo migration; Desktop handler mới trong Bridge/Handlers/PromptHandlers.cs. UI quản lý được nối tại T12a.

## Các bước triển khai

- [ ] Chốt purpose Script/Image/VideoPrompt, scope global/workspace, biến bắt buộc/tùy chọn và kiểu dữ liệu; không dùng eval/script làm renderer.
- [ ] Template tạo ý tưởng/kịch bản và tạo/tối ưu prompt yêu cầu kèm `tiktokPost` theo [SOURCE §7.4](../SOURCE_PLAN.md#74-tạo-ý-tưởngprompt-kèm-nội-dung-đăng-tiktok--p0); đưa ngữ cảnh bài đăng project vào snapshot. Giữ purpose hiện hành; preview template chỉ render text, không gọi AI. Kiểm tra template tùy chỉnh vẫn đáp ứng hợp đồng đầu ra bắt buộc.
- [x] Unique template theo scope/purpose; save cập nhật nội dung template hiện hành.
- [ ] Resolve Workspace override trước global; không cho đọc/override template riêng của kênh khác.
- [ ] Validate thiếu/biến lạ/placeholder lỗi; render deterministically và preview chính xác text sẽ enqueue.
- [x] Giữ snapshot rendered text bất biến cho T15; không cần version prompt trong DB.
- [ ] Chốt giới hạn body/output hợp lý, lỗi có vị trí biến; lưu editor draft tách preview/job snapshot.

## Kiểm thử và nghiệm thu

Đã kiểm tra build Infrastructure và Web sau thay đổi. Cần smoke test SQLite trên fixture có nhiều version để xác nhận migration chỉ giữ đúng version active; cần nghiệm thu snapshot job sau khi template được sửa.

Không hardcode prompt nghiệp vụ trong service; template mặc định nếu cần phải là dữ liệu người dùng sửa được.

