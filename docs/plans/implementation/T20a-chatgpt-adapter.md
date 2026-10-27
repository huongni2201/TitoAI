# Adapter ChatGPT Web cho kịch bản, ảnh và video prompt

> **Delta hiện hành:** [plan queue/Settings 1.2](../QUEUE_EXECUTION_SETTINGS_PLAN.md) thay popup Settings, nhóm account shared/private, connect tại Workspace và round-robin Workspace. Dùng [TASKS](../../agents/TASKS.md) cho trạng thái/gate; Baseline/checkbox dưới không là evidence đã chạy.

- Task gốc: **T20 (ChatGPT)** · Ưu tiên: **P0 / G3**.
- Điều kiện mở: T19 DONE; capability tương ứng đủ bằng chứng hoặc manual.
- Baseline 2026-10-26: Chưa có ChatGPT Web adapter; chưa có evidence automation.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Hành vi cần đạt

Thực thi đúng task được Settings chọn, trong tab role thuộc profile **ChatGPT riêng** do T13a cấp, providerKey chatgpt-web; không dùng profile Gemini/legacy. Conversation mới, context không lẫn kênh. Chỉ tự động hóa capability đã VERIFIED; phần còn lại dùng manual in-app T18c.

## File và contract

- Tạo đề xuất: `src/TiToAI.Providers/ChatGptWeb/{ChatGptWebAdapter.cs,ChatGptSessionProbe.cs,ChatGptResultReader.cs}`.
- Dùng registry/config T18, profile/tab owner T13a, submit/reconcile boundary T16, snapshot/typed result validator T15 theo [v1.1 §4](../../decisions/execution-contract-v1.1.md) và file store T09. T22 chỉ điều phối/apply kết quả, tránh dependency T20↔T22.
- Project/reference do integrator; không đưa selector/URL vào Application/React. Selector lấy từ website thật đã xác minh, không giả API.

## Các bước triển khai

- [ ] Nạp capability/evidence từ T19 và config version; kiểm session/identity mà không dùng profile cá nhân.
- [ ] Mỗi tab một job, hội thoại sạch theo job/role; chỉ gửi snapshot prompt/input đã validate. T15 chọn slot global khi reserve, adapter không pin account theo Workspace; cùng ChatGPT lần lượt phục vụ 10 TikTok không lẫn context. Tab hoàn tất nhả slot; browser chỉ đóng khi toàn profile idle.
- [ ] Commit intent qua T16 trước click; lưu conversation/result reference bền vững càng sớm càng có thể.
- [ ] Với text, đọc kết quả hoàn chỉnh rồi validate schema/purpose; với ảnh, download về root snapshot và kiểm file/hash.
- [ ] Text tạo ý tưởng/kịch bản và tạo/tối ưu prompt phải kèm `tiktokPost` theo [SOURCE §7.4](../SOURCE_PLAN.md#74-tạo-ý-tưởngprompt-kèm-nội-dung-đăng-tiktok--p0); dùng schema version được coordinator duyệt, không mở thêm job chỉ để viết caption. Fixture gồm output đủ bài đăng, thiếu caption/hashtag và job schema cũ; không finalize output mới thiếu trường bắt buộc.
- [ ] Timeout/mất session/UI đổi/CAPTCHA ngoài login trả reason đúng; không retry click hoặc chuyển provider ngầm.
- [ ] Reconcile bằng reference/history được phép; chưa chứng minh chưa nhận thì giữ AwaitingUser.
- [ ] Manual chỉ hiển thị instruction/copy/import trong app; login visible do LoginCoordinator giữ ownership.

## Kiểm thử và nghiệm thu

Fixture trang local có kiểm soát: response chưa xong, text sai schema, nhiều ảnh/download fail, cancel, missing selector và duplicate reconcile. Fixtures không thay website thật.

Account test thật: ít nhất một trường hợp hợp lệ cho từng capability công bố, hidden không ló/focus, login→hidden, restart/result reference. Nếu chỉ manual, ghi rõ từng task manual và evidence end-to-end; không công bố automation đã đạt.

Ca bắt buộc: Settings Image=ChatGPT/VideoPrompt=Gemini và đảo lại; yêu cầu ChatGPT dùng đúng global account/profileId trong ExecutionBinding; Gemini login không chặn job ChatGPT đủ điều kiện. Script job có project, không có scene; text output không tạo Asset giả.

