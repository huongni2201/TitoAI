# Tài khoản và phiên đăng nhập trong Cài đặt

Task T14 baseline DONE; thay đổi UI hiện hành thuộc **Q2**, sau Q0/Q1. [Plan 1.2 §3](../QUEUE_EXECUTION_SETTINGS_PLAN.md#3-trang-cài-đặt) thay trang Trình duyệt AI riêng và phân nhóm account theo Workspace.

- Chuyển card/browser/session/login banner vào Cài đặt → Tài khoản; một danh sách có filter provider/session, metadata thật và capacity/enable rõ.
- Mọi account action ở Settings; Workspace/Scene có thể dẫn tới tab đó, không chứa connect form riêng. Kênh TikTok nếu cần định danh vẫn quản lý ở Settings, không route account AI.
- Commands và DTO đúng Q0/Q1; login nhập tay, một visible window, closed/wrong-account/network giữ state/reason đúng. Không nút kill browser bận hoặc mở browser cho job preview.
- Snapshot/event reconnect có generation guard; event không được lẫn job scope. UI thông báo loading/empty/error/retry thật, không placeholder success.

UI owner giữ features/browsers và account settings; App/router/client chung do coordinator tích hợp. Kiểm A01/A02/A07/A13, Light/Dark/keyboard thật; thiếu WebView2/account runtime ghi NOT_RUN. Không lấy test SSR làm interaction evidence.
