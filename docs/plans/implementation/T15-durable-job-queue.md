# Hàng đợi bền vững và phân phối slot trong pool AI chung

- Task T15, P0/G2. Depends T08,T09,T10,T13,T02a,T13a DONE.
- Baseline theo STATUS: Job entity có lease/snapshot nhưng scheduler chưa DONE. Không triển khai trong lượt viết plan.
- Nguồn [v1.1 §3–5](../../decisions/execution-contract-v1.1.md); T16 xử lý recovery/cancel, T18 nối provider registry thật.

## Dữ liệu và ranh giới

- Job.ProjectId bắt buộc; SceneId null chỉ Script; composite ownership/FK và JobResult text/media theo v1.1.
- Input snapshot chốt tại enqueue: scope/provider/config/root/prompt/template/workflow/input/hash/revision. Chưa chọn account/profile, không đọc MuseAI link Workspace.
- Reserve lần đầu chốt ExecutionBinding account/profile/identity/tab slot; retry/reconcile giữ binding. Account selection diễn ra từ global pool lúc slot trống, không pin mỗi TikTok một account.
- Persist ProviderSlot, attempt, lease owner/token và unique constraints trong SQLite; Channel chỉ wakeup. Một persistence owner giữ schema/migration, coordinator giữ DTO/handlers.

## Thuật toán triển khai

1. job.enqueue kiểm scope/input/setting, idempotency lookup trước resolve snapshot. Thiếu provider setting báo lỗi; chưa có account hoặc hết slot vẫn lưu Queued với lý do chờ, không tăng attempts.
2. Scheduler lấy các Workspace Active theo round-robin; FIFO từng kênh/provider. Tài khoản AI là pool dùng chung, không blacklist account vì đã phục vụ Workspace khác.
3. Trong transaction ngắn chọn job đủ điều kiện và account cùng provider còn capacity (enabled, identity/session/mode phù hợp, không login/quota/backoff/uncertain). Chọn ít activeJobs nhất rồi lastAssignedAt/id. Reserve slot + binding nếu chưa có + CAS Queued→Reserved + leaseToken + attempt trong một transaction.
4. Commit rồi mới launch hidden/probe/submit. Một job/tab, cap theo account/task/global; maxOpenBrowsers mặc định 4, maxConcurrentJobs 8, MuseAI mặc định 1/account; config/capability thấp hơn thắng.
5. Job chưa bind có thể chọn bất cứ account rảnh đúng provider; job đã bind chỉ dùng account đó cho retry/reconcile. Không fallback Gemini↔ChatGPT khi pool đầy/quota lỗi.
6. Hoàn thành: validate/finalize JobResult/Asset/state → đóng context nếu profile không còn job/tab/checkpoint → release owner/browser budget/slot → wake ngay. Một tab AI xong trong browser có tab khác thì chỉ nhả tab, giữ context.
7. Hết slot giữ Queued/waiting_for_capacity, không busy poll hoặc tính là failed attempt. Quota/login account A không dừng B. Giảm giới hạn không kill running jobs.
8. Lease expiry uncertain không free slot mù; chuyển recovery T16. Crash sau commit result trước close được startup close/reconcile idempotent, không generate lại.
9. get_status/list_active/list_history/events theo v1.1. Metadata pool global, job/progress/results scope Workspace. Worker cũ không được heartbeat/complete sau đổi lease token.

## File ownership dự kiến

Application/Jobs: JobService, JobScheduler, JobReservation, JobSnapshotFactory, result validators; Infrastructure persistence: JobAttempt, JobResult, ExecutionBinding, ProviderSlot theo một owner. Desktop JobHandlers/allow-list và Web typed client do coordinator nối. Exact paths/hash được freeze trước dispatch; không scaffold dự án/framework mới nếu smoke runner hiện có đáp ứng.

## Kiểm thử bắt buộc

| ID | Fixture | Expected |
|---|---|---|
| Q01 | 10 Workspace, 1 ChatGPT, cap Image=1 rồi cap=3 verified fake | Tối đa 1 rồi 3 job Image, còn lại Queued; tất cả kênh được phục vụ |
| Q02 | 10 video, 4 MuseAI cap=1 | 4 Running/Reserved, 6 Queued; job đầu xong nhường ngay cho job 5, không chờ cả đợt |
| Q03 | Job video hoàn tất không theo thứ tự | Đúng 10 output về 10 scope gốc, không gắn theo UI Workspace đang mở |
| Q04 | Hai scheduler reserve cùng job/slot | Một winner duy nhất, attempts tăng một lần, token fencing chặn worker cũ |
| Q05 | Đóng browser rồi mở lại cho job kế | Cùng profile/session; không có hai owner trong Closing; lỗi close quarantine |
| Q06 | Mất wakeup/restart/Workspace pause | DB queue còn, không starvation quá hai vòng khi slot có sẵn |
| Q07 | Save provider/root/template lúc queue đang có job | Input snapshot không đổi; account chỉ chọn khi reserve; binding đã cấp không đổi |
| Q08 | Một account login/quota/uncertain | Pool còn lại vẫn chạy; không chuyển job đã submit sang account khác |
| Q09 | Cancel trước reserve, duplicate enqueue/output | Không slot leak/job trùng; history/results idempotent |
| Q10 | 1 shared account phục vụ kênh A rồi B | Context hội thoại sạch, hash/input/output không rò giữa kênh |

SQLite/file thật + fake clock/browser/provider có kiểm soát; log reserve timeline, active slot counts, close/reopen order, per-workspace latency và submit counts. Fake không chứng minh website/account/hidden; T19/T30 chạy thật riêng.
