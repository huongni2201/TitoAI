# Tích hợp trang Cài đặt và lưu đồng thời

Task T27 integration sau T07,T13,T18,T06a và Q4. Phần tính năng trang Settings làm sớm tại **Q2**, không chờ G4. [Plan 1.2 §3/§5](../QUEUE_EXECUTION_SETTINGS_PLAN.md) và [Settings](../SETTINGS_PLAN.md) là nguồn chuẩn.

- Trang trong shell, 4 tab ngang Tài khoản/Lưu trữ/AI & Prompt/Giao diện; không backdrop/focus trap Settings. Mọi account flow ở tab Tài khoản.
- Roots/provider/budget một draft/revision; đổi tab giữ nháp, rời trang có guard; Lưu commit cả bộ hoặc rollback, lỗi/conflict giữ input. Theme áp/lưu riêng ngay.
- Mở Settings rồi quay lại giữ Scene/Prompt draft; không dùng việc unmount để bỏ dữ liệu ngầm. Account/session operations độc lập với nút Lưu Settings, có feedback rõ.
- Reconnect/restart load saved snapshot thật; lỗi đọc cho retry, không đoán defaults để dispatch. Job đã enqueue giữ snapshot.

UI owner sửa SettingsPage/panels/router theo file hiện có; coordinator giữ SettingsService/handler/DTO dùng chung, không tạo store/handler save thứ hai. Nghiệm thu A01/A02/A10/A13, keyboard/2 theme và WPF/WebView2; backup nhiều root kiểm chéo T26. Theme/disk/bridge failure và revision conflict phải được chạy thật hoặc ghi NOT_RUN.
