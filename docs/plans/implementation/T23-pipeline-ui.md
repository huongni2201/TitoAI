# Giao diện chạy pipeline, review đầu ra và tiếp tục công việc

> **Tối ưu đã chốt:** [Plan sản xuất từ ảnh §3–4](../PRODUCT_IMAGE_VIDEO_WORKFLOW_PLAN.md) dùng đầu vào chung cấp project, lập cảnh có mục đích, tái sử dụng ảnh nền tảng và chỉ tạo biến thể cần thiết. Cảnh đủ input chạy tiếp ngay qua queue chung; sửa/tạo lại chỉ tác động phần phụ thuộc. UI chính là Chuẩn bị → Đang chạy → Kết quả, chi tiết ảnh/prompt mở khi cần. Đây là yêu cầu triển khai, chưa phải evidence đã hỗ trợ.


> **Đã chốt 2026-10-27:** [Luồng sản xuất từ ảnh](../PRODUCT_IMAGE_VIDEO_WORKFLOW_PLAN.md) tự chạy sau một lần Bắt đầu: lập nhiều cảnh theo prompt → ảnh → prompt chuyển động → video, không bắt duyệt trung gian. Audio tùy chọn; mẫu và sản phẩm dùng lại trong thư viện Workspace. Trong mode này, tự áp output hợp lệ bằng revision guard; yêu cầu audio bắt buộc/duyệt từng bước bên dưới là baseline cũ đã được thay trong phạm vi này. Contract/version/validator/adapter còn phải triển khai đồng bộ; chưa là evidence code hỗ trợ. Lỗi, capability không tương thích, login, xung đột hoặc uncertainty vẫn dừng đúng policy.


> **Delta 2026-10-27 — sản xuất từ ảnh:** [Plan ảnh mẫu/sản phẩm → ảnh đã duyệt → prompt chuyển động → video](../PRODUCT_IMAGE_VIDEO_WORKFLOW_PLAN.md) thay bố cục Project/Scene dạng bảng làm luồng chính và bổ sung ba chế độ đầu vào. Luồng ảnh đi thẳng, không bắt tạo kịch bản trước; audio tùy chọn và tự động chạy theo quyết định mới. Đây là spec bàn giao, chưa nghiệm thu code.

> **Delta hiện hành:** [plan queue/Settings 1.2](../QUEUE_EXECUTION_SETTINGS_PLAN.md) thay popup Settings, nhóm account shared/private, connect tại Workspace và round-robin Workspace. Dùng [TASKS](../../agents/TASKS.md) cho trạng thái/gate; Baseline/checkbox dưới không là evidence đã chạy.

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
- [ ] Review ý tưởng/kịch bản và kết quả prompt có vùng “Nội dung đăng TikTok” theo [SOURCE §7.4](../SOURCE_PLAN.md#74-tạo-ý-tưởngprompt-kèm-nội-dung-đăng-tiktok--p0): caption/hashtag, Sửa/Lưu/Áp dụng, Sao chép bài đăng và xuất TXT. Chọn ý tưởng đổi đúng candidate; bài đăng hiện hành thuộc project, sửa prompt scene chỉ đề xuất cập nhật. Thiếu nội dung hoặc bản cũ cần review phải hiện rõ, giữ draft khi chuyển trang/theme và xử lý conflict.
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

