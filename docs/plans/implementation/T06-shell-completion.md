# Hoàn tất React shell và navigation nền

> **Delta hiện hành:** [plan queue/Settings 1.2](../QUEUE_EXECUTION_SETTINGS_PLAN.md) thay popup Settings, nhóm account shared/private, connect tại Workspace và round-robin Workspace. Dùng [TASKS](../../agents/TASKS.md) cho trạng thái/gate; Baseline/checkbox dưới không là evidence đã chạy.

- Task gốc: **T06** · Ưu tiên: **P0 / G1**.
- Điều kiện mở: T03 DONE; phối hợp freeze với T05.
- Trạng thái hiện tại: **DONE** theo addendum trong [review T06](../../reviews/T06-coordinator-review.md).
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Hiện trạng và đích đến

App.tsx có sidebar/topbar/Workspace selector, SettingsModal và QueueDrawer. Findings code đã sửa; frontend/Desktop builds và asset hashes PASS. Keyboard modal/drawer, tab navigation, Escape và focus return được chạy trên WPF/WebView2 thật. Full DPI matrix và đọc DOM attributes là release qualification T30, không phải blocker của việc hoàn tất shell.

## File và contract

- Sửa: `src/TiToAI.Web/src/{App.tsx,style.css,services/bridgeClient.ts,contracts/bridge.ts,contracts/dtos.ts,components/SettingsPage.tsx (đích Q2, kiểm tra file hiện có trước tạo),components/QueueDrawer.tsx}`.
- Spec: UI_BROWSER §2,§4 và ảnh references/ui-reference.png; Theme §2. Không triển khai CRUD hoặc queue trong shell.
- G1 cần workspace.list đọc DB thật: integrator nối projection tối thiểu tại Desktop, không đợi toàn bộ CRUD T07. DB rỗng mới trả danh sách rỗng.

## Các bước triển khai

- [ ] Đối chiếu bốn findings trong T06-coordinator-review với source sau FIX R1, chạy lại frontend build.
- [ ] Không có host thì bridge trả BRIDGE_UNAVAILABLE; phân biệt loading, tải lỗi, rỗng thật và chưa hỗ trợ tính năng.
- [ ] Hoàn thiện navigation có context Workspace/project/scene; route sâu sẽ được thêm theo T11/T12, shell không tạo dữ liệu mẫu.
- [ ] Scope response theo context tại thời điểm gửi; khi đổi Workspace, response cũ không được thay dữ liệu mới, request app-global vẫn nhận đúng.
- [ ] Modal/drawer chỉ cho focus trong lớp đang mở, lọc control disabled; Escape/đóng trả focus về trigger, nền không nhận thao tác.
- [ ] Thể hiện CTA chưa khả dụng đúng trạng thái; bỏ mã task trong giao diện và số liệu chưa tải.
- [ ] Freeze Web files và bàn giao cho T05 integrated build; lưu ảnh hai theme để so reference.

## Kiểm thử và nghiệm thu

T06 kiểm tra mất host, timeout/retry, đổi Workspace nhanh, event sai scope, keyboard/focus trap, và responsive shell ở viewport hiện có; chạy frontend build. Full C1–C6 DPI matrix, 13 ảnh và đọc trực tiếp `inert`/`aria-hidden` được giữ cho T30 trên Windows thật. Không giả lập browser scale để thay bằng chứng DPI.

T06 hoàn tất khi findings đã đóng bằng evidence và integrated shell/keyboard hoạt động. Full DPI/DOM release evidence vẫn bắt buộc trước T30 PASS. Persist theme thuộc [T06a](T06a-theme-persistence.md).

