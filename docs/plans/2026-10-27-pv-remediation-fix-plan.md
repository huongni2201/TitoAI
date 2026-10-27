# Kế hoạch sửa pipeline PV sau review độc lập

Ngày: 2026-10-27 (UTC+07). Trạng thái: **PLAN — chưa triển khai, chưa nghiệm thu**.

Nguồn findings: [review độc lập PV-R1–PV-R11](../reviews/2026-10-27-audit-pv-independent-recheck.md). Nguồn hành vi: [workflow sản phẩm §3–8](PRODUCT_IMAGE_VIDEO_WORKFLOW_PLAN.md), [queue/Settings 1.2](QUEUE_EXECUTION_SETTINGS_PLAN.md), [Settings](SETTINGS_PLAN.md), [engineering rules](../agents/ENGINEERING_RULES.md). [Prompt giao triển khai](2026-10-27-pv-remediation-fix-prompt.md) chỉ cách dùng plan này.

## 1. Mục tiêu và ranh giới

Sửa tính đúng đắn của run/job/output trước; hoàn thiện luồng production có manual handoff khi capability chưa xác minh; sau đó mới nghiệm thu automation website thật. Một lần Start phải đi tới clip theo từng cảnh hoặc dừng với lý do và cách tiếp tục rõ ràng.

Giữ C#/.NET/WPF/WebView2/React/SQLite và một queue chung. Tận dụng module/job recovery hiện có. Không thêm queue theo project, broker, dịch vụ cache riêng, TTS, ghép clip hoặc đăng TikTok. Các bảng/DTO mới chỉ được tạo sau khi xác nhận không có cơ chế tương đương.

Chat Codex hiện tại chỉ soạn docs. Plan không thay quyền code, owner hoặc trạng thái trong [STATUS](../agents/STATUS.md)/[TASKS](../agents/TASKS.md). AI triển khai tiếp nhận theo [ORCHESTRATION](../agents/ORCHESTRATION.md), xác nhận task remediation/dependency/owner trước khi ghi. Thực hiện tuần tự vì nhiều mốc cùng sửa ProjectWorkflowService; không ghi chồng file của phiên khác.

Trong Antigravity, xác minh Gemini 3.8 Flash / High bằng UI/runtime metadata theo policy từng phiên. Không suy từ lời tự nhận hoặc chữ inherit. MODEL_UNVERIFIED/MODEL_BLOCKED xử lý đúng ORCHESTRATION; không tự đổi model để tiếp tục.

## 2. Quyết định phải chốt ở đầu đợt

Coordinator ghi contract ngắn trong decision/contract hiện hành, không tạo thêm một spec song song:

| Nội dung | Yêu cầu tối thiểu |
|---|---|
| Run | Scope, tập scene được chọn, brief/input revision, workflow version, giới hạn cảnh/thời lượng/retry và trạng thái bền vững |
| Start | Client request identity/idempotency và guard active run atomic; lặp request trả cùng run, không sinh job trùng |
| Step | Mapping run + project/scene + loại bước + generation + input revision/fingerprint tới job/output; DB unique constraint thích hợp |
| Snapshot | Schema version; media roles/asset IDs/hashes; prompt/template provenance; duration/audio mode; provider/config/root theo lúc enqueue |
| Apply | Output validation + expected revision + apply identity; stale/canceled/foreign-run giữ history/candidate hoặc Unlinked theo contract |
| Continuation | Intent bước sau hoặc job mapping commit atomic với apply; startup xử lý lại intent chưa hoàn tất, enqueue idempotent |
| Pause | Chặn reservation mới và continuation của run; job đã submit tiếp tục checkpoint/reconcile an toàn |
| Resume | Tiếp bước thiếu trong phạm vi đã cho phép; không tạo run mới, mở rộng scene hoặc resend uncertain |
| Cancel | Áp cho đúng run và mọi job active, gồm Running; không nhả occupancy uncertain trước đối soát |
| Legacy | Thiếu snapshot/run metadata phải báo/review theo schema cũ; không tự gán job cũ vào run mới hoặc gửi AI khi migrate |

Tên bảng/field cụ thể do coordinator chốt sau CodeGraph. Dùng concurrency guard trong SQL/transaction, không chỉ check rồi ghi trong C#. Nếu cần sửa DbContext boundary của JobService để cùng transaction, integrator sở hữu thay đổi đó.

Provider/root/config chốt ở thời điểm enqueue **từng job** theo spec; run lưu input/giới hạn/workflow đã cho phép. Không tự đổi quy tắc này thành “đọc Settings hiện tại lúc execute”, cũng không tự đóng băng toàn bộ Settings của mọi job tương lai nếu contract chưa quy định.

