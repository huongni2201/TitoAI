/**
 * TiToAI — Quản lý lưu trữ nhiều Prompt và Lựa chọn Prompt Active theo Workspace / Purpose
 */

import type { PromptPurpose } from "./promptApi.ts";

export interface CustomPromptItem {
  id: string;
  name: string;
  purpose: PromptPurpose;
  templateText: string;
  variableDefinitionsJson?: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  backendTemplateId?: string | null;
  isPreset?: boolean;
}

const STORAGE_KEY_PREFIX = "titoai_user_prompts_";
const ACTIVE_PROMPT_PREFIX = "titoai_active_prompt_";

/**
 * Các prompt mẫu chuẩn ban đầu cho từng mục đích
 */
export const DEFAULT_PRESET_PROMPTS: Record<PromptPurpose, CustomPromptItem[]> = {
  Script: [
    {
      id: "preset-script-tiktok-standard",
      name: "Kịch bản TikTok chuẩn (Hook + Story + CTA)",
      purpose: "Script",
      templateText:
        "Bạn là biên kịch video ngắn cho kênh được cung cấp. Phát triển USER_BRIEF hoặc SELECTED_IDEA thành kịch bản có hook rõ, diễn biến hợp lý và kết thúc phù hợp.\n\n" +
        "Tự đề xuất bối cảnh, nhân vật, phong cách và thời lượng còn thiếu, ghi rõ các giả định đáng kể. Không yêu cầu người dùng điền biến kỹ thuật. Giữ nhân vật, trang phục, đạo cụ và bối cảnh nhất quán giữa các cảnh. Chia cảnh sao cho hành động có thể tạo thành video, tránh nhồi quá nhiều diễn biến trong một cảnh.\n\n" +
        "Mỗi cảnh có tên, mô tả hình ảnh/hành động, thời lượng mục tiêu và imagePrompt theo OUTPUT_CONTRACT.\n\n" +
        "Trả kèm một bài đăng TikTok cho toàn video: caption có hook, nội dung và CTA tự nhiên, cùng hashtags liên quan {chủ_đề}. Viết tiếng Việt, riêng prompt tạo ảnh bằng tiếng Anh nếu hợp đồng yêu cầu. Tuân thủ OUTPUT_CONTRACT của ứng dụng.",
      createdAtUtc: new Date().toISOString(),
      updatedAtUtc: new Date().toISOString(),
      isPreset: true,
    },
    {
      id: "preset-script-ideas-analysis",
      name: "Phân tích dữ liệu & Đề xuất 5 ý tưởng video",
      purpose: "Script",
      templateText:
        "Bạn là chuyên gia chiến lược nội dung video ngắn cho kênh được cung cấp.\n" +
        "Hãy phân tích dữ liệu kênh và đề xuất 5 ý tưởng video có thể triển khai về chủ đề {chủ_đề}.\n\n" +
        "Sử dụng CHANNEL_CONTEXT, ANALYTICS_CONTEXT và RECENT_IDEAS trong phần dữ liệu. Nêu kỳ phân tích, nguồn, số video và dữ liệu còn thiếu. Phân biệt quan sát có bằng chứng với suy luận sáng tạo.\n\n" +
        "Mỗi ý tưởng gồm tiêu đề, hook, nội dung chính, thời lượng mục tiêu gợi ý, lý do phù hợp, evidenceRefs nếu có, giả thuyết cần kiểm chứng và một bài đăng TikTok gồm caption hoàn chỉnh cùng hashtags liên quan. Trả tiếng Việt và đúng OUTPUT_CONTRACT.",
      createdAtUtc: new Date().toISOString(),
      updatedAtUtc: new Date().toISOString(),
      isPreset: true,
    },
    {
      id: "preset-script-product-review",
      name: "Kịch bản Review sản phẩm / Bán hàng viral",
      purpose: "Script",
      templateText:
        "Bạn là nhà sáng tạo nội dung review sản phẩm chuyên nghiệp trên TikTok.\n" +
        "Tạo kịch bản review chân thực và cuốn hút cho sản phẩm: {tên_sản_phẩm} với phong cách {phong_cách}.\n\n" +
        "Cấu trúc 3 phần bắt buộc:\n" +
        "1. Hook 3 giây đầu: Nêu vấn đề bức xúc hoặc trải nghiệm bất ngờ gây tò mò.\n" +
        "2. Thân bài (15-40s): Mở hộp, trải nghiệm tính năng thực tế, nêu 2 ưu điểm nổi trội và 1 lưu ý chân thực.\n" +
        "3. Kết bài & CTA (5-10s): Đánh giá tổng quan, gợi ý đối tượng phù hợp và lời kêu gọi hành động xem thêm ở giỏ hàng/bio.",
      createdAtUtc: new Date().toISOString(),
      updatedAtUtc: new Date().toISOString(),
      isPreset: true,
    },
  ],
  Image: [
    {
      id: "preset-image-photorealistic",
      name: "Tạo ảnh TikTok chân thực (Photorealistic 8K)",
      purpose: "Image",
      templateText:
        "A hyper-realistic cinematic photo for TikTok video scene, depicting {chủ_đề}, shot on 35mm lens, f/1.8 aperture, natural lighting, high dynamic range, 8k resolution, detailed texture and atmosphere, professional color grading, photorealistic, cinematic composition.",
      createdAtUtc: new Date().toISOString(),
      updatedAtUtc: new Date().toISOString(),
      isPreset: true,
    },
    {
      id: "preset-image-3d-render",
      name: "Tạo ảnh Phong cách 3D / Hoạt hình Pixar",
      purpose: "Image",
      templateText:
        "A stylized 3D render animation style image of {chủ_đề}, vibrant colors, volumetric lighting, Pixar and Disney inspired aesthetic, clean background, 4k render, highly detailed character design, charming expression.",
      createdAtUtc: new Date().toISOString(),
      updatedAtUtc: new Date().toISOString(),
      isPreset: true,
    },
  ],
  VideoPrompt: [
    {
      id: "preset-video-cinematic-camera",
      name: "Prompt Video tiếng Anh điện ảnh (Cinematic Camera)",
      purpose: "VideoPrompt",
      templateText:
        "Bạn là chuyên gia viết prompt cho công cụ tạo video AI.\n" +
        "Từ ý tưởng/kịch bản, cảnh đã duyệt, hồ sơ nhân vật và ảnh tham chiếu được cung cấp, viết prompt video hoàn chỉnh bằng tiếng Anh cho từng cảnh về {chủ_đề}.\n\n" +
        "Tự chọn chi tiết phù hợp còn thiếu về bối cảnh, hành động, góc máy, chuyển động camera, ánh sáng và không khí. Không yêu cầu người dùng điền biến kỹ thuật. Ưu tiên hành động cụ thể, trình tự chuyển động rõ và khả thi trong thời lượng mục tiêu. Duy trì đặc điểm nhân vật, trang phục, đạo cụ và phong cách giữa cảnh.\n\n" +
        "Trả promptEn gắn đúng scene key theo OUTPUT_CONTRACT. Tuân thủ OUTPUT_CONTRACT.",
      createdAtUtc: new Date().toISOString(),
      updatedAtUtc: new Date().toISOString(),
      isPreset: true,
    },
    {
      id: "preset-video-dynamic-motion",
      name: "Prompt Video Chuyển động nhanh & Kịch tính",
      purpose: "VideoPrompt",
      templateText:
        "Dynamic camera tracking shot, high motion intensity, dramatic rim lighting, cinematic depth of field, depicting {hành_động} in rapid pacing, fast transitions, motion blur, 4k high quality, immersive soundstage feeling.",
      createdAtUtc: new Date().toISOString(),
      updatedAtUtc: new Date().toISOString(),
      isPreset: true,
    },
  ],
};

