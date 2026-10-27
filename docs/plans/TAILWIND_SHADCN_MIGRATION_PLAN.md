# TiToAI — migration toàn bộ React UI sang Tailwind + shadcn/ui

Ngày 2026-10-26 (UTC+07). Trạng thái: **MÃ NGUỒN ĐÃ HOÀN TẤT (Waves A–D2 hoàn thành; M6 Runtime Qualification đang chờ)**. Toàn bộ React UI đã chuyển đổi sang Tailwind CSS 4 + shadcn/ui primitives. Chi tiết nghiệm thu tại [STATUS](../agents/STATUS.md) và [Integration Closure Report](../reviews/integration-closure/2026-10-26-final-integration-closure-report.md).

Đọc cùng [Engineering rules](../agents/ENGINEERING_RULES.md), [Theme](UI_THEME_PLAN.md), [UI/Browser](UI_BROWSER_PLAN.md), [queue/Settings 1.2](QUEUE_EXECUTION_SETTINGS_PLAN.md) và [Settings](SETTINGS_PLAN.md). Plan này quy định công nghệ styling; các tài liệu đó tiếp tục quy định hành vi, bố cục và dữ liệu. Các ID M0–M6 bên dưới là bước đề xuất, chưa phải task đã được coordinator giao hoặc nghiệm thu.

## 1. Đích đến và baseline

- Toàn bộ UI React dùng utility Tailwind và component shadcn/ui; bỏ stylesheet theo feature và các selector trình bày cũ sau khi chuyển hết nơi sử dụng. Không chuyển CSS cũ nguyên xi thành hàng loạt `@apply`.
- Vẫn có một file `src/styles/globals.css`: import Tailwind, token sáng/tối, mapping theme, base tối thiểu và animation thực sự dùng chung. Tailwind tạo CSS khi build; “thay CSS” ở đây nghĩa là thay CSS component viết tay, không phải xóa mọi file CSS.
- Giữ trải nghiệm TiToAI: navy/tím, Segoe UI local, bố cục desktop, Light/Dark/System, Settings bốn tab và các hành vi dữ liệu đã có. shadcn được chỉnh theo thiết kế này.
- Baseline đọc ngày 2026-10-26: `src/TiToAI.Web/package.json` khai báo React/ReactDOM 19.3.1; Vite 8.3.5, TypeScript 7.0.2. Đây là số trong manifest, **chưa tái xác minh package registry/compatibility** ở lượt lập plan.
- Hai stylesheet hiện có: `src/style.css` và `src/features/prompts/promptStyles.css`. Manifest chưa có Tailwind, shadcn primitives, thư viện form/bảng/state ngoài React. Không giả định thư viện người dùng từng dùng ở dự án khác đã có trong repo này.
- Working tree có nhiều source/docs sửa và file untracked từ công việc khác. M0 ghi HEAD, diff và hash file trước tiếp nhận; không reset, stash hoặc ghi đè công việc của owner khác.

## 2. Bộ thư viện

| Thành phần | Quyết định |
|---|---|
| Styling | Tailwind CSS 4 + `@tailwindcss/vite`; kiểm tra và pin exact phiên bản tương thích ở M0 |
| Component | shadcn/ui, chọn bộ primitive Radix cho các component cần dùng; một cấu hình thống nhất, không trộn nhiều bộ primitive |
| Ghép class/variants | `clsx`, `tailwind-merge`, `class-variance-authority` theo component được sinh và phiên bản Tailwind đã chọn |
| Icon | `lucide-react`; thay dần `components/Icons.tsx`, giữ accessible name ở nút và ẩn icon trang trí khỏi screen reader |
| Animation | Chỉ cài dependency animation mà preset/component đã chọn thực sự cần; hỗ trợ reduced motion |
| React/Vite/TypeScript | Giữ toolchain hiện tại trong migration; nếu compatibility fail, báo riêng trước khi đổi major |
| Form, state, query, table | Giữ logic React/typed bridge hiện có; React Hook Form/Zod, Zustand, TanStack Query/Table không thuộc phạm vi mặc định. Chỉ mở task bổ sung khi có yêu cầu cụ thể và tra Context7 trước dùng |

shadcn đưa source component vào repo, không phải một package UI nguyên khối. CLI là công cụ phát triển: ghi phiên bản CLI, preset, primitive và các component đã sinh. Review diff của CLI trước khi tích hợp; không để init ghi đè theme hiện tại. Không dùng CDN/font remote; asset phải build và đóng gói local.

## 3. Cấu trúc và quy ước

```text
src/TiToAI.Web/
  components.json
  src/
    styles/globals.css
    lib/utils.ts
    components/ui/       # component shadcn được dùng thật
    components/         # shell và thành phần nghiệp vụ dùng chung
    features/           # component nghiệp vụ, class Tailwind tại JSX
```

