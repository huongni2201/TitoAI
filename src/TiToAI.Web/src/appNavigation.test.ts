import { register } from "node:module";
import { GlobalWindow } from "happy-dom";
import assert from "node:assert/strict";

// Đăng ký custom loader hỗ trợ import .tsx, .ts, .css và alias @/*
register("./features/prompts/test-loader.mjs", import.meta.url);

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

const mockWorkspaces = [
  {
    workspaceId: "ws_01",
    name: "Workspace Test",
    channelHandle: "@test_channel",
    topic: "Chủ đề Test",
    status: "Active",
    createdAtUtc: new Date().toISOString(),
    updatedAtUtc: new Date().toISOString(),
  },
];

const mockTemplates = [
  {
    templateId: "tmpl_test_script",
    purpose: "Script",
    name: "Kịch bản Mẫu Test",
    description: "Mẫu kịch bản test",
    latestVersion: 1,
    currentVersion: 1,
    isOverridden: false,
    contentTemplate: "Nội dung template test",
    variableDefinitionsJson: "[]",
    versions: [],
  },
];

const messageListeners: Array<(event: MessageEvent) => void> = [];

(domWindow as any).chrome = {
  webview: {
    addEventListener(_type: string, listener: (event: MessageEvent) => void) {
      messageListeners.push(listener);
    },
    removeEventListener(_type: string, listener: (event: MessageEvent) => void) {
      const idx = messageListeners.indexOf(listener);
      if (idx >= 0) messageListeners.splice(idx, 1);
    },
    postMessage: (msg: unknown) => {
      const req = msg as { id: string; command: string; payload?: any };
      let payloadData: unknown = {};

      if (req.command === "theme.get_preference") {
        payloadData = { preference: "system", effectiveTheme: "dark" };
      } else if (req.command === "workspace.list") {
        payloadData = { workspaces: mockWorkspaces };
      } else if (req.command === "prompt.list") {
        payloadData = { templates: mockTemplates };
      } else if (req.command === "prompt.get") {
        payloadData = { template: mockTemplates[0] };
      } else if (req.command === "asset.list") {
        payloadData = { assets: [], totalCount: 0, page: 1, pageSize: 20 };
      } else if (req.command === "settings.get") {
        payloadData = { documentsPath: "", imagesPath: "", videosPath: "" };
      }

      setTimeout(() => {
        const ev = {
          data: {
            id: req.id,
            type: "response",
            success: true,
            contractVersion: "1.0.4",
            data: payloadData,
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
const App = (await import("./App.tsx")).default;

async function runTests() {
  console.log("=== KIỂM THỬ TÁCH MẪU PROMPT VÀ TÀI NGUYÊN MEDIA THÀNH 2 TAB & 2 SCREEN ===");

  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);

  await act(async () => {
    root.render(React.createElement(App));
  });

  // Chờ load ban đầu của workspaces
  for (let i = 0; i < 20; i++) {
    await act(async () => {
      await new Promise((r) => setTimeout(r, 20));
    });
  }

  // 1. Kiểm tra 2 tab riêng biệt tồn tại trên Sidebar
  console.log("\n[TEST 1] Kiểm tra 2 tab riêng biệt trên Sidebar...");
  const navItems = Array.from(container.querySelectorAll<HTMLButtonElement>("nav.sidebar-nav button.nav-item"));
  const promptTab = navItems.find((b) => b.textContent?.trim() === "Prompt");
  const mediaTab = navItems.find((b) => b.textContent?.trim() === "Tài nguyên");
  const oldLibraryTab = navItems.find((b) => b.textContent?.trim() === "Thư viện");

  assert.ok(promptTab, "Nút 'Prompt' phải tồn tại trên sidebar điều hướng");
  assert.ok(mediaTab, "Nút 'Tài nguyên' phải tồn tại trên sidebar điều hướng");
  assert.strictEqual(oldLibraryTab, undefined, "Không còn tab 'Thư viện' gộp chung cũ");
  console.log("  -> PASS: Đã tách thành 2 tab 'Prompt' và 'Tài nguyên' độc lập trên Sidebar.");

  // 2. Chuyển sang tab Prompt
  console.log("\n[TEST 2] Chuyển sang màn hình Prompt...");
  await act(async () => {
    promptTab.click();
    await new Promise((r) => setTimeout(r, 50));
  });

  const breadcrumb = container.querySelector('nav[aria-label="Breadcrumb"]');
  assert.ok(breadcrumb?.textContent?.includes("Prompt"), `Breadcrumb phải là 'Prompt', thực tế: ${breadcrumb?.textContent}`);
  assert.strictEqual(promptTab.getAttribute("aria-current"), "page", "Tab 'Prompt' phải có aria-current='page'");
  assert.strictEqual(mediaTab.getAttribute("aria-current"), null, "Tab 'Tài nguyên' không được active");

  // Kiểm tra header và màn hình Prompt
  const promptHeading = Array.from(container.querySelectorAll("h2")).find((h) => h.textContent?.trim() === "Prompt");
  assert.ok(promptHeading, "Màn hình Prompt phải có heading 'Prompt'");
  console.log("  -> PASS: Màn hình Prompt hiển thị chuẩn xác với header và breadcrumb riêng.");

  // 3. Chuyển sang tab Tài nguyên
  console.log("\n[TEST 3] Chuyển sang màn hình Tài nguyên...");
  await act(async () => {
    mediaTab.click();
    await new Promise((r) => setTimeout(r, 50));
  });

  assert.ok(breadcrumb?.textContent?.includes("Tài nguyên"), `Breadcrumb phải là 'Tài nguyên', thực tế: ${breadcrumb?.textContent}`);
  assert.strictEqual(mediaTab.getAttribute("aria-current"), "page", "Tab 'Tài nguyên' phải có aria-current='page'");
  assert.strictEqual(promptTab.getAttribute("aria-current"), null, "Tab 'Prompt' không được active");

  // Kiểm tra header và màn hình Tài nguyên
  const mediaHeading = Array.from(container.querySelectorAll("h2")).find((h) => h.textContent?.trim() === "Tài nguyên");
  assert.ok(mediaHeading, "Màn hình Tài nguyên phải có heading 'Tài nguyên'");
  console.log("  -> PASS: Màn hình Tài nguyên hiển thị chuẩn xác với header và breadcrumb riêng.");

  // 4. Kiểm tra không còn nút switch tab con kiểu gộp trong cả 2 màn hình
  console.log("\n[TEST 4] Kiểm tra loại bỏ hoàn toàn các nút gộp tab con cũ...");
  const oldPromptToggleBtn = Array.from(container.querySelectorAll("button")).find((b) => b.textContent?.includes("Mẫu Prompt (AI Templates)"));
  assert.strictEqual(oldPromptToggleBtn, undefined, "Không còn nút toggle 'Mẫu Prompt (AI Templates)' gộp bên trong màn hình");
  console.log("  -> PASS: Không còn nút toggle tab gộp cũ.");

  await act(async () => {
    root.unmount();
  });
  container.remove();

  console.log("\n>>> TẤT CẢ KIỂM THỬ ĐIỀU HƯỚNG TÁCH TAB/SCREEN ĐÃ VƯỢT QUA (PASS 100%) <<<");
}

runTests().catch((err) => {
  console.error("TEST FAILED:", err);
  process.exit(1);
});
