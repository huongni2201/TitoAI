# Adapter Gemini Web cho kịch bản, ảnh và video prompt

- Task gốc: **T20 (Gemini)** · Ưu tiên: **P0 / G3**.
- Điều kiện mở: T19 DONE; capability tương ứng đủ bằng chứng hoặc manual.
- Baseline 2026-10-26: Chưa có Gemini Web adapter; chưa có evidence automation.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Hành vi cần đạt

Gemini nhận các task Script/Image/VideoPrompt theo Settings, giữ snapshot và scope job. Capability của Gemini phải xác minh độc lập, không suy từ adapter ChatGPT hoặc quảng cáo sản phẩm.

## File và contract

- Tạo đề xuất: `src/TiToAI.Providers/GeminiWeb/{GeminiWebAdapter.cs,GeminiSessionProbe.cs,GeminiResultReader.cs}`.
- Tái dùng profile owner/tab pool, lifecycle, storage và output contract; chỉ chia sẻ primitive đã tồn tại, không dựng framework selector chung sớm.
- Dùng snapshot/typed result validator T15 theo [v1.1 §4](../../decisions/execution-contract-v1.1.md), không phụ thuộc T22 chưa mở. T22 chỉ điều phối/apply kết quả; T13a cấp profile/migration.
- Configuration/selector version và evidence từ T19; adapter không sửa Settings khi lỗi.

## Các bước triển khai

- [ ] Resolve đúng account/session trên profile **Gemini riêng** của T13a, providerKey gemini-web, không dùng ChatGPT/legacy; tách role tab/conversation và không lấy transcript Workspace khác làm input.
- [ ] Validate task/input theo Gemini capability thật, kể cả ảnh tham chiếu nếu task hỗ trợ.
- [ ] Gọi submit-intent boundary trước hành động gửi và persist result/conversation reference.
- [ ] Nhận text/ảnh đủ hoàn chỉnh, validate typed output rồi finalize qua services chung; root/provider snapshot không đổi.
- [ ] Phân loại login/quota/network/UI changed/automation_blocked; login hiện đúng profile sau user CTA, còn lại manual trong app.
- [ ] Reconcile/result download thử lại không gửi lại generation; job uncertain giữ AwaitingUser.
- [ ] Đăng ký Gemini stable key/routing từ Settings, account/profile từ ExecutionBinding T15 của pool global; không pin Workspace hoặc tự chọn lại account. Nhiều kênh dùng cùng Gemini giữ conversation sạch; close context chỉ khi toàn profile idle.

## Kiểm thử và nghiệm thu

Fixture Gemini riêng: nút chưa sẵn sàng, response streaming, text thiếu trường, ảnh hết hạn download, wrong account, cancel/late output, selector đổi. Swap Settings giữa Gemini và ChatGPT chứng minh job mới đúng adapter, job cũ vẫn snapshot.

Account thật/hidden/login/restart cho từng capability công bố phải có evidence; thiếu điều kiện dùng manual T18c với trạng thái trung thực. T20 chỉ DONE khi cả hai provider đáp ứng mode được công bố.

Kiểm riêng Gemini hết session trong khi ChatGPT vẫn hoạt động; identity/profile sai key phải bị chặn trước submit. Fixture output khớp Script/VideoPrompt schema v1.1, không gắn text vào media Asset hoặc tự đổi provider khi input ảnh không được hỗ trợ.

