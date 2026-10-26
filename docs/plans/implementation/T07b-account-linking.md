# Liên kết TikTok theo Workspace và tài khoản AI dùng chung

- Task gốc T07 baseline DONE theo STATUS; evidence cũ giữ nguyên. Yêu cầu pool AI toàn app mới được triển khai ở T13a sau T02a, không coi DONE cũ là đã đạt yêu cầu mới.
- TikTok là account gắn Workspace; ChatGPT/Gemini/MuseAI là ProviderAccount global, không có liên kết bắt buộc hoặc account riêng cho từng TikTok.
- Nguồn: [v1.1 §1–3](../../decisions/execution-contract-v1.1.md), [T13a](T13a-provider-profile-isolation.md).

## Mô hình và hành vi

Workspace giữ project/scene/asset/job/template riêng và TikTok identity của kênh. Có 10 Workspace nhưng chỉ một ChatGPT hoặc bốn MuseAI vẫn tạo nội dung được; scheduler chọn slot AI rảnh dùng chung. Không có MuseAIAccountId bắt buộc trong Workspace mới và không báo Thiếu liên kết MuseAI nếu global pool có tài khoản phù hợp.

Account baseline và account.list_by_workspace/link/unlink chỉ tiếp tục phục vụ TikTok và lịch sử cũ. ProviderAccount global có lifecycle, identity/profile/capacity riêng; provider_account.list/create/update không nhận workspaceId. Job ownership và input/output scope không được suy từ account AI.

## Phạm vi tích hợp

- Coordinator giữ DTO/account/provider-account command catalog, bootstrap và allow-list.
- Một persistence owner thêm ProviderAccount/global profile và migration mapping legacy MuseAI Account ID; không xóa lịch sử link cũ.
- Browser/identity resolver xác minh đúng provider/identity; UI không gửi cookie/password/identity tự khai để bypass probe.
- T09/T11 baseline đã DONE; giữ evidence, owner source hiện hành (T15 Coordinator 2) được coordinator xác nhận trước sửa chung. UI link TikTok tiếp tục baseline; panel AI mới do T13a làm global, không chèn form chọn kênh cho account AI.

## Các bước bổ sung

1. Review schema/unique identity/legacy mapping ở T02a. Phân biệt TikTok Account ID và ProviderAccount ID trong DTO/JobBinding.
2. Chuyển MuseAI profile cũ sang global nếu identity verified; giữ nguyên thư mục/session, map legacy link thành provenance. Identity trùng chưa giải quyết thì quarantine, không clone session.
3. Luồng AI: thêm account theo provider → đăng nhập đúng profile → probe/verify → enable nhận job. Luồng TikTok: tạo profile đúng Workspace → login → link; không thể dùng profile TikTok làm AI.
4. Disable AI account ngừng cấp job chưa bind; job đã bind vẫn giữ account/reference để hoàn tất/reconcile. Không hard-delete history hoặc tự chuyển job đã gửi sang account khác.
5. Workspace delete chỉ xử lý dữ liệu/link TikTok theo guard; không xóa ProviderAccount/profile AI toàn app còn phục vụ kênh khác.

## Kiểm thử nghiệm thu

- 10 Workspace đọc cùng pool ChatGPT/MuseAI, không cần tạo/link thêm AI account theo kênh.
- Cùng MuseAI lần lượt xử lý A rồi B; output vẫn đúng workspace/project/scene của job. Đoán Asset/Job ID kênh khác bị chặn dù dùng chung AI.
- TikTok profile A không link vào B; profile kind/provider sai bị chặn; identity AI trùng không tạo account Active thứ hai.
- Legacy MuseAI link history/job binding còn sau migration/restart; xóa/unlink một TikTok không xóa pool AI.
- Fake/DB fixtures chứng minh mapping/routing, tài khoản thật mới chứng minh session. Chưa thử ghi NOT_RUN; không đổi trạng thái T07 baseline từ lượt viết plan.
