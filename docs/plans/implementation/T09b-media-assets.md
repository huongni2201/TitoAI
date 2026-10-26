# Nhập và lưu ảnh, audio gốc, video

- Task gốc: **T09 (media)** · Ưu tiên: **P0 / G2**.
- Điều kiện mở: T08 DONE; root registry T09a đã tích hợp.
- Baseline 2026-10-26: Có Asset metadata và FK scene; chưa có import/download/finalize service.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Hành vi cần đạt

Import ảnh/audio/video vào library đúng Workspace; chọn ảnh tham chiếu và audio cho scene. Ghi file qua staging, xác thực rồi finalize; thành công chỉ khi metadata/scene đúng. MP3/WAV giữ nguyên byte, không có xử lý audio.

## File và contract

- Tạo đề xuất: `src/TiToAI.Infrastructure/Storage/{AssetFileStore.cs,AssetReconciler.cs}`, `src/TiToAI.Application/Assets/AssetService.cs`, `src/TiToAI.Desktop/Bridge/Handlers/AssetHandlers.cs`.
- Dùng Asset/Scene và storage roots. Test tại `tests/TiToAI.Infrastructure.SmokeChecks/AssetStorageChecks.cs` (đề xuất).
- T09 baseline đã DONE theo STATUS; giữ asset.import_audio/get_metadata/open_folder. Catalog import_media/list/get_preview/release_preview/scene.set_inputs tại [v1.1 §5.1](../../decisions/execution-contract-v1.1.md), review T02a trước UI library/schema mới; không tạo file API tùy ý.

## Các bước triển khai

- [ ] Kiểm scope project/scene và file host picker; hủy import trả asset:null không thay selection cũ.
- [ ] Kiểm nội dung/metadata thực tế, extension/MIME và app limit v1.1 §5.1, stream media lớn thay vì bridge blob. Capability provider có thể thấp hơn app limit; tên ID an toàn, không tin tên nguồn.
- [ ] Copy streaming vào staging cùng root khi có thể, hash/byte size; atomic rename rồi commit Asset và liên kết Scene trong transaction ngắn.
- [ ] Nếu DB commit lỗi sau rename, ghi nhận file mồ côi để đối soát; không xóa file đã có trước thao tác.
- [ ] Audio vào root-internal-audio; ảnh/video dùng root snapshot của thao tác/job; lỗi root giữ result reference/staging để thử lưu lại.
- [ ] Tạo boundary preview/download local theo assetId và scope; frontend không nhận đường đọc arbitrary hoặc quyền vào profile.
- [ ] Reconcile phát hiện temp, file thiếu và metadata mồ côi; đưa lựa chọn khắc phục, không tự gọi AI.

## Kiểm thử và nghiệm thu

Test byte-identical MP3/WAV, file giả đuôi, file lớn bằng streaming, cancel giữa copy, crash trước/sau rename, DB fail, disk full/mất root và race junction. Import/select từ kênh khác bị chặn cả metadata/preview/open_folder.

Hash nguồn và bản audio lưu phải bằng nhau; job không succeeded trước file+metadata+Scene commit. Không gửi lại AI khi chỉ lỗi lưu đĩa.

