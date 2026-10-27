# TiToAI — mục lục triển khai

Ngày 2026-10-26 (UTC+07). Đọc theo thứ tự: [STATUS](../../agents/STATUS.md) → [TASKS](../../agents/TASKS.md) → [plan queue/Settings 1.2](../QUEUE_EXECUTION_SETTINGS_PLAN.md) → plan tính năng cần sửa. [Review bàn giao](../../reviews/2026-10-26-handoff-recheck.md) giữ H1–H6.

## Phạm vi hiện hành

Chat Codex chỉ review/docs và giữ code đang có. AI khác tiếp nhận Q0 trước triển khai. Q0–Q4 bao phủ delta queue độc lập và trang Cài đặt; task ID cũ dưới đây giữ phạm vi sản phẩm, không là dispatch hoặc chứng nhận DONE. STATUS/TASKS là nguồn trạng thái, không lấy dòng Baseline trong plan con làm inventory hiện tại.

- Trang Cài đặt có tab ngang Tài khoản/Lưu trữ/AI & Prompt/Giao diện; mọi quản lý tài khoản ở đó. Không có popup Settings hoặc trang account/browser riêng trên sidebar.
- Một queue SQLite chung, chọn job đủ điều kiện theo FIFO rồi account theo provider/capacity; không queue/account affinity theo Workspace, không nhóm account AI dùng chung/riêng. Workspace giữ dữ liệu nguồn/đích.
- Giữ stack và module có sẵn; CodeGraph trước locate/read source, Context7 cho API thư viện. Không scaffold lại phần đã có hoặc thêm broker/service chỉ để tách luồng logic.
- Một owner mỗi file; contract/DTO/migration/host/router qua coordinator. Antigravity xác minh Gemini 3.8 Flash / High từng phiên theo ORCHESTRATION.
- Test cùng revision/hash, SQLite fixtures thật, component interaction thật; fake provider không chứng minh website/identity/hidden. Thiếu evidence ghi NOT_RUN. Không ký binary/thay policy để vượt chặn.

## Nguồn thiết kế

Delta ngày 2026-10-27: [AI web tạo nội dung từ brief hoặc analytics](../AI_WEB_CONTENT_CREATION_PLAN.md), kèm [bộ prompt](../AI_WEB_CONTENT_CREATION_PROMPTS.md). Áp dụng cho T10/T12a/T18/T20/T22/T23/T25: hướng dẫn AI tự hoàn thiện chi tiết, không bắt người dùng điền biến kỹ thuật; analytics giữ P1. Không dùng DONE lịch sử để nghiệm thu delta này.

Yêu cầu migration UI mới: [Tailwind + shadcn/ui](../TAILWIND_SHADCN_MIGRATION_PLAN.md). M0–M6 là kế hoạch đề xuất, chưa dispatch hoặc nghiệm thu; áp dụng khi chuyển styling React, giữ các contract theme/Settings/nghiệp vụ hiện hành.

Yêu cầu P0 mới: [SOURCE §7.4 — nội dung đăng TikTok](../SOURCE_PLAN.md#74-tạo-ý-tưởngprompt-kèm-nội-dung-đăng-tiktok--p0). Tạo ý tưởng/kịch bản hoặc prompt bằng AI phải kèm bài đăng để sửa/copy/xuất. T09c/T10/T12a/T18c/T20/T22/T23 áp dụng yêu cầu này; coordinator chốt delta schema/DTO trước code phụ thuộc, không dùng nghiệm thu baseline để đóng phần bổ sung.

[Plan 1.2](../QUEUE_EXECUTION_SETTINGS_PLAN.md) có ưu tiên cho delta queue/account/Settings. [Settings](../SETTINGS_PLAN.md), [Theme](../UI_THEME_PLAN.md), [UI/Browser](../UI_BROWSER_PLAN.md) giữ chi tiết UI/storage/theme. [Contract v1.1](../../decisions/execution-contract-v1.1.md) giữ baseline kỹ thuật, đã thay một phần; Q0 chốt wire/schema delta. [SOURCE](../SOURCE_PLAN.md) là nguồn sản phẩm lịch sử, dùng tên TiToAI và ba roots hiện hành.

## Mục lục: mỗi tính năng một file

| Plan | Task gốc được bao phủ | Ưu tiên |
|---|---|---|
| [Tích hợp Desktop host và WebView2 bridge](T05-host-integration.md) | T05 | P0 |
| [Hoàn tất React shell và navigation nền](T06-shell-completion.md) | T06 | P0 |
| [Lưu theme và đồng bộ Windows–WPF–React](T06a-theme-persistence.md) | T06a | P0 |
| [Quản lý Workspace và tạm dừng kênh](T07a-workspace-backend.md) | T07 (phần Workspace) | P0 |
| [Tài khoản và định danh kênh](T07b-account-linking.md) | T07 (phần account) | P0 |
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
| [Tài khoản/profile executor và nâng cấp legacy](T13a-provider-profile-isolation.md) | T13a bổ sung sau T02a | P0 |
| [Màn hình quản lý browser và trạng thái đăng nhập](T14-browser-manager-ui.md) | T14 | P0 |
| [Queue bền vững và reservation theo capacity](T15-durable-job-queue.md) | T15 | P0 |
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
| [Tích hợp trang Cài đặt và kiểm tra lưu đồng thời](T27a-settings-integration.md) | T27 (Settings integration) | P0 |
| [Log có correlation, bảo vệ bí mật và cảnh báo dung lượng](T27b-logging-secrets.md) | T27 (logging/secrets) | P0 |
| [Nâng schema và phục hồi migration thất bại](T28-migration-upgrade-recovery.md) | T28 | P0 |
| [Đóng gói installer Windows và chiến lược dependency](T29-windows-installer.md) | T29 | P0 |
| [Nghiệm thu phát hành: dữ liệu, tải, browser, UI và máy sạch](T30-release-qualification.md) | T30 | P0 |
| [Đánh giá mở rộng server hoặc nhiều máy khi có nhu cầu](T31-server-scale-p2.md) | T31 | P2 |

P0 gồm cả phần hoàn tất nền tảng và nghiệm thu phát hành; T25 là P1, T31 P2 chưa mở. T00–T04 đã DONE nên không viết lại plan implementation cho phần đã nghiệm thu.


Các checkbox là acceptance dự kiến, không phải log thực thi. Lịch sử wave/model/source hash ở [STATUS-HISTORY](../../agents/STATUS-HISTORY-2026-10-26.md); không nạp lịch sử cho mọi task mới.
