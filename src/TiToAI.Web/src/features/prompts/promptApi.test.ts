/**
 * TiToAI — Kiểm thử Đơn vị cho Prompt Engine API Adapter & Logic Biến Số
 * Chạy trực tiếp qua Node.js: node --experimental-strip-types src/TiToAI.Web/src/features/prompts/promptApi.test.ts
 */

import { register } from "node:module";
import type { IPromptTransport } from "./promptApi.ts";

// Cấu hình module loader hook cho Node ESM để phân giải relative import TypeScript (.ts, .tsx)
const loaderCode = `
export async function resolve(specifier, context, nextResolve) {
  try {
    return await nextResolve(specifier, context);
  } catch (err) {
    if (specifier.startsWith('.')) {
      for (const ext of ['.ts', '.tsx', '/index.ts']) {
        try {
          return await nextResolve(specifier + ext, context);
        } catch {}
      }
    }
    throw err;
  }
}
`;
register("data:text/javascript," + encodeURIComponent(loaderCode), import.meta.url);

// Mock WebView2 Window trước khi nạp module
(globalThis as unknown as Record<string, unknown>).window = {
  chrome: {
    webview: {
      addEventListener() {},
      removeEventListener() {},
      postMessage() {},
    },
  },
};

const {
  extractTemplateVariables,
  parseVariableDefinitions,
  promptApi,
  normalizeVariableName,
  isValidVariableName,
  detectNonStandardVariables,
  normalizeTemplateText,
  renderPromptTemplateClient,
} = await import("./promptApi.ts");

