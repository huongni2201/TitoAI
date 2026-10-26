# Đóng gói installer Windows và chiến lược dependency

- Task gốc: **T29** · Ưu tiên: **P0 / G4**.
- Điều kiện mở: T28 DONE.
- Baseline 2026-10-26: Chưa có installer/release packaging.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Hành vi cần đạt

EXE/MSI cài TiToAI chạy được trên Windows sạch không SDK/Python/PostgreSQL. Self-contained .NET, React assets local, WebView2 Runtime và Playwright browser có chiến lược cài/kiểm tra rõ. Upgrade/uninstall bảo toàn dữ liệu theo lựa chọn.

## File và quyết định

- Tạo đề xuất `installer/`, `scripts/release/Build-WindowsPackage.ps1`, `docs/decisions/windows-packaging.md`.
- Integrator sửa publish/project/CI; chọn installer toolchain tối thiểu phù hợp môi trường, xác minh tài liệu chính thức trước pin. Không coi package framework đã tồn tại.
- Chốt OS/architecture hỗ trợ, per-user/per-machine, online/offline dependency và signing trong ADR packaging.

## Các bước triển khai

- [ ] Reproducible release build Web→Desktop publish self-contained cho RID đã chọn, lockfiles/pins giữ nguyên.
- [ ] Verify wwwroot, native SQLite runtime, WebView2 loader và browser binaries theo đúng Playwright package.
- [ ] Installer kiểm dependency WebView2/browser, cài hoặc hướng dẫn/retry rõ khi offline/thiếu quyền; không tải dependency âm thầm mỗi lần mở app.
- [ ] Data/DB/profiles/logs ở LocalAppData TiToAI; media tại roots đã lưu, không ghi user data dưới Program Files.
- [ ] Upgrade dừng app an toàn, giữ settings/profiles/external roots; schema upgrade đi T28 sau backup.
- [ ] Uninstall mặc định giữ user data; lựa chọn xóa chỉ khi xác nhận rõ và đúng thư mục app, không xóa shared/external root toàn bộ.
- [ ] Gói manifest version/checksum/signature, hướng dẫn cài/khắc phục; chưa có certificate thì ghi unsigned và hạn chế distribution.

## Kiểm thử và nghiệm thu

VM Windows sạch, tài khoản thường/admin theo scope, không internet, thiếu WebView2/browser, Unicode/space paths, install lại, upgrade đang có job, uninstall/reinstall. EXE không cần dotnet CLI hoặc Node chạy kèm.

Application Control chặn binary phải ghi blocker và xử lý bằng quy trình signing/deployment hợp lệ, không hướng dẫn bypass policy. Chứng cứ đầy đủ tiếp tục ở T30.

