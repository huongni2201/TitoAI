# Luồng tạo video chỉ chạy tự động

> **Delta sản phẩm 2026-10-27:** [VIDEO_FIRST_ARCHITECTURE_PLAN.md](VIDEO_FIRST_ARCHITECTURE_PLAN.md) chốt một Project = một video TikTok đích và thêm TikTok publish stage sau khi người dùng review. Quy tắc tự động-only dưới đây chỉ áp dụng generation; không có nghĩa tự đăng sau khi render.

Ngày 2026-10-27 (UTC+07). Yêu cầu trực tiếp mới nhất của người dùng: bỏ manual handoff trong luồng tạo video, chỉ giữ thực thi tự động. Đã triển khai phần loại bỏ handoff và kiểm chứng cục bộ; automation trên website thật vẫn chưa được triển khai/xác minh.

Tài liệu này thay mọi yêu cầu manual fallback của [workflow từ ảnh](PRODUCT_IMAGE_VIDEO_WORKFLOW_PLAN.md), [UI/browser](UI_BROWSER_PLAN.md), [Settings](SETTINGS_PLAN.md), [plan nguồn](SOURCE_PLAN.md) và [ma trận provider](../decisions/provider-matrix.md) trong phạm vi tạo video. Các báo cáo nghiệm thu handoff cũ là evidence lịch sử, không còn là acceptance của luồng này.

## Hành vi đích

- Một lần Bắt đầu chạy qua các bước cần thiết: lập cảnh → tạo ảnh → viết prompt chuyển động → render và lưu video. Áp dụng cho `Script`, `Image`, `VideoPrompt`, `VideoRender` của quy trình; không có chế độ chuyển giao thủ công giữa các bước.
- Gỡ CTA Xử lý thủ công và bảng copy prompt/nhập kết quả để hoàn tất job khỏi hàng đợi, chi tiết job và luồng tạo video. Không dùng `job.get_handoff` hoặc `job.import_result` làm đường hoàn tất quy trình.
- Vẫn cho phép nhập/chọn ảnh, audio ban đầu; chỉnh brief/prompt; xem, tải, tạm dừng, hủy và chủ động tạo lại. Những thao tác này không phải manual handoff.
- Đăng nhập tài khoản qua Cài đặt vẫn do người dùng thực hiện. Không tự vượt CAPTCHA hoặc giả trạng thái đăng nhập.
- Provider/capability chưa xác minh, không hỗ trợ hoặc thiếu adapter: chặn và hiển thị lỗi cụ thể, không chuyển sang nhập kết quả thủ công, không tiêu hao retry budget bằng cách thử lại cùng một thiếu sót cấu hình.
- Lỗi tạm thời trước submit được retry hữu hạn theo policy hiện có. Đã submit nhưng chưa rõ kết quả thì giữ checkpoint/occupancy và đối soát trước khi tiếp tục; không gửi lại mù để đạt mục tiêu tự động.
- Job cũ đang chờ handoff vẫn giữ snapshot/kết quả/lịch sử. Không tự coi là thành công, không tự enqueue lại hoặc xóa asset. Hiển thị lý do bị chặn và cho phép hủy an toàn; hủy tác vụ cũ và tạo yêu cầu mới có chủ đích sau khi automation sẵn sàng.
- Không đổi `NOT_VERIFIED` thành `Verified` để mở đường chạy. Không dùng nội dung hardcode, ảnh/video tối thiểu hoặc fake provider làm kết quả production.

## Phạm vi triển khai và nghiệm thu

Coordinator xác nhận ownership trước sửa các file đang có worker khác ghi. Các điểm cần cập nhật đồng bộ: `ProductionJobExecutor`, provider adapters/capabilities/dispatch, retry policy; wiring và bridge job handlers; action mapping, queue card, job details và Settings; tests và tài liệu contract hiện hành.

Kiểm tra tối thiểu:

1. Job automation hợp lệ thành công và tự kích hoạt bước tiếp theo, áp output đúng Workspace/scene, không có điểm bàn giao thủ công.
2. Automation chưa sẵn sàng báo lỗi hữu ích, không hiển thị CTA hoặc mở bảng handoff, không gọi RPC nhập kết quả, không tự retry thiếu adapter/capability.
3. Giữ đăng nhập/resume, hủy và đối soát kết quả chưa xác định; không gửi trùng sau crash hoặc mất phiên.
4. Job lịch sử chờ handoff không mất dữ liệu hoặc bị tự chạy lại; UI hiển thị đúng khi mở hàng đợi/chi tiết.
5. Build và regression đúng phần thay đổi. Kiểm chứng website/account thật phải có evidence riêng; chưa chạy ghi `NOT_RUN`, không tuyên bố hoàn tất automation chỉ vì đã gỡ handoff.

## Bằng chứng kiểm chứng cục bộ

Người dùng xác nhận không có AI khác đang triển khai và cho phép Codex sửa trực tiếp. Đã xóa service/DTO/RPC và UI/API/tests handoff; cập nhật provider metadata, lỗi executor, retry/resume guard và Settings. Không thay schema DB, không sửa job/asset đang lưu của người dùng.

- Frontend: `pnpm build` PASS; queueProduction 13/13, queueDrawer 9/9 và AI Settings 6/6 PASS. Các suite DOM chạy bằng `node src/features/.../*.test.ts` cho từng file; có cảnh báo React act trong queueProduction.
- Backend build: `dotnet build tests/TiToAI.Desktop.SmokeChecks/TiToAI.Desktop.SmokeChecks.csproj --no-restore -p:UseAppHost=false -p:UseSharedCompilation=false -p:BaseOutputPath=D:/workspace/TitoAI/.tmp/auto-video-build/ -m:1 -v:minimal` PASS, 0 warning/error. Output riêng vì ứng dụng đang mở khóa DLL ở thư mục mặc định.
- Chạy DLL từ output trên với các tham số: `provider` 8/8, `remediation` 11/11, `workflow` 14/14, `retry` 8/8, `t15` 19/19 PASS. Bao gồm chặn RPC cũ, job lịch sử không resume/handoff nhưng vẫn hủy được, lỗi automation không retry và kết quả uncertain vẫn phải đối soát.
- Fixture regression Image → VideoPrompt/continuation được cập nhật để tạo active run bằng service thật; không bỏ validation run ID để làm test xanh.
- Website/provider thật và nghiệm thu WPF/WebView2: `NOT_RUN`. Adapters thật hiện trả automation chưa sẵn sàng; không công bố hệ thống đã tạo video trên website chỉ dựa vào các test này.