- Cấu hình alias `@/*` thống nhất trong TypeScript và Vite, giữ `base: "./"` để đóng gói WebView2. `components.json`: client React (`rsc: false`), TSX, CSS variables, trỏ đúng globals.css; Tailwind v4 không dùng đường dẫn config v3.
- `cn()` dùng ghép class. Variant hữu hạn khai báo thành class đầy đủ hoặc CVA; không nội suy tên class kiểu `bg-${status}` khiến build thiếu style.
- Ưu tiên `Button`, `Input`, `Textarea`, `Label`, `Card`, `Badge`, `Separator`; sau đó `Dialog`, `AlertDialog`, `DropdownMenu`, `Select`, `Tabs`, `Tooltip`, `Sheet`, `Table`, `Progress`, `Skeleton`, `Checkbox`, `Switch` khi màn hình có nhu cầu. Chỉ thêm component được dùng.
- Wrapper nghiệp vụ chỉ tạo khi có hành vi/pattern lặp thật. Table shadcn phục vụ trình bày; không thay đổi thuật toán sort, reorder, concurrency hoặc pagination chỉ để migration.
- Chuyển inline style tĩnh sang utility. Giá trị động thực sự như tỷ lệ progress/kích thước runtime có thể truyền qua CSS variable hoặc style có giới hạn; ghi rõ ngoại lệ và kiểm tra đầu vào.
- Kiểm kê selector mà code/test đang dùng cho truy vấn DOM. Chuyển hook hành vi sang ref, role/name hoặc data attribute ổn định trước khi xóa class; không giữ class trình bày chỉ để test còn chạy.

## 4. Theme và giai đoạn chuyển tiếp

ThemeProvider/host hiện tại tiếp tục là nguồn theme duy nhất. Dùng `data-theme="light|dark"` trên root; cấu hình dark variant Tailwind theo attribute đó. Không thêm theme provider độc lập hoặc preference localStorage thứ hai. `System` vẫn được giải quyết theo contract Windows–WPF–React.

| Token hiện tại | Vai trò đích |
|---|---|
| `--bg-app`, `--text-primary` | background / foreground |
| `--surface-card` và màu chữ | card / card-foreground; popover có token phù hợp |
| `--accent`, màu chữ trên accent | primary / primary-foreground |
| `--surface-hover`, `--text-secondary` | accent hoặc muted theo ngữ cảnh; không nhầm accent shadcn với primary tím |
| `--border`, `--focus-ring` | border / input / ring |
| success, warning, error, info | token trạng thái bổ sung; destructive dành cho lỗi/xóa theo ngữ cảnh |
| sidebar và surface riêng | token sidebar và surface ngữ nghĩa bổ sung |

Chọn rõ định dạng giá trị màu và mapping `@theme inline`, không trộn hex với template `hsl(var(...))` không tương thích. Chốt đủ foreground/background cho từng cặp; portaled dialog/menu/tooltip phải thừa hưởng theme từ root.

Tailwind Preflight có thể đổi border, heading, button, form và media trên toàn app. CSS cũ không nằm trong layer có thể ưu tiên hơn utility có layer. M1 phải chốt và kiểm tra thứ tự layer/import, đưa CSS legacy vào layer chuyển tiếp nếu phù hợp, rồi chuyển một lát UI đại diện. Không giải quyết bằng tăng `!important` toàn cục. Chỉ bật cấu hình chuyển tiếp sau khi cả phần mới và phần chưa chuyển vẫn đọc/thao tác được.

## 5. Thứ tự triển khai và ownership

Một owner mỗi file. Owner tích hợp duy nhất ghi `App.tsx`, manifest/lockfile, cấu hình, globals và component dùng chung. Coordinator xác nhận dependencies trong TASKS trước giao việc; không dispatch dựa riêng plan này. Khi phát sinh thay đổi contract/host phải qua coordinator. Không cần sửa DB/backend cho styling.

| Bước | Phụ thuộc | File/phạm vi chính | Điều kiện bàn giao |
|---|---|---|---|
| M0 — tiếp nhận | Dependency/ownership được coordinator xác nhận | Inventory UI, CSS/inline style/DOM selectors, package versions, test runners; lưu baseline dưới docs/reviews | HEAD + hash/diff, màn hình/states hiện có, ảnh hai theme, commands thực tế, package compatibility và WebView2 minimum được xác minh; lỗi có sẵn tách khỏi regression |
| M1 — nền và lát UI mẫu | M0 | package/lock, Vite/TS aliases, components.json, globals.css, utils, primitives tối thiểu; một card/form mẫu trong màn hình thật | Tailwind build local; Preflight/layer không phá phần legacy; theme/portal/focus đúng; preset và exact pins được ghi |
| M2 — shell và controls chung | M1 | App, sidebar/topbar/Workspace selector, components/ui, Icons; CSS shell tương ứng | Navigation, responsive, modal layering, tab order và theme startup giữ hành vi; icon buttons có tên |
| M3 — Workspace và project/scene | M2 | features/workspaces, features/projects | CRUD, validation, unsaved draft, conflict/revision, reorder bàn phím, media preview và bảng cuộn nội bộ vẫn hoạt động |
| M4 — Settings, account/browser và queue | M3 | features/settings, features/browsers, QueueDrawer và consumer còn dùng SettingsModal | Settings bốn tab có arrow navigation; account flows đúng chỗ; dialog focus return/Escape; queue progress thật; draft không mất khi vào/ra Settings |
| M5 — Prompt và các UI còn lại | M4 | features/prompts, consumer library/overview hiện có, CSS/inline style còn lại | Template/version/override, preview stale, Apply, biến JSON, workspace switch/ở lại và draft được bảo toàn; promptStyles.css không còn consumer |
| M6 — dọn legacy và nghiệm thu | M5 | Xóa stylesheet/import/selector cũ sau inventory; cập nhật docs liên quan | Không còn CSS trình bày theo feature hoặc DOM hook phụ thuộc class cũ; mọi UI thực tế đã kiểm cả hai theme; bundle local và kiểm WebView2 đạt trong phạm vi ghi evidence |

