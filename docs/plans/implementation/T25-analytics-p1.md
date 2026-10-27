# TikTok Analytics và Affiliate theo dữ liệu có nguồn

> Delta 2026-10-27: [analytics → AI web đề xuất ý tưởng](../AI_WEB_CONTENT_CREATION_PLAN.md) quy định snapshot, preview dữ liệu gửi ChatGPT/Gemini, evidence và nối ý tưởng đã chọn vào pipeline. Giữ P1 và import hợp lệ trước automation đã xác minh.

- Task gốc: **T25** · Ưu tiên: **P1 / không chặn G4 MVP**.
- Điều kiện mở: T07,T09,T19 DONE; ưu tiên sau P0.
- Baseline 2026-10-26: Chưa có analytics schema/import/dashboard thật.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Hành vi cần đạt

Dashboard theo panel 06: KPI/chart/video/affiliate theo Workspace/account/khoảng thời gian. Nhập/xuất dữ liệu hợp lệ là đường đầu tiên; tự động đọc chỉ khi quyền/capability VERIFIED. Thiếu metric là null/chưa có dữ liệu.

## File và contract

- Tạo đề xuất: `src/TiToAI.Application/Analytics/{AnalyticsImportService.cs,AnalyticsQuery.cs}`, `src/TiToAI.Web/src/features/analytics/AnalyticsPage.tsx`.
- Persistence owner thêm video/affiliate snapshots; DTO/allow-list import/query/export do coordinator chốt.
- Schema đề xuất: workspaceId/accountId/videoKey/capturedAtUtc/source/sourceRecordId/completeness/currency/metrics; tiền minor-units integer hoặc text fixed-scale, không double.

## Các bước triển khai

- [ ] Chốt schema import CSV/JSON với null, timezone, source và completeness; preview mapping/lỗi theo dòng trước commit.
- [ ] Chống import lặp bằng khóa nguồn phù hợp; không trộn nhiều capture/source rồi cộng sai số.
- [ ] Query đúng account/kỳ; tính growth chỉ khi hai kỳ cùng định nghĩa và đủ dữ liệu.
- [ ] UI hiển thị freshness/source/thiếu dữ liệu, bảng thay thế chart và legend không chỉ màu.
- [ ] Affiliate chỉ hiện metric được cấp quyền; không biến thành catalog/mua hàng hoặc auto-post TikTok.
- [ ] Export đúng phạm vi, escape dữ liệu CSV có thể được spreadsheet diễn giải thành công thức.
- [ ] Feedback insight theo Workspace, nêu nguồn/độ đầy đủ; không tự sửa template hoặc suy đoán doanh thu.

## Kiểm thử và nghiệm thu

Import lặp, source xung đột, số lớn/tiền lẻ, null khác 0, timezone boundary, so kỳ thiếu dữ liệu, account A→B, CSV lỗi. Chart/KPI khớp bảng dữ liệu và tiền giữ độ chính xác sau round-trip.

Browser analytics chưa được phép thì manual import đủ theo scope P1; T25 không kéo vào P0 release.

