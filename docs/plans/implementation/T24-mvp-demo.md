# Demo MVP chức năng trên ba Workspace

- Task gốc: **T24** · Ưu tiên: **P0 / G3**.
- Điều kiện mở: T14,T23 DONE.
- Baseline 2026-10-26: Chưa có evidence end-to-end MVP.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Kết quả cần bàn giao

Một kịch bản demo lặp lại được, dataset test riêng và `docs/reviews/T24-mvp-demo.md` ghi từng bước/mode/provider/result/revision. Không thay demo bằng ảnh mock hoặc chỉ build.

## Chuẩn bị và file

- Tạo đề xuất: `tests/Scenarios/MvpThreeWorkspaces.md` và fixtures metadata/audio/image có quyền sử dụng.
- Dùng DB/profile/roots tạm, Workspace có tên/input/template/TikTok identity khác biệt, dùng chung account AI pool; không tạo account AI riêng chỉ vì thêm kênh.
- Capability lấy provider-matrix T19; demo manual được phép nhưng phải ghi rõ. Không lưu account secret trong fixture.

## Các bước thực hiện

- [ ] Dataset baseline ba kênh cho demo UI; mở rộng queue fixture 10 kênh dùng chung 1 ChatGPT/1 Gemini/4 MuseAI profiles global. TikTok riêng kênh, AI không link theo Workspace; context hội thoại sạch từng job.
- [ ] Mỗi kênh đi hết project/scene→prompt/ảnh→audio gốc→video→library; chỉ capability verified mới automation.
- [ ] So các màn P0 với reference, ghi khác biệt có chủ đích về audio, dữ liệu thật, login CTA và P1 chưa làm.
- [ ] Pause một Workspace, xử lý kênh khác; đổi root/provider khi có jobs cũ rồi kiểm snapshot không đổi.
- [ ] Thử mất session, sai account, đóng login và hai yêu cầu login; ngoài login browser không ló/giành focus.
- [ ] Inject crash trước/sau submit, restart đối soát đúng job, không submit trùng.
- [ ] Ghi hash/output metadata và audio byte-identity; che PII/token khỏi ảnh/log; mở findings có severity/owner/điều kiện đóng.
- [ ] Fake kiểm đủ 8 tổ hợp AI routing và 10 video/4 MuseAI: 4 chạy/6 chờ, account nào xong close/reopen rồi nhận job kế; không wait cả đợt. Output đúng kênh dù cùng account phục vụ nhiều kênh.
- [ ] Script trước khi có scene, duplicate apply/import và event/restart không tạo job/scene trùng; stale revision giữ lịch sử. Manual/automation thật ghi mode/evidence riêng, thiếu account ghi NOT_RUN.

## Nghiệm thu

Không có P0 rò scope/mất dữ liệu/gửi trùng; pipeline hoàn tất theo mode công bố cho cả ba kênh. Chưa có quyền/account/môi trường thì scenario NOT_RUN và task chưa DONE theo acceptance.

Demo không thay clean-machine installer, backup/migration/load qualification ở T26–T30.

