# Manual handoff trong app và nhập kết quả đúng job

- Task gốc: **T18 (manual mode)** · Ưu tiên: **P0 / G3**.
- Điều kiện mở: T09,T10,T16 DONE; registry/mode T18a đã chốt.
- Baseline 2026-10-26: Chưa có màn/handlers copy-export-import kết quả gắn job.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Hành vi cần đạt

Khi automation chưa hỗ trợ hoặc bị chặn ngoài login, job AwaitingUser có hướng dẫn/copy prompt/export input/import kết quả trong TiToAI. App chỉ mở browser quản lý cho login; người dùng tự dùng dịch vụ bên ngoài khi cần.

## File và contract

- Tạo đề xuất: `src/TiToAI.Application/Providers/ManualHandoffService.cs`, `src/TiToAI.Web/src/features/jobs/ManualHandoffPanel.tsx`.
- Dùng AssetFileStore/DocumentExportService/JobTransitionService; Desktop handler mới phải được coordinator chốt allow-list.
- Dùng job.get_handoff/import_result và result schema theo [v1.1 §4–5](../../decisions/execution-contract-v1.1.md). Text Script/VideoPrompt nhập vào form; Image/VideoRender chọn file bằng dialog host. Request có expectedRevision/clientRequestId; UI không gửi đường dẫn file tùy ý.

## Các bước triển khai

- [ ] Tạo handoff view model từ job snapshot: provider/task/input/scene/reason và bước thao tác; không lấy prompt mới từ editor thay snapshot.
- [ ] Copy/export có scope guard, file tên an toàn; không xuất secret/cookie/profile.
- [ ] Nhập text cho script/prompt hoặc file cho ảnh/video; validate type/size/hash/scope và đúng output task.
- [ ] Với request có thể đã submit, giữ intent/result reference và yêu cầu đối soát; import không đồng nghĩa được phép gửi lại.
- [ ] Finalize bằng boundary chung, idempotent theo job/output; submit import hai lần không tạo asset trùng.
- [ ] Cancel/late import theo T16; manual không được gắn kết quả vào scene đã đổi revision mà không review.
- [ ] Resume chỉ khi input/session/mode hợp lệ; không tự đổi provider hoặc tự mở cửa sổ website.
- [ ] Card manual lấy provider/input từ snapshot, account/profile từ ExecutionBinding nếu đã có; chưa bind thì ghi rõ chưa phân account, không đoán theo Workspace. Cảnh báo uncertain và reference, user xác nhận provenance. Legacy shared_ai chỉ đối soát/import, không chuyển profile mới.
- [ ] Media nhập sau cancel lưu Unlinked; text giữ JobResult lịch sử. Stale scene/run revision giữ kết quả để review, không apply; import hai lần trả output cũ theo idempotency key.

## Kiểm thử và nghiệm thu

Manual đủ bốn task, text schema sai, file sai loại, file kênh khác, import hai lần, import sau cancel, app crash trước/sau finalize, mất root rồi lưu lại. Kết quả đúng job/scene và library, không mất prompt snapshot.

Manual mode là deliverable MVP hợp lệ khi ghi rõ giới hạn; automation vẫn NOT_VERIFIED nếu chưa thử thật.

