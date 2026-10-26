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
  version: PromptVersionDto;
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
 * Trích xuất danh sách tên biến từ nội dung template theo cú pháp T10 ({tên_biến}).
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
      // T10 quy định tên biến hợp lệ: ^[a-zA-Z0-9_]+$
      if (/^[a-zA-Z0-9_]+$/.test(varName)) {
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
   * Lấy chi tiết Prompt Template và currentVersion của nó
   */
  async getTemplate(
    params: PromptGetTemplateRequest,
    transport: IPromptTransport = defaultPromptTransport,
    signal?: AbortSignal
  ): Promise<PromptGetTemplateResponseData> {
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
   * Tạo một phiên bản mới cho template (bất biến và đơn điệu)
   */
  async createVersion(
    request: PromptCreateVersionRequest,
    transport: IPromptTransport = defaultPromptTransport,
    signal?: AbortSignal
  ): Promise<PromptCreateVersionResponseData> {
    return await transport.invoke<PromptCreateVersionResponseData, PromptCreateVersionRequest>(
      "prompt.create_version",
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
