# Backup và restore DB cùng tài nguyên trên nhiều root

- Task gốc: **T26** · Ưu tiên: **P0 / G4**.
- Điều kiện mở: T09,T16 DONE.
- Baseline 2026-10-26: Chưa có backup/restore service hoặc manifest.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Hành vi cần đạt

Backup nhất quán SQLite WAL + documents/media/audio và mapping mọi root còn tham chiếu. Restore vào roots đích người dùng chọn, remap an toàn, không ghi mù đường dẫn nguồn. Không bao gồm browser session mặc định.

## File và contract

- Tạo đề xuất: `src/TiToAI.Infrastructure/Backup/{BackupService.cs,RestoreService.cs,BackupManifest.cs,RestoreJournal.cs}`, `src/TiToAI.Web/src/features/backup/BackupRestorePanel.tsx`.
- Manifest đề xuất: formatVersion/app/schemaVersion/timestamp/completeness, DB hash, roots, files(rootId/relativePath/size/hash), missingEntries.
- Coordinator chốt commands/progress/maintenance UX; integrator nối barrier với scheduler, metadata writers và AssetFileStore, không chỉ dừng dispatch.

## Các bước triển khai

- [ ] Maintenance gate ngăn metadata mutation/publish asset trong cửa sổ snapshot; quiesce đang finalize, downloads mới chỉ vào staging, không giữ SQL transaction khi copy media.
- [ ] Dùng SQLite backup API trên connection riêng; kiểm checksum/quick_check/FK và schema version của bản backup.
- [ ] Copy mọi file được DB snapshot tham chiếu, gồm root cũ/ngoài LocalAppData và audio; publish manifest chỉ sau xác thực, thiếu root đánh dấu incomplete rõ ràng.
- [ ] Restore preflight kiểm phiên bản, hash, đường dẫn archive/traversal/junction, dung lượng và root mapping đích; chưa ghi vào dữ liệu đang dùng.
- [ ] Khóa writers/dispatch, đóng connections, tạo safety backup; stage DB/files, ghi journal rồi promote theo từng bước có thể phục hồi.
- [ ] Multi-root khác ổ không có atomic rename toàn cục: journal cho phép rollback/restart sau crash; không báo atomic toàn bộ filesystem.
- [ ] Remap rootId tới đích đã xác nhận, đối soát file/schema/quick_check trước mở scheduler; jobs uncertain vẫn reconcile, không auto-submit.
- [ ] Backup metadata ProviderAccount/pool capacity/ExecutionBinding/slot/checkpoint global; không nhân bản account theo Workspace khi restore. Browser session không nằm trong backup mặc định nên restore account thiếu phiên giữ Unverified/login_required, không fake Connected hoặc dispatch lại request uncertain.
- [ ] UI xác nhận trước thay dữ liệu hiện tại, báo completeness và lỗi; không xóa dữ liệu gốc khi staging thất bại.

## Kiểm thử và nghiệm thu

WAL đang ghi, root chung/tách/cũ/offline, file đang tải, manifest sửa/hash sai, archive path escape, disk full, restore giữa chừng crash, DB connections còn mở. Restore sang roots tạm khác đường dẫn nguồn; đối chiếu đủ project/scene/asset/hash.

Context7 có tài liệu SQLite C API nhưng thiếu gói Microsoft.Data.Sqlite chính xác, nên đối chiếu [Microsoft Learn Backup](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/backup) ngày 2026-10-26: BackupDatabase hỗ trợ DB live và có thể chặn writer. Chạy ngoài UI thread; không hứa wrapper .NET là incremental/nonblocking.

