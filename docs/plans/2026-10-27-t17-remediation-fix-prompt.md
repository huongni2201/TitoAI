# Prompt giao Antigravity — Remediation T17 (R1–R6)

Ngày: 2026-10-27 (UTC+07). Dùng tại phiên Antigravity, workspace `D:\workspace\TitoAI`. Đây là prompt để copy, không phải lệnh đã chạy.

Plan đi kèm: [2026-10-27-t17-remediation-fix-plan.md](2026-10-27-t17-remediation-fix-plan.md). Plan giữ thứ tự, ownership, acceptance và lệnh kiểm chứng. Nếu prompt và plan mâu thuẫn, hỏi coordinator trước khi làm.

Copy khối dưới vào phiên Antigravity:

```text
Bạn là Coordinator remediation T17 (Queue UI / Progress) tại D:\workspace\TitoAI.

Mục tiêu: đóng T17-R1 đến T17-R6 bằng source và regression thực tế, theo plan:
docs/plans/2026-10-27-t17-remediation-fix-plan.md

Đọc trước khi làm:
- AGENTS.md, docs/agents/STATUS.md, docs/agents/TASKS.md, docs/agents/ORCHESTRATION.md, docs/agents/ENGINEERING_RULES.md
- docs/reviews/2026-10-27-t17-handoff-recheck.md (findings gốc)
- docs/plans/implementation/T17-queue-progress-ui.md
- docs/decisions/execution-contract-v1.1.md §5 (contract lệnh job.*)
- Plan mục 1 (trạng thái thật) — báo cáo remediation cũ KHÔNG phải bằng chứng; đã có khẳng định sai (xem dưới).

Không tin báo cáo cũ:
- docs/reviews/t17-queue-ui/remediation/2026-10-27-t17-remediation-report.md và ...-review.md đóng 6/6 nhưng đã được đối chiếu và có lỗi:
  - R2 đi đường job.reconcile action "retry" cho Failed. Backend chỉ nhận reconcile với AwaitingUser hoặc Canceled/Failed có occupancy; Failed thường bị từ chối. Contract yêu cầu job.retry riêng; backend chưa có.
  - R6 dùng completedHistoryRef trong bộ nhớ phiên, mất sau reload. Backend chưa có job.list_history.
  - R1 thiếu jobRevision authoritative trong event; guard cục bộ không đủ.
- Hai file đó phải được đánh dấu SUPERSEDED, không xóa.

Tuân thủ:
- Xác minh model policy cho coordinator và mọi subagent theo ORCHESTRATION (Gemini 3.8 Flash High). Ghi bằng chứng runtime metadata. Nếu không xác minh được, dừng và báo.
- CodeGraph trước khi locate/read source (codegraph_explore). Context7 cho API thư viện.
- Giữ working tree. Không git reset, stash, rollback. Không kill app TiToAI.Desktop đang chạy.
- Không sửa docs/agents/STATUS.md và docs/agents/TASKS.md. Không nhận task mới. Không đóng G2.
- T18 chạy song song: không ghi file thuộc T18 (AiProviderSettingsPanel.tsx, aiSettingsApi.ts, aiSettingsPanel.test.ts, các file settings AI).
- Không tạo command giả, không nới safety backend (không cho resume trên Failed, không bỏ expectedRevision, không đoán profile).

Quy trình:

Bước 0 — Coordinator:
- Xác minh model policy. Ghi SHA-256 baseline các file trong phạm vi.
- Gửi proposal C1–C6 (plan mục 2) cho integrator: Job.Revision + jobRevision trong event; job.retry; job.list_history; expectedRevision cho resume/cancel/reconcile; xác nhận event progress; xác nhận navigate_settings_accounts.
- Chờ integrator chốt contract. Không viết consumer phụ thuộc trước khi chốt.

Bước 1 — Backend/contract (integrator, tuần tự):
- Thực hiện C1, C2, C3 (C4 nếu được duyệt).
- Thêm test backend trong tests/TiToAI.Desktop.SmokeChecks: retry hợp lệ; retry bị chặn khi sideEffectSubmitted=1; chặn khi Canceled; expectedRevision stale bị từ chối; list_history phân trang và không lẫn active.
- dotnet build TiToAI.sln -v:minimal -m:1 -nr:false phải 0 warning, 0 error.
- Nếu integrator không duyệt C2, UI phải disable retry Failed kèm lý do rõ. Không gọi reconcile giả.

Bước 2 — Frontend song song (sau khi contract đóng băng). Không hai worker cùng sửa một file:
- Worker A (useQueueState): src/TiToAI.Web/src/features/jobs/useQueueState.ts, jobApi.ts, jobTypes.ts. Merge event/snapshot theo jobRevision (R1); generation và scope cho refresh; hydrate job lạ qua getStatus, không gán taskType/sceneId/attempts giả (R5); tải history bằng list_history theo cursor (R6).
- Worker B (UI): src/TiToAI.Web/src/components/QueueDrawer.tsx, features/jobs/JobList.tsx, JobItemCard.tsx, JobDetailsDialog.tsx, JobReconcileDialog.tsx. CTA retry theo eligibility authoritative (R2); CTA đối soát cho Canceled/Failed unresolved (R3); login dùng profileId thật từ binding, không providerKey, không chuỗi rỗng, không tự chọn account đầu tiên; chưa bind thì điều hướng Settings → Tài khoản (R4); placeholder loading khi hydrating (R5); tab Hoàn tất lấy từ history (R6); keyboard, focus, Escape.
- Worker C (test): tạo features/jobs/queueProduction.test.ts (hoặc tên tương đương). Test phải mount QueueDrawer/JobList/JobItemCard/JobReconcileDialog production. Mock bridge phải khớp DTO backend thật. Test đỏ trước khi sửa. Giữ queueDrawer.test.ts; không dùng 9/9 hook cũ làm bằng chứng toàn drawer.
- Reviewer (sau tích hợp, chỉ đọc): kiểm lại từng finding. Không chấp nhận PASS của worker làm bằng chứng.

Shared files chỉ integrator/coordinator sửa, tuần tự:
- src/TiToAI.Web/src/contracts/bridge.ts
- src/TiToAI.Web/src/App.tsx (callback điều hướng Settings → Tài khoản)
- DTO, handlers, composition root, Program.cs, JobHandlers.cs

Acceptance tối thiểu (plan mục 4, 12 mục):
1. Succeeded rồi progress Running trễ: vẫn Succeeded.
2. Response list cũ về sau event mới: không lùi trạng thái.
3. Hai refresh đảo thứ tự; scope đổi; duplicate; gap: theo revision mới nhất; reload khi gap.
4. Event job ảnh/video mới: hydrate đúng taskType/scene; placeholder loading; route đúng scene; Script project-level không có scene.
5. Click Retry trên Failed sideEffect=0: gọi job.retry với expectedRevision; backend nhận.
6. Failed có sideEffect=1, unresolved, hoặc sideEffect undefined: không retry, không resend; undefined không là 0.
7. Cancel uncertain → reload → click đối soát trên UI production → resolved, nhả occupancy, asset Unlinked, không sinh lại AI.
8. Click Login khi providerKey khác profileId: payload đúng profileId thật.
9. Job chưa bind tài khoản: điều hướng Settings → Tài khoản; không chọn account đầu tiên.
10. Complete → reload: tab Hoàn tất có job (từ list_history); active không chứa terminal resolved.
11. Double-click, conflict, lỗi request: không mất form; không cập nhật giả; nút disable khi xử lý.
12. Keyboard, focus, Escape, nested dialog, hai theme: không regression.

Lệnh kiểm chứng (một owner build tại một thời điểm, không kill app):
- pnpm --dir src/TiToAI.Web build
- node src/TiToAI.Web/src/features/jobs/queueDrawer.test.ts
- node src/TiToAI.Web/src/features/jobs/queueProduction.test.ts (worker C tạo)
- node src/TiToAI.Web/src/features/jobs/manualHandoffPanel.test.ts
- node src/TiToAI.Web/src/features/settings/settingsPage.test.ts
- node src/TiToAI.Web/src/features/prompts/promptComponents.test.ts
- node src/TiToAI.Web/src/appNavigation.test.ts
- dotnet build TiToAI.sln -v:minimal -m:1 -nr:false
- dotnet tests/TiToAI.Desktop.SmokeChecks/bin/Debug/net10.0-windows/TiToAI.Desktop.SmokeChecks.dll t16
- dotnet tests/TiToAI.Desktop.SmokeChecks/bin/Debug/net10.0-windows/TiToAI.Desktop.SmokeChecks.dll handoff
- dotnet tests/TiToAI.Desktop.SmokeChecks/bin/Debug/net10.0-windows/TiToAI.Desktop.SmokeChecks.dll remediation
- Suite backend mới cho C1–C3 (integrator đăng ký trong Program.cs).

Báo cáo: viết lại báo cáo mới bằng subagent (không dùng báo cáo cũ):
- docs/reviews/t17-queue-ui/remediation/2026-10-27-t17-remediation-report-v2.md
  - Closure matrix T17-R1 đến R6: PASS / FAIL / NOT_RUN từng dòng.
  - Root cause, file và SHA-256 sau sửa.
  - Thay đổi contract đã được integrator chấp thuận (C1–C6).
  - Lệnh đầy đủ, exit code, log thô.
  - Findings còn mở.
- Đánh dấu SUPERSEDED ở đầu hai file cũ.
- Reviewer độc lập viết docs/reviews/t17-queue-ui/remediation/2026-10-27-t17-remediation-review-v2.md.

NOT_RUN, ghi đúng phạm vi và chuyển T30:
- Runtime WebView2 trên desktop thật, DPI 100/125/150/200%.
- Đăng nhập tài khoản 3P thật (A13).
- Theme live switch và focus trong WPF thật.

Dừng và hỏi coordinator khi:
- Cần đổi contract ngoài C1–C6.
- Cần nới safety backend.
- Cần file thuộc T18 hoặc shared file mà worker không được giao.
- Model policy không xác minh được.

Không đề nghị T17 DONE nếu chưa có reviewer độc lập đồng ý và đủ acceptance 1–12.
```
