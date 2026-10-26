# Xác minh website, tài khoản và ma trận capability

- Task gốc: **T19** · Ưu tiên: **P0 / G3**.
- Điều kiện mở: T18 DONE.
- Baseline 2026-10-26: Chưa có provider-matrix.md; MuseAI chưa được xác định website cụ thể.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Kết quả cần bàn giao

Tài liệu quyết định `docs/decisions/provider-matrix.md`, mỗi provider/task có VERIFIED/NOT_VERIFIED/UNSUPPORTED và evidence. Đây là task nghiên cứu/kiểm chứng trước adapter, không đoán endpoint/selector từ tên sản phẩm.

## Phạm vi ghi và đầu vào

Coordinator sở hữu provider-matrix và capability config. Đọc registry T18, versions.md, UI_BROWSER §5–8. Cần URL chính xác và account test được cấp quyền; tìm context sẵn có trước khi hỏi phần còn thiếu. Không ghi secret hay credential vào tài liệu.

## Các bước thực hiện

- [ ] Xác định đúng website/tenant/account/tier, điều kiện sử dụng automation và quyền upload/download; ghi URL tài liệu chính thức, ngày tra.
- [ ] Tách hàng Script/Image/VideoPrompt/VideoRender; ghi input type/size, output text/file, audio MP3/WAV, result reference và timeout/quota quan sát được.
- [ ] Xác minh identity/session probe; thử logout, sai account, MFA/user đóng login và restart giữ profile.
- [ ] Thử hidden trên Windows, chuyển login rồi quay về hidden; quan sát cửa sổ/focus thật và shared AI nhiều tab.
- [ ] Dùng hai profile ChatGPT/Gemini riêng và ít nhất hai MuseAI accounts riêng khi có quyền; kiểm thư mục/context/session không lẫn, identity trùng/sai provider bị chặn, cùng account phục vụ nhiều Workspace vẫn hợp lệ; login một profile không pause bên kia. Thiếu account giữ từng ca NOT_RUN, không suy từ profile khác.
- [ ] Thử download/reconcile với result reference, job còn chạy, kết quả đã xong và không rõ đã nhận; không cố tạo tải hoặc request tính phí ngoài phạm vi test.
- [ ] Xác định manual in-app path cho capability không đạt; không bật automation cho NOT_VERIFIED.
- [ ] Công bố matrix theo capability + version adapter + môi trường/revision và liên kết evidence đã che thông tin nhạy cảm.

## Mẫu hàng ma trận

Provider/task | Website/account scope | Input/output | Hidden | Login/identity | Download | Reconcile | Mode | Status | Evidence/date.

Mỗi row gắn providerKey/taskType/configVersion/evidenceId; inputLimits chưa đo được để null và chặn input chưa thể validate cho automation. Ghi rõ identity probe dùng bằng chứng nào, URL login đã xác minh, account tier, ngày kiểm và điều kiện hết hiệu lực sau site/config thay đổi. T19 chỉ cập nhật VERIFIED cho ca đã chạy, không seed session Connected. Registry xuất schema [v1.1 §3](../../decisions/execution-contract-v1.1.md).

VERIFIED chỉ áp dụng đúng task/environment đã thử, không lan sang toàn provider. Nếu thiếu account/URL/quyền, giữ NOT_VERIFIED với điều kiện mở lại; manual hợp lệ cho phép tiếp tục T20/T21 theo TASKS, nhưng không thay evidence account thật trước release.

## Nghiệm thu

Mỗi capability automation bật trong registry có evidence truy vết được; mỗi giới hạn có manual hướng dẫn khả thi hoặc bị chặn rõ. Dọn kết luận cũ trong versions/review chỉ khi nội dung thay thế đã được giữ và liên kết cập nhật.

