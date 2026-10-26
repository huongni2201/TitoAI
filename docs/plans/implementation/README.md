# TiToAI — plan triển khai từng tính năng còn thiếu

Ngày cập nhật: **2026-10-26 (UTC+07)**. Có **41 file plan riêng**, mỗi file mô tả một tính năng hoặc một bước nghiệm thu; trang này là mục lục, baseline và quy ước chung. T02a review contract bổ sung được mô tả trong TASKS và contract v1.1.

## Cách dùng và nguồn sự thật

1. Đọc [STATUS](../../agents/STATUS.md), [TASKS](../../agents/TASKS.md) và [ENGINEERING_RULES](../../agents/ENGINEERING_RULES.md); khi điều phối Antigravity đọc [ORCHESTRATION](../../agents/ORCHESTRATION.md), xác minh model theo policy của từng phiên.
2. Chọn plan có dependency/gate đã đạt. Hậu tố a/b/c/d trong bộ này **đề xuất chia nhỏ** task gốc, không phải trạng thái hoặc quyền ghi mới. Coordinator xác lập owner và exact file list trước dispatch.
3. Đọc spec liên quan: [SOURCE](../SOURCE_PLAN.md), [Theme](../UI_THEME_PLAN.md), [UI/Browser](../UI_BROWSER_PLAN.md), [Settings](../SETTINGS_PLAN.md), [baseline v1.0.4](../../decisions/contracts.md), [contract bổ sung v1.1](../../decisions/execution-contract-v1.1.md), [versions](../../decisions/versions.md). V1.1 cụ thể hóa thiết kế mới và chờ review T02a; chưa phải code/DTO đã tích hợp hoặc gate PASS.
4. Làm từng checklist, kiểm hành vi và ghi evidence ngay trong báo cáo task. Chỉ coordinator cập nhật trạng thái chung. Người dùng đã yêu cầu triển khai qua CLI ngày 2026-10-26; trạng thái/ownership theo STATUS. Bộ plan không tự cấp quyền hoặc bỏ qua dependency.
5. Khi triển khai xong, giữ spec hiện hành, đánh dấu plan hoàn tất kèm report/revision thay vì tạo nhiều bản plan trùng; giữ evidence của finding chưa đóng.

SOURCE dùng tên AIContentStudio/paths cũ ở một số ví dụ: tên sản phẩm và namespace hiện hành là TiToAI, ba roots theo Settings, bridge WebView2 là đường giao tiếp đã chọn. Không tạo localhost API, Audio Worker, TTS, FFmpeg, auto-post TikTok hoặc PostgreSQL trong MVP.

## Baseline đã đối chiếu với source

| Vùng | Bằng chứng hiện có | Phần còn thiếu |
|---|---|---|
| Solution | Theo STATUS: năm projects Contracts/Infrastructure/Application/Browser/Desktop và frontend React | Providers/runtime pipeline còn T18–T23; không scaffold module trống |
| SQLite | Theo STATUS: G2 migration/core entities và smoke thật đã nghiệm thu | V1.1 cần provider-profile metadata, scope Script project, typed result, revision/fencing; T02a chốt, một persistence owner tích hợp |
| Desktop | [MainWindow](../../../src/TiToAI.Desktop/MainWindow.xaml.cs) tạo dispatcher/theme/host và đã nối bootstrap persistence; T05 DONE | Local page + `workspace.list` round-trip đã chạy trên WPF/WebView2 thật; live cancel được giữ cho T30 |
| Bridge | Theo STATUS: G2-INT DONE, 27 handlers gồm browser.* đã nối host | Commands v1.1 chưa triển khai; DTO + handler + client + test cùng lần tích hợp; allow-list không đồng nghĩa có tính năng |
| Theme | [HostThemeService](../../../src/TiToAI.Desktop/Hosting/HostThemeService.cs), [ThemeContext](../../../src/TiToAI.Web/src/context/ThemeContext.tsx), và `ui.theme` SQLite store; T06a DONE | Windows toggle/failure injection and full-screen flash qualification remain at T30 |
| UI | Theo STATUS mới: T09/T11 và browser manager baseline DONE; T12 READY, T15 đang Coordinator 2 triển khai baseline | V1.1 cần T02a/T13a review và bàn giao lại brief/schema trước tích hợp pool global; không đổi source/owner từ lượt viết plan |
| Test | Hai console smoke projects trong tests; frontend chỉ có dev/build/preview scripts | Test suites theo feature, tải và qualification thật; không có lệnh frontend test hiện hành |