function getStorageScope(workspaceId?: string | null): string {
  return workspaceId ? `ws_${workspaceId}` : "global";
}

/**
 * Lấy danh sách custom prompts đã lưu theo workspace
 */
export function getStoredPrompts(workspaceId?: string | null): CustomPromptItem[] {
  if (typeof window === "undefined" || !window.localStorage) {
    return [];
  }
  try {
    const raw = window.localStorage.getItem(`${STORAGE_KEY_PREFIX}${getStorageScope(workspaceId)}`);
    if (!raw) return [];
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed : [];
  } catch (err) {
    console.warn("Lỗi khi đọc danh sách prompts từ localStorage:", err);
    return [];
  }
}

/**
 * Lưu danh sách custom prompts theo workspace
 */
export function saveStoredPrompts(prompts: CustomPromptItem[], workspaceId?: string | null): void {
  if (typeof window === "undefined" || !window.localStorage) return;
  try {
    window.localStorage.setItem(
      `${STORAGE_KEY_PREFIX}${getStorageScope(workspaceId)}`,
      JSON.stringify(prompts)
    );
  } catch (err) {
    console.warn("Lỗi khi lưu danh sách prompts vào localStorage:", err);
  }
}

/**
 * Lấy ID prompt đang được chọn làm active cho purpose và workspace
 */
export function getActivePromptId(purpose: PromptPurpose, workspaceId?: string | null): string | null {
  if (typeof window === "undefined" || !window.localStorage) return null;
  try {
    return window.localStorage.getItem(
      `${ACTIVE_PROMPT_PREFIX}${getStorageScope(workspaceId)}_${purpose}`
    );
  } catch {
    return null;
  }
}

/**
 * Lưu lựa chọn prompt active cho lần sau
 */
export function setActivePromptId(
  purpose: PromptPurpose,
  promptId: string | null,
  workspaceId?: string | null
): void {
  if (typeof window === "undefined" || !window.localStorage) return;
  try {
    const key = `${ACTIVE_PROMPT_PREFIX}${getStorageScope(workspaceId)}_${purpose}`;
    if (promptId) {
      window.localStorage.setItem(key, promptId);
    } else {
      window.localStorage.removeItem(key);
    }
  } catch (err) {
    console.warn("Lỗi khi lưu active prompt vào localStorage:", err);
  }
}
