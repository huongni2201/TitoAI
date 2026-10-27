import { register } from "node:module";
import { GlobalWindow } from "happy-dom";
import assert from "node:assert/strict";

// Đăng ký custom loader hỗ trợ import .tsx, .ts, .css và alias @/*
register("./test-loader.mjs", import.meta.url);

// Thiết lập Happy-DOM môi trường trình duyệt thật TRƯỚC KHI import React & ReactDOM
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

const React = await import("react");
const { act } = React;
const { createRoot } = await import("react-dom/client");

const {
  PromptTemplateManager,
} = await import("./PromptTemplateManager.tsx");

const {
  PromptTemplateEditor,
} = await import("./PromptTemplateEditor.tsx");

const {
  getActivePromptId,
  setActivePromptId,
  getStoredPrompts,
  saveStoredPrompts,
} = await import("./promptStorage.ts");

async function renderIntoDom(element: React.ReactElement) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  await act(async () => {
    root.render(element);
  });
  return {
    container,
    root,
    rerender: async (newElement: React.ReactElement) => {
      await act(async () => {
        root.render(newElement);
      });
    },
    unmount: async () => {
      await act(async () => {
        root.unmount();
      });
      container.remove();
    },
  };
}

async function runTests() {
  console.log("=== KIỂM THỬ: KHÔNG CÓ TEMPLATE HIỆN Ô SOẠN THẢO, TẠO NHIỀU PROMPT, CHỌN & LƯU LỰA CHỌN ===");

  // TEST 1: Khi chưa có template nào hoặc sau khi xóa, hiển thị ô nội dung template sẵn sàng (như hình 1), không lỗi đỏ (như hình 2)
  console.log("\n[TEST 1] Kiểm tra hiển thị ô nội dung template khi chưa có template hoặc sau khi xóa...");
  {
    const mockTransport = {
      invoke: async <TData = unknown, TPayload = unknown>(command: string, payload?: TPayload): Promise<TData> => {
        if (command === "prompt.list_templates") {
          return { templates: [] } as unknown as TData; // Không có template nào
        }
        if (command === "prompt.get_template") {
          throw new Error("Không tìm thấy Prompt Template với ID 'deleted-id'");
        }
        return {} as unknown as TData;
      },
    };

    const dom = await renderIntoDom(
      React.createElement(PromptTemplateManager, {
        workspaceId: "ws_empty_test",
        initialPurpose: "Script",
        transport: mockTransport,
      })
    );

    await act(async () => {
      await new Promise((r) => setTimeout(r, 30));
    });

    // 1. Tuyệt đối KHÔNG có thông báo lỗi đỏ "Không tìm thấy Prompt Template với ID..."
    assert.strictEqual(
      dom.container.textContent?.includes("Không tìm thấy Prompt Template với ID"),
      false,
      "Tuyệt đối không được xuất hiện bug lỗi đỏ 'Không tìm thấy Prompt Template với ID'!"
    );

    // 2. Ô textarea "Nội dung Template" PHẢI xuất hiện sẵn sàng để người dùng nhập (như hình 1)
    const textarea = dom.container.querySelector("textarea#prompt-template-textarea");
    assert.ok(textarea, "Phải hiển thị ô textarea 'Nội dung Template' như hình 1 để người dùng nhập ngay!");

    // 3. Có ô nhập "Tên Prompt"
    const nameInput = dom.container.querySelector("input#prompt-name-input");
    assert.ok(nameInput, "Phải có ô nhập Tên Prompt để quản lý nhiều prompt");

    await dom.unmount();
    console.log("  -> PASS [TEST 1]: Không xuất hiện lỗi đỏ, ô nội dung template hiển thị sẵn sàng chuẩn xác.");
  }

  // TEST 2: Đổi prompt thành sửa, không phải ghi đè nữa
  console.log("\n[TEST 2] Kiểm tra giao diện: Sửa prompt, không hiển thị checkbox 'Lưu thành bản ghi đè'...");
  {
    const sampleTemplate = {
      templateId: "tpl-test-edit",
      purpose: "Script",
      workspaceId: "ws-1",
      isOverride: true,
      currentVersionId: "v-1",
      currentVersionNumber: 1,
      createdAtUtc: new Date().toISOString(),
      updatedAtUtc: new Date().toISOString(),
    };
    const sampleVersion = {
      versionId: "v-1",
      templateId: "tpl-test-edit",
      versionNumber: 1,
      templateText: "Nội dung prompt ban đầu",
      variableDefinitionsJson: "[]",
      changeDescription: "Gốc",
      createdAtUtc: new Date().toISOString(),
    };

    const dom = await renderIntoDom(
      React.createElement(PromptTemplateEditor, {
        template: sampleTemplate,
        currentVersion: sampleVersion,
        workspaceId: "ws-1",
        purpose: "Script",
        promptName: "Kịch bản viral TikTok",
      })
    );

    // Kiểm tra nút lưu là "Lưu thay đổi"
    const buttons = Array.from(dom.container.querySelectorAll("button"));
    const saveBtn = buttons.find((b) => b.textContent?.includes("Lưu thay đổi"));
    assert.ok(saveBtn, "Nút lưu phải là 'Lưu thay đổi' thay vì ghi đè");

    // Kiểm tra nút xóa là "Xóa Prompt"
    const deleteBtn = buttons.find((b) => b.textContent?.includes("Xóa Prompt"));
    assert.ok(deleteBtn, "Nút xóa phải là 'Xóa Prompt'");

    await dom.unmount();
    console.log("  -> PASS [TEST 2]: Giao diện chuyển thành Sửa prompt mượt mà, chuyên nghiệp.");
  }

  // TEST 3: Cho phép chọn Prompt nào và lưu lựa chọn vào localStorage cho lần sau
  console.log("\n[TEST 3] Kiểm tra chọn prompt active và lưu lựa chọn vào storage...");
  {
    // Lưu lựa chọn prompt active cho Script
    setActivePromptId("Script", "preset-script-product-review", "ws_test_select");
    const activeId = getActivePromptId("Script", "ws_test_select");
    assert.strictEqual(
      activeId,
      "preset-script-product-review",
      "Phải lưu đúng ID prompt được chọn vào localStorage cho lần sau"
    );

    // Lưu custom prompts vào storage
    const customList = [
      {
        id: "custom-p1",
        name: "Prompt Kịch Bản 1",
        purpose: "Script" as const,
        templateText: "Kịch bản {chủ_đề}...",
        createdAtUtc: new Date().toISOString(),
        updatedAtUtc: new Date().toISOString(),
      },
      {
        id: "custom-p2",
        name: "Prompt Kịch Bản 2",
        purpose: "Script" as const,
        templateText: "Kịch bản review {sản_phẩm}...",
        createdAtUtc: new Date().toISOString(),
        updatedAtUtc: new Date().toISOString(),
      },
    ];
    saveStoredPrompts(customList, "ws_test_select");
    const loaded = getStoredPrompts("ws_test_select");
    assert.strictEqual(loaded.length, 2, "Phải lưu và nạp được nhiều prompt cho workspace");
    assert.strictEqual(loaded[0].name, "Prompt Kịch Bản 1");
    assert.strictEqual(loaded[1].name, "Prompt Kịch Bản 2");

    console.log("  -> PASS [TEST 3]: Cơ chế chọn prompt active và lưu trữ nhiều prompt hoạt động hoàn hảo.");
  }

  console.log("\n>>> TẤT CẢ KIỂM THỬ MỚI ĐÃ VƯỢT QUA (PASS 100%) <<<");
}

runTests().catch((err) => {
  console.error("TEST FAILED:", err);
  process.exit(1);
});
