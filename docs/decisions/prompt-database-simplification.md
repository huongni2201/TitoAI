# Rà soát database prompt và kiến trúc cũ

Ngày 2026-10-27 (UTC+07). Phạm vi: bỏ lịch sử version của prompt, sửa lỗi tên biến tiếng Việt và rà dấu vết schema cũ liên quan.

## Thay đổi

- `PromptTemplate` lưu trực tiếp `template_text` và `variable_definitions_json`; EF model snapshot không còn bảng `PromptVersion` hoặc cột `current_version_id`.
- Migration `20261026203245_PromptTemplatesWithoutVersionHistory` chép nội dung và khai báo biến từ version đang active sang `PromptTemplate`, sau đó xóa bảng lịch sử. Các job cũ tiếp tục giữ nguyên rendered text snapshot.
- Khi có migration chờ và DB đã có bảng ứng dụng, bootstrap tạo SQLite online backup trước khi chạy migration. Backup thất bại thì dừng migrate.
- UI bỏ nhãn version, nút lịch sử/rollback và trường mô tả thay đổi version. Tên biến hỗ trợ ký tự Unicode, gồm tiếng Việt như `{chủ_đề}`.

## Dấu vết kiến trúc cũ còn lại

- **Account theo Workspace vẫn còn hoạt động.** Bảng `Account`, `AccountLinkService` và `account.*` handlers vẫn tồn tại song song với `ProviderAccount` toàn cục. Dữ liệu cũ có cả provider MuseAI và TikTok, nên cần chốt mapping/bảo toàn dữ liệu trước khi xóa bảng này.
- **Prompt version còn mã tương thích và migration lịch sử.** Các migration cũ phải giữ để nâng cấp DB; entity/DTO tương thích và một số hàm/API cũ còn trong source, nhưng `PromptVersion` bị loại khỏi EF model mới, `prompt.list_versions` và `prompt.rollback_version` không còn đăng ký trong Bridge.
- Không thấy yêu cầu dùng database server trong runtime; dự án tiếp tục dùng SQLite local.

## Kiểm chứng

- Build Infrastructure, Application, Desktop và Web: PASS.
- Tạo SQL migration bằng `dotnet ef migrations script`: PASS. EF báo warning ở một số migration cũ có raw SQL khi bảng `Job` đang được rebuild; chưa chạy upgrade trên fixture SQLite.
- Chưa áp dụng migration vào database người dùng. Bootstrap sẽ tạo backup nhất quán rồi mới migrate ở lần khởi động tiếp theo.
- Không chạy test suite trong lượt này.
