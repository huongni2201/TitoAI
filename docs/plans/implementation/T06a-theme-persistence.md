# Lưu theme và đồng bộ Windows–WPF–React

- Task gốc: **T06a** · Ưu tiên: **P0 / G1**.
- Điều kiện mở: T04,T05,T06 đều DONE.
- Trạng thái: **DONE** từ 2026-10-26 11:11 (+07); review/evidence tại [T06a coordinator review](../../reviews/T06a-coordinator-review.md).
- Baseline: Trước task này, host và React giữ preference riêng trong bộ nhớ; chưa có persistence.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Hành vi cần đạt

Một preference toàn app `ui.theme` trong AppSettings, ba giá trị system/light/dark. Host tính effectiveTheme, React nhận snapshot/event; đổi theme không mất form, route hoặc job. System theo Windows trong runtime và restart giữ đúng lựa chọn.

## File và dữ liệu

- Sửa: `src/TiToAI.Desktop/Hosting/{HostThemeService.cs,IHostThemeService.cs,WebView2HostManager.cs}`, MainWindow.xaml.cs và bootstrap qua integrator.
- Sửa: `src/TiToAI.Web/src/context/ThemeContext.tsx`, `src/TiToAI.Web/src/style.css`, `src/TiToAI.Web/src/components/SettingsModal.tsx`; thêm `src/TiToAI.Infrastructure/Settings/ThemePreferenceStore.cs`.
- Smoke evidence: `tests/TiToAI.Infrastructure.SmokeChecks/ThemePreferenceStoreSmokeChecks.cs`.
- Dùng AppSetting hiện có; không thêm bảng theme. Commands theme.get_preference/theme.set_preference, event theme.changed theo contracts §1.6 F,§6.

## Các bước triển khai

- [x] Đọc preference trước khi render nội dung; thiếu/sai dùng System, lỗi đọc giữ DB cũ và Settings báo có thể thử lại.
- [x] Host theo dõi thay đổi Windows, marshal về UI dispatcher và gỡ subscription khi đóng.
- [x] Lưu preference qua store; áp dụng ngay cả khi lưu thất bại nhưng trả lỗi rõ là chỉ có hiệu lực trong phiên.
- [x] Nối ThemeContext với bridge; host là nguồn effectiveTheme; bỏ listener `matchMedia` độc lập sau bootstrap.
- [x] Đồng bộ nút nhanh và nhóm Giao diện; theme áp dụng ngay, độc lập với bản nháp Storage/AI.
- [x] Chặn nội dung chính hiện trước khi nhận theme snapshot; đồng bộ WPF/WebView2/HTML theo preference hiện hành.
- [x] G1 đã đóng trên cùng working tree sau T05/T06.

## Kiểm thử và nghiệm thu

**Đã chạy:** Infrastructure smoke trên DB tạm xác nhận key thiếu, upsert dark/light và từ chối giá trị sai. WPF/WebView2 test app xác nhận System, Dark và Light đều giữ nguyên sau restart; System hiện dùng Windows Light. Frontend build PASS; solution build PASS 0 warning/error.

**T30 release checks còn bắt buộc:** đổi Windows theme khi app đang mở ở preference System; xác nhận Light/Dark thủ công không đổi theo Windows; fault-inject lỗi đọc/lưu và quan sát cảnh báo UI; kiểm startup flash cùng form/route/job đang chạy. Windows theme chưa được đổi trong phiên này. Các checks còn thiếu không được coi PASS và chặn T30 release sign-off.

Spec chuẩn: UI_THEME_PLAN §1–5. Nếu anti-flicker minh họa trong contracts không đúng API hoặc ép Dark khi chọn Light, coordinator sửa minh họa trước khi triển khai, giữ hành vi chuẩn tắc.

