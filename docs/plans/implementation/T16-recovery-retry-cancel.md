# Phục hồi crash, retry, cancel và đối soát không gửi trùng

- Task gốc: **T16** · Ưu tiên: **P0 / G2**.
- Điều kiện mở: T15 DONE.
- Baseline 2026-10-26: Job có các cờ phục hồi; chưa có transition/reconcile engine.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Hành vi cần đạt

State machine theo contracts §3. Retry chỉ khi chắc chắn chưa submit; side-effect intent commit trước click. Mọi kết quả đến muộn được lưu an toàn, không làm mất asset hoặc tự gửi lại AI.

## File và contract

- Tạo đề xuất: `src/TiToAI.Application/Jobs/{JobRecoveryService.cs,JobTransitionService.cs,JobCancellationService.cs,JobReconciler.cs}`.
- JobScheduler/JobService T15 gọi transition chung; adapter nhận submit-intent boundary, không tự ghi state tùy ý.
- job.cancel/reconcile/retry/resume theo [v1.1 §5](../../decisions/execution-contract-v1.1.md), đều kiểm expectedRevision. Retry giữ job/snapshot/attempt budget; resume sau side effect chỉ reconcile/finalize, không dùng enqueue tạo bản sao.

## Các bước triển khai

- [ ] Cài transition compare-and-set cùng lease fencing, phân loại lỗi trước/sau side effect và budget MaxAttempts.
- [ ] Commit SideEffectSubmitted và thời điểm trước khi adapter click; lưu ResultReference sớm khi nhận được.
- [ ] Lease hết khi chưa submit→Queued backoff hữu hạn hoặc Failed hết lượt; có thể đã submit→AwaitingUser/reconcile_required.
- [ ] Reconcile phân biệt kết quả thành công/đang xử lý/bị từ chối/chưa từng nhận/không xác định. Chỉ reset intent khi có bằng chứng chưa nhận; “không thấy kết quả” chưa đủ để resend.
- [ ] Cancel trước submit kết thúc Canceled; sau submit đánh dấu CancelRequested, thu hồi kết quả rồi lưu Unlinked và Canceled.
- [ ] Cancel AwaitingUser có side effect giữ evidence unresolved theo v1.1 §4–5; guard xóa vẫn chặn dù state Canceled cho đến đối soát. Late output lưu Unlinked, không đổi thành Succeeded.
- [ ] Lỗi lưu file→AwaitingUser/STORAGE_ROOT_UNAVAILABLE, giữ result để thử finalize, không retry generation.
- [ ] Login/drain đúng provider/profile theo T13a; về hidden và identity verified trước reconcile/resume. Legacy migration_required không reroute; chuyển manual/reconcile theo v1.1 §2.
- [ ] Giữ ExecutionBinding global account/profile sau reserve, retry không chọn account mới. Full pool Queued không tăng attempt; lease expired có side effect giữ slot Uncertain/quarantine, không nhả cho job khác đến khi đối soát an toàn.
- [ ] Sau finalize đóng profile khi idle rồi release owner/slot và wake queue; crash ở giữa finalize/close/release không generate lại hoặc mở context thứ hai. Tab khác bận thì không close toàn context.

## Kiểm thử và nghiệm thu

Fault injection: trước commit intent, sau commit trước click, sau click trước result, sau download trước DB commit, lease hết khi worker cũ còn sống. Kiểm fake số lần submit bằng 1 cho mọi nhánh chưa chắc chắn.

Test late result sau cancel, storage offline/reconnect, max attempts, user đóng login, shared AI còn tab đã submit. Restart phải bảo toàn checkpoint và không tự resend. Ghi rõ giới hạn exactly-once khi provider không có idempotency.

