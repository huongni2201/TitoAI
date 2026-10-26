# Cấu hình workflow và phiên bản áp dụng cho job mới

- Task gốc: **T18 (workflow config); T22 sử dụng** · Ưu tiên: **P0 / G3**.
- Điều kiện mở: T09,T10,T16 DONE; registry T18a đã chốt.
- Baseline 2026-10-26: Chưa có Workflow entity/service hoặc màn chỉnh workflow.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Hành vi cần đạt

Người dùng chỉnh workflow qua UI, không hardcode tên provider hoặc prompt vào chuỗi nghiệp vụ. MVP dùng pipeline tuyến tính được hỗ trợ, có version, gắn template theo bước và yêu cầu review/input; không cần editor DAG tổng quát.

## File và contract

- Tạo đề xuất: `src/TiToAI.Application/Workflows/WorkflowDefinitionService.cs`, `src/TiToAI.Infrastructure/Persistence/Entities/WorkflowVersion.cs`, `src/TiToAI.Web/src/features/workflows/WorkflowSettingsPanel.tsx`.
- Schema và commands workflow.get_active/save_version/activate theo [v1.1 §6](../../decisions/execution-contract-v1.1.md); scope MVP global, các step Script/Image/VideoPrompt/VideoRender tuyến tính. Input binding cố định theo loại bước; audio là input scene, không job. Ba provider AI resolve từ Settings lúc enqueue từng job, video dùng global MuseAI pool, account/profile cấp phát tại reserve.
- Panel trong nhóm AI & Prompt có chọn template/version, review và xem trước flow; save tạo version nháp, activate/rollback riêng với expectedRevision/clientRequestId. Persistence owner giữ migration duy nhất.

## Các bước triển khai

- [ ] Seed workflow MVP dưới dạng dữ liệu có version, không chỉ constants trong orchestrator.
- [ ] Validate thứ tự/loại bước, binding đầu ra→đầu vào và dependency; video luôn cần ảnh+prompt+audio gốc.
- [ ] UI cho chọn template/version và sửa cấu hình bước trong phạm vi hỗ trợ; preview luồng và lỗi trước lưu.
- [ ] Save tạo version mới bất biến, activate version mới cho run mới; rollback đổi active version, giữ lịch sử.
- [ ] Pipeline run/job chốt workflow version và definition snapshot; edit/rollback không đổi run đang chạy.
- [ ] Scope global/workspace nếu hỗ trợ phải rõ precedence và ownership; MVP chưa cần override thì chỉ global, không hiện tùy chọn không hoạt động.
- [ ] Script luôn requiresReview; mọi output stale bắt buộc review dù definition tắt review. Đổi workflow không đổi run đã tạo; đổi Settings chỉ ảnh hưởng job chưa enqueue, UI nêu rõ hai thời điểm chốt này.

## Kiểm thử và nghiệm thu

Validate binding sai, thiếu input, chu kỳ/step lạ, template đã xóa/khác Workspace, concurrent save và rollback. Tạo run V1, đổi V2, restart: run cũ vẫn V1, run mới V2. UI lỗi save giữ draft, hai theme và keyboard đạt.

Workflow engine T22 tiêu thụ definition này; không viết interpreter chạy mã tùy ý từ JSON.

