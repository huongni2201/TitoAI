# Prompt giao AI triển khai sửa PV

Ngày: 2026-10-27 (UTC+07). Dùng tại phiên AI triển khai trong workspace `D:\workspace\TitoAI`. Đây là prompt mẫu, không phải lệnh thực thi trong chat Codex soạn docs hiện tại.

Copy khối dưới vào phiên triển khai. [Plan chi tiết](2026-10-27-pv-remediation-fix-plan.md) giữ thứ tự, file ownership, tests và tiêu chí đóng; [review độc lập](../reviews/2026-10-27-audit-pv-independent-recheck.md) giữ evidence gốc.

```text
Bạn là Coordinator triển khai đợt sửa PV của TiToAI tại D:\workspace\TitoAI.

Hãy xử lý findings PV-R1 đến PV-R11 theo:
docs/plans/2026-10-27-pv-remediation-fix-plan.md
và evidence:
docs/reviews/2026-10-27-audit-pv-independent-recheck.md

Mục tiêu: pipeline production có run/step/snapshot bền vững, không tạo trùng,
không ghi đè input đã sửa, Pause/Cancel đúng thực thi, manual handoff dùng được
từ UI tới clip. Website thật có mốc xác minh riêng; giữ capability chưa xác minh
ở NOT_VERIFIED và kết quả live NOT_RUN/PARTIAL cho tới khi có evidence thật.

Trước khi sửa:
1. Đọc AGENTS.md, docs/agents/{STATUS,TASKS,ORCHESTRATION,ENGINEERING_RULES}.md,
   plan fix, review độc lập và các spec được plan dẫn theo vùng cần sửa.
2. Trong Antigravity, xác minh Gemini 3.8 Flash / High bằng metadata/UI runtime
   theo ORCHESTRATION cho phiên thực thi. Xử lý MODEL_UNVERIFIED/MODEL_BLOCKED
   đúng policy, không tự nhận inherit đã chứng minh model.
3. Xác nhận task/dependency và owner thật; ghi baseline revision, dirty files,
   hash source, kết quả build/test. Working tree đang có nhiều thay đổi của các
   phiên khác: giữ nguyên, không reset/stash/rollback, không kill app.
4. Dùng CodeGraph trước locate/read source nếu .codegraph tồn tại. Dùng Context7
   khi cần tra API thư viện; không suy API hoặc version chỉ từ plan.
5. Coordinator chốt contract, exact file ownership và persistence owner trước
   khi sửa Contracts/DbContext/migration/composition root. Thực hiện tuần tự
   mặc định; chỉ chia việc khi dependency và vùng ghi đã tách rõ.

Thực hiện FIX-0 → FIX-6 theo plan, hoàn tất regression của từng mốc trước khi
mở mốc phụ thuộc. Chọn thay đổi nhỏ nhất đủ acceptance; tái sử dụng queue,
T16 recovery, validator, asset store và các module hiện có.

Các điều kiện bắt buộc:
- Start/apply/event replay/concurrent calls có idempotency và DB guards.
- Job chốt media roles/hash/revision/duration/audio/provider/config/root.
- Apply output và continuation phục hồi được sau crash; output stale vào
  history/candidate, không thay selection/draft hiện hành.
- Pause chặn reservation mới, Resume không resend bước uncertain/thành công;
  Cancel gồm Running và không nhả occupancy uncertain trước đối soát.
- Image có script draft vẫn đi tiếp; USER_BRIEF/template/ảnh đúng đi vào mọi
  successor VideoPrompt. Stale/reuse/regenerate tuân thủ §3.2–3.4 của spec.
- Production route theo job snapshot; adapter chưa có browser implementation
  trả manual/unavailable, không báo thành công bằng URI tự dựng.
- Manual CTA/panel/import dùng được xuyên UI→bridge→SQLite→apply→successor.
- DTO backend thật đáp ứng CTA queue, đặc biệt cancel uncertain sau reload.

Giữ năm regression đã tái hiện trong review và test mất USER_BRIEF. Bổ sung
Start lặp/concurrent, crash giữa commit/enqueue, từng hàng stale matrix,
snapshot thay sau enqueue, legacy/cross-workspace, history sau restart.
Test E2E scheduler phải Start scheduler và chạy qua executor; test chỉ sửa
state DB rồi gọi apply được ghi đúng là test apply.

Khi bàn giao:
- Báo từng PV-R1–PV-R11: CLOSED/OPEN/PARTIAL cùng source và test evidence.
- Ma trận giữ nguyên số và nội dung 20 AC gốc trong
  docs/plans/PRODUCT_IMAGE_VIDEO_WORKFLOW_PLAN.md §8; ghi subcheck chưa chạy.
- Phân biệt logic, manual integration, live automation và runtime qualification.
- Chỉ Coordinator sửa STATUS/TASKS; cập nhật đúng checkpoint, giữ evidence
  lịch sử và sửa tài liệu mâu thuẫn/links. Không đóng gate chỉ từ test count.
- Ghi lệnh/exit code/hash, migration/legacy risks, NOT_RUN/blocker và bước sau.

Hãy bắt đầu FIX-0 và tiếp tục phần đã đủ dependency/quyền. Gặp file có writer
khác thì giữ checkpoint, làm phần độc lập; chỉ hỏi đúng thông tin/quyền còn
thiếu. Chưa có quyền dùng tài khoản thật/tiêu quota thì dừng riêng mốc live,
hoàn tất phần local và bàn giao rõ giới hạn. Không yêu cầu xác nhận lại ở mỗi
mốc local đã được giao.
```

Khi tiếp tục một phiên mới, gửi prompt ngắn:

```text
Tiếp tục cùng đợt sửa theo docs/plans/2026-10-27-pv-remediation-fix-plan.md.
Đọc checkpoint/STATUS/TASKS trước; xác minh lại model/owner/baseline của phiên.
Đối chiếu findings PV-R1–PV-R11 với source hiện tại, tiếp mốc chưa hoàn tất đủ
dependency. Giữ code đã sửa; kiểm lại thay đổi ngoài checkpoint trước ghi.
Bàn giao evidence mới, không lặp kết luận PASS từ báo cáo cũ.
```
