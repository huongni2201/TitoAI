# Nghiệm thu phát hành: dữ liệu, tải, browser, UI và máy sạch

> **Delta hiện hành:** [plan queue/Settings 1.2](../QUEUE_EXECUTION_SETTINGS_PLAN.md) thay popup Settings, nhóm account shared/private, connect tại Workspace và round-robin Workspace. Dùng [TASKS](../../agents/TASKS.md) cho trạng thái/gate; Baseline/checkbox dưới không là evidence đã chạy.

- Task gốc: **T30** · Ưu tiên: **P0 / G4**.
- Điều kiện mở: T24,T29 DONE; bằng chứng T26–T28 tích hợp.
- Baseline 2026-10-26: Chưa có release qualification; build/smoke chưa thay bộ nghiệm thu.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Kết quả cần bàn giao

`docs/reviews/T30-release-qualification.md` và evidence cho chín nhóm SOURCE §11, Theme §5, UI_BROWSER §8, SETTINGS §6. Mỗi scenario có revision/build/Windows/hardware/DPI/dataset/provider mode/result/log path và hạn chế.

## File và chuẩn bị

- Tạo đề xuất `tests/Scenarios/ReleaseQualification.md`, `tests/TiToAI.LoadChecks/` và fixture 100/1.000 job metadata.
- Load test dùng fake clock/provider/output có kiểm soát, không gửi 1.000 job tính phí tới website thật.
- Dataset pool: 10 Workspace/TikTok dùng chung 1 ChatGPT + 1 Gemini + 4 MuseAI account profiles; TikTok profiles riêng kênh. Fake kiểm đầy đủ capacity; account thật theo quyền được cấp, thiếu ghi NOT_RUN từng scenario, không dùng fake thay evidence session/hidden.

## Các bước nghiệm thu

- [ ] Tạo 3–10 Workspace; tấn công scope bằng IDs hợp lệ của kênh khác cho read/write/export/preview/event/account.
- [ ] Chạy pipeline/manual/automation đã công bố, download lớn đồng thời metadata; kiểm hash/đúng scene/root/audio gốc.
- [ ] Đo queue 100 và 1.000 jobs với nhiều worker: reserve race, pause/resume, fairness/quota, heartbeat, SQLITE_BUSY/retry.
- [ ] Kill/crash tại submit windows và handoff/login, restart, late cancel result; kiểm số lần submit và asset không trùng/mất. Với bridge, gửi request/cancel qua WebView2 thật và xác nhận request đang chạy bị hủy đúng ID.
- [ ] Login đúng/sai/đóng/MFA/nhiều profile và shared AI đa tab; hidden không ló/focus quan sát trên màn hình thật.
- [ ] Ba roots độc lập/chung/cũ, đổi provider/task và restart, mất ổ/quyền/disk full; snapshot cũ không đổi, storage failure không resend.
- [ ] Backup WAL nhiều roots→restore roots khác; upgrade fail/recovery; install/upgrade/uninstall trên Windows sạch.
- [ ] Kiểm toàn UI P0 Light/Dark/System, startup flash, keyboard/focus, 1280×800/1440×900 ở DPI 100/125/150; lưu 12 ảnh C1–C6 và ảnh bounds C6 theo [runbook T06](../../agents/runs/2026-10-26-cli/T06-viewport-runbook.md). Kiểm DOM `inert`/`aria-hidden` khi modal mở và đã đóng.
- [ ] Đổi Windows theme khi preference là System; xác nhận cập nhật tức thì. Chọn Light/Dark rồi đổi Windows theme và xác nhận preference thủ công không đổi. Fault-inject lỗi đọc/lưu `ui.theme`; xác nhận fallback, không ghi đè DB khi đọc lỗi và cảnh báo “chỉ trong phiên” khi lưu lỗi.
- [ ] Tổng hợp findings P0/P1 và capability matrix release; NOT_RUN còn thuộc acceptance bắt buộc thì gate chưa PASS.
- [ ] 10 kênh/1 ChatGPT: cap task=1 rồi 3 verified fixture, giới hạn đồng thời và FIFO/fairness đúng; đủ 8 tổ hợp provider, không cần 10 account AI.
- [ ] 10 video/4 MuseAI: mặc định 4 chạy/6 chờ khi budget đủ; completion out-of-order vẫn đúng 10 scope; close/reopen đúng profile trước job kế, account login/quota không làm pool còn lại dừng.
- [ ] Tách enqueue input snapshot và reserve ExecutionBinding; full pool không tăng attempts; uncertain không tự nhả slot/resend. Giảm cap/budget không kill browser/tab bận; crash sau finalize trước close không chạy lại AI.
- [ ] Migration global pool/legacy giữ history và session đúng quy tắc; Script không scene, text boundary, duplicate apply/import, stale lease token/revision theo [v1.1 §1–7](../../decisions/execution-contract-v1.1.md).

## Ngưỡng tải đề xuất phải chốt trước chạy

Không duplicate reservation/submit/output, không leak scope; không có job thất bại do SQLITE_BUSY sau retry budget; không job đủ điều kiện bị bỏ đói quá hai vòng fairness khi có slot. Đo p50/p95 reserve/DB commit/wakeup→dispatch, throughput, peak RAM và lock retries.

Mục tiêu ban đầu trên máy test được ghi cấu hình: p95 wakeup→dispatch ≤2 giây khi có slot và p95 metadata commit ≤500ms; coordinator chốt điều chỉnh trước chạy, không sửa ngưỡng sau để biến FAIL thành PASS. Queue wait do provider bận không tính vào latency này.

Chỉ phát hành khi toàn P0 đạt và giới hạn manual/automation được công khai chính xác; T25 P1 không bắt buộc G4, T31 P2 chưa mở.