// Bộ hàm kiểm chứng tự túc (self-contained assertions) không phụ thuộc @types/node
function assertEqual<T>(actual: T, expected: T, msg?: string): void {
  if (actual !== expected) {
    throw new Error(
      `${msg || "Assertion failed"}: Expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`
    );
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, msg?: string): void {
  const actualStr = JSON.stringify(actual);
  const expectedStr = JSON.stringify(expected);
  if (actualStr !== expectedStr) {
    throw new Error(
      `${msg || "Assertion failed"}: Expected ${expectedStr}, got ${actualStr}`
    );
  }
}

function assertTrue(value: boolean, msg?: string): void {
  if (!value) {
    throw new Error(msg || "Expected true, got false");
  }
}

async function assertRejects(fn: () => Promise<unknown>, expectedMsg: string): Promise<void> {
  try {
    await fn();
    throw new Error("Expected function to reject, but it succeeded.");
  } catch (err: unknown) {
    const actualMsg = err instanceof Error ? err.message : String(err);
    if (!actualMsg.includes(expectedMsg)) {
      throw new Error(`Expected error message containing "${expectedMsg}", got "${actualMsg}"`);
    }
  }
}

console.log("=== BẮT ĐẦU KIỂM THỬ PROMPT API & VARIABLE LOGIC ===");

// 1. Kiểm thử trích xuất biến số
{
  console.log("Test 1: Trích xuất biến số từ template rỗng/null...");
  assertDeepEqual(extractTemplateVariables(""), []);
  assertDeepEqual(extractTemplateVariables(null), []);
  assertDeepEqual(extractTemplateVariables(undefined), []);
  console.log("  -> PASS");
}

{
  console.log("Test 2: Trích xuất biến số thông thường...");
  const text = "Video về {chu_de} với phong cách {phong_cach} và thời lượng {thoi_luong}s.";
  const vars = extractTemplateVariables(text);
  assertDeepEqual(vars, ["chu_de", "phong_cach", "thoi_luong"]);
  console.log("  -> PASS");
}

{
  console.log("Test 3: Bỏ qua cú pháp thoát {{...");
  const text = "Đây là {{escaped_var}} không tính, nhưng {valid_var} là biến thật.";
  const vars = extractTemplateVariables(text);
  assertDeepEqual(vars, ["valid_var"]);
  console.log("  -> PASS");
}

{
  console.log("Test 4: Loại bỏ biến trùng lặp (deduplication)...");
  const text = "Chủ đề {topic} xuất hiện ở đây {topic} và {style} rồi lại {topic}.";
  const vars = extractTemplateVariables(text);
  assertDeepEqual(vars, ["topic", "style"]);
  console.log("  -> PASS");
}

{
  console.log("Test 5: Xử lý an toàn ngoặc mở lồng hoặc không đóng...");
  const text = "Ngoặc lỗi {chua_dong va {bien_hop_le} tiep theo.";
  const vars = extractTemplateVariables(text);
  assertTrue(Array.isArray(vars));
  assertTrue(vars.includes("bien_hop_le"));
  console.log("  -> PASS");
}

{
  console.log("Test 6: Chỉ chấp nhận tên biến hợp lệ [a-zA-Z0-9_]...");
  const text = "Biến chuẩn {var_1} và biến lỗi {var-dash} {var space}.";
  const vars = extractTemplateVariables(text);
  assertDeepEqual(vars, ["var_1"]);
  console.log("  -> PASS");
}

// 2. Kiểm thử parse variable definitions
{
  console.log("Test 7: Parse JSON định nghĩa biến số...");
  assertDeepEqual(parseVariableDefinitions(""), []);
  assertDeepEqual(parseVariableDefinitions("{invalid json"), []);

  const json = JSON.stringify([
    {
      name: "chu_de",
      description: "Chủ đề chính",
      isRequired: true,
      defaultValue: "AI",
      example: "Video AI",
    },
    {
      name: "phong_cach",
      isRequired: false,
    },
  ]);

  const defs = parseVariableDefinitions(json);
  assertEqual(defs.length, 2);
  assertEqual(defs[0].name, "chu_de");
  assertEqual(defs[0].isRequired, true);
  assertEqual(defs[0].defaultValue, "AI");
  assertEqual(defs[1].name, "phong_cach");
  assertEqual(defs[1].isRequired, false);
  console.log("  -> PASS");
}

// 3. Kiểm thử Prompt API Adapter với Mock Transport
{
  console.log("Test 8: listTemplates gọi lệnh 'prompt.list_templates'...");
  let capturedCmd = "";
  let capturedPayload: unknown = null;

  const mockTransport: IPromptTransport = {
    invoke: async <TData>(command: string, payload: unknown): Promise<TData> => {
      capturedCmd = command;
      capturedPayload = payload;
      const res = {
        templates: [
          {
            templateId: "tpl-1",
            purpose: "Script",
            workspaceId: null,
            isOverride: false,
            currentVersionId: "v-1",
            currentVersionNumber: 1,
            createdAtUtc: "2026-10-26T00:00:00Z",
            updatedAtUtc: "2026-10-26T00:00:00Z",
          },
        ],
      };
      return res as unknown as TData;
    },
  };

  const list = await promptApi.listTemplates("ws-test-1", mockTransport);
  assertEqual(capturedCmd, "prompt.list_templates");
  assertDeepEqual(capturedPayload, { workspaceId: "ws-test-1" });
  assertEqual(list.length, 1);
  assertEqual(list[0].templateId, "tpl-1");
  console.log("  -> PASS");
}

{
  console.log("Test 9: getTemplate gọi lệnh 'prompt.get_template'...");
  let capturedCmd = "";
  const mockTransport: IPromptTransport = {
    invoke: async <TData>(command: string): Promise<TData> => {
      capturedCmd = command;
      const res = {
        template: {
          templateId: "tpl-image-1",
          purpose: "Image",
          workspaceId: null,
          isOverride: false,
          currentVersionId: "v-1",
          currentVersionNumber: 1,
          createdAtUtc: "2026-10-26T00:00:00Z",
          updatedAtUtc: "2026-10-26T00:00:00Z",
        },
        currentVersion: {
          versionId: "v-1",
          templateId: "tpl-image-1",
          versionNumber: 1,
          templateText: "Ảnh {phong_canh}",
          variableDefinitionsJson: "[]",
          changeDescription: "Khởi tạo",
          createdAtUtc: "2026-10-26T00:00:00Z",
        },
      };
      return res as unknown as TData;
    },
  };

  const res = await promptApi.getTemplate({ templateId: "tpl-image-1" }, mockTransport);
  assertEqual(capturedCmd, "prompt.get_template");
  assertEqual(res.template.templateId, "tpl-image-1");
  assertEqual(res.currentVersion?.versionNumber, 1);
  console.log("  -> PASS");
}

{
  console.log("Test 10: createVersion gọi lệnh 'prompt.create_version'...");
  let capturedCmd = "";
  let capturedPayload: unknown = null;
  const mockTransport: IPromptTransport = {
    invoke: async <TData>(command: string, payload: unknown): Promise<TData> => {
      capturedCmd = command;
      capturedPayload = payload;
      const res = {
        template: {
          templateId: "tpl-video-1",
          purpose: "VideoPrompt",
          workspaceId: "ws-override",
          isOverride: true,
          currentVersionId: "v-3",
          currentVersionNumber: 3,
          createdAtUtc: "2026-10-26T00:00:00Z",
          updatedAtUtc: "2026-10-26T02:00:00Z",
        },
        version: {
          versionId: "v-3",
          templateId: "tpl-video-1",
          versionNumber: 3,
          templateText: "Video {chu_de} 4K",
          variableDefinitionsJson: null,
          changeDescription: "Nâng cấp 4K",
          createdAtUtc: "2026-10-26T02:00:00Z",
        },
      };
      return res as unknown as TData;
    },
  };

  const res = await promptApi.createVersion(
    {
      purpose: "VideoPrompt",
      workspaceId: "ws-override",
      templateText: "Video {chu_de} 4K",
      variableDefinitionsJson: null,
      changeDescription: "Nâng cấp 4K",
    },
    mockTransport
  );

  assertEqual(capturedCmd, "prompt.save_template");
  assertDeepEqual(capturedPayload, {
    purpose: "VideoPrompt",
    workspaceId: "ws-override",
    templateText: "Video {chu_de} 4K",
    variableDefinitionsJson: null,
    changeDescription: "Nâng cấp 4K",
  });
  assertEqual(res.version.versionNumber, 3);
  assertEqual(res.template.isOverride, true);
  console.log("  -> PASS");
}

{
  console.log("Test 11: preview gửi draft templateText và variables...");
  let capturedCmd = "";
  let capturedPayload: unknown = null;
  const mockTransport: IPromptTransport = {
    invoke: async <TData>(command: string, payload: unknown): Promise<TData> => {
      capturedCmd = command;
      capturedPayload = payload;
      const res = {
        renderedText: "Rendered: Video Công nghệ phong cách Hiện đại",
        templateId: "tpl-1",
        versionId: "v-1",
        variableCount: 2,
        isOverride: false,
      };
      return res as unknown as TData;
    },
  };

  const res = await promptApi.preview(
    {
      templateId: "tpl-1",
      purpose: "Script",
      templateText: "Video {chu_de} phong cách {phong_cach}",
      variables: { chu_de: "Công nghệ", phong_cach: "Hiện đại" },
    },
    mockTransport
  );

  assertEqual(capturedCmd, "prompt.preview");
  assertEqual(res.renderedText, "Rendered: Video Công nghệ phong cách Hiện đại");
  assertEqual(res.variableCount, 2);
  assertDeepEqual(capturedPayload, {
    templateId: "tpl-1",
    purpose: "Script",
    templateText: "Video {chu_de} phong cách {phong_cach}",
    variables: { chu_de: "Công nghệ", phong_cach: "Hiện đại" },
  });
  console.log("  -> PASS");
}

{
  console.log("Test 12: rollbackVersion gọi 'prompt.rollback_version'...");
  let capturedCmd = "";
  let capturedPayload: unknown = null;
  const mockTransport: IPromptTransport = {
    invoke: async <TData>(command: string, payload: unknown): Promise<TData> => {
      capturedCmd = command;
      capturedPayload = payload;
      const res = {
        template: {
          templateId: "tpl-1",
          purpose: "Script",
          workspaceId: null,
          isOverride: false,
          currentVersionId: "v-1",
          currentVersionNumber: 1,
          createdAtUtc: "2026-10-26T00:00:00Z",
          updatedAtUtc: "2026-10-26T03:00:00Z",
        },
        currentVersion: {
          versionId: "v-1",
          templateId: "tpl-1",
          versionNumber: 1,
          templateText: "Văn bản bản v1",
          variableDefinitionsJson: "[]",
          changeDescription: "Bản đầu",
          createdAtUtc: "2026-10-26T00:00:00Z",
        },
      };
      return res as unknown as TData;
    },
  };

  const res = await promptApi.rollbackVersion(
    { templateId: "tpl-1", targetVersionId: "v-1" },
    null,
    mockTransport
  );

  assertEqual(capturedCmd, "prompt.rollback_version");
  assertDeepEqual(capturedPayload, { templateId: "tpl-1", targetVersionId: "v-1" });
  assertEqual(res.currentVersion.versionNumber, 1);
  console.log("  -> PASS");
}

{
  console.log("Test 13: deleteOverride gọi 'prompt.delete_override'...");
  let capturedCmd = "";
  let capturedPayload: unknown = null;
  const mockTransport: IPromptTransport = {
    invoke: async <TData>(command: string, payload: unknown): Promise<TData> => {
      capturedCmd = command;
      capturedPayload = payload;
      const res = {
        templateId: "tpl-override-1",
        deleted: true,
      };
      return res as unknown as TData;
    },
  };

  const res = await promptApi.deleteOverride("tpl-override-1", "ws-1", mockTransport);
  assertEqual(capturedCmd, "prompt.delete_override");
  assertDeepEqual(capturedPayload, { templateId: "tpl-override-1" });
  assertEqual(res.deleted, true);
  console.log("  -> PASS");
}

{
  console.log("Test 14: Lỗi từ backend được giữ nguyên, không làm mất ngữ cảnh...");
  const mockTransport: IPromptTransport = {
    invoke: async <TData>(): Promise<TData> => {
      throw new Error("Lỗi xác thực: Biến số không hợp lệ");
    },
  };

  await assertRejects(async () => {
    await promptApi.preview(
      { purpose: "Script", templateText: "Nội dung", variables: {} },
      mockTransport
    );
  }, "Lỗi xác thực: Biến số không hợp lệ");
  console.log("  -> PASS");
}

{
  console.log("Test 15: Chuẩn hóa tên biến không dấu (§2.2)...");
  assertEqual(normalizeVariableName("chủ_đề"), "chu_de");
  assertEqual(normalizeVariableName("sản phẩm"), "san_pham");
  assertEqual(normalizeVariableName("Đối Tượng Khách Hàng"), "doi_tuong_khach_hang");
  assertEqual(normalizeVariableName("phong cách video"), "phong_cach_video");
  assertEqual(normalizeVariableName("thời_lượng"), "thoi_luong");
  assertTrue(isValidVariableName("chu_de"));
  assertTrue(isValidVariableName("san_pham"));
  assertTrue(!isValidVariableName("chủ_đề"));
  assertTrue(!isValidVariableName("sản phẩm"));
  console.log("  -> PASS");
}

{
  console.log("Test 16: detectNonStandardVariables và normalizeTemplateText (§2.2)...");
  const oldTemplate = "Kịch bản về {chủ_đề} cho {sản phẩm} với {phong cách video}.";
  const nonStandard = detectNonStandardVariables(oldTemplate);
  assertEqual(nonStandard.length, 3);
  assertEqual(nonStandard[0].normalized, "chu_de");
  assertEqual(nonStandard[1].normalized, "san_pham");
  assertEqual(nonStandard[2].normalized, "phong_cach_video");

  const { normalizedText } = normalizeTemplateText(oldTemplate);
  assertEqual(normalizedText, "Kịch bản về {chu_de} cho {san_pham} với {phong_cach_video}.");
  console.log("  -> PASS");
}

{
  console.log("Test 17: Frontend Prompt Rendering ghép biến số tất định tại client...");
  const template = "Bạn là chuyên gia {linh_vuc}. Hãy viết {so_luong} kịch bản về {chu_de}.";
  const rendered = renderPromptTemplateClient(template, {
    linh_vuc: "TikTok Affiliate",
    so_luong: "5",
    chu_de: "Thời trang hè",
  });
  assertEqual(
    rendered.renderedText,
    "Bạn là chuyên gia TikTok Affiliate. Hãy viết 5 kịch bản về Thời trang hè."
  );
  assertEqual(rendered.missingVariables.length, 0);
  assertEqual(rendered.variableCount, 3);
  console.log("  -> PASS");
}

console.log("=== TẤT CẢ 17 KIỂM THỬ PROMPT API, VARIABLE & RENDERING ĐỀU PASS 100% ===");
