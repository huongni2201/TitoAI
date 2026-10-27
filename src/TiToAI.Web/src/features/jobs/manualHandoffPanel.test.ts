import { register } from "node:module";
import { GlobalWindow } from "happy-dom";
import assert from "node:assert/strict";

register("../prompts/test-loader.mjs", import.meta.url);

// Cấu hình môi trường DOM giả lập với happy-dom
const domWindow = new GlobalWindow({ url: "http://localhost:3000" });
globalThis.window = domWindow as unknown as Window & typeof globalThis;
globalThis.document = domWindow.document as unknown as Document;
globalThis.HTMLElement = domWindow.HTMLElement as unknown as typeof HTMLElement;
globalThis.HTMLButtonElement = domWindow.HTMLButtonElement as unknown as typeof HTMLButtonElement;
globalThis.HTMLDivElement = domWindow.HTMLDivElement as unknown as typeof HTMLDivElement;
globalThis.HTMLTextAreaElement = domWindow.HTMLTextAreaElement as unknown as typeof HTMLTextAreaElement;
globalThis.HTMLInputElement = domWindow.HTMLInputElement as unknown as typeof HTMLInputElement;
globalThis.KeyboardEvent = domWindow.KeyboardEvent as unknown as typeof KeyboardEvent;
globalThis.MouseEvent = domWindow.MouseEvent as unknown as typeof MouseEvent;
globalThis.Event = domWindow.Event as unknown as typeof Event;
globalThis.requestAnimationFrame = (cb) => setTimeout(cb, 0);
globalThis.cancelAnimationFrame = (id) => clearTimeout(id);
(globalThis as unknown as Record<string, unknown>).IS_REACT_ACT_ENVIRONMENT = true;

// Mock Clipboard API
let lastCopiedText = "";
Object.defineProperty(globalThis.navigator, "clipboard", {
  value: {
    writeText: async (text: string) => {
      lastCopiedText = text;
    },
  },
  configurable: true,
});

// Mock Chrome WebView2 Bridge
const messageListeners: Array<(event: MessageEvent) => void> = [];
let lastExecutedCommand: string | null = null;
let lastCommandPayload: Record<string, unknown> | null = null;
let importResultShouldFail = false;
let importResultFailMessage = "Cấu trúc dữ liệu không hợp lệ";

const mockHandoffData = {
  jobId: "job_handoff_001",
  workspaceId: "ws_test_01",
  projectId: "prj_test_01",
  sceneId: null,
  taskType: "Script",
  providerKey: "chatgpt-web",
  promptText: "Hãy viết kịch bản video ngắn 30 giây về trí tuệ nhân tạo TiToAI.",
  configSnapshotJson: JSON.stringify({ workflowVersion: 1 }),
  workflowVersion: "1",
  revision: 5,
  reasonCode: "manual_required",
  instructions: "Bước 1: Sao chép prompt kịch bản bên dưới.\nBước 2: Dán vào AI và lấy kết quả JSON.\nBước 3: Dán kết quả vào ô bên dưới.",
  expectedOutputSchema: "script_json_v1",
  hasUnresolvedSideEffect: false,
  resultReference: null,
};

(domWindow as unknown as { chrome: unknown }).chrome = {
  webview: {
    addEventListener(_type: string, listener: (event: MessageEvent) => void) {
      messageListeners.push(listener);
    },
    removeEventListener(_type: string, listener: (event: MessageEvent) => void) {
      const idx = messageListeners.indexOf(listener);
      if (idx >= 0) messageListeners.splice(idx, 1);
    },
    postMessage: (msg: unknown) => {
      const req = msg as { id: string; command: string; payload?: Record<string, unknown> };
      lastExecutedCommand = req.command;
      lastCommandPayload = req.payload ?? null;

      let success = true;
      let payloadData: unknown = null;
      let errorData: unknown = null;

      if (req.command === "job.get_handoff") {
        payloadData = { handoff: { ...mockHandoffData } };
      } else if (req.command === "job.import_result") {
        if (importResultShouldFail) {
          success = false;
          errorData = {
            code: "VALIDATION_FAILED",
            message: importResultFailMessage,
            retryable: false,
            correlationId: req.id,
            details: null,
          };
        } else {
          payloadData = {
            jobId: req.payload?.jobId ?? "job_handoff_001",
            state: "Succeeded",
            revision: ((req.payload?.expectedRevision as number) || 5) + 1,
            resultId: "res_mock_123",
            assetId: (req.payload?.assetId as string) || null,
            isDuplicate: false,
            wasCanceled: false,
          };
        }
      }

      setTimeout(() => {
        const ev = {
          data: {
            id: req.id,
            type: "response",
            success,
            contractVersion: "1.0.4",
            data: payloadData,
            error: errorData,
          },
        } as MessageEvent;

        for (const listener of [...messageListeners]) {
          listener(ev);
        }
      }, 0);
    },
  },
};

