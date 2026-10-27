# TiToAI — đọc tài liệu từ đây

> **Sửa pipeline sau review PV:** dùng [plan remediation FIX-0–FIX-6](plans/2026-10-27-pv-remediation-fix-plan.md) và [prompt giao AI triển khai](plans/2026-10-27-pv-remediation-fix-prompt.md). [Review độc lập PV-R1–PV-R11](reviews/2026-10-27-audit-pv-independent-recheck.md) ghi các lỗi đã tái hiện và giới hạn của kết luận hoàn tất trước đó. Plan mới chưa được triển khai/nghiệm thu; STATUS/TASKS do coordinator cập nhật theo evidence.

> **Nhật ký và tiến trình tác vụ:** [Kế hoạch log và thanh tiến trình theo job](plans/JOB_LOG_PROGRESS_PLAN.md) mô tả lịch sử bền vững, cập nhật theo bước thực tế và tích hợp workflow. Đây là kế hoạch đề xuất, chưa triển khai.

> **Sửa form số clip/thời lượng:** đọc [plan clip và phân cảnh](plans/CLIP_DURATION_STORYBOARD_PLAN.md) và [prompt triển khai](plans/CLIP_DURATION_STORYBOARD_PROMPT.md). Một Scene là một clip; các phân cảnh theo giây nằm trong prompt; thời lượng theo capability provider. Ghép clip nằm ngoài phạm vi. Đây là spec bàn giao, chưa nghiệm thu code.

> **Tối ưu đã chốt:** [Plan sản xuất từ ảnh §3–4](plans/PRODUCT_IMAGE_VIDEO_WORKFLOW_PLAN.md) dùng đầu vào chung cấp project, lập cảnh có mục đích, tái sử dụng ảnh nền tảng và chỉ tạo biến thể cần thiết. Cảnh đủ input chạy tiếp ngay qua queue chung; sửa/tạo lại chỉ tác động phần phụ thuộc. UI chính là Chuẩn bị → Đang chạy → Kết quả, chi tiết ảnh/prompt mở khi cần. Đây là yêu cầu triển khai, chưa phải evidence đã hỗ trợ.


> **Đã chốt 2026-10-27:** [Luồng sản xuất từ ảnh](plans/PRODUCT_IMAGE_VIDEO_WORKFLOW_PLAN.md) tự chạy sau một lần Bắt đầu: lập nhiều cảnh theo prompt → ảnh → prompt chuyển động → video, không bắt duyệt trung gian. Audio tùy chọn; mẫu và sản phẩm dùng lại trong thư viện Workspace. Trong mode này, tự áp output hợp lệ bằng revision guard; yêu cầu audio bắt buộc/duyệt từng bước bên dưới là baseline cũ đã được thay trong phạm vi này. Contract/version/validator/adapter còn phải triển khai đồng bộ; chưa là evidence code hỗ trợ. Lỗi, capability không tương thích, login, xung đột hoặc uncertainty vẫn dừng đúng policy.


> **Delta 2026-10-27 — sản xuất từ ảnh:** [Plan ảnh mẫu/sản phẩm → ảnh đã duyệt → prompt chuyển động → video](plans/PRODUCT_IMAGE_VIDEO_WORKFLOW_PLAN.md) thay bố cục Project/Scene dạng bảng làm luồng chính và bổ sung ba chế độ đầu vào. Luồng ảnh đi thẳng, không bắt tạo kịch bản trước; audio tùy chọn và tự động chạy theo quyết định mới. Đây là spec bàn giao, chưa nghiệm thu code.

Yêu cầu nội dung mới ngày 2026-10-27: [hai luồng tạo nội dung qua AI web](plans/AI_WEB_CONTENT_CREATION_PLAN.md) và [prompt triển khai/hướng dẫn AI web](plans/AI_WEB_CONTENT_CREATION_PROMPTS.md). Hỗ trợ người dùng đưa ý tưởng hoặc ChatGPT/Gemini Web phân tích analytics để đề xuất; đây là spec bổ sung, chưa nghiệm thu code.

1. [STATUS](agents/STATUS.md): chỉ dẫn mới nhất, phạm vi làm việc, checkpoint và giới hạn evidence.
2. [TASKS](agents/TASKS.md): dependency/trạng thái, Q0–Q4 và task sản phẩm còn lại.
3. [Queue và Settings 1.2](plans/QUEUE_EXECUTION_SETTINGS_PLAN.md): quyết định hiện hành, ownership và A01–A13 để AI khác triển khai.
4. [Review bàn giao](reviews/2026-10-26-handoff-recheck.md): sáu findings H1–H6, kiểm tra đã/chưa chạy và tình trạng source đang dở.

Đọc theo task: [Settings](plans/SETTINGS_PLAN.md), [UI/Browser](plans/UI_BROWSER_PLAN.md), [Theme](plans/UI_THEME_PLAN.md), [mục lục tính năng](plans/implementation/README.md). Kỹ thuật/ownership/model: [ENGINEERING_RULES](agents/ENGINEERING_RULES.md), [ORCHESTRATION](agents/ORCHESTRATION.md).

[Rà soát phạm vi plan 2026-10-26](reviews/2026-10-26-plan-coverage-audit.md): phần T12 ngoài Q3, content TikTok chưa vào bảng điều phối/gate cuối, evidence Q0 cần liên kết và các kiểm tra hoãn sang release. Báo cáo không thay trạng thái trong TASKS.

SOURCE_PLAN và contract v1.1 giữ baseline; plan 1.2 có ưu tiên trong phần queue/account/Settings đã thay. STATUS-HISTORY/TASKS-HISTORY giữ evidence và phiên cũ, chỉ đọc khi truy nguyên. Báo cáo review/log cũ không bị xóa; DONE/PASS theo revision lịch sử không chứng minh bản hiện tại đã đạt.
