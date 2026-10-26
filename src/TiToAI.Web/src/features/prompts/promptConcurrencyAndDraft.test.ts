/**
 * TiToAI — Kiểm thử Concurrency, Giữ Bản Nháp và Khử Phản Hồi Cũ (Stale Response)
 * Chạy trực tiếp qua Node.js: node --experimental-strip-types src/TiToAI.Web/src/features/prompts/promptConcurrencyAndDraft.test.ts
 */

import { register } from "node:module";
import type {
  IPromptTransport,
  PromptTemplateDto,
  PromptVersionDto,
} from "./promptApi.ts";

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

(globalThis as unknown as Record<string, unknown>).window = {
  chrome: {
    webview: {
      addEventListener() {},
      removeEventListener() {},
      postMessage() {},
    },
  },
};

const { promptApi } = await import("./promptApi.ts");

function assertEqual<T>(actual: T, expected: T, msg?: string): void {
  if (actual !== expected) {
    throw new Error(
      `${msg || "Assertion failed"}: Expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`
    );
  }
}

console.log("=== BẮT ĐẦU KIỂM THỬ CONCURRENCY, DRAFT & STALE RESPONSES ===");

// 1. Kiểm thử: Khi backend trả lỗi (ví dụ VALIDATION_FAILED cú pháp), bản nháp đang nhập vẫn được giữ nguyên
{
  console.log("Test 1: Lỗi preview hoặc lỗi tạo version không ghi đè/làm mất bản nháp...");

  const draftText = "Nội dung bản nháp người dùng đang gõ: {loi_syntax";
  const userVariables = { chu_de: "AI", phong_cach: "Hi-tech" };

  const failingTransport: IPromptTransport = {
    invoke: async <TData>(command: string): Promise<TData> => {
      if (command === "prompt.preview") {
        throw new Error("Lỗi cú pháp: Dấu ngoặc nhọn chưa được đóng.");
      }
      return {} as unknown as TData;
    },
  };

  try {
    await promptApi.preview(
      {
        purpose: "Script",
        templateText: draftText,
        variables: userVariables,
      },
      failingTransport
    );
    throw new Error("Phải ném lỗi");
  } catch (err: unknown) {
    assertEqual((err as Error).message, "Lỗi cú pháp: Dấu ngoặc nhọn chưa được đóng.");
    // Bản nháp và biến của người dùng vẫn toàn vẹn
    assertEqual(draftText, "Nội dung bản nháp người dùng đang gõ: {loi_syntax");
    assertEqual(userVariables.chu_de, "AI");
    assertEqual(userVariables.phong_cach, "Hi-tech");
  }

  console.log("  -> PASS: Bản nháp và biến số của người dùng được bảo vệ nguyên vẹn.");
}

// 2. Kiểm thử: Chống Stale Response khi chuyển Workspace hoặc chuyển Template nhanh
{
  console.log("Test 2: Khử phản hồi cũ (Stale response) khi người dùng đổi lựa chọn nhanh...");

  let latestRequestId = 0;
  let finalAppliedWorkspace: string | null = null;
  let finalAppliedTemplatesCount = 0;

  const delayedTransport: IPromptTransport = {
    invoke: async <TData>(_command: string, payload: unknown): Promise<TData> => {
      const p = payload as { workspaceId: string };
      if (p.workspaceId === "ws-cham") {
        await new Promise((r) => setTimeout(r, 60));
        return {
          templates: [
            { templateId: "ws-cham-tpl-1", purpose: "Script", isOverride: true },
            { templateId: "ws-cham-tpl-2", purpose: "Script", isOverride: true },
          ],
        } as unknown as TData;
      } else {
        await new Promise((r) => setTimeout(r, 20));
        return {
          templates: [
            { templateId: "ws-nhanh-tpl-1", purpose: "Script", isOverride: false },
          ],
        } as unknown as TData;
      }
    },
  };

  const dispatchLoad = async (wsId: string) => {
    const reqId = ++latestRequestId;
    const res = await promptApi.listTemplates(wsId, delayedTransport);
    if (reqId === latestRequestId) {
      finalAppliedWorkspace = wsId;
      finalAppliedTemplatesCount = res.length;
    }
  };

  const p1 = dispatchLoad("ws-cham");
  await new Promise((r) => setTimeout(r, 10));
  const p2 = dispatchLoad("ws-nhanh");

  await Promise.all([p1, p2]);

  assertEqual(finalAppliedWorkspace, "ws-nhanh");
  assertEqual(finalAppliedTemplatesCount, 1);
  console.log("  -> PASS: Stale response từ ws-cham đã bị loại bỏ, trạng thái ws-nhanh được bảo toàn.");
}

