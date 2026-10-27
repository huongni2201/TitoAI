# Plan sửa T17-R1 đến T17-R6 — Queue UI / Progress

Ngày: 2026-10-27 (UTC+07). Căn cứ: [review handoff T17](../reviews/2026-10-27-t17-handoff-recheck.md), [plan T17](implementation/T17-queue-progress-ui.md), [contract v1.1 §5](../decisions/execution-contract-v1.1.md). Prompt giao việc cho Antigravity: [2026-10-27-t17-remediation-fix-prompt.md](2026-10-27-t17-remediation-fix-prompt.md).

Plan này không đóng T17. Chỉ đóng khi reviewer độc lập kiểm lại và chấp nhận từng finding.

## 1. Trạng thái thật trước khi sửa

Báo cáo remediation cũ ([report](../reviews/t17-queue-ui/remediation/2026-10-27-t17-remediation-report.md)) và review đi kèm ([review](../reviews/t17-queue-ui/remediation/2026-10-27-t17-remediation-review.md)) đóng 6/6 finding. Khi đối chiếu source, phát hiện các điểm sau:

| Finding | Khẳng định cũ | Đối chiếu source | Kết luận |
|---|---|---|---|
| R1 | Guard terminal + generation cho refresh | Event `job.state_changed` không có `jobRevision`; client chỉ có timestamp và guard cục bộ. Contract §5 yêu cầu `jobRevision` để sắp thứ tự | **Chưa đóng**. Guard cục bộ không đủ khi thiếu revision authoritative |
| R2 | Retry Failed qua `job.reconcile` với `action: "retry"` | Backend `ReconcileAsync` chỉ nhận `AwaitingUser`, hoặc Canceled/Failed **khi còn occupancy** (`JobReconciliationService.cs:72-86`). Failed thường không có occupancy nên bị từ chối. Contract §5 quy định `job.retry` riêng, backend **không có** (grep rỗng) | **Chưa đóng**. Đường retry trong báo cáo sẽ bị backend từ chối; test chỉ kiểm tra từ chối và mock bridge |
| R3 | CTA đối soát cho Canceled/Failed unresolved | Backend đã hỗ trợ reconcile cho terminal unresolved có occupancy (`JobReconciliationService.cs:81`). UI CTA có trong `jobTypes.ts`/`JobItemCard.tsx`. Cần chạy lại qua component production | **Có khả năng đúng**, cần chứng minh bằng test production |
| R4 | Login dùng `job.profileId`, điều hướng Settings nếu chưa bind | `JobSummaryDto.ProfileId` có; action `navigate_settings_accounts` có trong UI. Cần xác minh `profileId` null/chưa reserve và payload gửi đi | **Có khả năng đúng**, cần test production |
| R5 | Hydrate bằng `getStatus`, cờ `isHydrating` | Có cờ `isHydrating` trong `jobTypes.ts`; `getStatus` có. Cần xác minh hydrate thật trước khi route và khóa thao tác | **Có khả năng đúng**, cần test production |
| R6 | Lịch sử bằng `completedHistoryRef` | `completedHistoryRef` là bộ nhớ trong phiên, **mất khi reload**. Backend **không có** `job.list_history` (contract §5 yêu cầu). Acceptance 7 yêu cầu lịch sử **sau reload** | **Chưa đóng** |

Ngoài ra:
- `JobSummaryDto` và `JobDetailDto` chưa có `revision`. `Job` entity không có cột revision. Các lệnh `resume`, `retry`, `cancel`, `reconcile` theo contract §5 đều có `expectedRevision`; backend hiện chưa nhận.
- `queueDrawer.test.ts` có mount `QueueDrawer` và `JobItemCard` production (dòng 257–258, 575, 659, 755, 935). Đây là điểm tốt, nhưng response mock cho `retry`/`resume` phải được đối chiếu với backend thật.
- Review cũ ghi "VERIFIED & CLOSED" và không phát hiện các điểm trên. Review này được coi là **superseded** sau khi có review mới. Không xóa.