Baseline trên cập nhật theo STATUS và evidence đã dẫn, không phải một lượt test runtime mới. Các đoạn Wave 2/Wave 4 dưới là lịch sử còn giữ bằng chứng; STATUS quyết định tiến độ hiện hành. Không gọi một entity/table có sẵn là tính năng đã xong.

Dòng Baseline trong file con mô tả mốc lập plan ban đầu, không phải inventory hiện tại. Với T07/T08/T09/T10/T11/T13/T14 đã DONE, dùng source và report trong STATUS để sửa phần có sẵn, không tạo lại module chỉ vì plan cũ ghi Chưa có. Yêu cầu mới dùng T02a/T13a và contract v1.1; checklist cũ không tự chứng minh đã đạt phần mới.

Wave 2 đã bàn giao qua ba luồng tách file: navigation/log và keyboard/focus source đã sửa, frontend/.NET build PASS; final smoke runner **17/17 PASS**. Hash của `BridgeSmokeTests.cs` vẫn trùng evidence; `Program.cs` có thêm kiểm tra tham số test, được xác nhận bằng app launch với DB/profile riêng. Wave 4 xác nhận WPF/WebView2 local load, `workspace.list` round-trip và keyboard modal/drawer. T05/T06/T06a DONE; G1 hoàn tất. Full DPI matrix, DOM attrs trực tiếp, live bridge cancel và Windows theme switch/fault injection được giữ cho T30. Các report cũ là lịch sử; STATUS và addendum review hiện hành là nguồn sự thật.

Runner là công cụ kiểm tra, không phải acceptance độc lập. Khi runner bị policy chặn, dùng evidence còn hiệu lực theo source hash và runtime check; không bypass policy và không lặp chạy chỉ để làm xanh một lệnh.

## Mục lục: mỗi tính năng một file