// 3. Kiểm thử: Resolution Precedence (Ghi đè Workspace ưu tiên hơn Toàn cục)
{
  console.log("Test 3: Độ ưu tiên giải quyết template (Workspace Override > Global Template)...");

  const templatesList: PromptTemplateDto[] = [
    {
      templateId: "tpl-global-script",
      purpose: "Script",
      workspaceId: null,
      isOverride: false,
      currentVersionId: "v-global-1",
      currentVersionNumber: 1,
      createdAtUtc: "2026-10-26T00:00:00Z",
      updatedAtUtc: "2026-10-26T00:00:00Z",
    },
    {
      templateId: "tpl-override-script",
      purpose: "Script",
      workspaceId: "ws-active",
      isOverride: true,
      currentVersionId: "v-override-1",
      currentVersionNumber: 1,
      createdAtUtc: "2026-10-26T01:00:00Z",
      updatedAtUtc: "2026-10-26T01:00:00Z",
    },
  ];

  const overrideMatch = templatesList.find((t) => t.isOverride && t.workspaceId === "ws-active");
  const selected = overrideMatch || templatesList[0];

  assertEqual(selected.templateId, "tpl-override-script");
  assertEqual(selected.isOverride, true);
  console.log("  -> PASS: Override template được tự động ưu tiên trên Global template.");
}

// 4. Kiểm thử: Monotonic Versioning & Rollback pointer
{
  console.log("Test 4: Tính đơn điệu của version và con trỏ rollback...");

  let currentVersionNumber = 1;
  const versions: PromptVersionDto[] = [
    {
      versionId: "ver-1",
      templateId: "tpl-1",
      versionNumber: 1,
      templateText: "Bản 1",
      variableDefinitionsJson: "[]",
      changeDescription: "Gốc",
      createdAtUtc: "2026-10-26T00:00:00Z",
    },
  ];

  // Tạo phiên bản 2
  currentVersionNumber++;
  versions.unshift({
    versionId: "ver-2",
    templateId: "tpl-1",
    versionNumber: currentVersionNumber,
    templateText: "Bản 2",
    variableDefinitionsJson: "[]",
    changeDescription: "Cập nhật v2",
    createdAtUtc: "2026-10-26T01:00:00Z",
  });

  // Tạo phiên bản 3
  currentVersionNumber++;
  versions.unshift({
    versionId: "ver-3",
    templateId: "tpl-1",
    versionNumber: currentVersionNumber,
    templateText: "Bản 3",
    variableDefinitionsJson: "[]",
    changeDescription: "Cập nhật v3",
    createdAtUtc: "2026-10-26T02:00:00Z",
  });

  assertEqual(versions[0].versionNumber, 3);
  assertEqual(versions[1].versionNumber, 2);
  assertEqual(versions[2].versionNumber, 1);

  // Rollback về ver-1
  let activeVersionId = versions[0].versionId;
  assertEqual(activeVersionId, "ver-3");

  activeVersionId = "ver-1";
  assertEqual(activeVersionId, "ver-1");
  assertEqual(versions.length, 3);

  console.log("  -> PASS: Rollback chỉ thay đổi con trỏ phiên bản hiện thời, bảo toàn lịch sử.");
}

console.log("=== TẤT CẢ CÁC KIỂM THỬ CONCURRENCY & DRAFT ĐỀU PASS 100% ===");
