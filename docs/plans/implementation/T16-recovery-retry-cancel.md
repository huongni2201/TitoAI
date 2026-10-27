# Phục hồi crash, retry, cancel và đối soát không gửi trùng

> **Delta hiện hành:** [plan queue/Settings 1.2](../QUEUE_EXECUTION_SETTINGS_PLAN.md) thay popup Settings, nhóm account shared/private, connect tại Workspace và round-robin Workspace. Dùng [TASKS](../../agents/TASKS.md) cho trạng thái/gate; Baseline/checkbox dưới phản ánh trạng thái đã nghiệm thu.

- Task gốc: **T16** · Ưu tiên: **P0 / G2**.
- Điều kiện mở: T15 DONE, Q1 DONE.
- Trạng thái hiện tại: **DONE (ĐÃ NGHIỆM THU ĐẠT 100%)**.

## Hành vi cần đạt

State machine theo contracts §3 và execution-contract-v1.1 §5. Retry chỉ khi chắc chắn chưa submit; side-effect intent commit trước click. Mọi kết quả đến muộn được lưu an toàn, không làm mất asset hoặc tự gửi lại AI.

## File và module đã triển khai

- `src/TiToAI.Application/Jobs/JobRetryAndCancelPolicy.cs`: Chính sách tính toán backoff hữu hạn (2^attempts * 5s), phân loại lỗi được phép retry, bảo toàn attempts khi hết hạn ngạch, hủy trước submit (chuyển Canceled và xóa occupancy) vs sau submit (CancelRequested=1, giữ occupancy), thu hồi late result an toàn và chặn zombie worker bằng Fencing Token.
- `src/TiToAI.Application/Jobs/JobCrashRecoveryService.cs`: Quét phục hồi định kỳ và lúc khởi động (Startup & Periodic Sweep), phân định ranh giới side effect (chưa submit -> retry backoff / failed, đã submit -> AwaitingUser reconcile_required và bảo tồn occupancy), CAS guard trên SQLite IMMEDIATE transaction.
- `src/TiToAI.Application/Jobs/JobReconciliationService.cs`: Động cơ đối soát tác vụ (job.reconcile), ngăn chặn resend mù nếu thiếu ConfirmedNotReceived, xử lý mark_succeeded / mark_failed / cancel và giải phóng occupancy an toàn.
- `src/TiToAI.Browser/BrowserSessionCheckpointCoordinator.cs`: Điều phối session checkpoint, cách ly profile khi mất session (profile khác không bị ảnh hưởng), quiescing an toàn không ngắt ngang in-flight side effects, quy trình login thủ công kiểm tra identity và bắt buộc đóng browser về Hidden trước khi resume, bắt sự kiện đóng nút X giữ auth_closed không auto-retry.
- `tests/TiToAI.Desktop.SmokeChecks/JobRetryAndCancelSmokeTests.cs`: 7/7 test cases PASS.
- `tests/TiToAI.Desktop.SmokeChecks/JobCrashRecoverySmokeTests.cs`: 8/8 test cases PASS.
- `tests/TiToAI.Desktop.SmokeChecks/BrowserSessionCheckpointSmokeTests.cs`: 5/5 test cases PASS.
- `tests/TiToAI.Desktop.SmokeChecks/JobRecoverySmokeTests.cs`: 6/6 test cases PASS.

## Các bước triển khai

- [x] Cài transition compare-and-set cùng lease fencing, phân loại lỗi trước/sau side effect và budget MaxAttempts (`JobRetryAndCancelPolicy.cs`, `JobReservation.cs`).
- [x] Commit SideEffectSubmitted và thời điểm trước khi adapter click; lưu ResultReference sớm khi nhận được (`JobScheduler.ExecuteWorkerAsync`).
- [x] Lease hết khi chưa submit→Queued backoff hữu hạn hoặc Failed hết lượt; có thể đã submit→AwaitingUser/reconcile_required (`JobCrashRecoveryService.cs`).
- [x] Reconcile phân biệt kết quả thành công/đang xử lý/bị từ chối/chưa từng nhận/không xác định. Chỉ reset intent khi có bằng chứng chưa nhận (`ConfirmedNotReceived == true`); “không thấy kết quả” chưa đủ để resend (`JobReconciliationService.cs`).
- [x] Cancel trước submit kết thúc Canceled; sau submit đánh dấu CancelRequested, thu hồi kết quả rồi lưu Unlinked và Canceled (`JobRetryAndCancelPolicy.cs`).
- [x] Cancel AwaitingUser có side effect giữ evidence unresolved theo v1.1 §4–5; guard xóa vẫn chặn dù state Canceled cho đến đối soát. Late output lưu Unlinked, không đổi thành Succeeded.
- [x] Lỗi lưu file→AwaitingUser/STORAGE_ROOT_UNAVAILABLE, giữ result để thử finalize, không retry generation (`JobRetryAndCancelPolicy.EvaluateFailure`).
- [x] Login/drain đúng provider/profile theo T13a; về hidden và identity verified trước reconcile/resume (`BrowserSessionCheckpointCoordinator.cs`).
- [x] Giữ ExecutionBinding global account/profile sau reserve, retry không chọn account mới. Full pool Queued không tăng attempt; lease expired có side effect giữ slot Uncertain/quarantine, không nhả cho job khác đến khi đối soát an toàn.
- [x] Sau finalize đóng profile khi idle rồi release owner/slot và wake queue; crash ở giữa finalize/close/release không generate lại hoặc mở context thứ hai. Tab khác bận thì không close toàn context.

## Kiểm thử và nghiệm thu (2026-10-26)

- Solution Build: `dotnet build TiToAI.sln` -> 0 Warning(s), 0 Error(s).
- Tổng kiểm thử khói định lượng Task T16: **26/26 checks PASS 100%**.
- Báo cáo review độc lập của Subagent Reviewer: ĐẠT chuẩn nghiệm thu DoD, 0 finding High/Medium.
