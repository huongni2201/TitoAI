# Provider registry, capability và cấu hình có version

- Task gốc: **T18 (registry)** · Ưu tiên: **P0 / G3**.
- Điều kiện mở: T09,T10,T16 DONE.
- Baseline 2026-10-26: Chưa có provider registry/config/workflow version runtime.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Hành vi cần đạt

Registry tách provider mong muốn, capability đã xác minh, session hiện tại và execution mode. Công bố theo task Script/Image/VideoPrompt/VideoRender; selector website nằm trong adapter.

## File và dữ liệu

- Tạo đề xuất: `src/TiToAI.Application/Providers/{ProviderRegistry.cs,ProviderCapabilities.cs,IProviderAdapter.cs,ProviderDispatchService.cs}`.
- Provider/config/workflow version models và migrations theo persistence owner; chỉ thêm trường dùng thật.
- provider.list({workspaceId?}) theo [v1.1 §1/§3](../../decisions/execution-contract-v1.1.md): tasks[] có mode/verification/limits/evidence; accounts[] có profile/session/capacity từng account global. Filter Workspace không loại global AI pool; một status chung MuseAI không thể thay probe từng profile.
- Registry ban đầu chỉ có entries chưa verified và fake trong test; manual được khai báo riêng.

## Các bước triển khai

- [ ] Định nghĩa stable keys gemini-web/chatgpt-web/museai và capability per-task, mode, verification evidence/version/date.
- [ ] Lưu config/workflow version bất biến; new config tạo version, job cũ dùng snapshot, không đọc lại current config khi retry.
- [ ] Nối resolver T15 với registry thật; thiếu provider/adapter/config trả lỗi cụ thể, không fallback.
- [ ] Dispatch kiểm capability/mode/session/account và hidden readiness trước lấy browser slot.
- [ ] Enqueue resolve task→providerKey từ Settings (VideoRender=museai), không chọn account theo Workspace. T15 reserve chọn global account/slot và ExecutionBinding; adapter dùng binding, không chọn lại. Chặn wrong provider/identity/legacy trước side effect; full pool chờ capacity.
- [ ] Capability NOT_VERIFIED/UNSUPPORTED không chạy automation; có manual path được phép thì AwaitingUser.
- [ ] provider.list chỉ trả DTO công khai, không config secret/profile path; cập nhật event/cache khi verification đổi.
- [ ] Chốt workflow MVP tuyến tính có version và cấu hình điều kiện/tác vụ cần thiết; không tạo visual DAG engine ngoài nhu cầu.

## Kiểm thử và nghiệm thu

Fake adapters: registry key trùng, missing/disabled, routing từng task, config version đổi, không hidden, hết session/quota. Chứng minh không gọi adapter khác khi lỗi; snapshot job cũ giữ nguyên.

Thêm ca ba MuseAI profiles có session khác nhau, Gemini expired trong khi ChatGPT Connected; capability cùng provider nhưng task Image không verified không được chạy automation chỉ vì Script đã verified. Session thay đổi không sửa capability hoặc Settings.

Xác minh website ở T19, AI Settings ở T18b và manual workflow ở T18c là các file riêng. Không bật capability thật chỉ vì đã tạo tên provider.

