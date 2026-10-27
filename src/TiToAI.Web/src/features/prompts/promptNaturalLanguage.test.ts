/**
 * TiToAI — Kiểm thử Gói AC1: Hướng dẫn tự nhiên AI và UI Prompt Video trên DOM thật
 * Kiểm tra:
 * 1. Nhãn và mô tả hướng dẫn tự nhiên không bắt buộc biến kỹ thuật (VideoPrompt).
 * 2. Nút "Lưu phiên bản hướng dẫn" phân định rõ ràng.
 * 3. Nút "Xem yêu cầu gửi AI (Render xem trước)" và ghi chú không gọi AI / không tốn quota.
 * 4. Tab "Hướng dẫn AI tạo prompt video" và "Kịch bản & Ý tưởng (Script)".
 */

import { register } from "node:module";
import { GlobalWindow } from "happy-dom";

// Đăng ký custom loader hỗ trợ import .tsx
register("./test-loader.mjs", import.meta.url);

// Thiết lập môi trường Browser DOM thật bằng Happy-DOM
const domWindow = new GlobalWindow();
globalThis.window = domWindow as unknown as Window & typeof globalThis;
globalThis.document = domWindow.document as unknown as Document;
globalThis.Event = domWindow.Event as unknown as typeof Event;
globalThis.MouseEvent = domWindow.MouseEvent as unknown as typeof MouseEvent;
globalThis.KeyboardEvent = domWindow.KeyboardEvent as unknown as typeof KeyboardEvent;
globalThis.requestAnimationFrame = (cb) => setTimeout(cb, 0);
globalThis.cancelAnimationFrame = (id) => clearTimeout(id);
(globalThis as unknown as Record<string, unknown>).IS_REACT_ACT_ENVIRONMENT = true;

const { createRoot } = await import("react-dom/client");
const { act } = await import("react");
const React = await import("react");

const { PromptTemplateEditor } = await import("./PromptTemplateEditor.tsx");
const { PromptPreviewPanel } = await import("./PromptPreviewPanel.tsx");
const { PromptTemplateManager } = await import("./PromptTemplateManager.tsx");

function assertTrue(condition: boolean, msg: string) {
  if (!condition) {
    console.error(`[FAIL ASSERTION] ${msg}`);
    throw new Error(msg);
  }
}

async function renderIntoDom(element: React.ReactElement) {
  const container = domWindow.document.createElement("div");
  domWindow.document.body.appendChild(container);
  const root = createRoot(container as unknown as HTMLElement);
  await act(async () => {
    root.render(element);
  });
  return {
    container,
    cleanup: async () => {
      await act(async () => {
        root.unmount();
      });
      container.remove();
    },
  };
}

console.log("=== BẮT ĐẦU KIỂM THỬ GÓI AC1: HƯỚNG DẪN TỰ NHIÊN & UI PROMPT VIDEO TRÊN DOM THẬT ===");

// 1. Kiểm tra PromptTemplateEditor khi purpose là VideoPrompt
console.log("Test 1: PromptTemplateEditor VideoPrompt hiển thị đúng nhãn và banner tự nhiên...");
{
  const mockTemplate = {
    templateId: "tpl-video-1",
    purpose: "VideoPrompt" as const,
    workspaceId: null,
    isOverride: false,
    currentVersionId: "ver-1",
    currentVersionNumber: 1,
    createdAtUtc: "2026-10-27T00:00:00Z",
    updatedAtUtc: "2026-10-27T00:00:00Z",
  };

  const mockVersion = {
    versionId: "ver-1",
    templateId: "tpl-video-1",
    versionNumber: 1,
    templateText: "Cinematic shot of hero looking at the horizon, golden hour",
    variableDefinitionsJson: "[]",
    changeDescription: "Initial version",
    createdAtUtc: "2026-10-27T00:00:00Z",
  };

  const dom = await renderIntoDom(
    React.createElement(PromptTemplateEditor, {
      template: mockTemplate,
      currentVersion: mockVersion,
      purpose: "VideoPrompt",
      workspaceId: null,
    })
  );

  const text = dom.container.textContent || "";
  assertTrue(text.includes("Hướng dẫn AI tạo prompt video"), "Phải có tiêu đề 'Hướng dẫn AI tạo prompt video'");
  assertTrue(
    text.includes("AI tự quyết định chi tiết còn thiếu về góc máy, chuyển động camera, ánh sáng và không khí"),
    "Phải có mô tả AI tự quyết định góc máy, camera, ánh sáng"
  );
  assertTrue(
    text.includes("Bạn không cần điền biến kỹ thuật"),
    "Phải có hướng dẫn không bắt người dùng điền biến kỹ thuật"
  );
  assertTrue(text.includes("Lưu phiên bản hướng dẫn"), "Nút lưu phải là 'Lưu phiên bản hướng dẫn'");

  await dom.cleanup();
  console.log("  -> PASS Test 1");
}