## 3. Thứ tự thực hiện và ownership

Các owner dưới đây là vai trò, không xác nhận có worker đang giữ file. Coordinator điền tên phiên và exact file list trước khi mở mốc. File dùng chung Contracts, persistence, MainWindow và Program.cs của smoke runner chỉ có một writer.

| Mốc | Dependency trong đợt sửa | Finding | Owner | Điều kiện chuyển tiếp |
|---|---|---|---|---|
| FIX-0 Baseline | Coordinator xác nhận ownership | R10, toàn bộ | Coordinator | Baseline và contract đã ghi; ca regression đỏ đã xác định |
| FIX-1 Run/step/snapshot | FIX-0 | R3, R6, R7 | Integrator + persistence owner tuần tự | Constraint/migration/snapshot tests PASS |
| FIX-2 Apply và recovery | FIX-1 | R3, R7 | Workflow owner | Apply lặp/concurrent/stale/crash tests PASS |
| FIX-3 Pause/Resume/Cancel | FIX-2 | R4 | Workflow + scheduler owner tuần tự | Dispatch thật tuân thủ run state; T16 regression PASS |
| FIX-4 Planner/prompt/stale | FIX-2, FIX-3 | R5, R8, R11 | Workflow/prompt owner | Đủ các nhánh scene, brief, reuse và §3.4 tests PASS |
| FIX-5 Production/manual/Queue | FIX-1–FIX-4 | R1, R2, R9 | Integrator/backend/UI tuần tự | Start → manual → clip chạy xuyên suốt, DTO backend thật đáp ứng UI |
| FIX-6 Review và evidence | FIX-5 | R10, toàn bộ | Reviewer rồi Coordinator | Mỗi finding có bằng chứng đóng; 20 AC gốc có kết quả trung thực |

T19/T20/T21 live qualification theo mục 6 là mốc tiếp theo riêng, không được suy đã đạt từ FIX-6.

### FIX-0 — Chốt baseline và ca lỗi

File đọc: STATUS/TASKS/ORCHESTRATION, review độc lập, workflow plan; source liên quan qua CodeGraph. File ghi: báo cáo/checkpoint của đợt sửa, contract hiện hành khi cần; STATUS/TASKS chỉ coordinator.

- [ ] Ghi git revision, dirty file list và hash các file trọng yếu; xác nhận các phiên đang ghi prompt/migration/workflow. Bảo toàn working tree, không reset/stash/rollback hoặc kill app.
- [ ] Build từ source và chạy lại phạm vi liên quan. Kết quả cuối review trước là workflow 13 PASS/1 FAIL, không mặc định còn đúng ở baseline mới.
- [ ] Đưa năm ca tái hiện trong review vào suite của repo: Script apply lặp, output sau sửa tay, cancel Running, pause vẫn enqueue, Image có draft bị đứng. Dùng SQLite thật và file tạm hợp lệ khi test media.
- [ ] Giữ test mất USER_BRIEF là ca đỏ cho R11. Fake chỉ ở test; test chỉ đổi State=Succeeded trong DB được ghi là test apply, không gọi E2E scheduler.
- [ ] Ghi các lỗi baseline ngoài phạm vi cùng owner; hoàn thành công việc độc lập còn làm được, không sửa chồng để làm build xanh.

### FIX-1 — Dữ liệu run/step và input snapshot

Phạm vi dự kiến: `src/TiToAI.Contracts/{Content,Jobs,Providers}/`, `src/TiToAI.Application/Jobs/JobSnapshotFactory.cs`, `JobService.cs`, workflow run persistence hiện có nếu có; Entities/Configurations/DbContext/migration qua persistence owner. Coordinator chốt file mới sau khảo sát.

- [ ] Persist run/step/job/output mapping theo contract mục 2, tái sử dụng cơ chế idempotency/provenance hiện có.
- [ ] Start và generation creation atomic; giới hạn một active run theo phạm vi đã chốt; Start lặp/concurrent không tạo nhiều bộ job.
- [ ] Đóng băng hai ảnh có roles, ảnh được chọn, audio option/asset/hash, duration, brief/input revision và template provenance; backend validate ownership/type/file/capability.
- [ ] Snapshot cho job cũ có schema/version policy rõ. Retry/resume giữ input và ExecutionBinding; metadata thư viện/Settings sửa sau đó không đổi job cũ.
- [ ] Test SQLite constraint, migration từ fixture cũ, cross-workspace, sai media/hash/file thiếu; đổi input/Settings sau enqueue; legacy không tự dispatch. Không sửa migration lịch sử đã áp vào DB người dùng để thêm schema mới.

