# Ma trận Năng lực Nhà cung cấp AI (Provider Capability Matrix) — TiToAI

Cập nhật: 2026-10-27 (UTC+07).
Nguồn chuẩn: [PRODUCT_IMAGE_VIDEO_WORKFLOW_PLAN.md](../plans/PRODUCT_IMAGE_VIDEO_WORKFLOW_PLAN.md), [execution-contract-v1.1.md](execution-contract-v1.1.md), [contracts.md](contracts.md).

Tài liệu này xác lập ma trận năng lực chính thức của 3 nhà cung cấp AI: **ChatGPT Web**, **Gemini Web**, và **MuseAI** cho quy trình sản xuất video từ ảnh mẫu và sản phẩm.

---

## 1. Nguyên tắc Quản lý Năng lực (Capability Governance)

1. **Tuyệt đối không giả mạo xác minh (No Fabricated Verification):**
   - Trạng thái `VERIFIED` chỉ được cấp khi đã có bằng chứng kiểm thử tự động hóa thực tế (Playwright automation) trên giao diện website trực tiếp với tài khoản live.
   - Khi chưa có phiên đăng nhập trực tiếp của người dùng (`A13 Manual Login`) và chưa chạy xác minh selector trên website thực, trạng thái năng lực bắt buộc phải ghi là **`NOT_VERIFIED`**.
   - Tuyệt đối không đoán selector, không giả lập tiến trình tự động hóa thành công trong cơ sở dữ liệu khi chạy production.

2. **Định tuyến nghiêm ngặt, cấm Fallback ngầm (Strict Routing, No Silent Fallback):**
   - Loại tác vụ nào cấu hình cho ChatGPT thì chỉ dispatch tới tài khoản/profile của ChatGPT.
   - Tuyệt đối không tự ý hoán đổi ngầm sang Gemini hoặc ngược lại khi gặp lỗi hoặc thiếu năng lực.

3. **Chuyển giao thủ công minh bạch (Manual Handoff):**
   - Với mọi năng lực ở trạng thái `NOT_VERIFIED` hoặc khi website thay đổi giao diện/gặp lỗi mạng/chặn automation: hệ thống cung cấp luồng **Manual Handoff** (`job.get_handoff` và `job.import_result`).
   - Người dùng được cung cấp hướng dẫn thao tác chi tiết, văn bản prompt sạch, và xuất/nhập kết quả có kiểm tra schema và provenance chặt chẽ.

4. **Hành vi âm thanh (Audio Behavior) khi tạo video:**
   - Quyết định đã chốt: **Audio là tùy chọn (Không bắt buộc)**. Mặc định là không gửi audio (`AudioMode = None`).
   - **Lưu ý đặc thù về Provider:** "Không gửi audio" có nghĩa là hệ thống TiToAI không tải tệp âm thanh lên và không gửi chỉ thị audio tới nhà cung cấp. Tuy nhiên, điều này **không đồng nghĩa video xuất xưởng từ MuseAI chắc chắn im lặng tuyệt đối**. Một số phiên bản web của MuseAI có thể tự động gán nhạc nền ngẫu nhiên hoặc xuất video không có audio track. Hệ thống TiToAI phản ánh trung thực kết quả của provider, không can thiệp cắt ghép hay lọc audio cục bộ.

---

## 2. Ma trận Năng lực Chi tiết (Capability Matrix)

