# Cài đặt AI riêng cho ảnh, kịch bản và video prompt

> **Delta hiện hành:** [plan queue/Settings 1.2](../QUEUE_EXECUTION_SETTINGS_PLAN.md) thay popup Settings, nhóm account shared/private, connect tại Workspace và round-robin Workspace. Dùng [TASKS](../../agents/TASKS.md) cho trạng thái/gate; Baseline/checkbox dưới không là evidence đã chạy.

- Task gốc: **T18 (AI Settings)** · Ưu tiên: **P0 / G3**.
- Điều kiện mở: T18 prerequisites DONE; registry T18a đã chốt.
- Baseline 2026-10-26: UI nền có nhóm AI nhưng chưa có lưu/routing thật.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Hành vi cần đạt

Ba lựa chọn độc lập Gemini Web/ChatGPT Web; lần đầu “Chưa chọn”. Lưu/restart phục hồi đúng; job mới dùng provider đã chọn, job cũ giữ snapshot. VideoRender dùng pool MuseAI global; scheduler chọn account rảnh khi reserve, không account riêng Workspace.

## File và contract

- Tạo đề xuất: `src/TiToAI.Web/src/features/settings/AiProviderSettingsPanel.tsx`.
- Sửa SettingsStore/SettingsHandlers T09a, SettingsPage.tsx (đích Q2, kiểm tra file hiện có trước tạo) qua owner chung; không tạo handler settings.save thứ hai.
- Keys ai.providers.image/script/videoPrompt; SettingsDto và provider.list theo contracts §1.6 C,§5.
- [V1.1 §3](../../decisions/execution-contract-v1.1.md) thêm Settings revision/expectedRevision và mode per-task; cùng SettingsStore/transaction với storage, không tạo config routing thứ hai.

## Các bước triển khai

- [ ] Load saved settings và registry, giữ null khi chưa chọn; lỗi đọc không tự điền default.
- [ ] Dropdown từng task chỉ nhận stable key phù hợp; hiện session/mode/verification và lý do bị chặn.
- [ ] Save validation cả roots và provider fields trong cùng transaction; Hủy/đóng bỏ draft nhưng theme đã áp dụng vẫn giữ.
- [ ] Disabled/invalid key yêu cầu chọn lại; provider còn manual được trình bày rõ mode, không giả verified automation.
- [ ] Editor/pipeline đọc provider hiệu lực từ một nguồn Settings; link đổi provider mở đúng tab Cài đặt.
- [ ] Retry/resume hiển thị provider snapshot cũ; đổi current config không reroute hoặc resend job cũ.
- [ ] Workflow/config có version nếu cho chỉnh từ UI phải cùng registry; không có nguồn cấu hình routing riêng theo Workspace trong MVP.
- [ ] Hiển thị liên kết tới card ChatGPT/Gemini tương ứng, phân biệt đã chọn provider với đã đăng nhập/verified capability; chọn Gemini không đăng nhập vào profile ChatGPT. MuseAI/account AI được quản lý ở browser global pool, không thêm vào ba dropdown AI hoặc link từng Workspace.

## Kiểm thử và nghiệm thu

Chọn Gemini ảnh/ChatGPT script+prompt, đảo lại, chọn cùng provider, giữ null một tác vụ; restart và dispatch fake kiểm đúng đích. Invalid key/adapter disabled/lỗi save giữ config cũ và draft mới; không tự chuyển provider khi quota/login lỗi.

Kiểm đầy đủ 8 tổ hợp Script/Image/VideoPrompt; job tạo trước save giữ provider/config/root input snapshot; account/profile chưa chọn tới reserve và binding đã chọn thì không đổi. Job sau save đổi đúng field. Hai editor Settings cùng revision: save thứ hai conflict và giữ draft. Batch enqueue trong lúc save chỉ dùng một version Settings.

T19/T20 bổ sung evidence thật từng capability; fake chứng minh routing, chưa chứng minh website hỗ trợ.

