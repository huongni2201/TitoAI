# Profile riêng từng tài khoản và pool AI toàn ứng dụng

- Task T13a, P0/G2 bổ sung. Depends: T02a,T13,T14 DONE.
- Trạng thái: plan, chưa giao worker/chưa sửa source. T13/T14 baseline DONE giữ nguyên evidence.
- Nguồn chuẩn: [v1.1 §1–3](../../decisions/execution-contract-v1.1.md), [Settings §4](../SETTINGS_PLAN.md), [UI/Browser §3.5/§5–6](../UI_BROWSER_PLAN.md).

## Phạm vi và ownership

Coordinator giữ DTO/allow-list/bootstrap/shared client. Một persistence owner giữ ProviderAccount/BrowserProfile/ExecutionBinding schema liên quan, migrations/model snapshot/upgrade journal. Browser owner giữ ProfileRegistry/ProfileOwner/TabPool/LoginCoordinator/SessionProbe. UI owner giữ browserManagerApi/BrowserManagerPage/ProfileCard/LoginStatusBanner. Brief phải freeze exact paths/hash và thứ tự bàn giao trước dispatch; T15 hiện Coordinator 2 giữ source baseline, phải thống nhất bàn giao contract mới trước sửa chung.

## Hành vi cần đạt

- ChatGPT, Gemini, MuseAI có pool account global; một account một profile/userDataDir/session, không gắn với số TikTok. TikTok vẫn có profile theo Workspace.
- Trang browser nhóm ChatGPT/Gemini/MuseAI dùng nhãn Dùng chung toàn ứng dụng và slot rảnh/bận; nhóm TikTok theo kênh. Thêm tài khoản AI không hỏi Workspace.
- 10 TikTok có thể dùng chung một ChatGPT. Bốn MuseAI có thể phục vụ 10 video, slot nào rảnh lấy job tiếp theo, không cần mapping kênh→MuseAI.
- Login/quiescing chỉ profile liên quan, một login visible toàn app. Profile/role slot có identity/session độc lập; shared account không trộn hội thoại/nội dung kênh.
- Mở ẩn khi dispatch, đóng profile khi không còn tab/job/checkpoint đang dùng; giữ userDataDir để lần sau mở lại. Không đóng browser nếu còn tab khác đang sinh.

## Thực hiện theo mốc

1. T02a review provider-account schema, command catalog, slot/binding/capacity, migration và fixtures. Chỉ code khi contract/file scope đã freeze.
2. ProviderAccount global + BrowserProfile providerKey/lifecycle/revision/identity verification; uniqueness verified provider identity và một account một profile. Không tạo account/session giả ở startup; fresh pool rỗng có CTA thêm.
3. Luồng thêm AI: provider_account.create → card Stopped/Unverified → Đăng nhập → probe → enable. Nhãn chỉ để hiển thị; identity trùng/sai provider không được nhận job. TikTok dùng browser.create_profile/link theo Workspace riêng.
4. TabPool hỗ trợ maxTabs/maxConcurrentJobs/perTaskLimits per account, một job/tab; mặc định AI text/image 3 tabs với một slot mỗi task, MuseAI 1. Thay cap phải có revision và capability validation; giảm cap không kill job.
5. Chuyển shared_ai gộp sang ArchivedLegacy, tạo profile mới riêng và yêu cầu login lại, không copy cookie. MuseAI legacy gắn Workspace chuyển thành global account giữ profile/session và mapping lịch sử; duplicate identity quarantine/user resolve.
6. Profile lock bao trùm launch/probe/job/finalize/close; Closing vẫn chiếm browser budget. Relaunch chỉ sau close/release owner; lỗi close quarantine, không mở context thứ hai. Session Connected chỉ sau probe lại, không suy từ persisted cookie.
7. Tích hợp handlers/client/events global account metadata; job/progress vẫn scope kênh. Phần scheduler reserve/binding hoàn chỉnh do T15 thực hiện theo contract đã chốt, tránh làm hai scheduler.
8. DB/files fixture rồi integrated host/UI trên profile test riêng. Ghi report/hash/migration/version/scenario/mode, không tự đánh DONE bằng build.

## Ma trận nghiệm thu

| ID | Scenario | Expected |
|---|---|---|
| P01 | Fresh app, thêm 1 ChatGPT/1 Gemini/4 MuseAI | Sáu profile/context độc lập global, không hỏi TikTok/Workspace; duplicate request không thêm profile |
| P02 | Đổi qua 10 Workspace | Cùng global pool, chỉ TikTok cards đổi theo kênh |
| P03 | Hai login cùng AI identity | Chỉ một Active account, profile thứ hai không nhận automation |
| P04 | Gemini login; ChatGPT đang chạy | Chỉ Gemini quiesce, ChatGPT tiếp tục |
| P05 | MuseAI A login, B có job | B tiếp tục, identity/session không lẫn |
| P06 | Hai login cạnh tranh/đóng/sai identity | Một visible; closed giữ auth_closed; không Connected giả |
| P07 | MuseAI job xong→close→job sau mở lại | Owner duy nhất, cùng userDataDir, probe đúng, không mất session/result |
| P08 | ChatGPT tab 1 xong khi tab 2 đang chạy | Nhả tab 1 nhưng không đóng toàn browser |
| P09 | Legacy gộp + MuseAI link nhiều Workspace | Global migration giữ identity/profile/history; duplicate không clone cookie |
| P10 | Crash ở archive/metadata/dir/close; hai app tranh lock | Idempotent upgrade, không context thứ hai/resend/mất checkpoint |

P01/P07/P09/P10 phải có DB/files fixtures thật; state machine có thể fake. Session/hidden trên Windows cần account test thật; thiếu ghi NOT_RUN, chuyển evidence đầy đủ T19/T30.