const React = (await import("react")).default;
const { act } = await import("react");
const { createRoot } = await import("react-dom/client");
const { bridgeClient } = await import("../../services/bridgeClient.ts");
const { ManualHandoffPanel } = await import("./ManualHandoffPanel.tsx");

function setNativeInputValue(element: HTMLInputElement | HTMLTextAreaElement, value: string) {
  const proto = Object.getPrototypeOf(element);
  const setter = Object.getOwnPropertyDescriptor(proto, "value")?.set;
  if (setter) {
    setter.call(element, value);
  } else {
    element.value = value;
  }
  element.dispatchEvent(new Event("input", { bubbles: true }));
  element.dispatchEvent(new Event("change", { bubbles: true }));
}

async function waitTicks(count = 5) {
  for (let i = 0; i < count; i++) {
    await act(async () => {
      await new Promise((r) => setTimeout(r, 10));
    });
  }
}

async function runAllTests() {
  console.log("=== BỘ KIỂM THỬ MANUAL HANDOFF PANEL (HAPPY-DOM) ===");

  bridgeClient.setActiveWorkspaceId("ws_test_01");

  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);

  try {
    // -------------------------------------------------------------
    // TEST 1: Render handoff panel đầy đủ thông tin job, prompt và hướng dẫn
    // -------------------------------------------------------------
    console.log("\n[TEST 1] Render Handoff Panel đầy đủ thông tin job, prompt và hướng dẫn...");
    await act(async () => {
      root.render(
        React.createElement(ManualHandoffPanel, {
          workspaceId: "ws_test_01",
          jobId: "job_handoff_001",
        }),
      );
    });

    await waitTicks(10);

    // Kiểm tra thông tin Job ID, task type, provider
    const panelText = container.textContent || "";
    assert.ok(panelText.includes("job_handoff_001"), "Phải hiển thị Job ID");
    assert.ok(panelText.includes("Script"), "Phải hiển thị TaskType Script");
    assert.ok(panelText.includes("chatgpt-web"), "Phải hiển thị Provider Key");

    // Kiểm tra prompt và instructions
    assert.ok(
      panelText.includes("Hãy viết kịch bản video ngắn 30 giây"),
      "Phải hiển thị nội dung prompt đã snapshot",
    );
    assert.ok(
      panelText.includes("Bước 1: Sao chép prompt"),
      "Phải hiển thị hướng dẫn thực hiện từng bước",
    );
    console.log("  -> PASS: Render đầy đủ thông tin Job, Prompt và Hướng dẫn.");

    // -------------------------------------------------------------
    // TEST 2: Bấm nút Copy Prompt thực hiện sao chép
    // -------------------------------------------------------------
    console.log("\n[TEST 2] Bấm nút Copy Prompt thực hiện sao chép clipboard...");
    lastCopiedText = "";
    const copyButton = Array.from(
      container.querySelectorAll<HTMLButtonElement>("button"),
    ).find((b) => b.textContent?.includes("Sao chép Prompt"));

    assert.ok(copyButton, "Phải tìm thấy nút 'Sao chép Prompt'");

    await act(async () => {
      copyButton.click();
    });
    await waitTicks(3);

    assert.strictEqual(
      lastCopiedText,
      mockHandoffData.promptText,
      "Nội dung sao chép vào clipboard phải trùng khớp chính xác với promptText",
    );
    console.log("  -> PASS: Bấm nút Copy Prompt đã sao chép chính xác prompt vào clipboard.");

    // -------------------------------------------------------------
    // TEST 3: Nhập kết quả hợp lệ bấm Gửi thành công
    // -------------------------------------------------------------
    console.log("\n[TEST 3] Nhập kết quả JSON kịch bản hợp lệ và bấm Gửi thành công...");
    importResultShouldFail = false;

    const validScriptJson = JSON.stringify({
      title: "Kịch bản test thành công",
      scenes: [
        {
          key: "sc_1",
          order: 1,
          name: "Cảnh 1",
          description: "Mô tả cảnh 1",
          targetDurationSeconds: 10,
          imagePrompt: "Ảnh cảnh 1",
        },
      ],
    });

    const textarea = container.querySelector<HTMLTextAreaElement>("textarea#script-json-input");
    assert.ok(textarea, "Phải có textarea nhập kịch bản JSON");

    // Giả lập người dùng nhập dữ liệu qua setNativeInputValue
    await act(async () => {
      setNativeInputValue(textarea, validScriptJson);
    });
    await waitTicks(3);

    const submitButton = Array.from(
      container.querySelectorAll<HTMLButtonElement>("button"),
    ).find((b) => b.textContent?.includes("Gửi kết quả thủ công"));
    assert.ok(submitButton, "Phải tìm thấy nút 'Gửi kết quả thủ công'");

    await act(async () => {
      submitButton.click();
    });
    await waitTicks(10);

    assert.strictEqual(lastExecutedCommand, "job.import_result", "Lệnh gọi phải là 'job.import_result'");
    assert.strictEqual(
      (lastCommandPayload as any)?.jobId,
      "job_handoff_001",
      "Payload gửi đi phải đúng Job ID",
    );
    assert.strictEqual(
      (lastCommandPayload as any)?.textResult,
      validScriptJson,
      "Payload gửi đi phải chứa JSON kịch bản",
    );

    const updatedText = container.textContent || "";
    assert.ok(
      updatedText.includes("Nhập kết quả thành công"),
      "Giao diện phải hiển thị thông báo nhập kết quả thành công",
    );
    console.log("  -> PASS: Gửi kết quả hợp lệ thành công và nhận phản hồi Succeeded.");

    // -------------------------------------------------------------
    // TEST 4: Nhập kịch bản sai JSON báo lỗi và BẢO TOÀN nội dung textarea
    // -------------------------------------------------------------
    console.log("\n[TEST 4] Nhập kịch bản sai cấu trúc: hiển thị thông báo lỗi và BẢO TOÀN DRAFT INPUTS...");
    importResultShouldFail = true;
    importResultFailMessage = "Dữ liệu JSON sai định dạng: mảng scenes không được để trống";

    // Nhập nội dung draft sai
    const invalidDraftInput = "{ title: 'Lỗi cú pháp', scenes: [] }";

    // Reset component sang trạng thái mới với key riêng để test submit lỗi
    await act(async () => {
      root.render(
        React.createElement(ManualHandoffPanel, {
          key: "panel-test-4",
          workspaceId: "ws_test_01",
          jobId: "job_handoff_001",
          initialHandoff: { ...mockHandoffData, revision: 6 },
        }),
      );
    });
    await waitTicks(5);

    const freshTextarea = container.querySelector<HTMLTextAreaElement>("textarea#script-json-input");
    assert.ok(freshTextarea, "Phải tìm thấy textarea");

    await act(async () => {
      setNativeInputValue(freshTextarea, invalidDraftInput);
    });
    await waitTicks(3);

    const freshSubmitButton = Array.from(
      container.querySelectorAll<HTMLButtonElement>("button"),
    ).find((b) => b.textContent?.includes("Gửi kết quả thủ công"));
    assert.ok(freshSubmitButton, "Phải tìm thấy nút Submit");

    await act(async () => {
      freshSubmitButton.click();
    });
    await waitTicks(20);

    // Kiểm tra lỗi hiển thị
    const errorText = container.textContent || "";
    assert.ok(
      errorText.includes("Không thể nhập kết quả") || errorText.includes(importResultFailMessage),
      "Phải hiển thị thông báo lỗi từ backend",
    );

    // QUAN TRỌNG: Kiểm tra textarea vẫn giữ nguyên giá trị người dùng đã nhập
    assert.strictEqual(
      freshTextarea.value,
      invalidDraftInput,
      "Nội dung textarea PHẢI ĐƯỢC BẢO TOÀN (Draft preservation), không bị xóa rỗng khi gặp lỗi!",
    );
    console.log("  -> PASS: Hiển thị lỗi rõ ràng và BẢO TOÀN NGUYÊN VẸN nội dung draft trong textarea.");

    console.log("\n>>> TẤT CẢ 4 TESTS TRÊN MANUAL HANDOFF PANEL ĐÃ THÀNH CÔNG RỰC RỠ! <<<\n");
  } finally {
    root.unmount();
    container.remove();
  }
}

// Chạy test suite
runAllTests().catch((err) => {
  console.error("TEST FAILED:", err);
  process.exit(1);
});
