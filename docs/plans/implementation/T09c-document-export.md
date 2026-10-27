# Xuất kịch bản và prompt thành tài liệu

- Task gốc: **T09 (tài liệu)** · Ưu tiên: **P0 / G2**.
- Điều kiện mở: T08 DONE; storage T09a và file store T09b sẵn sàng.
- Baseline 2026-10-26: Có AssetType Document; chưa có document.export handler.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Hành vi cần đạt

Người dùng chủ động xuất nội dung scene ra TXT UTF-8 trong root Tài liệu; trả metadata Asset Document. Nội dung editor/template vẫn ở DB, không xuất file ở mỗi lần gõ.

Phần bổ sung [SOURCE §7.4](../SOURCE_PLAN.md#74-tạo-ý-tưởngprompt-kèm-nội-dung-đăng-tiktok--p0) cho xuất bài đăng TikTok đã lưu cấp project, gồm caption và hashtag. Coordinator chốt delta DTO nhận project và loại nội dung; contract scene-only dưới đây là baseline, không tạo scene giả để xuất bài đăng. Dùng cùng file store/root/ownership; nghiệm thu copy và TXT giữ đúng tiếng Việt, xuống dòng, hashtag, không lẫn prompt AI.

## File và contract

- Tạo đề xuất: `src/TiToAI.Application/Documents/DocumentExportService.cs`, `src/TiToAI.Desktop/Bridge/Handlers/DocumentHandlers.cs`.
- Dùng AssetFileStore, StorageRootRegistry và Asset; không tự tạo pipeline ghi file thứ hai.
- document.export theo contracts §1.6 E: workspaceId,sceneId,format,customFileName. MVP chỉ hứa txt; format khác trả lỗi không hỗ trợ.
- Path documents/{assetId}.txt dưới Workspace/project theo root chốt lúc bắt đầu.

## Các bước triển khai

- [ ] Đọc scene đúng ownership và chốt nội dung đã lưu để xuất; quy tắc xuất prompt/kịch bản và displayName chốt trong DTO.
- [ ] Capture documentsRootId tại đầu thao tác; thay Settings giữa lúc xuất không đổi đích.
- [ ] Chuẩn hóa tên hiển thị; dùng assetId cho tên vật lý, xử lý ký tự Windows/reserved names và traversal.
- [ ] Ghi UTF-8 qua staging/hash/finalize, lưu Asset Document; không báo thành công khi file hoặc DB lỗi.
- [ ] Trả rootId/relativePath/displayName/byteSize; mở thư mục qua command an toàn, không tùy ý shell command.
- [ ] Nối nút Xuất ở editor/library sau T12, thiếu nội dung hoặc root có hướng dẫn rõ.

## Kiểm thử và nghiệm thu

Đối chiếu dấu tiếng Việt/newline trong tệp thật; cùng tên xuất hai lần không ghi đè; sai scope, format không hỗ trợ, root đổi giữa thao tác, hủy/lỗi quyền/disk full. Restart và library vẫn mở tài liệu ở root cũ.

Chỉ hoàn tất T09 khi lưu trữ, media và xuất tài liệu đều có kiểm tra thực tế; thêm DOCX/PDF là scope riêng khi người dùng yêu cầu.

