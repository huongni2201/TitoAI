# Editor cảnh và quản lý prompt theo mục đích

- Task gốc: **T12 (editor)** · Ưu tiên: **P0 / G2**.
- Điều kiện mở baseline: T09,T10 DONE, route từ T11c; DTO/schema mở rộng v1.1 cần T02a review trước tích hợp.
- Baseline 2026-10-26: Chưa có editor cảnh/template hoạt động.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Màn hình và hành vi

Theo panel 04: cột trái thông tin cảnh/prompt, cột phải ảnh/audio/preview; hẹp thì xếp dọc. Chỉnh prompt Image và VideoPrompt rõ mục đích; template/version/biến/preview/rollback có thể thao tác từ UI.

## File và contract

- Tạo đề xuất: `src/TiToAI.Web/src/features/scenes/{SceneEditorPage.tsx,PromptEditor.tsx,PromptTemplatePanel.tsx,SceneMediaPanel.tsx}`.
- Scene inputs theo [v1.1 §4/§5.1](../../decisions/execution-contract-v1.1.md): imagePromptText/videoPromptText riêng, scene.set_inputs có expectedRevision; omitted giữ nguyên, null bỏ chọn. Legacy prompt không rõ purpose giữ provenance, không tự sao chép sang hai field; audio MVP scene-level, không project fallback.
- Nút tối ưu/thử prompt là job sau T18/T20/T22; chưa có capability thì disabled có lý do, không local giả kết quả.

## Các bước triển khai

- [ ] Load scene đúng context, phân biệt target duration và duration media; lưu nháp có trạng thái unsaved/saving/saved/error.
- [ ] Cho chọn/edit/version template, override Workspace, nhập biến, render preview và rollback qua backend T10.
- [ ] Kết quả preview/tối ưu vào bản xem trước, user chọn Áp dụng; không ghi đè nội dung đang gõ.
- [ ] Chọn ảnh từ library, import/nghe thử/thay/bỏ audio MP3/WAV; clear selection chỉ đổi liên kết, không xóa file gốc.
- [ ] Hiển thị provider hiệu lực từ Settings theo tác vụ, có lối mở AI & Prompt; không lưu dropdown provider riêng trong scene.
- [ ] Trước/sau scene hoặc rời trang có dirty guard; đổi theme/Settings giữ nháp.
- [ ] “Tạo video” giải thích thiếu ảnh/prompt/audio/account; snapshot chỉ từ input đã lưu khi T22 nối chạy.

## Kiểm thử và nghiệm thu

Lưu lỗi/concurrency không mất text, đổi scene nhanh không stale preview; render/rollback đúng version; media A không chọn được cho B. Audio nguyên bản nghe được; reset/thay không xóa asset dùng lại. Export TXT dùng T09c.

Kiểm tra hai theme, form hẹp, keyboard/focus và cảnh báo rời nháp; output job tương lai không tự áp vào scene revision mới hơn.