// 2. Kiểm tra PromptPreviewPanel làm rõ render xem trước, không gọi AI
console.log("Test 2: PromptPreviewPanel hiển thị rõ 'Xem yêu cầu gửi AI' và lưu ý không gọi AI...");
{
  const dom = await renderIntoDom(
    React.createElement(PromptPreviewPanel, {
      previewData: {
        renderedText: "Rendered test text",
        templateId: "tpl-1",
        versionId: "ver-1",
        variableCount: 0,
        isOverride: false,
      },
      isLoading: false,
      error: null,
      onRender: () => {},
    })
  );

  const text = dom.container.textContent || "";
  assertTrue(
    text.includes("Xem yêu cầu gửi AI (Render xem trước)"),
    "Nút preview phải là 'Xem yêu cầu gửi AI (Render xem trước)'"
  );
  assertTrue(
    text.includes("hoàn toàn không gọi AI và không tốn lượt gửi"),
    "Phải có lưu ý rõ ràng không gọi AI và không tốn lượt gửi"
  );

  await dom.cleanup();
  console.log("  -> PASS Test 2");
}

// 3. Kiểm tra PromptTemplateManager hiển thị tab Hướng dẫn AI tạo prompt video
console.log("Test 3: PromptTemplateManager hiển thị tab theo nhãn mới...");
{
  const mockTransport = {
    invoke: async <TData = unknown, TPayload = unknown>(command: string, _payload?: TPayload): Promise<TData> => {
      if (command === "prompt.list_templates") {
        return {
          templates: [
            {
              templateId: "tpl-script-1",
              purpose: "Script",
              workspaceId: null,
              isOverride: false,
              currentVersionId: "v1",
              currentVersionNumber: 1,
              createdAtUtc: "2026-10-27T00:00:00Z",
              updatedAtUtc: "2026-10-27T00:00:00Z",
            },
            {
              templateId: "tpl-video-1",
              purpose: "VideoPrompt",
              workspaceId: null,
              isOverride: false,
              currentVersionId: "v2",
              currentVersionNumber: 1,
              createdAtUtc: "2026-10-27T00:00:00Z",
              updatedAtUtc: "2026-10-27T00:00:00Z",
            },
          ],
        } as unknown as TData;
      }
      if (command === "prompt.get_template") {
        return {
          template: {
            templateId: "tpl-video-1",
            purpose: "VideoPrompt",
            workspaceId: null,
            isOverride: false,
            currentVersionId: "v2",
            currentVersionNumber: 1,
            createdAtUtc: "2026-10-27T00:00:00Z",
            updatedAtUtc: "2026-10-27T00:00:00Z",
          },
          currentVersion: {
            versionId: "v2",
            templateId: "tpl-video-1",
            versionNumber: 1,
            templateText: "Test video prompt",
            variableDefinitionsJson: "[]",
            changeDescription: null,
            createdAtUtc: "2026-10-27T00:00:00Z",
          },
        } as unknown as TData;
      }
      return {} as unknown as TData;
    },
  };

  const dom = await renderIntoDom(
    React.createElement(PromptTemplateManager, {
      workspaceId: null,
      initialPurpose: "VideoPrompt",
      transport: mockTransport,
    })
  );

  const text = dom.container.textContent || "";
  assertTrue(
    text.includes("Hướng dẫn AI tạo prompt video"),
    "Tab VideoPrompt phải hiển thị 'Hướng dẫn AI tạo prompt video'"
  );
  assertTrue(
    text.includes("Kịch bản & Ý tưởng (Script)"),
    "Tab Script phải hiển thị 'Kịch bản & Ý tưởng (Script)'"
  );

  await dom.cleanup();
  console.log("  -> PASS Test 3");
}

console.log("=================================================================");
console.log(">>> TOÀN BỘ KIỂM THỬ AC1 TRÊN DOM THẬT ĐÃ VƯỢT QUA (PASS 100%) <<<");
console.log("=================================================================");