## 2. Quyết định cần integrator chốt trước khi sửa consumer

Theo prompt, không tự tạo command giả và không nới safety backend. Contract §5 đã có các command dưới đây, nên triển khai theo contract không phải tạo mới. Vẫn cần integrator duyệt vì chạm DTO, handler, bridge và migration.

| Mã | Thay đổi | Lý do | Ghi chú triển khai |
|---|---|---|---|
| C1 | Thêm `Job.Revision` (migration), tăng mỗi lần đổi state/field; trả `revision` trong `JobSummaryDto`/`JobDetailDto`; thêm `jobRevision` vào `job.state_changed` và `job.progress` | R1, và điều kiện CAS cho C2–C4 | Mọi nơi đổi state (`JobService`, `JobReservation`, `JobScheduler`, `JobReconciliationService`, `ProjectWorkflowService`) phải tăng revision. Migration mới |
| C2 | `job.retry {workspaceId, jobId, expectedRevision}` theo §5: chỉ `Failed`, `sideEffectSubmitted=0`, `attempts < maxAttempts`; không reset attempts; chặn job đã Canceled; giữ snapshot và binding | R2 | Thêm `JobService.RetryAsync`, handler, allow-list, `bridge.ts`. Nếu integrator không duyệt, UI phải disable retry Failed với lý do rõ, **không** gọi reconcile giả |
| C3 | `job.list_history {workspaceId?, projectId?, cursor?, pageSize}` theo §5: terminal đã resolved, sort `updatedAt/jobId`, 1–100; `list_active` giữ nguyên | R6 | Thêm DTO trang, handler, allow-list, `bridge.ts`. Không đổi nghĩa `list_active` |
| C4 | `job.resume`, `job.cancel`, `job.reconcile` nhận `expectedRevision`; conflict trả lỗi có revision mới | Acceptance 8 (conflict không mất form, không cập nhật giả) | Có thể tách sang vòng sau nếu integrator chấp nhận UI chỉ hiển thị conflict và reload |
| C5 | Xác nhận event progress thực sự được emit từ backend (`job.progress` / `job.progress_updated`); UI đang lắng nghe `job.progress_updated` | R1 | Chưa thấy emit trong `Application/Jobs` và `JobHandlers.cs`; cần kiểm tra `MainWindow.xaml.cs` và `JobScheduler` |
| C6 | Xác nhận điều hướng `navigate_settings_accounts` là contract navigation đã chốt | R4 | Nếu chưa có trong contract, integrator chốt tên và cách gọi |

Quyết định cần người dùng xác nhận: có duyệt C1–C3 ngay trong đợt này không. Khuyến nghị: **duyệt**. Các command này đã nằm trong contract v1.1, nên đây là đi đúng contract. Nếu không duyệt, R2 và R6 chỉ đóng được ở mức "UI an toàn, chưa có chức năng", và phải ghi rõ là chưa đạt.

## 3. Thứ tự thực hiện

**Bước 0 — Chuẩn bị (coordinator).**
- Xác minh model policy theo [ORCHESTRATION](../agents/ORCHESTRATION.md) cho coordinator và mọi subagent.
- Ghi hash SHA-256 của các file thuộc phạm vi (baseline).
- Xác nhận ownership: không ghi file thuộc T18 (`AiProviderSettingsPanel.tsx`, `aiSettingsApi.ts`, `aiSettingsPanel.test.ts`, các file settings AI).
- Gửi proposal C1–C6 cho integrator. Chờ chốt trước khi viết consumer.

**Bước 1 — Contract và backend (integrator, tuần tự).**
- Thực hiện C1, C2, C3 (và C4 nếu được duyệt) trong `JobService`, `JobReconciliationService`, `JobHandlers`, `JobDtos`, `bridge.ts`, migration.
- Thêm test backend trong `tests/TiToAI.Desktop.SmokeChecks` cho: retry Failed hợp lệ, retry bị từ chối khi `sideEffectSubmitted=1`, retry bị từ chối khi Canceled, `expectedRevision` stale bị từ chối, `list_history` phân trang và không lẫn active, migration thêm cột revision.
- Chạy `dotnet build TiToAI.sln` (0 warning, 0 error) và các suite liên quan.

