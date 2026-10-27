# Tổng quan Workspace và dữ liệu tiến độ

> **Delta hiện hành:** [plan queue/Settings 1.2](../QUEUE_EXECUTION_SETTINGS_PLAN.md) thay popup Settings, nhóm account shared/private, connect tại Workspace và round-robin Workspace. Dùng [TASKS](../../agents/TASKS.md) cho trạng thái/gate; Baseline/checkbox dưới không là evidence đã chạy.

- Task gốc: **T11 (Overview)** · Ưu tiên: **P0 / G2**.
- Điều kiện mở: T07,T08 DONE; KPI jobs nối T15–T17 khi hoàn tất.
- Baseline 2026-10-26: Route overview mới là khung; chưa có read model tổng quan.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Màn hình và dữ liệu

Theo panel 02: header kênh, KPI P0 dự án/cảnh/job chạy/job cần người dùng, dự án gần đây và các bước pipeline. TikTok views/likes/affiliate thuộc P1 T25; chưa có nguồn thì “Chưa có dữ liệu”.

## File và contract

- Tạo đề xuất: `src/TiToAI.Web/src/features/workspaces/WorkspaceOverviewPage.tsx`, `src/TiToAI.Application/Workspaces/WorkspaceOverviewQuery.cs`.
- Coordinator chốt read DTO tổng hợp hoặc composition từ các list hiện có, paging và continuation target; không tự tạo command overview ngoài allow-list.
- App.tsx/DTO/handlers chung do integrator nối; T23 bổ sung workflow actions vào panel sẵn có, tránh tạo overview thứ hai.

## Các bước triển khai

- [ ] Đọc thông tin kênh và projects/scenes thật; counts chưa có job source hiển thị chưa khả dụng, không số 0 giả.
- [ ] Hiển thị dự án gần đây và “Tiếp tục” vào đúng project/scene/bước đã lưu.
- [ ] Progress có mẫu số đủ điều kiện và dữ liệu hoàn thành; chưa biết phần trăm hiển thị bước, không timer.
- [ ] Sau T17 nối snapshot job counts + event invalidation đúng Workspace, reload khi mất event/gap.
- [ ] Pipeline khung hiển thị kịch bản→ảnh→prompt→audio input→video→library; action thực thi do T23 nối.
- [ ] Giữ context khi mở Settings hoặc đổi theme, ngăn stale result/event A ghi sang B.

## Kiểm thử và nghiệm thu

Dataset ba kênh có số projects/scenes khác nhau; KPI khớp query DB, dữ liệu thiếu không thành 0. “Tiếp tục” đúng scene dù đang đứng kênh khác; empty/error, backend timeout và reconnect hoạt động. Test keyboard và cả hai theme theo panel 02.

Phần không có source trước scheduler phải ghi rõ chưa nối; nghiệm thu pipeline hoàn chỉnh ở T23/T24, tránh mở dependency giả hoặc làm sớm adapter.