| Plan | Task gốc được bao phủ | Ưu tiên |
|---|---|---|
| [Tích hợp Desktop host và WebView2 bridge](T05-host-integration.md) | T05 | P0 |
| [Hoàn tất React shell và navigation nền](T06-shell-completion.md) | T06 | P0 |
| [Lưu theme và đồng bộ Windows–WPF–React](T06a-theme-persistence.md) | T06a | P0 |
| [Quản lý Workspace và tạm dừng kênh](T07a-workspace-backend.md) | T07 (phần Workspace) | P0 |
| [TikTok theo Workspace và tài khoản AI global](T07b-account-linking.md) | T07 (phần account) | P0 |
| [Quản lý dự án và xóa an toàn](T08a-projects-backend.md) | T08 (phần project) | P0 |
| [Quản lý cảnh và thứ tự cảnh](T08b-scenes-backend.md) | T08 (phần scene) | P0 |
| [Cài đặt ba thư mục lưu trữ và lịch sử root](T09a-storage-settings.md) | T09 (Lưu trữ) | P0 |
| [Nhập và lưu ảnh, audio gốc, video](T09b-media-assets.md) | T09 (media) | P0 |
| [Xuất kịch bản và prompt thành tài liệu](T09c-document-export.md) | T09 (tài liệu) | P0 |
| [Prompt template, version, override và render preview](T10-prompt-templates.md) | T10 | P0 |
| [Màn hình danh sách Workspace và form quản lý](T11a-workspace-ui.md) | T11 (Workspace UI) | P0 |
| [Tổng quan Workspace và dữ liệu tiến độ](T11b-workspace-overview.md) | T11 (Overview) | P0 |
| [Màn hình dự án và bảng danh sách cảnh](T11c-project-scenes-ui.md) | T11 (Project/Scene UI) | P0 |
| [Editor cảnh và quản lý prompt theo mục đích](T12a-scene-prompt-editor.md) | T12 (editor) | P0 |
| [Thư viện tài nguyên và preview có kiểm scope](T12b-media-library.md) | T12 (library) | P0 |
| [Browser profile, tab pool và chuyển sang login thủ công](T13-browser-ownership.md) | T13 | P0 |
| [Profile theo account, pool AI toàn app và nâng cấp legacy](T13a-provider-profile-isolation.md) | T13a bổ sung sau T02a | P0 |
| [Màn hình quản lý browser và trạng thái đăng nhập](T14-browser-manager-ui.md) | T14 | P0 |
| [Hàng đợi bền vững, reservation và phân phối công bằng](T15-durable-job-queue.md) | T15 | P0 |
| [Phục hồi crash, retry, cancel và đối soát không gửi trùng](T16-recovery-retry-cancel.md) | T16 | P0 |
| [Hàng đợi, tiến độ và thao tác cần người dùng](T17-queue-progress-ui.md) | T17 | P0 |
| [Provider registry, capability và cấu hình có version](T18a-provider-registry.md) | T18 (registry) | P0 |
| [Cài đặt AI riêng cho ảnh, kịch bản và video prompt](T18b-ai-provider-settings.md) | T18 (AI Settings) | P0 |
| [Manual handoff trong app và nhập kết quả đúng job](T18c-manual-handoff.md) | T18 (manual mode) | P0 |
| [Cấu hình workflow và phiên bản áp dụng cho job mới](T18d-workflow-configuration.md) | T18 (workflow config); T22 sử dụng | P0 |
| [Xác minh website, tài khoản và ma trận capability](T19-provider-verification.md) | T19 | P0 |
| [Adapter ChatGPT Web cho kịch bản, ảnh và video prompt](T20a-chatgpt-adapter.md) | T20 (ChatGPT) | P0 |
| [Adapter Gemini Web cho kịch bản, ảnh và video prompt](T20b-gemini-adapter.md) | T20 (Gemini) | P0 |
| [Adapter MuseAI nhận ảnh, prompt và audio gốc](T21-museai-video-adapter.md) | T21 | P0 |
| [Pipeline nội dung từ brief tới thư viện](T22-pipeline-orchestration.md) | T22 | P0 |
| [Giao diện chạy pipeline, review đầu ra và tiếp tục công việc](T23-pipeline-ui.md) | T23 | P0 |
| [Demo MVP chức năng trên ba Workspace](T24-mvp-demo.md) | T24 | P0 |
| [TikTok Analytics và Affiliate theo dữ liệu có nguồn](T25-analytics-p1.md) | T25 | P1 |
| [Backup và restore DB cùng tài nguyên trên nhiều root](T26-backup-restore.md) | T26 | P0 |
| [Tích hợp popup Cài đặt và kiểm tra lưu đồng thời](T27a-settings-integration.md) | T27 (Settings integration) | P0 |
| [Log có correlation, bảo vệ bí mật và cảnh báo dung lượng](T27b-logging-secrets.md) | T27 (logging/secrets) | P0 |
| [Nâng schema và phục hồi migration thất bại](T28-migration-upgrade-recovery.md) | T28 | P0 |
| [Đóng gói installer Windows và chiến lược dependency](T29-windows-installer.md) | T29 | P0 |
| [Nghiệm thu phát hành: dữ liệu, tải, browser, UI và máy sạch](T30-release-qualification.md) | T30 | P0 |
| [Đánh giá mở rộng server hoặc nhiều máy khi có nhu cầu](T31-server-scale-p2.md) | T31 | P2 |

P0 gồm cả phần hoàn tất nền tảng và nghiệm thu phát hành; T25 là P1, T31 P2 chưa mở. T00–T04 đã DONE nên không viết lại plan implementation cho phần đã nghiệm thu.

## Thứ tự triển khai và ranh giới tích hợp