**Bước 2 — Frontend song song (sau khi contract đã đóng băng).**

| Worker | Phạm vi ghi | Việc |
|---|---|---|
| A — useQueueState | `features/jobs/useQueueState.ts`, `features/jobs/jobApi.ts`, `features/jobs/jobTypes.ts` | Merge event/snapshot theo `jobRevision` (R1); refresh có generation và scope; hydrate job lạ qua `getStatus` (R5); tải history bằng `list_history` theo cursor (R6); gap/reconnect reload |
| B — UI | `components/QueueDrawer.tsx`, `features/jobs/JobList.tsx`, `JobItemCard.tsx`, `JobDetailsDialog.tsx`, `JobReconcileDialog.tsx` | CTA retry theo eligibility authoritative (R2); CTA đối soát terminal unresolved (R3); login đúng `profileId` hoặc điều hướng Settings (R4); loading/unknown khi hydrating (R5); tab Hoàn tất lấy từ history (R6); keyboard/focus/Escape |
| C — Test | `features/jobs/*.test.ts` mới (ví dụ `queueProduction.test.ts`), giữ `queueDrawer.test.ts` | Viết test mount component production; mock bridge phải khớp schema backend thật; test đỏ trước khi sửa |

Quy tắc ghi: không hai worker cùng một file. `App.tsx`, `bridge.ts`, DTO, handler, composition root và `Program.cs` chỉ integrator sửa.

**Bước 3 — Tích hợp (coordinator + integrator).**
- Gộp các thay đổi chung theo thứ tự: `bridge.ts` → `App.tsx` (callback điều hướng Settings → Tài khoản) → chạy build.
- Không sửa `STATUS.md` và `TASKS.md`.

**Bước 4 — Review độc lập (reviewer, chỉ đọc).**
- Kiểm lại từng finding trên checkpoint đã ổn định, đọc source và chạy test. Không chấp nhận báo cáo PASS của worker làm bằng chứng.
- Viết lại báo cáo remediation (xem mục 5).

## 4. Acceptance

Mỗi acceptance phải có test **mount component production** (QueueDrawer / JobList / JobItemCard / JobReconcileDialog), không chỉ gọi hook. Mock bridge cho phép, nhưng response phải khớp DTO backend thật.

| # | Kịch bản | Finding | Yêu cầu |
|---|---|---|---|
| 1 | Succeeded → progress Running trễ | R1 | Vẫn Succeeded |
| 2 | Response list cũ về sau event mới | R1 | Không lùi trạng thái |
| 3 | Hai refresh đảo thứ tự; scope đổi; duplicate; gap | R1 | Kết quả theo `revision` mới nhất; reload khi gap |
| 4 | Job mới qua event (Image/VideoRender) | R5 | Hydrate đúng `taskType`/`sceneId`; placeholder loading khi chưa hydrate; khóa thao tác cần metadata; route đúng scene; Script project-level không có scene |
| 5 | Click Retry trên Failed sideEffect=0 | R2 | Gọi `job.retry` với `expectedRevision`; backend nhận; không tạo generation khi attempts hết |
| 6 | Failed có `sideEffectSubmitted=1` hoặc unresolved, hoặc `undefined` | R2 | Không có nút retry; không resend; `undefined` không coi là 0 |
| 7 | Cancel uncertain → reload → đối soát | R3 | CTA hiện; dialog mở; resolved; occupancy được nhả; asset Unlinked; không sinh lại AI |
| 8 | Click Login khi `providerKey` khác `profileId` | R4 | Payload `profileId` đúng là profile thật từ binding |
| 9 | Job chưa bind tài khoản | R4 | Điều hướng Settings → Tài khoản; không chọn account đầu tiên |
| 10 | Complete → reload | R6 | Tab Hoàn tất lấy từ `list_history`; active không chứa terminal resolved |
| 11 | Double-click, conflict, lỗi request | Acc. 8 | Không mất form; không cập nhật giả; nút disable khi đang xử lý |
| 12 | Keyboard, focus, Escape, nested dialog, hai theme | Acc. 9 | Không regression |

