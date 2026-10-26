# TiToAI — bổ sung plan Cài đặt

**Phiên bản:** 1.1 — cập nhật ngày 2026-10-26.  
**Mức ưu tiên:** P0 trong MVP.  
**Trạng thái:** spec hiện hành; popup/theme/storage baseline đã có theo [STATUS](../agents/STATUS.md); provider settings/global account pool là phần chưa nghiệm thu. Xem [plan triển khai từng phần](implementation/README.md).

Tài liệu này bổ sung `SOURCE_PLAN.md` §1, §3–5, §7–8, §10–12 và Definition of Done theo yêu cầu người dùng. Giữ bản nguồn để đối chiếu. Quy định §2–3 dưới đây thay thế giả định mọi tài liệu/ảnh/video luôn nằm trong một root LocalAppData cố định. Giao diện sáng/tối theo `UI_THEME_PLAN.md`; bố cục và browser ẩn/login theo `UI_BROWSER_PLAN.md`.

## 1. Popup Cài đặt

- Có mục **Cài đặt** với nhãn và biểu tượng bánh răng trên app shell, truy cập được từ mọi màn hình chính. Bấm vào mở popup trong ứng dụng, giữ nguyên màn hình, Workspace và nội dung đang nhập phía sau.
- Popup gồm **Lưu trữ**, **AI & Prompt**, **Giao diện**. Nhóm Giao diện dùng chung preference và hành vi áp dụng ngay trong UI_THEME_PLAN.
- Lưu trữ và AI & Prompt chỉnh trên bản nháp, có **Lưu**, **Hủy**, nút đóng và thông báo kết quả. Hủy/đóng bỏ bản nháp chưa lưu; không đổi job hoặc cấu hình đã lưu. Việc bỏ bản nháp không hoàn tác theme đã áp dụng ngay ở nhóm Giao diện.
- Backend xác thực rồi lưu đồng thời các thay đổi lưu trữ/provider; chỉ báo thành công khi đã lưu bền vững. Lỗi validation/lưu giữ bản nháp để sửa hoặc thử lại, giữ cấu hình đã lưu trước đó và chỉ rõ trường có lỗi.
- Thiết lập có phạm vi toàn ứng dụng theo Windows user; chuyển Workspace không đổi thiết lập. Popup hỗ trợ bàn phím ở cả Light/Dark, cuộn khi thiếu chiều cao và trả focus về nút Cài đặt khi đóng.

## 2. Chọn nơi lưu tài liệu, ảnh và video

Ba trường chọn thư mục độc lập, mỗi trường hiển thị đường dẫn hiện tại và nút **Chọn thư mục…** mở hộp thoại Windows:

| Trường | Nội dung được lưu | Root mặc định |
|---|---|---|
| Tài liệu | Kịch bản, prompt và tài liệu được người dùng lưu/xuất thành file | `%LOCALAPPDATA%/TiToAI/documents` |
| Ảnh AI | Ảnh AI tạo ra và ảnh tham chiếu được nhập vào library | `%LOCALAPPDATA%/TiToAI/images` |
| Video AI | Video AI trả về hoặc được nhập thủ công vào library | `%LOCALAPPDATA%/TiToAI/videos` |

- Có thể chọn các ổ/thư mục khác nhau cho từng loại hoặc dùng cùng một root. App phân tách bên dưới theo `<workspace-id>/projects/<project-id>/<documents|images|videos>/...` để tránh trùng tên và lẫn kênh. Tên file an toàn và ID duy nhất ngăn ghi đè ngoài ý muốn.
- Đây là nơi lưu bản file thật. Nội dung editor/template tiếp tục lưu version trong DB; chọn thư mục không tự xuất file ở mọi lần gõ. Audio gốc giữ layout nội bộ của plan nguồn.
- DB, browser profiles, logs và secrets ở `%LOCALAPPDATA%/TiToAI`; popup này không chuyển DB hoặc session browser.
- Host chuẩn hóa đường dẫn và kiểm tra có thể tạo/ghi file; báo rõ đường dẫn sai, thiếu quyền hoặc ổ không sẵn sàng. Thư mục chưa tồn tại được tạo khi lưu hợp lệ; không lưu lựa chọn mới không dùng được.
- Khi ghi file, hết dung lượng/mất ổ/mất quyền phải báo lỗi, giữ thông tin job và kết quả đã nhận để thử lưu lại. Không âm thầm đổi thư mục hoặc gửi lại yêu cầu sinh AI chỉ vì lưu file thất bại.
- Host kiểm tra đường dẫn sau chuẩn hóa, kể cả junction/symlink, vẫn thuộc root đã cấp. UI chọn thư mục qua contract cho phép; không có lệnh đọc/ghi file tùy ý.

