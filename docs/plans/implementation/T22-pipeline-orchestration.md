# Pipeline nội dung từ brief tới thư viện

- Task gốc: **T22** · Ưu tiên: **P0 / G3**.
- Điều kiện mở: T12,T17,T20,T21 DONE; workflow version T18d sẵn sàng.
- Baseline 2026-10-26: Chưa có orchestration; Job hiện bắt buộc SceneId nên chưa hỗ trợ script cấp project.
- Đọc [quy ước và thứ tự triển khai](README.md) trước khi nhận task; đây là plan, chưa phải bằng chứng hoàn thành.

## Hành vi cần đạt

Brief→script review/scenes→ảnh→video prompt chỉnh được→audio người dùng nhập→video→library. Mỗi bước dùng config/prompt/provider/root snapshot và đúng Workspace; người dùng review trước khi áp kết quả vào cảnh.

## File và contract

- Tạo đề xuất: `src/TiToAI.Application/Workflows/{PipelineRunService.cs,PipelinePlanner.cs,PipelineOutputApplier.cs,ScriptOutputValidator.cs}`.
- PipelineRun/step/output metadata mới và migration qua persistence owner; DTO run/review/apply/batch enqueue do coordinator chốt trước T15 nếu ảnh hưởng Job.
- Script project-level, typed text result, project/scene revision và run/step/apply commands tại [v1.1 §4–6](../../decisions/execution-contract-v1.1.md). T02a review trước T15; T15 cung cấp snapshot/result validator cho adapter, không để T20 phụ thuộc T22. T22 tạo PipelineRun/Step và tiêu thụ contract này.

## Các bước triển khai

- [ ] Lưu project brief/revision, tạo run giữ brief/workflow snapshot; validate Script payload theo v1.1 §4 (scene key/order/name/description/target duration/imagePrompt). Video prompt là bước riêng; output AI là dữ liệu không đáng tin.
- [ ] Tạo run chốt workflow version; enqueue Script cấp project theo delta đã duyệt, user review scene drafts rồi apply trong transaction.
- [ ] Lập kế hoạch per-scene bằng prerequisites, không auto chạy bước user phải review; batch chọn scene trả lỗi thiếu input theo từng scene.
- [ ] Resolve ba provider từ Settings, VideoRender provider=museai pool global; enqueue chốt input/provider/config/root snapshot, T15 reserve mới chọn account/profile rảnh và tạo ExecutionBinding. Không account/TikTok mapping hoặc fallback provider.
- [ ] Video chỉ đủ selected image+saved video prompt+scene audio; MVP không project audio fallback. Ảnh/prompt nhập tay vẫn hợp lệ nếu đúng scope/type, không buộc sinh AI.
- [ ] Apply output kiểm scene revision; stale output vào lịch sử/chờ áp dụng, không ghi đè user đang chỉnh.
- [ ] Persist step→job/output IDs để restart tiếp tục từ bước thiếu; không enqueue trùng step khi event lặp.
- [ ] Success sau finalize thật; storage failure tiếp tục save result cũ, không chạy lại AI. Text output có boundary riêng đã chốt.
- [ ] create_run không tự enqueue; Script sceneIds rỗng, các bước scene phải chọn rõ. Batch validate toàn bộ trước transaction, một scene thiếu input thì không enqueue một phần; trả lỗi theo scene.
- [ ] apply_script chỉ vào project chưa có scene; result/run đã apply trả IDs cũ. Project đã có scene yêu cầu project mới trong MVP, không xóa dữ liệu cũ để thay kịch bản.
- [ ] Run/step→job mapping và idempotency commit cùng transaction; event lặp/restart không tạo job mới. Người dùng chủ động chạy lại mới tạo step generation mới, input revision mới kiểm trước apply.

## Mốc triển khai

1. T02a/T15 đã có project scope, snapshot và result validation; T18 có definition version/registry. Kiểm fixture Script không scene trước viết orchestrator.
2. Làm brief/save/create/get run và Script enqueue/review/apply trước; chứng minh apply hai lần không sinh scene trùng trên SQLite thật.
3. Nối Image/VideoPrompt/VideoRender theo prerequisites/input snapshot và binding cấp lúc reserve; kiểm 8 tổ hợp AI routing, 10 Workspace/1 ChatGPT/4 MuseAI, thiếu input và batch rollback.
4. Nối restart/reconnect/manual/stale output và UI T23 sau khi DTO/run state ổn định; evidence ghi mode thực tế, không gọi fake là website thật.

## Kiểm thử và nghiệm thu

Fake end-to-end ba Workspace, script không hợp lệ/scene list rỗng, batch thiếu audio, đổi Settings/template giữa steps, event lặp, scene sửa trong khi job chạy và crash giữa apply/result commit.

Checkpoint tồn tại sau restart, đúng root/account và không sinh trùng. Workflow/config chỉnh từ UI qua T18d; pipeline không hardcode provider theo task.

