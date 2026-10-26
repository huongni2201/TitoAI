# MuseAI video adapter trên pool account dùng chung

- Task T21, P0/G3. Depends T19 DONE; T13a/T15/T16/T18 contracts/runtime đã có theo dependency.
- URL/site/account capabilities phải xác minh tại T19; chưa có evidence automation. [V1.1 §1–5](../../decisions/execution-contract-v1.1.md) là contract đầu vào/slot/binding/result.

## Hành vi

Mọi Workspace dùng chung pool MuseAI toàn app. Một tài khoản là một browser/profile riêng; không có yêu cầu link MuseAI riêng cho TikTok. Scheduler chọn account rảnh khi reserve rồi adapter nhận ExecutionBinding bất biến, không tự chọn/chuyển account.

Input là selected image + saved videoPrompt + audio MP3/WAV nguyên bản từ job snapshot; output lưu root/job scope gốc. Không TTS/FFmpeg/cắt ghép. Một account mặc định một video đang xử lý; quota/giới hạn thực tế có thể thấp hơn.

## Các bước triển khai

1. Resolve binding.providerAccountId/profileId và đúng provider museai; kiểm identity global còn đúng, không kiểm Workspace sở hữu account AI. Kiểm workspace/project/scene ownership của input/output độc lập.
2. Host mở hidden đúng profile được reserve, probe lại session/identity; mất phiên AwaitingUser/login_required đúng profile. Một MuseAI login không pause pool còn lại.
3. Validate asset type/hash/readability và limits đã xác minh; upload file snapshot, commit side-effect intent trước Generate, persist task/result reference sớm.
4. Giữ slot trong lúc website xử lý, nhận/download/finalize; không nhả ngay sau submit. Quota/network/selector/CAPTCHA ngoài login xử lý T16/manual, không reroute request uncertain.
5. Finalize file/Asset/JobResult và state; cancel sau submit giữ Unlinked result. Lỗi lưu file giữ reference để tải/lưu lại, không generate lại.
6. Khi profile không còn job/checkpoint: đóng context và release owner rồi nhả slot/wake scheduler. Job sau mở lại cùng profile/userDataDir và probe; không luân phiên nhiều identity trong profile, không context trùng khi Closing.
7. Browser close/probe/reconcile lỗi quarantine account và giữ evidence; pool account khác tiếp tục nhận job chưa bind. Không đổi snapshot/input hay binding để chữa lỗi.
8. Manual export/import theo T18c có account/reference/provenance; website không đáp ứng automation vẫn phải ghi mode thực tế.

## Files và tích hợp

Tạo khi cần Providers/MuseAi/{MuseAiVideoAdapter,MuseAiSessionProbe,MuseAiReconciler}; reuse T13a owner/pool, T15 binding/result validator, T16 submit/reconcile, T09 file store. Coordinator giữ project refs/DTO/allow-list; adapter không tạo scheduler/profile registry thứ hai hoặc đoán URL/selector.

## Nghiệm thu

- Fake: 10 Workspace tạo 10 video qua 4 global MuseAI, max 4 concurrent khi budget đủ; slot xong cấp ngay, close/reopen đúng thứ tự; không mapping kênh cố định.
- Cùng account xử lý A rồi B, input image/audio hashes đúng và output gắn đúng job/scene; cross-workspace asset injection bị chặn.
- Quota/login A trong khi B/C/D chạy; bound A không resend sang B, job chưa bind vẫn dùng pool còn lại.
- Crash trước/sau intent/submit/reference/finalize/close; uncertain slot giữ quarantine, không duplicate generate.
- Account thật: từng capability công bố có job hợp lệ, hidden không ló/focus, login→hidden, session giữ qua close/reopen/restart, result download/reconcile. T30 kiểm 4 MuseAI/10 kênh nếu được cấp quyền; thiếu account ghi NOT_RUN, không dùng fake thay bằng chứng thật.