### FIX-2 — Apply idempotent và tiếp bước sau crash

Phạm vi dự kiến: `Content/ProjectWorkflowService.cs`, workflow persistence đã chốt FIX-1, `Jobs/JobService.cs` khi cần transaction boundary; startup wiring ở `Desktop/MainWindow.xaml.cs` do integrator; smoke tests workflow/recovery.

- [ ] Chỉ apply output hợp lệ đúng run/step/generation/revision và scope. Script kiểm scene keys, schema, duration, count và tổng budget; vượt giới hạn phải báo lỗi, không âm thầm cắt.
- [ ] Apply Script lặp trả các scene IDs đã tạo; apply Image/VideoPrompt/VideoRender lặp không tạo thêm successor. Test cả hai caller đồng thời trên SQLite thật.
- [ ] Kiểm expected revision bằng CAS trong transaction. Output đến sau sửa tay/cancel/run mới vào lịch sử/candidate hoặc Unlinked; selection và draft hiện hành được bảo toàn.
- [ ] Commit output/apply marker và successor job hoặc continuation intent trong cùng transaction. Nếu dùng intent, tạo/link job duy nhất và xác nhận intent atomically; Channel wakeup sau commit.
- [ ] Startup xử lý intent còn thiếu. Event chỉ đánh thức; subscriber apply được await/observe/log lỗi, có đường retry bền vững. Không dựa duy nhất vào event in-memory.
- [ ] Fault injection trước/sau commit apply, insert job và wakeup; restart ở mỗi điểm phải không mất bước/không trùng. Không gọi lại AI chỉ vì finalize storage thất bại.

### FIX-3 — Điều khiển run thật

Phạm vi dự kiến: ProjectWorkflowService, JobScheduler, JobReservation, JobService; tests workflow và T16 liên quan.

- [ ] Reservation kiểm run state atomic với việc nhận job; Pause có ranh giới rõ để sau khi trả thành công không reserve thêm job run đó. Những job ngoài workflow vẫn giữ hành vi queue hiện hành.
- [ ] Kết quả hợp lệ đến khi Paused được lưu/checkpoint, successor chưa dispatch; Resume xử lý đúng bước thiếu, không mất completion trong thời gian pause.
- [ ] Cancel persist trạng thái chặn run trước khi có thể phát sinh bước con, hủy đúng job của run gồm Running. Lỗi từng job được ghi nhận và có recovery, không nuốt catch rồi báo thành công toàn bộ.
- [ ] Job chưa submit có thể nhả chỗ theo T16; đã submit/uncertain giữ fencing/binding/occupancy cần bảo vệ tới khi đối soát. Output muộn không mở lại run.
- [ ] Test race Pause/reserve, Cancel/apply, Resume/event lặp; cancel trước/sau side effect, Running/AwaitingUser/Queued, restart giữa cancel. Chạy lại retry/recovery/checkpoint T16 bị ảnh hưởng.

### FIX-4 — Chọn bước tiếp theo, prompt và phạm vi làm lại

Phạm vi dự kiến: ProjectWorkflowService, `Content/VideoWorkflowPromptComposer.cs`, content DTO/validator đã chốt; `Web/src/features/projects/VideoProductionTab.tsx` và API khi cần UI stale/history/tạo lại; workflow/prompt/DOM tests.

- [ ] Dùng một quy tắc readiness/planning cho Start, completion, manual import và Resume. Phân biệt image prompt, script motion draft và prompt chuyển động hợp lệ có provenance ảnh.
- [ ] Image success luôn có kết quả rõ: enqueue bước thiếu, chờ input, conflict hoặc hoàn tất; không đứng im vì PromptText khác null. Scene có ảnh hợp lệ không tạo ảnh thừa.
- [ ] Mọi VideoPrompt request dùng cùng composer và validator. Snapshot chứa USER_BRIEF, clip context, template và ảnh đầu vào đúng revision; MuseAI nhận prompt đã áp, không nhận nhầm chỉ thị dành cho AI writer.
- [ ] Thực thi từng hàng §3.4: audio chỉ stale video; duration stale prompt/video; ảnh stale prompt/video; đổi input chung chỉ tác động scene phụ thuộc; reorder không gọi AI. Lưu stale ngay cả khi chưa có video.
- [ ] Giữ output/history cũ và lựa chọn bền qua restart. Regenerate chủ động tạo generation mới đúng bước; retry kỹ thuật không bị dùng thay cho regenerate.
- [ ] Ảnh nền tảng dùng chung, biến thể và reuse theo fingerprint đúng §3.2/AC17. Có test một ảnh dùng nhiều cảnh, override độc lập, file/hash/template thay đổi làm mất eligibility reuse; không chỉ lưu nhãn ReuseFoundation.
- [ ] Test scene A đi tiếp khi B chờ/lỗi, lỗi ảnh chung chặn đúng nhánh; tất cả đường ModelAndProduct/ReadyImage/ProductOnly gồm dùng trực tiếp và chỉnh ảnh tùy chọn. Test giới hạn run và không tự phát sinh job khi chỉ xem/sửa nháp.