## 3. Đổi nơi lưu và giữ dữ liệu cũ

- Đổi thư mục chỉ áp dụng cho asset mới hoặc job mới tạo sau khi Lưu. MVP không tự di chuyển/xóa file đã có; popup hiển thị rõ điều này trước khi lưu.
- Asset lưu `storage_root_id` và `relative_path`; mapping root được lưu bền vững. Khi đổi đường dẫn, tạo tham chiếu root mới, giữ root cũ còn được asset/job tham chiếu; không sửa mapping cũ để trỏ tất cả dữ liệu sang nơi mới. Metadata tài liệu xuất cũng dùng cơ chế này.
- Khi tạo job, chốt root đích theo loại đầu ra. Job queued/running/awaiting_user và retry/resume giữ root đã chốt; đổi cài đặt không chuyển đích của job cũ. File do người dùng chủ động lưu/xuất dùng root hiệu lực khi bắt đầu thao tác.
- Root cũ không truy cập được thì library báo file/ổ không sẵn sàng, cho thử lại sau khi kết nối đúng root; không báo asset bị xóa hoặc tự sinh lại bằng AI.
- Backup gồm tất cả root còn được tham chiếu, kể cả ngoài LocalAppData. Manifest ánh xạ root và relative path; thiếu root phải báo backup chưa đầy đủ. Restore vào root đích được người dùng chọn/xác nhận, kiểm tra đường dẫn và remap tham chiếu; không ghi mù vào đường dẫn tuyệt đối trên máy nguồn.

## 4. Chọn provider và dùng pool tài khoản AI toàn ứng dụng

**Yêu cầu hiện hành ngày 2026-10-26:** tài khoản ChatGPT, Gemini và MuseAI dùng chung cho mọi Workspace/TikTok. Không cần mỗi TikTok một account AI và không gắn cố định Workspace với MuseAI. Một account AI tương ứng một browser/profile riêng để đăng nhập và giữ session; ChatGPT/Gemini không chung profile, các MuseAI accounts cũng không chung profile.

| Tác vụ | Chọn provider | Chọn tài khoản thực thi |
|---|---|---|
| Tạo kịch bản | Gemini Web hoặc ChatGPT Web | Scheduler lấy slot rảnh trong pool provider đã chọn |
| Tạo ảnh | Gemini Web hoặc ChatGPT Web | Tương tự, dùng chung cho mọi kênh |
| Tạo prompt chuyển động/video | Gemini Web hoặc ChatGPT Web | Tương tự, độc lập hai lựa chọn trên |
| Tạo video | MuseAI | Scheduler lấy MuseAI account rảnh trong pool global |

Có đủ 8 tổ hợp ba lựa chọn AI, gồm cùng provider cho mọi bước hoặc Gemini ảnh/ChatGPT prompt và ngược lại. Lần đầu Chưa chọn; lưu/restart giữ đúng. Thiếu setting chặn tạo job tác vụ đó; không âm thầm chọn provider hoặc fallback khi lỗi/quota/full pool.

### 4.1. Thêm account và giới hạn đồng thời

- Trang Trình duyệt AI nhóm ChatGPT/Gemini/MuseAI theo provider, mỗi account một card Dùng chung toàn ứng dụng, có slot rảnh/bận và login/session riêng. Thêm account → nhập nhãn → tạo profile → login thủ công → probe đúng identity → enable nhận job. Không chọn TikTok/Workspace hoặc link MuseAI vào kênh.
- Tài khoản TikTok và dữ liệu project/scene/job/asset vẫn theo Workspace. Dùng chung account AI không được làm lẫn prompt/transcript/output giữa kênh.
- Capacity mỗi account có maxConcurrentJobs/maxTabs/perTaskLimits; một job/tab. Mặc định ChatGPT/Gemini tối đa 3 tabs, mỗi loại Script/Image/VideoPrompt 1; MuseAI 1 job/account. Có thể thay giới hạn theo capability/quota đã xác minh, không coi nhiều tab/browser là thêm quota.
- Budget toàn app maxOpenBrowsers mặc định 4 và maxConcurrentJobs 8, chỉnh trong AI & Prompt; giới hạn account chỉnh trên card account. Giảm cap không kill job, chỉ dừng cấp slot mới cho tới dưới cap. Validation/revision/schema chi tiết tại [contract v1.1 §1/§3](../decisions/execution-contract-v1.1.md).
- Thêm tên/URL không tạo adapter; URL login, identity probe và automation phải được xác minh tại T19. Chưa xác minh để Unverified/NOT_VERIFIED và hướng dẫn manual trong app, không báo đã chạy thật.

