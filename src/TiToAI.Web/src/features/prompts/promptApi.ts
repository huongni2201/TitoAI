/**
 * TiToAI — Prompt Engine API Adapter & Types (Gate G2 / T10)
 * Tuân thủ contracts.md v1.0.4, PromptDtos.cs và PromptHandlers.cs
 */

import { bridgeClient } from "../../services/bridgeClient.ts";
import type { G1Command } from "../../contracts/bridge.ts";

/**
 * Ba mục đích prompt hợp lệ theo T10 backend (PromptTemplateService.ValidPurposes)
 */
export type PromptPurpose = "Script" | "Image" | "VideoPrompt";

export const PROMPT_PURPOSES: readonly PromptPurpose[] = [
  "Script",
  "Image",
  "VideoPrompt",
] as const;

export interface PromptVariableDefinition {
  name: string;
  description?: string | null;
  isRequired: boolean;
  defaultValue?: string | null;
  example?: string | null;
}

export interface PromptTemplateDto {
  templateId: string;
  purpose: PromptPurpose | string;
  workspaceId: string | null;
  isOverride: boolean;
  currentVersionId: string | null;
  currentVersionNumber: number | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface PromptVersionDto {
  versionId: string;
  templateId: string;
  versionNumber: number;
  templateText: string;
  variableDefinitionsJson: string;
  changeDescription: string | null;
  createdAtUtc: string;
}

export interface PromptListTemplatesRequest {
  workspaceId?: string | null;
}

export interface PromptListTemplatesResponseData {
  templates: PromptTemplateDto[];
}

export interface PromptGetTemplateRequest {
  templateId?: string | null;
  purpose?: string | null;
  workspaceId?: string | null;
}

export interface PromptGetTemplateResponseData {
  template: PromptTemplateDto;
  currentVersion: PromptVersionDto | null;
}

export interface PromptListVersionsRequest {
  templateId: string;
}

export interface PromptListVersionsResponseData {
  versions: PromptVersionDto[];
}

export interface PromptCreateVersionRequest {
  purpose: string;
  workspaceId?: string | null;
  templateText: string;
  variableDefinitionsJson?: string | null;
  changeDescription?: string | null;
}

export interface PromptCreateVersionResponseData {
  template: PromptTemplateDto;
  prompt: PromptVersionDto;
}

export interface PromptPreviewRequest {
  templateId?: string | null;
  purpose?: string | null;
  workspaceId?: string | null;
  templateText?: string | null;
  variables?: Record<string, string> | null;
}

export interface PromptPreviewResponseData {
  renderedText: string;
  templateId: string | null;
  versionId: string | null;
  variableCount: number;
  isOverride: boolean;
}

export interface PromptRollbackVersionRequest {
  templateId: string;
  targetVersionId: string;
}

export interface PromptRollbackVersionResponseData {
  template: PromptTemplateDto;
  currentVersion: PromptVersionDto;
}

export interface PromptDeleteOverrideRequest {
  templateId: string;
}

export interface PromptDeleteOverrideResponseData {
  templateId: string;
  deleted: boolean;
}

export interface PromptRenderSnapshotDto {
  templateId: string;
  versionId: string;
  renderedText: string;
  variables: Record<string, string>;
  capturedAtUtc: string;
}

/**
 * Interface cho cơ chế vận chuyển lệnh bridge.
 * Cho phép các component UI hoặc test suites truyền transport giả lập (test fixture)
 * hoặc sử dụng bridgeClient mặc định.
 */
export interface IPromptTransport {
  invoke<TData = unknown, TPayload = unknown>(
    command: string,
    payload: TPayload,
    options?: { workspaceId?: string | null; signal?: AbortSignal; timeoutMs?: number }
  ): Promise<TData>;
}

/**
 * Transport mặc định sử dụng bridgeClient qua WebView2.
 */
export const defaultPromptTransport: IPromptTransport = {
  invoke: async <TData = unknown, TPayload = unknown>(
    command: string,
    payload: TPayload,
    options?: { workspaceId?: string | null; signal?: AbortSignal; timeoutMs?: number }
  ): Promise<TData> => {
    return await bridgeClient.invoke<TData, TPayload>(
      command as G1Command,
      payload,
      options
    );
  },
};

/**
 * Loại bỏ dấu tiếng Việt chuẩn xác (hỗ trợ cả tổ hợp NFD và ký tự đ/Đ).
 */
export function removeVietnameseAccents(str: string): string {
  return str
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .replace(/đ/g, "d")
    .replace(/Đ/g, "D");
}

/**
 * Chuẩn hóa tên biến theo quy tắc §2.2:
 * - Không dùng dấu tiếng Việt.
 * - Không chứa khoảng trắng.
 * - Viết thường, dùng dấu _ phân tách từ.
 * - Chỉ cho phép chữ cái a-z, chữ số 0-9 và _.
 * - Ký tự đầu tiên bắt buộc là chữ cái a-z.
 *
 * Ví dụ:
 *  - "chủ_đề" -> "chu_de"
 *  - "sản phẩm" -> "san_pham"
 *  - "Đối Tượng Khách Hàng" -> "doi_tuong_khach_hang"
 *  - "phong cách video" -> "phong_cach_video"
 *  - "thời_lượng" -> "thoi_luong"
 */
export function normalizeVariableName(raw: string): string {
  let clean = removeVietnameseAccents(raw).toLowerCase();
  clean = clean.replace(/[^a-z0-9_]+/g, "_");
  clean = clean.replace(/_+/g, "_");
  clean = clean.replace(/^_+|_+$/g, "");
  if (/^[0-9]/.test(clean)) {
    clean = "v_" + clean;
  }
  if (!clean) {
    clean = "bien";
  }
  return clean;
}

/**
 * Kiểm tra tên biến có đúng chuẩn {ten_bien}:
 * Bắt đầu bằng a-z, chỉ gồm a-z, 0-9 và _
 */
export function isValidVariableName(name: string): boolean {
  return /^[a-z][a-z0-9_]*$/.test(name);
}

export interface NonStandardVariableInfo {
  original: string;
  normalized: string;
}

/**
 * Phát hiện tất cả biến trong template chưa đạt chuẩn {ten_bien}
 * (ví dụ có dấu tiếng Việt, có khoảng trắng, viết hoa, v.v.)
 */
export function detectNonStandardVariables(templateText: string | null | undefined): NonStandardVariableInfo[] {
  if (!templateText) return [];

  const result: NonStandardVariableInfo[] = [];
  const seenOriginal = new Set<string>();
  const len = templateText.length;
  let i = 0;

  while (i < len) {
    if (templateText[i] === "{") {
      if (i + 1 < len && templateText[i + 1] === "{") {
        i += 2;
        continue;
      }
      const close = templateText.indexOf("}", i + 1);
      const nextOpen = templateText.indexOf("{", i + 1);
      if (nextOpen !== -1 && (close === -1 || nextOpen < close)) {
        i = nextOpen;
        continue;
      }
      if (close === -1) break;

      const raw = templateText.substring(i + 1, close).trim();
      if (raw.length > 0 && !isValidVariableName(raw)) {
        const normalized = normalizeVariableName(raw);
        if (isValidVariableName(normalized) && !seenOriginal.has(raw)) {
          seenOriginal.add(raw);
          result.push({
            original: raw,
            normalized,
          });
        }
      }
      i = close + 1;
    } else {
      i++;
    }
  }

  return result;
}

/**
 * Tự động chuẩn hóa toàn bộ placeholder chưa đạt chuẩn trong template:
 * Thay thế {chủ_đề} -> {chu_de}, {sản phẩm} -> {san_pham}, v.v.
 * Ngăn chặn xung đột tên khi 2 biến khác nhau chuẩn hóa ra cùng tên.
 */
export function normalizeTemplateText(templateText: string): {
  normalizedText: string;
  replacements: NonStandardVariableInfo[];
} {
  const nonStandard = detectNonStandardVariables(templateText);
  if (nonStandard.length === 0) {
    return { normalizedText: templateText, replacements: [] };
  }

  // Xử lý xung đột tên: nếu có 2 biến khác nhau chuẩn hóa ra cùng 1 tên
  const usedNames = new Set<string>();
  const mapping = new Map<string, string>();

  // Trước tiên ghi nhận các tên biến đã chuẩn sẵn
  const allVars = extractTemplateVariables(templateText);
  for (const v of allVars) {
    if (isValidVariableName(v)) {
      usedNames.add(v);
    }
  }

  for (const item of nonStandard) {
    let candidate = item.normalized;
    let suffix = 2;
    while (usedNames.has(candidate) && !mapping.has(item.original)) {
      candidate = `${item.normalized}_${suffix++}`;
    }
    usedNames.add(candidate);
    mapping.set(item.original, candidate);
  }

  let result = templateText;
  const replacements: NonStandardVariableInfo[] = [];

  for (const item of nonStandard) {
    const targetName = mapping.get(item.original) || item.normalized;
    // Thay thế chính xác {original} bằng {targetName} (không chạm vào {{ hoặc }})
    // Sử dụng replace an toàn theo regex
    const escapedOrig = item.original.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    const regex = new RegExp(`\\{${escapedOrig}\\}`, "g");
    result = result.replace(regex, `{${targetName}}`);
    replacements.push({
      original: item.original,
      normalized: targetName,
    });
  }

  return {
    normalizedText: result,
    replacements,
  };
}

/**
 * Trích xuất danh sách tên biến từ nội dung template theo cú pháp {tên_biến}.
 * Bỏ qua cú pháp thoát {{ và trả về danh sách tên biến không trùng lặp theo thứ tự xuất hiện.
 */
export function extractTemplateVariables(templateText: string | null | undefined): string[] {
  if (!templateText) return [];

  const detected: string[] = [];
  const seen = new Set<string>();
  const len = templateText.length;
  let i = 0;

  while (i < len) {
    const c = templateText[i];
    if (c === "{") {
      // Bỏ qua escape {{
      if (i + 1 < len && templateText[i + 1] === "{") {
        i += 2;
        continue;
      }

      const closeIndex = templateText.indexOf("}", i + 1);
      const nextOpenIndex = templateText.indexOf("{", i + 1);

      // Kiểm tra lồng ngoặc không hợp lệ
      if (nextOpenIndex !== -1 && (closeIndex === -1 || nextOpenIndex < closeIndex)) {
        i = nextOpenIndex;
        continue;
      }

      if (closeIndex === -1) {
        break;
      }

      const varName = templateText.substring(i + 1, closeIndex).trim();
      // Cho phép chữ Unicode để hỗ trợ cả tên biến chuẩn và tên biến cũ có dấu
      if (/^[\p{L}\p{M}\p{N}_]+$/u.test(varName)) {
        if (!seen.has(varName)) {
          seen.add(varName);
          detected.push(varName);
        }
      }

      i = closeIndex + 1;
    } else {
      i++;
    }
  }

  return detected;
}

export interface ClientPromptRenderResult {
  renderedText: string;
  missingVariables: string[];
  variableCount: number;
  detectedVariables: string[];
}

/**
 * Frontend Prompt Rendering — Thực hiện ghép tất định biến số vào template tại client.
 * Thay thế các {ten_bien} bằng giá trị người dùng nhập, giữ nguyên {{ và }} thoát.
 */
export function renderPromptTemplateClient(
  templateText: string,
  variables: Record<string, string>
): ClientPromptRenderResult {
  const detected = extractTemplateVariables(templateText);
  const missing: string[] = [];

  for (const v of detected) {
    const val = variables[v];
    if (val === undefined || val === null || String(val).trim() === "") {
      missing.push(v);
    }
  }

  // Tạm thay thế {{ và }} bằng token bảo vệ
  const ESC_OPEN = "___TITO_ESC_LBRACE___";
  const ESC_CLOSE = "___TITO_ESC_RBRACE___";

  let working = templateText
    .replace(/\{\{/g, ESC_OPEN)
    .replace(/\}\}/g, ESC_CLOSE);

  // Thay thế từng placeholder {ten_bien}
  working = working.replace(/\{([^{}]+)\}/g, (match, rawName) => {
    const trimmed = rawName.trim();
    if (variables[trimmed] !== undefined && variables[trimmed] !== null) {
      return String(variables[trimmed]);
    }
    // Nếu chưa nhập giá trị, giữ nguyên match hoặc trả về chuỗi rỗng
    return match;
  });

  // Khôi phục escape {{ -> {, }} -> }
  working = working
    .replace(new RegExp(ESC_OPEN, "g"), "{")
    .replace(new RegExp(ESC_CLOSE, "g"), "}");

  return {
    renderedText: working,
    missingVariables: missing,
    variableCount: detected.length,
    detectedVariables: detected,
  };
}

/**
 * Parse an toàn chuỗi JSON định nghĩa biến số
 */
export function parseVariableDefinitions(
  json: string | null | undefined
): PromptVariableDefinition[] {
  if (!json) return [];
  try {
    const parsed = JSON.parse(json);
    if (Array.isArray(parsed)) {
      return parsed.map((item) => ({
        name: String(item.name || "").trim(),
        description: item.description ? String(item.description) : null,
        isRequired: item.isRequired !== false,
        defaultValue: item.defaultValue ? String(item.defaultValue) : null,
        example: item.example ? String(item.example) : null,
      })).filter((v) => v.name.length > 0);
    }
  } catch {
    // Json không hợp lệ trả về mảng rỗng an toàn
  }
  return [];
}

/**
 * Adapter API tương tác với 7 lệnh Prompt Engine của T10
 */
export const promptApi = {
  /**
   * Lấy danh sách Prompt Templates theo Workspace hoặc Toàn cục
   */
  async listTemplates(
    workspaceId?: string | null,
    transport: IPromptTransport = defaultPromptTransport,
    signal?: AbortSignal
  ): Promise<PromptTemplateDto[]> {
    const data = await transport.invoke<PromptListTemplatesResponseData, PromptListTemplatesRequest>(
      "prompt.list_templates",
      { workspaceId: workspaceId ?? null },
      { workspaceId: workspaceId ?? null, signal }
    );
    return data?.templates ?? [];
  },

  /**
   * Lấy chi tiết Prompt Template và currentVersion của nó.
   * Hỗ trợ gọi theo object params { templateId, purpose, workspaceId } hoặc positional (templateId, purpose, workspaceId).
   */
  async getTemplate(
    paramsOrTemplateId?: PromptGetTemplateRequest | string | null,
    purposeOrTransport?: string | IPromptTransport | null,
    workspaceIdOrSignal?: string | AbortSignal | null,
    maybeTransport?: IPromptTransport,
    maybeSignal?: AbortSignal
  ): Promise<PromptGetTemplateResponseData> {
    let params: PromptGetTemplateRequest;
    let transport: IPromptTransport = defaultPromptTransport;
    let signal: AbortSignal | undefined;

    if (paramsOrTemplateId && typeof paramsOrTemplateId === "object" && !("invoke" in paramsOrTemplateId)) {
      params = paramsOrTemplateId as PromptGetTemplateRequest;
      if (purposeOrTransport && typeof purposeOrTransport === "object" && "invoke" in purposeOrTransport) {
        transport = purposeOrTransport as IPromptTransport;
      }
      if (workspaceIdOrSignal && workspaceIdOrSignal instanceof AbortSignal) {
        signal = workspaceIdOrSignal;
      }
    } else {
      params = {
        templateId: typeof paramsOrTemplateId === "string" ? paramsOrTemplateId : null,
        purpose: typeof purposeOrTransport === "string" ? purposeOrTransport : null,
        workspaceId: typeof workspaceIdOrSignal === "string" ? workspaceIdOrSignal : null,
      };
      if (maybeTransport && typeof maybeTransport === "object" && "invoke" in maybeTransport) {
        transport = maybeTransport;
      }
      if (maybeSignal && maybeSignal instanceof AbortSignal) {
        signal = maybeSignal;
      }
    }

    return await transport.invoke<PromptGetTemplateResponseData, PromptGetTemplateRequest>(
      "prompt.get_template",
      params,
      { workspaceId: params.workspaceId ?? null, signal }
    );
  },

  /**
   * Lấy lịch sử tất cả các phiên bản của một template
   */
  async listVersions(
    templateId: string,
    workspaceId?: string | null,
    transport: IPromptTransport = defaultPromptTransport,
    signal?: AbortSignal
  ): Promise<PromptVersionDto[]> {
    const data = await transport.invoke<PromptListVersionsResponseData, PromptListVersionsRequest>(
      "prompt.list_versions",
      { templateId },
      { workspaceId: workspaceId ?? null, signal }
    );
    return data?.versions ?? [];
  },

  /**
   * Lưu nội dung hiện hành của template.
   */
  async createVersion(
    request: PromptCreateVersionRequest,
    transport: IPromptTransport = defaultPromptTransport,
    signal?: AbortSignal
  ): Promise<PromptCreateVersionResponseData> {
    return await transport.invoke<PromptCreateVersionResponseData, PromptCreateVersionRequest>(
      "prompt.save_template",
      request,
      { workspaceId: request.workspaceId ?? null, signal }
    );
  },

  /**
   * Render xem trước nội dung prompt (tính toán biến số qua backend T10)
   * Hỗ trợ render cả bản nháp (templateText) chưa lưu
   */
  async preview(
    request: PromptPreviewRequest,
    transport: IPromptTransport = defaultPromptTransport,
    signal?: AbortSignal
  ): Promise<PromptPreviewResponseData> {
    return await transport.invoke<PromptPreviewResponseData, PromptPreviewRequest>(
      "prompt.preview",
      request,
      { workspaceId: request.workspaceId ?? null, signal }
    );
  },

  /**
   * Khôi phục con trỏ current_version_id về phiên bản trước (Rollback)
   */
  async rollbackVersion(
    request: PromptRollbackVersionRequest,
    workspaceId?: string | null,
    transport: IPromptTransport = defaultPromptTransport,
    signal?: AbortSignal
  ): Promise<PromptRollbackVersionResponseData> {
    return await transport.invoke<PromptRollbackVersionResponseData, PromptRollbackVersionRequest>(
      "prompt.rollback_version",
      request,
      { workspaceId: workspaceId ?? null, signal }
    );
  },

  /**
   * Xóa bản ghi đè Workspace, quay về sử dụng Global template mặc định
   */
  async deleteOverride(
    templateId: string,
    workspaceId?: string | null,
    transport: IPromptTransport = defaultPromptTransport,
    signal?: AbortSignal
  ): Promise<PromptDeleteOverrideResponseData> {
    return await transport.invoke<PromptDeleteOverrideResponseData, PromptDeleteOverrideRequest>(
      "prompt.delete_override",
      { templateId },
      { workspaceId: workspaceId ?? null, signal }
    );
  },
};
