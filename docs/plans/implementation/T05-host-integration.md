# Tích hợp Desktop host và WebView2 bridge

- Task gốc: **T05** · Ưu tiên: **P0 / G1**.
- Điều kiện mở: T03 DONE; tích hợp sau khi T06 ngừng ghi.
- Trạng thái hiện tại: **DONE** theo addendum trong [review T05](../../reviews/T05-coordinator-review.md).
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Hiện trạng và đích đến

Đã có MainWindow, WebView2HostManager, BridgeMessageRouter/Dispatcher và smoke checks. Evidence được ghép theo lớp: router cases chạy 17/17, sau đó app thật tải local page và nhận response `workspace.list` từ SQLite tạm. Lệnh runner không phải acceptance riêng; lần gọi mới bị Application Control chặn không làm mất evidence từ bộ test có source hash khớp.

## File và contract

- Sửa khi cần: `src/TiToAI.Desktop/{App.xaml.cs,MainWindow.xaml.cs,Hosting/WebView2HostManager.cs,Bridge/BridgeMessageRouter.cs,Bridge/BridgeDispatcher.cs}`.
- Kiểm tra copy target tại `src/TiToAI.Desktop/TiToAI.Desktop.csproj`; test hiện có tại `tests/TiToAI.Desktop.SmokeChecks/`.
- Integrator sở hữu bootstrap/project files. Spec: contracts §1,§7; không mở rộng allow-list nghiệp vụ ở task này.

## Các bước triển khai

- [x] Xác nhận T06 đã freeze trước khi tích hợp.
- [x] Nối bootstrap SQLite trước khi đăng ký `workspace.list`; lỗi startup có Retry và không chặn UI thread.
- [x] Build Web/Desktop; output có đủ local assets và WebView2 tải trang `appassets.local`.
- [x] Request JSON object thật từ React nhận response `workspace.list` đọc SQLite tạm.
- [x] Router smoke cases kiểm tra error, cancel, duplicate ID, dispose/race và origin; build sạch.
- [x] Rà navigation/popup và log theo origin allow-list; source review PASS.
- [x] Ghi evidence tại STATUS, runtime log và review hiện có.

## Kiểm thử và nghiệm thu

| Kịch bản | Kết quả cần đạt |
|---|---|
| Build thiếu dist / Runtime thiếu | Lỗi đọc được, Retry hoạt động; không cửa sổ trắng |
| Origin ngoài, version sai, secret lồng array | Bị từ chối trước handler; không lộ dữ liệu |
| Hai request cùng ID, cancel muộn | Không hủy nhầm; không response trùng |
| WebView2 thật, app offline | UI local và round-trip hoạt động |

T05 đóng khi build/source review, router smoke suite có bằng chứng 17/17 gắn với source hash hiện hành, và integrated WPF/WebView2 xác nhận local load cùng bridge round-trip. Không bắt buộc chạy lại cùng runner chỉ để lặp lại công cụ. Live WebView2 cancellation chưa được thao tác riêng; giữ thành kịch bản T30, không báo PASS. Không đổi Windows Application Control.