### FIX-5 — Nối production và manual xuyên suốt

Phạm vi dự kiến: `Providers/{ProviderDispatchService,ProviderRegistry,ManualHandoffService,ChatGptWebAdapter,GeminiWebAdapter,MuseAiVideoAdapter}.cs`, implementation IJobExecutor nếu cần; MainWindow, JobHandlers, JobDtos và projection JobService; QueueDrawer, jobTypes/useQueueState/ManualHandoffPanel/manualHandoffApi/JobDetailsDialog; tests tương ứng.

- [ ] Production executor lấy provider từ **job snapshot**, không định tuyến lại theo Settings hiện tại; kiểm adapter/capability và giữ binding của retry. Test Settings đổi khi job còn Queued vẫn chạy đúng provider cũ.
- [ ] Capability unverified đi vào manual/awaiting_user với reason được chuẩn hóa backend/frontend, không generic retry vòng lặp. Không bật VERIFIED để vượt guard. Nhánh chưa có browser implementation phải trả manual/unavailable trung thực, không trả URI thành công tự dựng.
- [ ] Gắn CTA handoff và panel vào Queue/chi tiết job production. Xuất/copy input có roles từ snapshot; chọn/import file qua host dialog hoặc AssetPicker, không bắt người dùng gõ ID và không nhận đường dẫn tùy ý từ React. Giữ nháp khi lỗi/conflict.
- [ ] Manual finalize dùng cùng output validator/apply/continuation bền vững của FIX-2. UI nhận authoritative response/event; nhập lặp, app crash sau import hoặc event mất không làm mất bước.
- [ ] Đồng bộ DTO C#/TypeScript và mapping binding/occupancy: sideEffectSubmitted, hasUnresolvedSideEffect, cancelRequested, maxAttempts, profileId; job chưa bind có hành vi Settings phù hợp. Thêm contract version/serialization tests.
- [ ] Test backend thật serialize payload vào UI, gồm Canceled uncertain sau reload → CTA reconcile, bound profile → login đúng profile. Kiểm lịch sử sau remount/restart theo contract; ref cache trong một mount không là persistence.
- [ ] Test dùng scheduler thật + deterministic fake **chỉ trong test**, chạy tự động từ Start tới output; không sửa state job trực tiếp. Tách thêm test manual UI → bridge → import file thật → asset → apply → successor → kết quả clip/player. Không gọi test service riêng là nghiệm thu luồng UI.

### FIX-6 — Review, sửa evidence và bàn giao

Phạm vi ghi: báo cáo review/closure của đợt sửa, decision/plan links bị thay, docs index; STATUS/TASKS chỉ coordinator. Giữ review cũ như evidence lịch sử và trỏ tới kết luận mới, không sửa lịch sử thành PASS.

- [ ] Mỗi PV-R1–PV-R11 có source diff, regression case, lệnh chạy/exit code/kết quả và reviewer verdict. Reviewer xác minh snapshot/hash đang test; ghi rõ nếu tự review thay vì review độc lập.
- [ ] Ma trận nghiệm thu trích **nguyên nội dung và số thứ tự** 20 AC tại workflow §8; mỗi AC có subchecks, PASS/PARTIAL/FAIL/NOT_RUN và evidence. AC có subcheck NOT_RUN không được gộp PASS toàn bộ.
- [ ] Đóng findings logic/manual khi đủ evidence; R1 phân tách rõ wiring/guard đã sửa với adapter live còn chờ T19–T21. PV5 full, G3 và G4 chỉ đóng khi đủ dependency/acceptance thật.
- [ ] Rà cả foundation reuse, run limits, history/restart, caption/hashtag cấp project và legacy/media preservation theo spec hiện hành; thiếu phần nào ghi gap giao owner, không thay tiêu chí để phù hợp test đã có.
- [ ] Dọn kết luận tài liệu mâu thuẫn và liên kết hỏng, giữ spec hiện hành/evidence findings mở. Báo rõ phần chưa chạy và bước tiếp theo.

