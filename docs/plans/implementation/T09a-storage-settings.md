# Cài đặt ba thư mục lưu trữ và lịch sử root

> **Delta hiện hành:** [plan queue/Settings 1.2](../QUEUE_EXECUTION_SETTINGS_PLAN.md) thay popup Settings, nhóm account shared/private, connect tại Workspace và round-robin Workspace. Dùng [TASKS](../../agents/TASKS.md) cho trạng thái/gate; Baseline/checkbox dưới không là evidence đã chạy.

- Task gốc: **T09 (Lưu trữ)** · Ưu tiên: **P0 / G2**.
- Điều kiện mở: T08 DONE.
- Baseline 2026-10-26: AppSetting và Asset.StorageRootId đã có; popup mới là placeholder.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Hành vi cần đạt

Chọn độc lập root tài liệu/ảnh/video, lưu bền vững và restart khôi phục. Root mới áp dụng cho thao tác/job mới; asset/job cũ giữ root cũ. Audio luôn ở root-internal-audio.

## File và dữ liệu

- Tạo đề xuất: `src/TiToAI.Infrastructure/Storage/{StorageRootRegistry.cs,StoragePathGuard.cs}`, `src/TiToAI.Infrastructure/Settings/SettingsStore.cs`, `src/TiToAI.Desktop/Bridge/Handlers/SettingsHandlers.cs`.
- UI mới: `src/TiToAI.Web/src/features/settings/StorageSettingsPanel.tsx`; sửa SettingsPage.tsx (đích Q2, kiểm tra file hiện có trước tạo) qua UI owner.
- Dùng app_settings keys tại contracts §5; mapping absolute path chỉ host quản lý, SettingsDto được phép hiển thị ba selected paths, không trả profile/secrets.
- Commands settings.get/save/browse_folder/validate_roots. SettingsStore dùng chung với T18b/T27a, một handler save cho toàn draft.

## Các bước triển khai

- [ ] Seed roots mặc định khi thật sự chưa cấu hình; lỗi đọc cấu hình có sẵn không được ghi đè mặc định.
- [ ] Windows folder picker trả null khi hủy; chuẩn hóa đường dẫn, kiểm quyền tạo/ghi và kiểm đích thực sau junction/symlink.
- [ ] Tái sử dụng rootId khi cùng path không phân biệt hoa thường; path mới tạo mapping mới, giữ mapping còn được tham chiếu.
- [ ] Validate cả ba paths trước DB transaction; save atomic toàn draft, lỗi trả fieldErrors và giữ bản nháp.
- [ ] Ở G2, provider fields chưa triển khai vẫn giữ null/giá trị hợp lệ hiện có; non-null chưa được registry kiểm chứng phải bị từ chối rõ, không báo đã chọn được.
- [ ] UI đọc SettingsDto thật, Lưu/Hủy/đóng đúng spec; ghi chú đổi root không di chuyển file cũ.
- [ ] Cung cấp resolve root theo ID cho assets/queue/backup; không dùng “root hiện hành” để mở asset cũ.

## Kiểm thử và nghiệm thu

Dùng roots tạm: ba ổ/thư mục khác nhau, chung root, path khác chữ hoa thường, restart, thiếu quyền, ổ mất, disk full, traversal và junction thoát root. Một field sai làm cả save rollback; Hủy không đổi root.

Spec SETTINGS §1–3,§5–6 và contracts §5. Test đổi settings khi có queued/running/awaiting_user hoàn tất lại ở T15/T30; T09 chứng minh root history bằng asset fixtures.