| Nhà cung cấp (Provider) | Loại tác vụ (TaskType) | Năng lực đặc thù yêu cầu | Trạng thái xác minh (Verification Status) | Hỗ trợ Tự động (Automation) | Hỗ trợ Thủ công (Manual Handoff) | Ghi chú kỹ thuật & Ràng buộc |
|---|---|---|---|---|---|---|
| **ChatGPT Web** (`chatgpt-web`) | `Script` | Lập danh sách cảnh từ brief ngôn ngữ tự nhiên | `NOT_VERIFIED` | `false` | `true` | Đòi hỏi prompt cấu trúc JSON; trả về danh sách các cảnh có mục đích rõ ràng. |
| **ChatGPT Web** (`chatgpt-web`) | `Image` | **Nhận 2 ảnh tham chiếu (Mẫu + Sản phẩm)** | `NOT_VERIFIED` | `false` | `true` | Trên giao diện web, tính năng nhận 2 ảnh đính kèm đồng thời để tạo ảnh biến thể cần được kiểm chứng live; khi chưa verify, xuất 2 ảnh cho người dùng copy/upload thủ công. |
| **ChatGPT Web** (`chatgpt-web`) | `VideoPrompt` | **Đọc ảnh đã chọn để viết prompt chuyển động** | `NOT_VERIFIED` | `false` | `true` | Gửi ảnh kết quả hoặc ảnh sẵn sàng vào chat, yêu cầu mô tả chuyển động camera/chủ thể; khi chưa verify, người dùng thao tác qua handoff. |
| **ChatGPT Web** (`chatgpt-web`) | `VideoRender` | Tạo video clip từ ảnh và prompt | `UNSUPPORTED` | `false` | `false` | ChatGPT không hỗ trợ trực tiếp render video clip; bị chặn ngay từ khâu validate cấu hình. |
| **Gemini Web** (`gemini-web`) | `Script` | Lập danh sách cảnh từ brief ngôn ngữ tự nhiên | `NOT_VERIFIED` | `false` | `true` | Hỗ trợ xử lý văn bản ngữ cảnh dài; xuất JSON schema danh sách cảnh. |
| **Gemini Web** (`gemini-web`) | `Image` | **Nhận 2 ảnh tham chiếu (Mẫu + Sản phẩm)** | `NOT_VERIFIED` | `false` | `true` | Cần xác minh khả năng nhận đa ảnh đầu vào của giao diện Gemini Web; fallback về Manual Handoff khi chưa có test live. |
| **Gemini Web** (`gemini-web`) | `VideoPrompt` | **Đọc ảnh đã chọn để viết prompt chuyển động** | `NOT_VERIFIED` | `false` | `true` | Mô hình thị giác của Gemini có khả năng đọc ảnh tốt; cần kiểm chứng DOM input trên website thật. |
| **Gemini Web** (`gemini-web`) | `VideoRender` | Tạo video clip từ ảnh và prompt | `UNSUPPORTED` | `false` | `false` | Gemini Web không hỗ trợ render video trực tiếp; bị chặn tại validator. |
| **MuseAI** (`museai`) | `VideoRender` | **Tạo video clip không gửi audio** | `NOT_VERIFIED` | `false` | `true` | Chỉ nhận 1 ảnh được chọn + video prompt. Cho phép bỏ qua tệp âm thanh. Video kết quả có thể có âm thanh mặc định của provider. |
| **MuseAI** (`museai`) | `VideoRender` | **Tạo video clip có tệp audio đính kèm** | `NOT_VERIFIED` | `false` | `true` | Chỉ hỗ trợ khi người dùng chọn chế độ `CustomAudio` và cung cấp tệp MP3/WAV hợp lệ. |
| **MuseAI** (`museai`) | `Script` | Lập kịch bản | `UNSUPPORTED` | `false` | `false` | Không hỗ trợ tác vụ kịch bản. |
| **MuseAI** (`museai`) | `Image` | Tạo ảnh tĩnh | `UNSUPPORTED` | `false` | `false` | Không hỗ trợ tác vụ tạo ảnh tĩnh. |
| **MuseAI** (`museai`) | `VideoPrompt` | Viết prompt chuyển động | `UNSUPPORTED` | `false` | `false` | Không hỗ trợ tác vụ viết prompt chuyển động. |

---

## 3. Quy chuẩn Thao tác Thủ công (Manual Handoff Specifications)

Khi một tác vụ rơi vào trạng thái `AwaitingUser` hoặc chạy ở chế độ Thủ công (do capability chưa được verify tự động):
1. **Lấy Handoff (`job.get_handoff`):**
   - UI hiển thị cửa sổ hướng dẫn từng bước.
   - Nút **Sao chép Prompt**: Sao chép nội dung prompt snapshot nguyên bản vào clipboard.
   - Nút **Mở thư mục ảnh tham chiếu**: Mở vị trí tệp ảnh trên máy tính (nếu tác vụ có ảnh tham chiếu như Image hoặc VideoPrompt).
2. **Nhập Kết quả (`job.import_result`):**
   - Với tác vụ văn bản (`Script`, `VideoPrompt`): Người dùng dán nội dung văn bản phản hồi từ AI vào ô nhập liệu. Hệ thống xác thực cấu trúc (schema validator).
   - Với tác vụ hình ảnh / video (`Image`, `VideoRender`): Người dùng tải tệp ảnh/video kết quả lên. Hệ thống tính toán SHA-256, chuyển tệp vào kho lưu trữ bền vững `AssetFileStore`, tạo bản ghi `Asset` với provenance nguồn là `manual_import`.
3. **Tiếp tục Luồng:**
   - Sau khi kết quả được nhập hợp lệ, hệ thống hoàn tất tác vụ (`CommitSuccessAsync`) và tự động kích hoạt bước phụ thuộc tiếp theo trong quy trình.