### 4.2. Hàng đợi và vòng đời browser

- Job enqueue chốt input/provider/config/root/prompt/workflow, chưa chọn account cụ thể. Scheduler chọn global account cùng provider còn slot khi reserve lần đầu; full pool giữ Queued/Chờ slot, không tăng attempt.
- Sau reserve tạo binding account/profile riêng bất biến; retry/resume/reconcile dùng account đã bind, không chuyển request đã gửi sang account khác. Job chưa bind có thể nhận bất cứ account rảnh phù hợp.
- Ví dụ 10 TikTok + 1 ChatGPT: dùng chung account theo số tab/task slots cho phép, phần còn lại chờ. 10 video + 4 MuseAI cap=1: chạy tối đa 4 nếu budget đủ, 6 chờ; mỗi job xong nhường ngay cho job kế, không chờ cả đợt.
- Mở browser ẩn khi thực thi; hoàn tất nhận/lưu kết quả rồi đóng context khi profile không còn job/tab/checkpoint, nhả owner/slot và wake queue. Job sau mở lại đúng userDataDir, probe session lại. ChatGPT còn tab bận thì không đóng toàn browser khi một tab xong.
- Job uncertain phải giữ checkpoint/reconcile/quarantine, không tự đóng/nhả slot rồi gửi lại. Đóng browser lỗi chặn profile đó, pool khác tiếp tục. Một login visible toàn app; login chỉ pause profile liên quan, xác minh xong về Hidden trước resume.

### 4.3. Thời điểm áp cấu hình và migration

- Run giữ workflow/brief; mỗi job chốt Settings lúc enqueue bước đó. Đổi Settings/template/root chỉ ảnh hưởng job chưa tạo, một batch dùng một snapshot Settings. Account/profile chốt muộn ở reservation nên queue dùng đúng slot rảnh, tách khỏi input snapshot.
- Settings revision/expectedRevision, roots + provider + global budgets save atomically. Hủy/đóng bỏ draft, theme áp ngay độc lập; read/save lỗi giữ draft/config cũ, không đoán default để dispatch.
- Nâng profile shared_ai gộp: archive/giữ lịch sử, login lại từng provider, không copy cookie. MuseAI cũ gắn Workspace chuyển thành global pool theo identity/profile đã xác minh, giữ legacy mapping/job/reference. Không âm thầm sửa binding hoặc resend job cũ.
- [V1.1](../decisions/execution-contract-v1.1.md) thay giả định AI/MuseAI riêng Workspace trong SOURCE_PLAN/contracts baseline và account/profile snapshot chốt ngay enqueue. T02a review rồi T13a triển khai profile/migration; T15 dùng global queue/capacity. Đây là plan, chưa là code/runtime PASS.

## 5. Lưu cấu hình và contract

- Dùng `app_settings` cho `storage.documentsRootId`, `storage.imagesRootId`, `storage.videosRootId` và mapping `storage.roots` từ root ID sang đường dẫn do host quản lý. Đường dẫn tuyệt đối chỉ nằm tại mapping; asset/job giữ root ID và relative path, không phụ thuộc root hiện tại.
- Lựa chọn tác vụ ở `ai.providers.image`, `ai.providers.script`, `ai.providers.videoPrompt`; giá trị là `provider_key` ổn định từ registry, không là nhãn UI hoặc selector website. `provider_configs` giữ cấu hình adapter/version; không tạo nguồn cấu hình thứ hai cho cùng lựa chọn. MVP dùng mặc định toàn app này cho ba tác vụ, chưa có Workspace override cho routing AI; T02 quy định rõ cách liên kết với scope provider config của plan nguồn.
- Budget toàn app ở `scheduler.maxOpenBrowsers` và `scheduler.maxConcurrentJobs`; DTO fields maxOpenBrowsers/maxConcurrentJobs. Capacity account lưu tại ProviderAccount của v1.1, không lặp vào từng Workspace/Settings key. Snapshot input/config bất biến, ExecutionBinding account/profile riêng tại reserve; giới hạn capacity thay đổi chỉ ảnh hưởng reservation mới.
- T02 chốt schema/DTO đọc, chọn thư mục, xác thực và lưu cấu hình; capability/session status; snapshot job; metadata `document`/`storage_root_id`; cách import dữ liệu cũ dùng root mặc định. Dùng bridge/API đã chọn, có validation và mã lỗi. Persistence/assets owners thống nhất mapping root, metadata và backup trước triển khai.
- Lần đầu thiếu nơi lưu thì dùng root mặc định sau khi kiểm tra; thiếu provider thì Chưa chọn. Provider key sai/bị vô hiệu hóa yêu cầu chọn lại. Lỗi đọc settings đã có phải báo và cho thử lại; không ghi đè bằng mặc định hoặc dispatch job bằng provider/root suy đoán.