Migrate theo từng bước có thể review và commit riêng; build chạy được sau mỗi bước. Không xóa toàn bộ CSS ngay đầu. Trang chưa triển khai được ghi N/A; không dựng Library/analytics mới chỉ để hoàn thành migration. Với component legacy, xác minh consumer trước xóa; chuyển mọi consumer còn dùng hoặc loại bỏ code thật sự không còn được tham chiếu.

## 6. Kiểm tra bắt buộc khi triển khai

- M0 tìm đúng runner hiện có; `package.json` hiện chưa có script test. Ghi lệnh chạy thật trước nhận việc, không suy ra `pnpm test` sẽ có. Lượt lập plan này không chạy build/test.
- Sau mỗi bước: frontend typecheck/build qua script hiện có (`pnpm build` tại `src/TiToAI.Web`) và test tương tác liên quan. Portals/focus/pointer cần browser/WebView2 thật khi happy-dom không thể chứng minh.
- Chạy lại regression Prompt và Settings hiện có; cập nhật selector theo role/name/hook ổn định. Bổ sung test cho hành vi thay đổi bởi primitive mới: focus trap/return, Escape, keyboard menu/select/tabs và lớp overlay. Không thay assertion nghiệp vụ bằng snapshot class.
- Kiểm Light/Dark/System, startup/restart, Windows theme change, lỗi đọc/lưu preference và chuyển Workspace; không chớp nền sai hoặc mất draft. Host theme logic không được thay chỉ để khớp shadcn.
- Kiểm app shell, các form, bảng cảnh, Prompt, Settings/account và queue ở viewport nhỏ nhất đã hỗ trợ, màn hình rộng, DPI 100/125/150/200%; bàn phím và reduced motion. Ghi kích thước/DPI/môi trường cụ thể, không chỉ ghi “responsive PASS”.
- Kiểm bản build production trong WPF/WebView2, asset/font local, menu/dialog không bị clip, scroll/focus đúng; xác minh runtime WebView2 hỗ trợ CSS mà Tailwind v4 và component được chọn cần. Browser provider/đăng nhập chỉ kiểm khi thay đổi thực sự ảnh hưởng, dùng profile test được cấp.
- Evidence phải gắn revision/hash, bước/lệnh, kết quả và giới hạn. Thiếu Windows/runtime/account thì ghi NOT_RUN; không nhận M6 hoàn tất nếu check bắt buộc còn thiếu. Lỗi baseline được ghi riêng và coordinator quyết định gate, không gọi lỗi cũ là PASS.

## 7. Hoàn tất và phục hồi

M6 chỉ đạt khi toàn bộ UI React hiện có đã chuyển, `style.css`/`promptStyles.css` và import cũ được bỏ, CSS nền chỉ còn nhiệm vụ đã nêu và ngoại lệ có lý do. CSS WPF/XAML hoặc style của website provider ngoài app không thuộc migration React.

Bàn giao gồm exact dependency versions, component inventory, file thay đổi, check PASS/FAIL/NOT_RUN và ảnh so sánh. Cập nhật liên kết ở Theme/UI và các plan tính năng bị ảnh hưởng; chỉ coordinator cập nhật STATUS/TASKS, không sửa evidence lịch sử thành nghiệm thu migration.

Nếu một bước gây regression, sửa hoặc revert đúng commit của bước đó sau đối chiếu owner; không reset working tree chung. Vì migration UI không đổi schema, không tạo DB migration/rollback. Giữ release trước cho tới khi bản WebView2 mới đạt gate.

## 8. Nguồn kỹ thuật

Tra qua Context7 ngày 2026-10-26; xác minh lại khi chọn exact versions ở M0:

- [Tailwind với Vite](https://tailwindcss.com/docs/installation/using-vite), [Preflight/layers](https://tailwindcss.com/docs/preflight), [dark mode bằng data attribute](https://tailwindcss.com/docs/dark-mode), [theme variables](https://tailwindcss.com/docs/theme).
- [shadcn trong Vite](https://ui.shadcn.com/docs/installation/vite), [components.json](https://ui.shadcn.com/docs/components-json), [theming](https://ui.shadcn.com/docs/theming).