Bắt buộc: viết test đỏ trước (mỗi acceptance phải fail trên code hiện tại), rồi mới sửa.

## 5. Evidence và báo cáo

Thư mục: `docs/reviews/t17-queue-ui/remediation/`.
- Đánh dấu `SUPERSEDED` ở đầu hai file cũ (`2026-10-27-t17-remediation-report.md`, `2026-10-27-t17-remediation-review.md`). Không xóa.
- Subagent viết lại báo cáo mới: `2026-10-27-t17-remediation-report-v2.md`, gồm:
  - Closure matrix T17-R1 đến R6, PASS/FAIL/NOT_RUN từng dòng.
  - Root cause, file và SHA-256 sau sửa.
  - Thay đổi contract đã được integrator chấp thuận (C1–C6).
  - Lệnh chạy đầy đủ, exit code, log thô.
  - Findings còn mở.
- Reviewer viết báo cáo riêng: `2026-10-27-t17-remediation-review-v2.md`.

## 6. Lệnh kiểm chứng

Chạy tuần tự, chỉ một owner build output tại một thời điểm. Không kill app đang chạy.

```bash
pnpm --dir src/TiToAI.Web build
node src/TiToAI.Web/src/features/jobs/queueDrawer.test.ts
node src/TiToAI.Web/src/features/jobs/queueProduction.test.ts   # worker C tạo
node src/TiToAI.Web/src/features/jobs/manualHandoffPanel.test.ts
node src/TiToAI.Web/src/features/settings/settingsPage.test.ts
node src/TiToAI.Web/src/features/prompts/promptComponents.test.ts
node src/TiToAI.Web/src/appNavigation.test.ts
dotnet build TiToAI.sln -v:minimal -m:1 -nr:false
dotnet build tests/TiToAI.Desktop.SmokeChecks -v:minimal -m:1 -nr:false
dotnet tests/TiToAI.Desktop.SmokeChecks/bin/Debug/net10.0-windows/TiToAI.Desktop.SmokeChecks.dll t16
dotnet tests/TiToAI.Desktop.SmokeChecks/bin/Debug/net10.0-windows/TiToAI.Desktop.SmokeChecks.dll handoff
dotnet tests/TiToAI.Desktop.SmokeChecks/bin/Debug/net10.0-windows/TiToAI.Desktop.SmokeChecks.dll remediation
```

Suite backend mới cho C1–C3 do integrator đăng ký trong `Program.cs`.

Kết quả lần kiểm trước (baseline, trước remediation T17): `queueDrawer.test.ts` 9/9, `videoProductionTab.test.ts` 4/4, `t16` (dòng cuối 6/6 Browser Checkpoint). Các số này chỉ là baseline, không phải bằng chứng đóng T17.

## 7. NOT_RUN

Các mục sau không thuộc phạm vi đợt này và phải ghi NOT_RUN, chuyển T30:
- Runtime WebView2 trên desktop thật, DPI 100/125/150/200%.
- Đăng nhập tài khoản 3P thật (A13).
- Theme live switch và focus trong cửa sổ WPF thật.

## 8. Đóng T17

T17 chỉ được đề nghị DONE khi:
1. Đủ 12 acceptance mục 4 PASS bằng test production.
2. Backend C1–C3 có test PASS.
3. Reviewer độc lập đồng ý với từng finding R1–R6.
4. Các mục NOT_RUN được ghi đúng phạm vi.

Việc cập nhật `TASKS.md`, `STATUS.md` và đánh giá G2 do coordinator chung thực hiện, không do đợt này.