## 4. Ma trận phủ findings

| Finding | Mốc sửa chính | Bằng chứng đóng tối thiểu |
|---|---|---|
| R1 | FIX-5 + live qualification | Scheduler production dùng dispatcher đúng snapshot; unverified manual; verified chỉ có khi browser path thật có evidence |
| R2 | FIX-5 | Người dùng mở handoff từ Queue, import và thấy successor chạy qua bridge thật |
| R3 | FIX-1/FIX-2 | Apply lặp/concurrent giữ IDs/job count; output stale không thay selection |
| R4 | FIX-3 | Pause chặn reserve; Cancel Running được yêu cầu; uncertainty không nhả chỗ sớm |
| R5 | FIX-4 | Image + script draft vẫn tiến tới prompt hợp lệ/clip qua scheduler |
| R6 | FIX-1 | Media/options snapshot đầy đủ và bất biến, validator âm tính thực |
| R7 | FIX-2 | Restart tại mọi boundary apply/continuation không mất/trùng bước |
| R8 | FIX-4 | Từng hàng §3.4 và history/reuse/restart có regression |
| R9 | FIX-5 | Serialized DTO backend thật đáp ứng CTA UI sau reload |
| R10 | FIX-0/FIX-6 | Đúng 20 AC gốc và phân biệt logic/manual/live/runtime evidence |
| R11 | FIX-4 | USER_BRIEF/template/ảnh đúng trong successor prompt snapshot; ca lỗi hiện có PASS |

## 5. Kiểm chứng khi tích hợp

Coordinator lấy lệnh từ project/runner hiện tại, không chốt số bài theo báo cáo cũ. Chạy kiểm tra đúng các phần bị ảnh hưởng, sau đó một lượt tích hợp trên cùng source snapshot:

1. Build frontend, desktop smoke và infrastructure smoke từ source. Full solution build khi owner đã đóng app bình thường; không kill app để gỡ apphost lock.
2. Workflow, provider, handoff; queue/snapshot/concurrency/T16 recovery-retry-checkpoint liên quan; infrastructure migration/ownership/storage.
3. DOM Queue, manual handoff, Video tab, AI Settings/Settings và media/prompt regression bị ảnh hưởng.
4. Integration scheduler E2E với fake adapter; integration manual có media thật trong kho tạm; restart/crash tests.
5. Build/run log ghi revision/hash, cấu hình, exit code, suite/case counts và thời điểm. Không lấy --no-build trên artifact chưa khớp source làm bằng chứng bản mới.

Nếu apphost đang được ứng dụng giữ, có thể build smoke bằng `-p:UseAppHost=false -m:1 -nr:false` rồi chạy DLL bằng `dotnet <đường-dẫn-DLL-vừa-build> <suite>`. Build này không thay full installer/runtime qualification. Dùng temp DB/profile/roots riêng; test media phải có file hợp lệ, không chỉ một Asset row giả thỏa FK.

## 6. Mốc website thật và phát hành sau remediation

Tách kết luận **logic/manual integration đã đạt** khỏi **live automation đã đạt**. Năng lực chưa có evidence giữ NOT_VERIFIED; nghiệm thu tương ứng giữ NOT_RUN/PARTIAL. FIX-6 không tự cho phép dùng tài khoản cá nhân/tiêu quota.

Theo quyền test account/quota cụ thể người dùng cấp, thực hiện T19 trước rồi T20/T21 theo capability: đúng URL/identity/quyền automation; hai ảnh có vai trò; ảnh vào AI writer; duration/no-audio/custom-audio; output download/asset hash; login/uncertainty/reconcile. Thiếu tài khoản/quyền/môi trường thì ghi blocker và tiếp tục phần local độc lập.

Sau đó T24 demo ba Workspace; T26 backup/restore và T27 integration/logging/secrets theo dependency; T28 migration recovery → T29 installer → T30 Windows sạch/DPI/WebView2. Analytics T25 P1 không thay thế việc đóng lỗi pipeline P0.

## 7. Định dạng bàn giao

Mỗi mốc trả: owner/model evidence khi áp dụng; baseline/hash; file đổi và contract/schema delta; finding IDs; test đã chạy với exit code; test NOT_RUN và lý do; rủi ro/migration/legacy; dependency được mở tiếp. Chỉ đánh dấu checkbox sau khi kiểm thực tế. Không tự nhận đóng toàn bộ task từ việc tạo đủ file hoặc build xanh.
