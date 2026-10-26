# Quản lý cảnh và thứ tự cảnh

- Task gốc: **T08 (phần scene)** · Ưu tiên: **P0 / G2**.
- Điều kiện mở: T07 DONE; model/service project T08a đã chốt.
- Baseline 2026-10-26: Có Scene, khóa asset và thứ tự; chưa có CRUD/reorder handlers.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Hành vi cần đạt

Thêm/sửa/get/list/xóa/reorder scene trong project, lưu prompt nháp và input media đúng kênh. Thời lượng mục tiêu tách khỏi thời lượng media thực tế. Không tự sửa snapshot job khi sửa scene.

## File và contract

- Tạo đề xuất: `src/TiToAI.Application/Scenes/SceneService.cs`, `src/TiToAI.Desktop/Bridge/Handlers/SceneHandlers.cs`, `src/TiToAI.Contracts/Scenes/SceneDtos.cs`.
- Dùng Scene.cs/SceneConfiguration.cs. Nếu cần description hoặc prompt theo mục đích, coordinator bổ sung schema, không nhồi mọi thứ vào PromptText không có quy ước.
- Chốt scene.* DTO, thứ tự zero/one-based, concurrency revision và response reorder trước T11c/T12a.

## Các bước triển khai

- [ ] Xác thực chuỗi workspaceId→projectId→sceneId ở mọi lệnh, kể cả asset input và delete.
- [ ] Validate tên, duration dương và prompt; chưa có media vẫn cho lưu Draft.
- [ ] Reorder nhận đúng tập scene của project, chặn ID trùng/thiếu/khác project; đổi thứ tự trong transaction, tránh unique collision bằng thứ tự cập nhật phù hợp.
- [ ] Update input dùng asset đã kiểm scope/type; hỗ trợ audio project-level chỉ sau khi schema/precedence được chốt, không giả đã có.
- [ ] Delete theo guard/transaction chung; scene bị xóa không làm mất file input dùng lại bởi scene khác.
- [ ] Trả version/conflict để editor không ghi đè thay đổi mới hơn; snapshot job vẫn bất biến.

## Kiểm thử và nghiệm thu

Test SQLite: reorder 1/3/100 scene, reorder đồng thời, duplicate/out-of-scope IDs, rollback giữa chừng; delete có active job; gán Image/Audio/Video sai loại hoặc kênh khác; target duration không bằng audio duration vẫn lưu được khi hợp lệ.

T08 đóng sau cả CRUD project và scene, kèm bằng chứng active-job guard thật. Batch enqueue và render thuộc T15/T22.