| Lượt | Điều kiện và công việc |
|---|---|
| 1 — Khép G1 | T05/T06 DONE theo addendum hiện hành; triển khai T06a sau T04/T05/T06; xác minh G1 trên cùng snapshot source |
| 2 — Nền nghiệp vụ | Sau G1 mở T07, T10, T13 theo dependency; integrator tạo Application/project refs một lần trước khi các owner phụ thuộc |
| 3 — Nội dung và UI | T08 sau T07; T09 sau T08; T11 sau T07/T08; T12 sau T09/T10; T14 sau T07/T13 |
| 3a — Yêu cầu browser mới | T02a review v1.1 sau T02/T08/T10/T13/T14; T13a sau T02a/T13/T14. giữ owner hiện hành, đặc biệt T15 Coordinator 2; không viết chồng hoặc tự resume worker bằng contract cũ |
| 4 — Thực thi bền vững | T12 cần contract T02a trước DTO/schema phụ thuộc; T15 sau T08/T09/T10/T13/T02a/T13a; T16 sau T15; T17 sau T16. Khép cả bổ sung T13a để đạt G2 mới |
| 5 — Provider/workflow | G2 đạt, T18 prerequisites đạt; chốt registry/config trước AI Settings/manual/workflow UI; T19 xác minh rồi T20 ChatGPT/Gemini và T21 MuseAI |
| 6 — MVP chức năng | T22 sau T12/T17/T20/T21; T23 sau T22; T24 sau T14/T23 |
| 7 — Reliability/release | T26 và T27 có thể làm sớm khi đủ dependency riêng, không tranh vùng ghi; T28 sau T24/T26/T27, rồi T29→T30 |
| Sau P0 | T25 chỉ khi được ưu tiên và đủ T07/T09/T19; T31 chỉ khi người dùng mở nhu cầu server |

Một task gốc có nhiều plan chỉ DONE khi mọi phần acceptance của task đều đạt. File chung (composition root, DTO, allow-list, SettingsModal, App.tsx, migrations/locks/manifests) đi qua coordinator. Chỉ chạy song song khi owner/file không giao nhau và mỗi phiên Antigravity đủ model evidence; các đợt dispatch và kết quả thực tế được ghi trong STATUS.

### Contract và điểm tích hợp trước code phụ thuộc

Thiết kế chuẩn hóa tại [v1.1](../../decisions/execution-contract-v1.1.md), review T02a rồi mới tích hợp. Bảng này chỉ định owner/thời điểm; không là nguồn schema thứ hai. Các đề xuất cũ bên dưới đã được cụ thể hóa như sau:

| Khoảng trống quan sát được | Đề xuất cụ thể và nơi chốt |
|---|---|
| G1 selector cần workspace.list nhưng CRUD nằm T07 | T05/T06 tích hợp projection DB read-only cho G1; T07 tiếp quản service CRUD, không dùng fake empty để vượt gate |
| T07 account.link cần profile trước T13 | T07 dùng profile identity resolver contract + fixtures, registry rỗng trả lỗi thật; T13 triển khai resolver/registry, T14 kiểm binding/login thật; không thêm dependency vòng |
| CRUD project/scene và các màn tổng quan chưa có DTO đầy đủ | T07/T08 chốt payload/response/paging/status/concurrency trước UI; bổ sung read model KPI khi source dữ liệu sẵn sàng |
| Library/manual/workflow command catalog | V1.1 §5–6 đã mô tả DTO/idempotency/revision; T02a review, từng owner tích hợp handler/client/negative test, không wildcard/file API tùy ý |
| Script trước scene, text/media result và scene inputs | V1.1 §4/§6: Script project-level; typed result; image/video prompt riêng; audio scene-level. T02a review schema, T12/T15/T22 triển khai đúng phần |
| Snapshot/binding và fencing | V1.1 §3–4: input/provider/root snapshot lúc enqueue; ExecutionBinding account/profile/slot lúc reserve; lease owner/token, unique slot; không pin AI account theo kênh |
| Delete guard và Canceled unresolved | V1.1 §4–5 giữ guard nonterminal hoặc unresolved side effect, kể cả Canceled; T16 kiểm không xóa evidence, không auto-resend |
| AI config chưa có trước scheduler T15; Settings.save chứa cả storage/provider | T09 giữ null/provider hợp lệ, không tự chọn; T15 fake resolver chỉ phục vụ test; T18 nối registry thật và một SettingsStore/handler save atomic, không store song song |
| Retry/resume/manual/library/workflow và thêm MuseAI profile | V1.1 §1/§5/§6 có catalog DTO và idempotency; T13a/T16/T17/T18/T12 triển khai handler tương ứng. MVP không có Stop tùy ý hoặc hard-delete profile |
| Browser/slot lifecycle | operationalState baseline đã có; v1.1 thêm capacity/Closing slot phase, context chỉ Stopped sau close; T13a/T15 phối hợp owner/reservation |
| Audio/prompt scene | V1.1 §4 chốt audio scene-level cho MVP; hai prompt fields riêng; không tự mở project audio fallback |
| Profile gộp và MuseAI riêng Workspace | T13a theo v1.1 §1–3: account AI global pool, legacy mapping/evidence còn; account đầy chờ, browser idle close/reopen; review T02a trước migration |
| Tab/role capacity và browser budget | V1.1 §1.3/§3 quy định cap config theo account/task và global budget; T02a review, T13a/T15 triển khai. Các ví dụ API cũ không thay hành vi Theme/UI_BROWSER |

