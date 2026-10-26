# Thư viện tài nguyên và preview có kiểm scope

- Task gốc: **T12 (library)** · Ưu tiên: **P0 / G2**.
- Điều kiện mở: T09,T10 DONE.
- Baseline 2026-10-26: Có route library khung, chưa có list/filter/preview/import UI.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Hành vi cần đạt

Xem ảnh/video/audio/tài liệu theo Workspace/project, lọc loại, import và chọn input cho scene. Hiện root/file thiếu, nguồn manual/AI và linked/unlinked; dữ liệu lịch sử vẫn mở sau đổi root.

## File và contract

- Tạo đề xuất: `src/TiToAI.Web/src/features/library/{LibraryPage.tsx,AssetPreviewDialog.tsx,AssetPicker.tsx}`, `src/TiToAI.Application/Assets/AssetLibraryQuery.cs`.
- Dùng T09b file store, không trả file:// arbitrary path vào WebView2.
- Catalog [v1.1 §5.1](../../decisions/execution-contract-v1.1.md): asset.list/import_media/get_preview/release_preview và scene.set_inputs, review T02a trước tích hợp. Paging 1–100, preview token 5 phút có scope/session và revoke; không map toàn root. Bỏ chọn chỉ detach, chưa có hard-delete.

## Các bước triển khai

- [ ] List metadata thật có paging; scope Workspace/project kiểm tại host cả list/get/preview/export/open_folder.
- [ ] Preview ảnh/video/audio theo resource ID có session/scope guard, hạn chế truy cập toàn folder bằng virtual mapping.
- [ ] Import file và dialog hủy đúng T09b; hiển thị progress I/O thật nếu có, không giả phần trăm.
- [ ] AssetPicker chỉ cho chọn loại phù hợp, kiểm project/kênh theo contract; bỏ chọn không hard-delete file.
- [ ] Hiển thị file thiếu/mất root và Retry sau kết nối ổ; không xóa metadata hay tự tái tạo AI.
- [ ] Đổi root/Workspace/theme không làm lẫn preview hoặc selection; revoke resource/object URL khi rời view.
- [ ] Nếu UI cần xóa vật lý, tách proposal có reference guard và xác nhận; MVP ưu tiên detach/không xóa file ngầm.

## Kiểm thử và nghiệm thu

Import thật đủ bốn loại, file giả MIME, assets Unlinked, root cũ offline, đổi root rồi restart. URL preview đoán assetId kênh khác phải bị từ chối. Chọn ảnh/audio trả đúng scene; preview đóng trả focus.

Thư viện không lưu blob media trong SQLite và không mở browser provider để xem kết quả. T24 kiểm workflow sinh→library sau adapter.