## 6. Task và nghiệm thu

- T02 chốt contract §5; T06 dựng entry/popup và nhóm Giao diện; T09 hoàn tất nhóm Lưu trữ và xử lý asset theo §2–3. T15 lưu root/provider snapshot theo contract; T18 hoàn tất nhóm AI & Prompt và dispatch qua registry; T20 tích hợp adapter theo lựa chọn. Coordinator phân file UI/backend cụ thể để các owner không ghi chồng.
- T22 dùng cấu hình trong pipeline; T26 backup/restore nhiều root; T27 kiểm tra Settings tích hợp; T30 kiểm tra các tiêu chí dưới đây. Không đánh dấu nhóm storage/provider hoàn tất chỉ vì popup đã mở được.
- Phần bổ sung browser được theo dõi bằng T02a (review contract/schema) và T13a (profile isolation/migration); không dùng DONE của T13/T14 baseline để đóng phần mới. Settings revision/expectedRevision và fieldErrors theo contract v1.1 §3.

1. Bấm Cài đặt ở các màn hình chính mở popup; Lưu/Hủy/đóng và lỗi đúng §1, giữ màn hình/form/Workspace. Giao diện đúng Light/Dark/System.
2. Chọn ba thư mục khác nhau, lưu và restart: tài liệu xuất, ảnh và video thực sự ghi đúng root và Workspace/project; cũng kiểm tra dùng chung root.
3. Thiếu quyền, đường dẫn thoát root/junction, mất ổ và hết dung lượng cho lỗi hữu ích; cấu hình cũ còn hiệu lực khi lưu Settings lỗi, không false success hoặc gửi lại AI vì lỗi ghi đĩa.
4. Đổi root khi có asset cũ và job queued/running/awaiting_user: dữ liệu cũ mở đúng nơi, job cũ ghi root cũ, job mới dùng root mới; không tự di chuyển/xóa/ghi đè file.
5. Dùng Gemini Web cho ảnh và ChatGPT Web cho prompt, rồi đảo lại; dispatch đúng provider và giữ lựa chọn sau restart. Fake xác định chỉ chứng minh routing; account thật/manual mode ghi evidence riêng theo T19/T20/T30.
6. Thiếu/sai provider, chưa đăng nhập, mất session hoặc không hỗ trợ automation: hướng dẫn/handoff đúng provider, không ngầm chọn dịch vụ khác. Đổi cấu hình không thay snapshot hoặc resend job cũ.
7. Backup/restore nhiều root ngoài LocalAppData và root cũ: manifest đủ, restore đúng đích đã chọn, metadata/library nhất quán; mất root nguồn được báo backup chưa đầy đủ.
8. Kiểm 8 tổ hợp AI; 10 TikTok dùng 1 ChatGPT theo tab cap; 10 video/4 MuseAI mặc định 4 chạy/6 chờ khi budget đủ. Account rảnh nhận job bất cứ Workspace; result vẫn đúng scope gốc, login một profile không pause profile khác. Xong job close/reopen đúng profile rồi nhường slot, không đóng tab còn bận.
9. Nâng cấp từ shared_ai giữ legacy/checkpoint và snapshot; yêu cầu login lại từng provider, không copy cookie, không resend job uncertain. Hai form Settings save cùng revision chỉ một commit; form stale giữ draft và báo conflict.

MVP chỉ hoàn tất khi popup, lưu bền vững, routing provider, lưu file thật và backup/restore đạt các tiêu chí trên. Ghi kiểm tra đã chạy và NOT_RUN; tài liệu này chưa phải bằng chứng triển khai.
