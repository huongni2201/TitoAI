# Log có correlation, bảo vệ bí mật và cảnh báo dung lượng

- Task gốc: **T27 (logging/secrets)** · Ưu tiên: **P0 / G4**.
- Điều kiện mở: T07,T13,T18,T06a DONE.
- Baseline 2026-10-26: Bridge đã tránh ex.Message ở một số log; chưa có secret store/retention tích hợp.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Hành vi cần đạt

Theo dõi lỗi bằng correlation/job IDs mà không lộ token/cookie/profile path/credential. Bí mật phải được host bảo vệ bằng Windows user; disk/root/profile warnings có hành động khắc phục, không làm UI giả thành công.

## File và contract

- Tạo đề xuất: `src/TiToAI.Infrastructure/{Diagnostics/AppLog.cs,Security/WindowsSecretStore.cs,Storage/StorageHealthService.cs}`.
- Rà BridgeMessageRouter, WebView2HostManager, adapters, scheduler và persistence logs qua owners tương ứng.
- Không phát secret/path profile trong bridge DTO; Settings chỉ hiển thị ba root paths được spec cho phép. Logging severity/event schema dùng chung, không tạo log framework nếu nền tảng sẵn đủ.

## Các bước triển khai

- [ ] Chốt loại secret thực sự cần lưu; dùng DPAPI CurrentUser hoặc Credential Manager qua API đã kiểm chứng, không lưu password đăng nhập website từ app.
- [ ] ACL TiToAI profiles/logs/secrets đúng Windows user; tách profile host WebView2 và provider profile.
- [ ] Structured log bằng correlationId/jobId/event/error code và exception type; redaction trước ghi, không dump payload/prompt/cookie tự động.
- [ ] Rotation/retention theo dung lượng và thời gian đã chọn; export diagnostics preview phạm vi, giữ secrets ngoài gói.
- [ ] Theo dõi root unavailable/low space/temp orphan, cung cấp thông báo và Retry; không auto xóa media người dùng.
- [ ] Error boundary host/UI trả thông báo hữu ích không raw exception; cancel/retry không che mất nguyên nhân.
- [ ] Rà nguồn navigation/popup và resource preview không tạo quyền file/shell arbitrary.

## Kiểm thử và nghiệm thu

Canary secrets/paths cài vào nested errors, arrays, download URL, config: tìm trong log/DTO/export không còn canary. Secret chỉ đọc bởi Windows user phù hợp; dữ liệu mã hóa hỏng trả lỗi kiểm soát được. Root offline/low disk không resend AI.

Kiểm tra cross-workspace bridge/resource access và profile permission bằng môi trường test. Đây là invariant của sản phẩm, không thu thập hoặc xuất credential người dùng.

