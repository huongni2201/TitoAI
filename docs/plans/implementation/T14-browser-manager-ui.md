# Màn hình quản lý browser và trạng thái đăng nhập

- Task gốc: **T14** · Ưu tiên: **P0 / G2**.
- Điều kiện mở: T07,T13 DONE.
- Checkpoint theo STATUS: T14 baseline DONE; mở rộng card/provider/account và migration UX theo [T13a](T13a-provider-profile-isolation.md), không nhận evidence cũ làm PASS phần mới.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Màn hình và hành vi

Nhóm ChatGPT/Gemini/MuseAI là account pool global, một card/account với nhãn Dùng chung toàn ứng dụng, identity/session/capacity/jobs/tabs; TikTok có Workspace. Thêm tài khoản AI→nhãn/provider→profile→login/probe→enable, không chọn/link Workspace. Full pool hiện Chờ slot, account đã bind hiển thị trên job detail theo [v1.1 §1–3](../../decisions/execution-contract-v1.1.md).

## File và contract

- Tạo đề xuất: `src/TiToAI.Web/src/features/browsers/{BrowserManagerPage.tsx,ProfileCard.tsx,LoginStatusBanner.tsx}`.
- Dùng browser.list_profiles/request_login/cancel_login/check_session; mapping enum đúng contracts §1.6 B.
- T13a nối provider_account.create/list/update (global AI) và browser.create_profile/account.link (chỉ TikTok theo kênh), expectedRevision/capacity theo v1.1. Tắt nhận job mới không đóng browser bận; không hard-delete. Job resume/reconcile ở T17, không mở browser để xem job.
- Nhãn Đã dừng/Đang chạy ẩn lấy operationalState được bổ sung ở T13; visibility Hidden hoặc không có job không tự chứng minh browser đã dừng.

## Các bước triển khai

- [ ] Load app-global hoặc Workspace-scoped theo list_profiles; tách Shared AI khỏi profile kênh.
- [ ] Hiển thị Unverified khác SessionExpired/Connected; linked account không suy ra session.
- [ ] Request login chỉ sau CTA, hiện hàng chờ VisibleWaiting và ai đang giữ slot; account sai giữ cảnh báo trong app.
- [ ] Browser đóng giữa login giữ trạng thái chờ và cho user chủ động thử lại; không popup lặp.
- [ ] Banner ghi đúng ChatGPT/Gemini hoặc MuseAI account đang login, chỉ nêu các tab/job của profile đó; không lộ userDataDir/cookie hoặc báo cả nhóm AI bị dừng.
- [ ] Đồng bộ event/snapshot khi reconnect và đổi Workspace; không nhầm global shared profile event với dữ liệu riêng kênh.
- [ ] Nối account dialog T07b với registry thật; quản lý profile metadata nếu UI cần phải theo contract mới đã duyệt.

## Kiểm thử và nghiệm thu

UI với fake state machine: login A đang mở, B xếp hàng, cancel B, đóng A; sai account, network probe Unverified, restart khôi phục mapping. Kiểm tra thật một profile TiToAI riêng: người dùng tự nhập login, xác minh, browser về ẩn trước resume.

Không có nút “Mở browser” chung cho job/preview; manual ngoài login hướng dẫn/copy/import trong app. Theme/layout/keyboard theo panel 05 và UI_BROWSER §3.5.