Thiết kế mới chờ T02a review; T13a/T15 phối hợp tích hợp. Giữ owner T15 Coordinator 2, không tự dispatch hoặc coi task in-progress baseline đã đáp ứng pool mới.

## Quy ước code và bằng chứng cho các plan

- Đường dẫn ghi “tạo đề xuất” chưa tồn tại và không cấp ownership. Không scaffold hàng loạt thư mục trống theo bộ plan.
- Giữ modular monolith: Application chứa use case/ports nghiệp vụ, không tham chiếu WPF/React/selector; Infrastructure triển khai persistence/storage ports thực sự cần. Desktop là composition root; Browser/Providers được nối qua contract. Không tạo repository cho mọi entity nếu một seam đơn giản đủ.
- Tests dùng DB/file/profile tạm; kiểm cả IDs hợp lệ nhưng thuộc Workspace khác. Dùng console assert checks hiện có nếu đủ; coordinator có thể tạo một Application smoke-check project dùng chung khi cần, không giả đang có xUnit/Vitest.
- Test file mới theo feature trong smoke project phù hợp; ví dụ WorkspaceChecks/SceneChecks/AssetStorageChecks/JobRecoveryChecks. Chốt runner/discovery trước ghi lệnh chạy mới. Khi test project chưa có thì ghi rõ bước tạo và pin dependency nếu thật sự cần.
- Có thể dùng các lệnh **đã tồn tại** khi thực thi sau này: `dotnet build TiToAI.sln --no-restore -m:1`; `pnpm run build` từ src/TiToAI.Web; `dotnet run --project tests/TiToAI.Infrastructure.SmokeChecks/TiToAI.Infrastructure.SmokeChecks.csproj` và project Desktop.SmokeChecks tương ứng. Kiểm tra lại khả năng chạy/policy trước dùng, không xem lệnh trong plan là evidence PASS.
- Bàn giao gồm files/hash hoặc revision, scenario, lệnh/bước đã chạy, expected/observed, report path và NOT_RUN/blocker. Khi chưa có commit, dùng source hash/snapshot ID; không bịa commit SHA.
- Fake chứng minh logic/routing/recovery; profile/account thật mới chứng minh website/identity/session/hidden. Clean Windows VM mới chứng minh installer. Không báo DONE từ build hoặc tạo file.
- Tài liệu thư viện được kiểm tra bằng Context7 ngày 2026-10-26 cho persistent browser profile và SQLite backup; Microsoft.Data.Sqlite không resolve đúng package nên dùng tài liệu chính thức Microsoft trong plan T26. Không pin/nâng version mới trong lượt viết plan.

## Phạm vi kiểm tra của lần lập plan

Đã đọc trạng thái, task dependencies, engineering/orchestration rules, source spec và bổ sung; khảo sát code host/bridge/theme/entities/UI, inventory source/tests và reports hiện có. Bộ plan là thiết kế dựa trên baseline đó, không phải audit mọi dòng code. Không chạy build/runtime/provider login/test suite trong lượt chỉ sửa tài liệu này; các kết quả PASS/NOT_RUN nêu ở baseline trích evidence cũ có dẫn nguồn.

