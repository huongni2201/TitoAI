# Prompt template, version, override và render preview

- Task gốc: **T10** · Ưu tiên: **P0 / G2**.
- Điều kiện mở: T04,T05,T06,T06a DONE.
- Baseline 2026-10-26: Chưa có bảng/service prompt; Scene.PromptText không thay hệ template.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Hành vi cần đạt

Template global có override theo Workspace; lưu version bất biến, khai báo biến, render preview và rollback. Job chốt bản render và version khi enqueue, sửa template không làm thay job cũ.

## File và contract

- Tạo đề xuất: `src/TiToAI.Application/Prompts/{PromptTemplateService.cs,PromptRenderer.cs}`; `src/TiToAI.Infrastructure/Persistence/Entities/{PromptTemplate.cs,PromptVersion.cs}` và configurations.
- Coordinator thêm DTO và command allow-list cho list/get/create_version/preview/rollback/override; các tên này là thao tác đề xuất, chưa phải lệnh bridge được chốt.
- Persistence owner tạo migration; Desktop handler mới trong Bridge/Handlers/PromptHandlers.cs. UI quản lý được nối tại T12a.

## Các bước triển khai

- [ ] Chốt purpose Script/Image/VideoPrompt, scope global/workspace, biến bắt buộc/tùy chọn và kiểu dữ liệu; không dùng eval/script làm renderer.
- [ ] Thiết kế unique template/version, current-version pointer và transaction publish; rollback đổi pointer hoặc tạo version mới theo quy tắc được chốt, không sửa body lịch sử.
- [ ] Resolve Workspace override trước global; không cho đọc/override template riêng của kênh khác.
- [ ] Validate thiếu/biến lạ/placeholder lỗi; render deterministically và preview chính xác text sẽ enqueue.
- [ ] Expose render result gồm template/version/variables/rendered text cho T15; không giữ reference mutable.
- [ ] Chốt giới hạn body/output hợp lý, lỗi có vị trí biến; lưu editor draft tách preview/job snapshot.

## Kiểm thử và nghiệm thu

Test parser/renderer với tiếng Việt, biến thiếu/rỗng, ký tự brace, escape, schema sai; SQLite version publish đồng thời/rollback; override giữa 3 Workspace. Enqueue fixture rồi sửa/rollback template: snapshot cũ không đổi.

Không hardcode prompt nghiệp vụ trong service; template mặc định nếu cần là dữ liệu có version và người dùng sửa được.

