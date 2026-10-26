# Browser profile, tab pool và chuyển sang login thủ công

- Task gốc: **T13** · Ưu tiên: **P0 / G2**.
- Điều kiện mở: T04,T05,T06,T06a DONE.
- Checkpoint theo STATUS: T13 baseline DONE; browser/account thật còn NOT_RUN. Phần thay đổi topology theo yêu cầu mới thuộc [T13a](T13a-provider-profile-isolation.md), không được tính DONE từ evidence cũ.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Hành vi cần đạt

Một owner/userDataDir, một account một profile. ChatGPT/Gemini/MuseAI account pool dùng chung toàn app; TikTok riêng Workspace. Tab/job caps cấu hình theo account/task, default AI 3 tabs và MuseAI 1; không giới hạn cứng mỗi role chỉ một tab khi cap đã verified khác. Automation chạy ẩn; login hoàn tất về hidden và xác minh identity; đóng khi toàn profile idle rồi job kế mở lại. Dữ liệu/migration mới theo [v1.1 §1–2](../../decisions/execution-contract-v1.1.md), review T02a rồi triển khai T13a.

## File và contract

- Tạo đề xuất: `src/TiToAI.Browser/{ProfileRegistry.cs,ProfileOwner.cs,TabPool.cs,LoginCoordinator.cs,SessionProbe.cs}`.
- Registry metadata cần persistence mới, không giữ chỉ Dictionary RAM; persistence owner chốt BrowserProfile entity/config/migration.
- DTO browser.* và browser.profile_state_changed theo contracts §1.6 B,§4; state nội bộ phong phú hơn ba visibilityState wire.
- operationalState đã có trong runtime baseline theo STATUS; giữ độc lập visibility/session, không suy Đã dừng từ activeJobCount=0. T13a thêm providerKey/lifecycle/revision theo v1.1.
- Project/reference/packages do integrator; tái sử dụng khóa nền tảng khi đủ, không thêm thư viện lock chỉ vì tên ví dụ trong contract.

## Các bước triển khai

- [ ] Registry cấp profile ID/đường dẫn TiToAI an toàn; không chứa email/handle, không đọc profile Chrome cá nhân.
- [ ] Khóa profile trong tiến trình và cơ chế chống hai app/process cùng mở profile; acquire/release có owner, phục hồi sau crash.
- [ ] Tab pool tách hội thoại, xóa context nghiệp vụ trước tái sử dụng; một job/tab, quota theo account.
- [ ] Login coordinator dùng một slot visible toàn app; request khác VisibleWaiting; user đóng không tự bật lại.
- [ ] Pause dispatch các tab của đúng profile provider cần login, dừng tab chưa submit, đợi in-flight hữu hạn/checkpoint trước close hidden; profile khác không bị pause. Chuyển owner rồi mở login.
- [ ] Probe identity/session theo adapter; chưa có adapter thật chỉ Unverified. Đóng login→mở hidden→xác minh lại→reconcile, không suy thành công từ việc đóng cửa sổ.
- [ ] Chốt interface pause/drain/resume cho T15/T16 và persist handoff checkpoint; không giữ transaction trong lúc chờ user.

## Kiểm thử và nghiệm thu

Fake browser có kiểm soát: hai owner/profile, ba tabs, hai login cạnh tranh, user đóng/sai account/mất mạng, crash ở từng handoff step. Profile test thật chứng minh restart/session và hidden không ló cửa sổ; thiếu account ghi NOT_RUN.

Playwright persistent context đóng là đóng browser; nhiều instance cùng userDataDir không được hỗ trợ. Nguồn tra Context7 ngày 2026-10-26: [BrowserType .NET](https://playwright.dev/dotnet/docs/api/class-browsertype). Giải pháp close/relaunch phải được thử thật trên Windows; không hứa đổi cờ có thể hiện/ẩn context đang chạy.

