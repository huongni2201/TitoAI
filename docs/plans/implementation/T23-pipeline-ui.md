# Giao diện chạy pipeline, review đầu ra và tiếp tục công việc

- Task gốc: **T23** · Ưu tiên: **P0 / G3**.
- Điều kiện mở: T22 DONE.
- Baseline 2026-10-26: Chưa có UI thao tác pipeline và review kết quả.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Màn hình và hành vi

Tích hợp vào Overview/Project/Scene editor đã có: nhập brief, review script/scenes, chạy ảnh/prompt/video, chọn audio, theo dõi queue và mở library. Giữ bố cục panel 02–04; không dựng một app shell mới.

## File và contract

- Tạo đề xuất: `src/TiToAI.Web/src/features/workflows/{PipelinePanel.tsx,BriefForm.tsx,ScriptReviewPanel.tsx,StepResultReview.tsx}`.
- Sửa có phối hợp WorkspaceOverviewPage/ProjectPage/SceneEditorPage; dùng run/step/review DTO T22.
- Dùng ManualHandoffPanel T18c, Queue T17, MediaPicker T12b; một nguồn trạng thái job.
- [V1.1 §3/§6](../../decisions/execution-contract-v1.1.md): user chọn provider, không account theo kênh. Trước reserve hiện Chờ slot/chưa phân tài khoản; sau reserve hiện account/profile binding. Run giữ workflow/brief, job bước sau chưa tạo dùng Settings mới. Commands apply/enqueue có expected revisions/clientRequestId.

## Các bước triển khai

- [ ] Brief lưu draft thật, review scenes trước apply; lỗi schema hiện nội dung để sửa, không tạo scene nửa chừng.
- [ ] Step CTA thể hiện input/provider/account còn thiếu và dẫn tới đúng ô/Settings/login; không enqueue khi thiếu điều kiện.
- [ ] Chọn scenes rõ cho batch, preview provider/root snapshot trước chạy nếu có thay đổi cần người dùng hiểu.
- [ ] Tách prompt xem trước/kết quả khỏi prompt đã áp; dùng nút Áp dụng có conflict handling, không tự ghi đè draft.
- [ ] Progress bước thực tế, trạng thái AwaitingUser có manual/login CTA đúng reason; video đến muộn sau cancel hiện trong library Unlinked.
- [ ] “Tiếp tục” phục hồi run/scene/bước sau restart; event nền không chuyển Workspace hoặc làm mất form.
- [ ] Hiển thị pipeline có audio input riêng, không thêm “Đăng TikTok”, TTS hay render cục bộ từ ảnh mẫu.
- [ ] Tạo run chưa tự chạy: bấm Tạo kịch bản→review→Áp dụng scenes; project đã có scene yêu cầu project mới, không thay/xóa dữ liệu cũ.
- [ ] Batch thiếu input trả lỗi từng scene và không chạy một phần; duplicate apply/import giữ requestId, conflict reload nhưng giữ draft. Script project-level mở review project, không route scene null.
- [ ] Pool đầy là Chờ slot, không lỗi và không yêu cầu mua/thêm account theo số TikTok. Hiển thị account bận/capacity, account quota/login giảm pool chứ không đổi provider.

## Kiểm thử và nghiệm thu

Thực hiện luồng từ kênh mới→project→brief→review→ảnh→prompt→audio→video→library với fake xác định và manual thật. Test missing input, save lỗi, switch Workspace/theme, reconnect event và kết quả stale.

So panel 02–04 ở Light/Dark và DPI; mọi hành động có phản hồi lỗi/cancel. Hoàn tất UI không thay bằng chứng adapter/hidden thật ở T24/T30.

