# Tích hợp popup Cài đặt và kiểm tra lưu đồng thời

- Task gốc: **T27 (Settings integration)** · Ưu tiên: **P0 / G4**.
- Điều kiện mở: T07,T13,T18,T06a DONE.
- Baseline 2026-10-26: Popup nền đã có; storage/provider/theme chưa tích hợp đầy đủ.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Hành vi cần đạt

Một popup ba nhóm Lưu trữ/AI & Prompt/Giao diện từ mọi trang, cùng SettingsStore và handler save. Storage/provider draft Lưu/Hủy; theme áp ngay và không rollback cùng draft. Không chờ task này mới làm các nhóm T09/T18.

## File và ownership

- Sửa SettingsModal.tsx và `features/settings/{StorageSettingsPanel.tsx,AiProviderSettingsPanel.tsx}`, SettingsStore/SettingsHandlers đã có từ các task trước.
- Tạo đề xuất `tests/Scenarios/SettingsIntegration.md`; coordinator sở hữu kết nối host/DTO, không viết store thứ hai.
- Spec SETTINGS §1–6, Theme §1–5; đọc lại contract draft/save và fieldErrors trước tích hợp.

## Các bước triển khai

- [ ] Mở popup từ Workspace/project/editor/library/browser/queue, giữ route/context và form đang nhập.
- [ ] Load settings một snapshot, lỗi đọc cho retry và chặn save dựa trên default suy đoán.
- [ ] Chỉnh roots và provider cùng draft; Save validation đủ trường rồi commit cùng transaction. Không lưu nửa bộ.
- [ ] Cancel/Escape/backdrop/close bỏ draft; reopen nhận saved state, theme mới đã áp vẫn giữ.
- [ ] Save lỗi giữ draft/field errors, double-click không tạo hai commit; revision/expectedRevision theo [v1.1 §3](../../decisions/execution-contract-v1.1.md). Stale form conflict giữ draft; budget toàn app/giới hạn account giảm không kill job, không có link AI theo Workspace.
- [ ] Restart và đổi Workspace giữ config; jobs cũ giữ provider/root/template/workflow/account snapshot.
- [ ] Kiểm focus trap, background inert, scroll/keyboard hai theme và min usable viewport/DPI.

## Kiểm thử và nghiệm thu

Matrix: root hợp lệ/provider sai; root sai/provider hợp lệ; lỗi DB; mất bridge; đổi theme rồi Hủy; đổi root khi jobs queued/running/awaiting_user; restart sau Save. Cả store cũ và file cũ không bị đổi khi save thất bại.

Không có count/path/provider giả, không nội dung mã task; Settings §6 đạt và giữ evidence. Backup nhiều root được cross-check T26.

