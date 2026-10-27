# Queue bền vững và cấp tài nguyên executor

Task T15 hiện **REVIEW** theo H1–H4. Bổ sung theo **Q1**, sau Q0; [plan 1.2 §2](../QUEUE_EXECUTION_SETTINGS_PLAN.md#2-luồng-job-và-ranh-giới-dữ-liệu) là thuật toán chuẩn. V1.1 round-robin theo Workspace đã được thay.

## Phần cần hoàn thiện

- Một queue SQLite chung; FIFO giữa job đủ điều kiện, skip job chờ provider/capacity/login/backoff; Workspace là scope dữ liệu/pause source, không queue hoặc account affinity.
- Input snapshot immutable tại enqueue; binding job–account/profile chốt tại reserve; retry/reconcile giữ binding nhưng vẫn kiểm lại eligibility và capacity.
- Atomic Job + binding + slot occupancy + lease token/attempt. Kiểm budget browser/job toàn app, maxTabs/maxConcurrentJobs/perTaskLimits và capability tại mọi reserve.
- Chiếm slot cụ thể rảnh với uniqueness/CAS ở DB. Binding lịch sử không là occupancy; không cấp slot từ ActiveCount+1. Uncertain/Closing còn giữ tài nguyên cần bảo vệ.
- Launch/probe/submit sau commit; finalize/persist/close/release/wake theo thứ tự an toàn. Close host thật, không no-op; tab khác đang chạy thì không đóng context.
- Heartbeat/side-effect/progress/result kiểm state/owner/token/expiry; restart/recovery không resend mù. T16 bổ sung recovery đầy đủ trên invariant này.

## Ownership và evidence

Một backend/persistence owner giữ Application/Jobs và occupancy/migration liên quan. Coordinator giữ DTO/handlers/bootstrap. Giữ implementation đang có và test có giá trị; thêm regression cho H1–H4, không viết lại scheduler thứ hai.

Đạt A03–A09/A11/A13; SQLite concurrency/upgrade fixtures thật, fake clock/provider xác định, timeline reserve/submit/close/slot và attempts. Kiểm budget=1, cap giảm, retry account disabled, slot-1 xong trước slot-2, uncertain và hai dispatcher cạnh tranh. Account/site thật là evidence riêng, ghi NOT_RUN khi thiếu.
