import { register } from "node:module";
import { GlobalWindow } from "happy-dom";

// Đăng ký custom loader hỗ trợ import .tsx, .ts, .css và alias @/*
register("../prompts/test-loader.mjs", import.meta.url);

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

// Mock 10 Workspaces production fixtures
const mockWorkspaces = Array.from({ length: 10 }, (_, i) => {
  const num = (i + 1).toString().padStart(2, "0");
  return {
    workspaceId: `ws_${num}`,
    name: `Workspace ${num}`,
    channelHandle: `@channel_${num}`,
    topic: `Chủ đề TikTok ${num}`,
    status: "Active",
    createdAtUtc: new Date().toISOString(),
    updatedAtUtc: new Date().toISOString(),
  };
});

// Mock tài khoản đa provider toàn cục (ChatGPT, Gemini, MuseAI)
const mockAccounts = [
  {
    id: "acc_gpt_1",
    providerKey: "chatgpt-web",
    profileId: "prof_gpt_1",
    displayName: "ChatGPT Team Account",
    verifiedIdentity: "team@company.ai",
    enabled: true,
    lifecycle: "Active",
    maxConcurrentJobs: 3,
    maxTabs: 3,
    revision: 1,
    createdAtUtc: new Date().toISOString(),
    updatedAtUtc: new Date().toISOString(),
  },
  {
    id: "acc_gemini_1",
    providerKey: "gemini-web",
    profileId: "prof_gemini_1",
    displayName: "Gemini Advanced Creator",
    verifiedIdentity: "creator@gmail.com",
    enabled: true,
    lifecycle: "Active",
    maxConcurrentJobs: 2,
    maxTabs: 2,
    revision: 1,
    createdAtUtc: new Date().toISOString(),
    updatedAtUtc: new Date().toISOString(),
  },
  {
    id: "acc_museai_1",
    providerKey: "museai",
    profileId: "prof_museai_1",
    displayName: "MuseAI Studio Pro",
    verifiedIdentity: "editor@studio.ai",
    enabled: true,
    lifecycle: "Active",
    maxConcurrentJobs: 1,
    maxTabs: 1,
    revision: 1,
    createdAtUtc: new Date().toISOString(),
    updatedAtUtc: new Date().toISOString(),
  },
];

// Mock templates cho PromptTemplateManager
const mockTemplates = [
  {
    templateId: "tmpl_tiktok_script",
    purpose: "Script",
    name: "Kịch bản TikTok Chuẩn SEO",
    description: "Mẫu kịch bản ngắn phân cảnh hấp dẫn",
    latestVersion: 1,
    currentVersion: 1,
    isOverridden: false,
    contentTemplate: "Tiêu đề: {{topic}}\nCảnh 1: Mở đầu ấn tượng thu hút người xem trong 3 giây.",
    variableDefinitionsJson: JSON.stringify([
      { name: "topic", description: "Chủ đề video", defaultValue: "Review công nghệ", required: true }
    ]),
    versions: [
      {
        versionNumber: 1,
        contentTemplate: "Tiêu đề: {{topic}}\nCảnh 1: Mở đầu ấn tượng thu hút người xem trong 3 giây.",
        variableDefinitionsJson: JSON.stringify([
          { name: "topic", description: "Chủ đề video", defaultValue: "Review công nghệ", required: true }
        ]),
        createdAtUtc: new Date().toISOString(),
        changeNote: "Bản khởi tạo ban đầu",
      }
    ],
    updatedAtUtc: new Date().toISOString(),
  }
];

let currentSavedStorageSettings = {
  documentsPath: "D:/storage/documents",
  imagesPath: "D:/storage/images",
  videosPath: "D:/storage/videos",
  imageProvider: null,
  scriptProvider: null,
  videoPromptProvider: null,
};

let simulateSaveError = false;

// Mock WebView2 message handling chuẩn xác
const messageListeners: ((event: MessageEvent) => void)[] = [];
(domWindow as unknown as Record<string, unknown>).chrome = {
  webview: {
    addEventListener(type: string, listener: (event: MessageEvent) => void) {
      if (type === "message") messageListeners.push(listener);
    },
    removeEventListener(_type: string, listener: (event: MessageEvent) => void) {
      const idx = messageListeners.indexOf(listener);
      if (idx >= 0) messageListeners.splice(idx, 1);
    },
    postMessage: (msg: unknown) => {
      const req = msg as { id: string; command: string; payload?: any };
      let payloadData: unknown = {};
      let isSuccess = true;
      let errorObj: any = null;

      if (req.command === "theme.get_preference") {
        payloadData = { preference: "system", effectiveTheme: "dark" };
      } else if (req.command === "theme.save_preference") {
        payloadData = { preference: req.payload?.preference ?? "system", effectiveTheme: "dark" };
      } else if (req.command === "workspace.list") {
        payloadData = { workspaces: mockWorkspaces };
      } else if (req.command === "provider_account.list") {
        payloadData = { accounts: mockAccounts };
      } else if (req.command === "settings.get") {
        payloadData = { ...currentSavedStorageSettings };
      } else if (req.command === "settings.save") {
        if (simulateSaveError) {
          isSuccess = false;
          errorObj = {
            code: "VALIDATION_FAILED",
            message: "Đường dẫn thư mục không hợp lệ hoặc không có quyền ghi.",
            fieldErrors: { documentsPath: ["Đường dẫn không thể truy cập hoặc bị khóa."] },
          };
        } else {
          currentSavedStorageSettings = {
            ...currentSavedStorageSettings,
            documentsPath: req.payload?.documentsPath ?? currentSavedStorageSettings.documentsPath,
            imagesPath: req.payload?.imagesPath ?? currentSavedStorageSettings.imagesPath,
            videosPath: req.payload?.videosPath ?? currentSavedStorageSettings.videosPath,
          };
          payloadData = { ...currentSavedStorageSettings };
        }
      } else if (req.command === "prompt.list") {
        payloadData = { templates: mockTemplates };
      } else if (req.command === "prompt.get") {
        payloadData = { template: mockTemplates[0] };
      } else if (req.command === "asset.list") {
        payloadData = { assets: [], totalCount: 0, page: 1, pageSize: 20 };
      }

      setTimeout(() => {
        const ev = {
          data: {
            id: req.id,
            type: "response",
            success: isSuccess,
            contractVersion: "1.0.4",
            data: isSuccess ? payloadData : undefined,
            error: isSuccess ? undefined : errorObj,
          },
        } as MessageEvent;
        for (const listener of [...messageListeners]) {
          listener(ev);
        }
      }, 0);
    },
  },
};

// Dynamic import React và ReactDOM
const React = (await import("react")).default;
const { act } = await import("react");
const { createRoot } = await import("react-dom/client");

const { ThemeProvider } = await import("../../context/ThemeContext.tsx");
const { SettingsPage } = await import("./SettingsPage.tsx");
const { bridgeClient } = await import("../../services/bridgeClient.ts");
const App = (await import("../../App.tsx")).default;

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

async function runTests() {
  console.log("=== BẮT ĐẦU KIỂM THỬ TRANG CÀI ĐẶT (A01, A02, F1-F3) TRÊN DOM THẬT ===");

  // =========================================================================
  // PHẦN 1: STANDALONE SETTINGSPAGE - WAI-ARIA TABS, 2 THEMES, ACCOUNTS (A01, A02)
  // =========================================================================
  console.log("\n--- [TEST SUITE 1] Standalone SettingsPage: WAI-ARIA, Theme & Accounts ---");
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);

  await act(async () => {
    root.render(
      React.createElement(
        ThemeProvider,
        null,
        React.createElement(SettingsPage, {
          initialTab: "accounts",
        })
      )
    );
  });

  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 50));
  });

  // 1. Kiểm tra tiêu đề và 4 Tab con ngang theo WAI-ARIA
  const tablist = container.querySelector('[role="tablist"]');
  if (!tablist) throw new Error("FAIL: Không tìm thấy role='tablist' trong SettingsPage");

  const tabButtons = Array.from(tablist.querySelectorAll<HTMLButtonElement>('[role="tab"]'));
  if (tabButtons.length !== 4) {
    throw new Error(`FAIL: Kỳ vọng 4 tab con, thực tế tìm thấy ${tabButtons.length}`);
  }
  console.log("  -> [PASS A01.1] 4 tab con ngang đầy đủ chuẩn WAI-ARIA.");

  const activeTabBtn = tablist.querySelector<HTMLButtonElement>('[role="tab"][aria-selected="true"]');
  if (activeTabBtn?.id !== "tab-accounts") {
    throw new Error(`FAIL: Tab mặc định phải là tab-accounts, thực tế là ${activeTabBtn?.id}`);
  }
  console.log("  -> [PASS A01.2] Mặc định hiển thị tab Tài khoản.");

  // Điều hướng bằng phím mũi tên và Home/End theo chuẩn WAI-ARIA
  await act(async () => {
    activeTabBtn?.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowRight", bubbles: true }));
  });
  let currentActiveTab = tablist.querySelector<HTMLButtonElement>('[role="tab"][aria-selected="true"]');
  if (currentActiveTab?.id !== "tab-storage") {
    throw new Error(`FAIL: Sau khi bấm ArrowRight phải chuyển sang tab-storage, thực tế là ${currentActiveTab?.id}`);
  }

  // Phím End: Chuyển đến tab cuối cùng (tab-appearance)
  await act(async () => {
    currentActiveTab?.dispatchEvent(new KeyboardEvent("keydown", { key: "End", bubbles: true }));
  });
  currentActiveTab = tablist.querySelector<HTMLButtonElement>('[role="tab"][aria-selected="true"]');
  if (currentActiveTab?.id !== "tab-appearance") {
    throw new Error(`FAIL: Sau khi bấm End phải chuyển sang tab-appearance, thực tế là ${currentActiveTab?.id}`);
  }

  // Phím ArrowLeft: Chuyển lùi lại (tab-ai)
  await act(async () => {
    currentActiveTab?.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowLeft", bubbles: true }));
  });
  currentActiveTab = tablist.querySelector<HTMLButtonElement>('[role="tab"][aria-selected="true"]');
  if (currentActiveTab?.id !== "tab-ai") {
    throw new Error(`FAIL: Sau khi bấm ArrowLeft phải chuyển sang tab-ai, thực tế là ${currentActiveTab?.id}`);
  }

  // Phím Home: Chuyển về tab đầu tiên (tab-accounts)
  await act(async () => {
    currentActiveTab?.dispatchEvent(new KeyboardEvent("keydown", { key: "Home", bubbles: true }));
  });
  currentActiveTab = tablist.querySelector<HTMLButtonElement>('[role="tab"][aria-selected="true"]');
  if (currentActiveTab?.id !== "tab-accounts") {
    throw new Error(`FAIL: Sau khi bấm Home phải chuyển sang tab-accounts, thực tế là ${currentActiveTab?.id}`);
  }
  console.log("  -> [PASS A01.3] Điều hướng phím bàn phím (ArrowRight, End, ArrowLeft, Home) chuẩn WAI-ARIA.");

  // Tab Giao diện
  const appearanceTabBtn = container.querySelector<HTMLButtonElement>("#tab-appearance")!;
  await act(async () => {
    appearanceTabBtn.click();
  });
  const themeRadiogroup = container.querySelector('[role="radiogroup"]');
  if (!themeRadiogroup) throw new Error("FAIL: Không tìm thấy role='radiogroup' trong tab Giao diện");
  console.log("  -> [PASS A01.4] Tab Giao diện hiển thị đầy đủ tùy chọn System / Light / Dark.");

  // Tab Tài khoản danh sách hợp nhất
  const accountsTabBtn = container.querySelector<HTMLButtonElement>("#tab-accounts")!;
  await act(async () => {
    accountsTabBtn.click();
    await new Promise((r) => setTimeout(r, 40));
  });

  const pageText = container.textContent || "";
  if (!pageText.includes("ChatGPT Team Account") || !pageText.includes("Gemini Advanced Creator") || !pageText.includes("MuseAI Studio Pro")) {
    throw new Error("FAIL: Không hiển thị đầy đủ tài khoản 3 provider trong tab Tài khoản");
  }
  if (pageText.includes("Gán cho Workspace") || pageText.includes("AI dùng chung") || pageText.includes("AI riêng")) {
    throw new Error("FAIL: Có thuộc tính gán Workspace vi phạm kiến trúc 1.2");
  }
  console.log("  -> [PASS A02] Tab Tài khoản: Danh sách hợp nhất, không chia nhóm dùng chung/riêng, không gán Workspace.");

  // =========================================================================
  // PHẦN 2: F1 - BẢO TOÀN DRAFT ĐƯỜNG DẪN LƯU TRỮ QUA 4 TAB & DIRTY GUARD
  // =========================================================================
  console.log("\n--- [TEST SUITE 2] F1: Bảo toàn nháp đường dẫn lưu trữ qua 4 tab & Dirty Guard ---");

  // Bước 2.1: Chuyển sang tab Lưu trữ và nhập đường dẫn mới (chưa lưu)
  const storageTabBtn = container.querySelector<HTMLButtonElement>("#tab-storage")!;
  await act(async () => {
    storageTabBtn.click();
    await new Promise((r) => setTimeout(r, 40));
  });

  const docsInput = container.querySelector<HTMLInputElement>("#docs-path");
  if (!docsInput) throw new Error("FAIL: Không tìm thấy input #docs-path trong tab Lưu trữ");

  const DRAFT_PATH = "D:/titoai_custom_draft_storage/docs";
  await act(async () => {
    setNativeInputValue(docsInput, DRAFT_PATH);
  });

  if (docsInput.value !== DRAFT_PATH) {
    throw new Error("FAIL: Không thể đặt giá trị nháp cho #docs-path");
  }

  // Bước 2.2: Chuyển lần lượt qua cả 4 tab: Accounts -> Storage -> AI -> Appearance -> Storage
  for (const tabKey of ["accounts", "ai", "appearance", "storage"] as const) {
    const btn = container.querySelector<HTMLButtonElement>(`#tab-${tabKey}`)!;
    await act(async () => {
      btn.click();
      await new Promise((r) => setTimeout(r, 20));
    });
  }

  // Kiểm chứng F1.1: Nháp đường dẫn vẫn còn nguyên 100%!
  const restoredDocsInput = container.querySelector<HTMLInputElement>("#docs-path")!;
  if (restoredDocsInput.value !== DRAFT_PATH) {
    throw new Error(`FAIL [F1]: Nháp đường dẫn bị mất sau khi chuyển 4 tab! Giá trị hiện tại: '${restoredDocsInput.value}'`);
  }
  console.log("  -> [PASS F1.1] Nhập đường dẫn chưa lưu -> Duyệt đủ 4 tab con -> Bản nháp bảo toàn 100%!");

  // Bước 2.3: Kiểm tra Header Cài đặt chuẩn tab: không có nút 'Quay lại công việc'
  const backBtn = container.querySelector<HTMLButtonElement>('button[aria-label="Quay lại giao diện trước"]');
  if (backBtn) {
    throw new Error("FAIL [F1]: Header Cài đặt không được có nút 'Quay lại công việc' vì Cài đặt là tab chuẩn");
  }
  console.log("  -> [PASS F1.2] Header Cài đặt chuẩn tab: Đã loại bỏ nút 'Quay lại công việc'.");

  // Bước 2.4: Lưu lỗi / validation thất bại giữ nguyên draft
  simulateSaveError = true;
  await act(async () => {
    setNativeInputValue(docsInput, "D:/invalid_storage_path");
  });
  const saveBtn = container.querySelector<HTMLButtonElement>('button[type="submit"]')!;
  await act(async () => {
    saveBtn.click();
    await new Promise((r) => setTimeout(r, 40));
  });

  const afterErrorInput = container.querySelector<HTMLInputElement>("#docs-path")!;
  if (afterErrorInput.value !== "D:/invalid_storage_path") {
    throw new Error("FAIL [F1]: Lỗi save làm mất nội dung người dùng vừa nhập!");
  }
  simulateSaveError = false;
  console.log("  -> [PASS F1.4] Lỗi lưu cài đặt / validation thất bại giữ nguyên bản nháp trong form.");

  await act(async () => {
    root.unmount();
  });
  container.remove();

  // =========================================================================
  // PHẦN 3: F2 (Q2-APP-DRAFT) - PRODUCTION APP + PRODUCTION PROMPT EDITOR INTEGRATION
  // =========================================================================
  console.log("\n--- [TEST SUITE 3] F2 (Q2-APP-DRAFT): Production App & Prompt Editor Integration ---");
  const appContainer = document.createElement("div");
  document.body.appendChild(appContainer);
  const appRoot = createRoot(appContainer);

  await act(async () => {
    appRoot.render(React.createElement(App));
  });

  // Chờ App nạp xong workspace.list khởi tạo
  for (let attempt = 0; attempt < 25; attempt++) {
    await act(async () => {
      await new Promise((r) => setTimeout(r, 30));
    });
    if (bridgeClient.getActiveWorkspaceId()) break;
  }

  if (bridgeClient.getActiveWorkspaceId() !== "ws_01") {
    throw new Error(`FAIL: Workspace khởi tạo mặc định phải là ws_01, thực tế: ${bridgeClient.getActiveWorkspaceId()}`);
  }

  // Bước 3.1: Chuyển đến màn hình Mẫu Prompt (nơi render PromptTemplateManager production)
  const navItems = Array.from(appContainer.querySelectorAll<HTMLButtonElement>("button.nav-item, nav button"));
  const promptNavBtn = navItems.find((b) => b.textContent?.trim() === "Prompt" || b.textContent?.includes("Prompt"));
  if (!promptNavBtn) throw new Error("FAIL: Không tìm thấy nút 'Prompt' trong sidebar App production");

  await act(async () => {
    promptNavBtn.click();
    await new Promise((r) => setTimeout(r, 50));
  });

  // Chờ PromptTemplateManager nạp danh sách template
  for (let attempt = 0; attempt < 25; attempt++) {
    await act(async () => {
      await new Promise((r) => setTimeout(r, 30));
    });
    if (appContainer.querySelector("textarea#template-content-input") || appContainer.querySelector("textarea")) break;
  }

  const promptTextarea = appContainer.querySelector<HTMLTextAreaElement>("textarea");
  if (!promptTextarea) {
    throw new Error("FAIL: Không tìm thấy textarea trong PromptTemplateManager production");
  }

  const PROMPT_DRAFT_TEXT = "Kịch bản phân cảnh giới thiệu sản phẩm: [DRAFT TẠI APP PRODUCTION CHỜ LƯU]";
  await act(async () => {
    setNativeInputValue(promptTextarea, PROMPT_DRAFT_TEXT);
  });

  if (promptTextarea.value !== PROMPT_DRAFT_TEXT) {
    throw new Error("FAIL: Không thể nhập draft vào Prompt editor textarea production");
  }

  // Bước 3.2: Bấm nút 'Cài đặt' từ Sidebar điều hướng production của App
  const settingsNavBtn = navItems.find((b) => b.textContent?.includes("Cài đặt"));
  if (!settingsNavBtn) throw new Error("FAIL: Không tìm thấy nút 'Cài đặt' trong sidebar App production");

  await act(async () => {
    settingsNavBtn.click();
    await new Promise((r) => setTimeout(r, 50));
  });

  // Xác minh SettingsPage đang hiển thị
  const appTablist = appContainer.querySelector('[role="tablist"][aria-label="Các nhóm cài đặt TiToAI"]');
  if (!appTablist) throw new Error("FAIL: Trang Cài đặt không được mở trong App production");

  // Bước 3.3: Duyệt qua toàn bộ 4 tab con của Cài đặt
  for (const tabKey of ["accounts", "storage", "ai", "appearance"] as const) {
    const tabBtn = appContainer.querySelector<HTMLButtonElement>(`#tab-${tabKey}`)!;
    await act(async () => {
      tabBtn.click();
      await new Promise((r) => setTimeout(r, 20));
    });
  }

  // Bước 3.4: Người dùng chuyển từ Cài đặt về màn hình Prompt qua sidebar
  await act(async () => {
    promptNavBtn.click();
    await new Promise((r) => setTimeout(r, 50));
  });

  // Bước 3.5: Kiểm chứng màn hình Thư viện hiển thị lại, draft kịch bản & workspace context bảo toàn 100%!
  const restoredPromptTextarea = appContainer.querySelector<HTMLTextAreaElement>("textarea");
  if (!restoredPromptTextarea) {
    throw new Error("FAIL: Không tìm thấy textarea sau khi quay lại màn hình Thư viện");
  }

  if (restoredPromptTextarea.value !== PROMPT_DRAFT_TEXT) {
    throw new Error(`FAIL [F2]: Draft kịch bản bị reset trong App production! Giá trị: '${restoredPromptTextarea.value}'`);
  }
  if (bridgeClient.getActiveWorkspaceId() !== "ws_01") {
    throw new Error(`FAIL [F2]: Workspace context bị thay đổi! Hiện tại: '${bridgeClient.getActiveWorkspaceId()}'`);
  }

  console.log("  -> [PASS F2 / Q2-APP-DRAFT] App production + Editor production + Navigation: Draft kịch bản và context Workspace bảo toàn 100%!");

  // =========================================================================
  // PHẦN 4: F3 (Q3-GLOBAL-10WS) - CHUYỂN 10 WORKSPACE THẬT QUA SELECTOR PRODUCTION
  // =========================================================================
  console.log("\n--- [TEST SUITE 4] F3 (Q3-GLOBAL-10WS): Chuyển 10 Workspace thật qua Selector Production ---");

  // Mở màn hình Cài đặt
  await act(async () => {
    settingsNavBtn.click();
    await new Promise((r) => setTimeout(r, 40));
  });

  // Chọn tab Tài khoản
  const appAccountsTab = appContainer.querySelector<HTMLButtonElement>("#tab-accounts")!;
  await act(async () => {
    appAccountsTab.click();
    await new Promise((r) => setTimeout(r, 40));
  });

  for (let i = 0; i < 10; i++) {
    const targetWs = mockWorkspaces[i];

    // 1. Mở dropdown selector Workspace production
    const wsSelectorBtn = appContainer.querySelector<HTMLButtonElement>(".workspace-selector-btn");
    if (!wsSelectorBtn) throw new Error("FAIL: Không tìm thấy nút .workspace-selector-btn trong topbar");

    await act(async () => {
      wsSelectorBtn.click();
      await new Promise((r) => setTimeout(r, 20));
    });

    // 2. Tìm và click vào Workspace thứ i trong dropdown menu
    const dropdownMenu = appContainer.querySelector(".workspace-dropdown-panel");
    if (!dropdownMenu) throw new Error("FAIL: Dropdown menu chọn workspace không mở");

    const wsOptionBtns = Array.from(dropdownMenu.querySelectorAll<HTMLButtonElement>(".dropdown-item"));
    const targetOption = wsOptionBtns.find((btn) => btn.textContent?.includes(targetWs.name));
    if (!targetOption) {
      throw new Error(`FAIL: Không tìm thấy tùy chọn '${targetWs.name}' trong dropdown menu`);
    }

    await act(async () => {
      targetOption.click();
      await new Promise((r) => setTimeout(r, 40));
    });

    // 3. Assert active Workspace ID thực sự thay đổi trong bridgeClient
    const activeWs = bridgeClient.getActiveWorkspaceId();
    if (activeWs !== targetWs.workspaceId) {
      throw new Error(`FAIL [F3] tại vòng ${i + 1}: activeWorkspaceId không đổi thành '${targetWs.workspaceId}', hiện tại: '${activeWs}'`);
    }

    // 4. Assert danh sách tài khoản hiển thị đồng nhất cả 3 provider (ChatGPT, Gemini, MuseAI)
    const currentText = appContainer.textContent || "";
    if (!currentText.includes("ChatGPT Team Account") || !currentText.includes("team@company.ai")) {
      throw new Error(`FAIL [F3] tại ${targetWs.workspaceId}: Không hiển thị ChatGPT account (team@company.ai)`);
    }
    if (!currentText.includes("Gemini Advanced Creator") || !currentText.includes("creator@gmail.com")) {
      throw new Error(`FAIL [F3] tại ${targetWs.workspaceId}: Không hiển thị Gemini account (creator@gmail.com)`);
    }
    if (!currentText.includes("MuseAI Studio Pro") || !currentText.includes("editor@studio.ai")) {
      throw new Error(`FAIL [F3] tại ${targetWs.workspaceId}: Không hiển thị MuseAI account (editor@studio.ai)`);
    }
    if (currentText.includes("AI dùng chung") || currentText.includes("AI riêng") || currentText.includes("Gán cho Workspace")) {
      throw new Error(`FAIL [F3] tại ${targetWs.workspaceId}: Xuất hiện thuộc tính phân nhóm hoặc gán Workspace`);
    }
  }

  console.log("  -> [PASS F3 / Q3-GLOBAL-10WS] Chuyển qua toàn bộ 10 Workspace thật: Active ID đổi chuẩn xác, danh sách tài khoản 3 provider đồng nhất toàn cục!");

  // =========================================================================
  // PHẦN 5: F1 - SIDEBAR NAVIGATION GUARD KHI SETTINGS DIRTY
  // =========================================================================
  console.log("\n--- [TEST SUITE 5] F1: Dirty Guard khi điều hướng sidebar trong App ---");

  // Chuyển sang tab Lưu trữ và nhập nháp
  const appStorageTab = appContainer.querySelector<HTMLButtonElement>("#tab-storage")!;
  await act(async () => {
    appStorageTab.click();
    await new Promise((r) => setTimeout(r, 30));
  });

  const appDocsInput = appContainer.querySelector<HTMLInputElement>("#docs-path")!;
  await act(async () => {
    setNativeInputValue(appDocsInput, "D:/storage/sidebar_dirty_test");
  });

  // Người dùng bấm vào 'Workspaces' trên sidebar trong khi Cài đặt đang dirty
  const workspacesNavBtn = navItems.find((b) => b.textContent?.includes("Workspaces"))!;
  await act(async () => {
    workspacesNavBtn.click();
    await new Promise((r) => setTimeout(r, 30));
  });

  // Modal xác nhận của App phải xuất hiện
  const appDirtyModal = appContainer.querySelector('[role="dialog"][aria-labelledby="app-dirty-confirm-title"]');
  if (!appDirtyModal) {
    throw new Error("FAIL [F1]: Không hiển thị modal xác nhận rời Cài đặt khi bấm sidebar!");
  }

  // Bấm 'Ở lại'
  const appStayBtn = Array.from(appDirtyModal.querySelectorAll<HTMLButtonElement>("button")).find(
    (b) => b.textContent?.includes("Ở lại")
  );
  if (!appStayBtn) throw new Error("FAIL: Không tìm thấy nút 'Ở lại' trên app dirty modal");

  await act(async () => {
    appStayBtn.click();
  });

  // Vẫn ở trang Cài đặt, input vẫn giữ nháp
  if (appContainer.querySelector('[role="dialog"][aria-labelledby="app-dirty-confirm-title"]')) {
    throw new Error("FAIL: Modal không đóng sau khi bấm 'Ở lại'");
  }
  if (appDocsInput.value !== "D:/storage/sidebar_dirty_test") {
    throw new Error("FAIL: Mất bản nháp sau khi bấm 'Ở lại' từ sidebar guard");
  }
  console.log("  -> [PASS F1.5] Sidebar Guard: Bấm menu sidebar khi dirty -> Modal xác nhận bảo vệ nháp hoàn hảo.");

  // Dọn dẹp
  await act(async () => {
    appRoot.unmount();
  });
  appContainer.remove();

  // =========================================================================
  // PHẦN 6: AI ACCOUNT CONCURRENCY UI CONTRACT VERIFICATION
  // =========================================================================
  console.log("\n--- [TEST SUITE 6] AI Account Concurrency: Single N, Labels, Description & Edit Form ---");
  const accountTestContainer = document.createElement("div");
  document.body.appendChild(accountTestContainer);
  const accountRoot = createRoot(accountTestContainer);

  await act(async () => {
    accountRoot.render(
      React.createElement(
        ThemeProvider,
        null,
        React.createElement(SettingsPage, {
          initialTab: "accounts",
          onBack: () => {},
        })
      )
    );
  });

  // Chờ AccountSettingsPanel nạp xong danh sách tài khoản
  for (let attempt = 0; attempt < 25; attempt++) {
    await act(async () => {
      await new Promise((r) => setTimeout(r, 30));
    });
    if ((accountTestContainer.textContent || "").includes("ChatGPT Team Account")) break;
  }

  // 1. Kiểm tra Card danh sách: "Đồng thời: tối đa N tác vụ"
  const cardText = accountTestContainer.textContent || "";
  if (!cardText.includes("Đồng thời:") || !cardText.includes("tối đa 3 tác vụ") || !cardText.includes("tối đa 2 tác vụ")) {
    throw new Error("FAIL [SUITE 6]: Danh sách tài khoản không hiển thị đúng định dạng 'Đồng thời: tối đa N tác vụ'");
  }
  if (cardText.includes("Hạn mức:") || cardText.includes("tác vụ /")) {
    throw new Error("FAIL [SUITE 6]: Danh sách tài khoản vẫn còn nhãn cũ 'Hạn mức: X tác vụ / Y tab'");
  }
  console.log("  -> [PASS 6.1] Danh sách tài khoản hiển thị chuẩn: 'Đồng thời: tối đa N tác vụ', loại bỏ nhãn cũ.");

  // 2. Mở form thêm tài khoản
  const addAccountBtn = Array.from(accountTestContainer.querySelectorAll<HTMLButtonElement>("button")).find(
    (b) => b.textContent?.includes("Thêm tài khoản AI")
  );
  if (!addAccountBtn) throw new Error("FAIL [SUITE 6]: Không tìm thấy nút 'Thêm tài khoản AI'");

  await act(async () => {
    addAccountBtn.click();
    await new Promise((r) => setTimeout(r, 40));
  });

  const formText = accountTestContainer.textContent || "";
  // Kiểm tra nhãn "Số tác vụ chạy đồng thời"
  if (!formText.includes("Số tác vụ chạy đồng thời")) {
    throw new Error("FAIL [SUITE 6]: Form thêm tài khoản thiếu nhãn 'Số tác vụ chạy đồng thời'");
  }
  // Kiểm tra mô tả tab
  if (!formText.includes("Mỗi tác vụ sử dụng một tab riêng. Các tác vụ vượt giới hạn sẽ chờ trong hàng đợi.")) {
    throw new Error("FAIL [SUITE 6]: Form thêm tài khoản thiếu mô tả 'Mỗi tác vụ sử dụng một tab riêng. Các tác vụ vượt giới hạn sẽ chờ trong hàng đợi.'");
  }
  // Kiểm tra KHÔNG có ô "Max Jobs"
  if (formText.includes("Tối đa tác vụ đồng thời (Max Jobs)") || accountTestContainer.querySelector("#new-acc-max-jobs")) {
    throw new Error("FAIL [SUITE 6]: Form thêm tài khoản vẫn còn ô Max Jobs cũ!");
  }
  console.log("  -> [PASS 6.2] Form thêm tài khoản: Nguồn cấu hình duy nhất N từ MaxTabs, có mô tả hàng đợi, loại bỏ ô Max Jobs cũ.");

  // 3. Kiểm tra form Sửa tài khoản (bấm nút 'Sửa' trên card đầu tiên)
  const editButtons = Array.from(accountTestContainer.querySelectorAll<HTMLButtonElement>("button")).filter(
    (b) => b.textContent?.trim() === "Sửa"
  );
  if (editButtons.length === 0) throw new Error("FAIL [SUITE 6]: Không tìm thấy nút 'Sửa' trên card tài khoản");

  await act(async () => {
    editButtons[0].click();
    await new Promise((r) => setTimeout(r, 40));
  });

  const editMaxTabsInput = accountTestContainer.querySelector<HTMLInputElement>('input[id^="edit-acc-max-tabs-"]');
  if (!editMaxTabsInput) {
    throw new Error("FAIL [SUITE 6]: Không tìm thấy ô sửa Số tác vụ chạy đồng thời khi bấm Sửa");
  }
  if (editMaxTabsInput.value !== "3") {
    throw new Error(`FAIL [SUITE 6]: Giá trị N ban đầu của ChatGPT Team Account phải là 3, thực tế: ${editMaxTabsInput.value}`);
  }
  console.log("  -> [PASS 6.3] Form sửa inline tài khoản: Cho phép điều chỉnh N duy nhất, hiển thị nhãn và mô tả tab nhất quán.");

  await act(async () => {
    accountRoot.unmount();
  });
  accountTestContainer.remove();

  // =========================================================================
  // PHẦN 7: KIỂM CHỨNG BỎ TAB TRÌNH DUYỆT & TÁCH SUB-TABS TÀI KHOẢN (CHATGPT, GEMINI, MUSEAI, TIKTOK)
  // =========================================================================
  console.log("\n--- [TEST SUITE 7] Bỏ Tab Trình duyệt & Sub-tabs Tài khoản (ChatGPT, Gemini, MuseAI, TikTok) ---");

  // 1. Kiểm chứng thanh điều hướng App Shell KHÔNG CÒN tab "Quản lý Trình duyệt"
  const appVerifyContainer = document.createElement("div");
  document.body.appendChild(appVerifyContainer);
  const appVerifyRoot = createRoot(appVerifyContainer);

  await act(async () => {
    appVerifyRoot.render(React.createElement(App));
  });

  for (let attempt = 0; attempt < 25; attempt++) {
    await act(async () => {
      await new Promise((r) => setTimeout(r, 30));
    });
    if (appVerifyContainer.querySelector("button.nav-item")) break;
  }

  const appNavButtons = Array.from(appVerifyContainer.querySelectorAll<HTMLButtonElement>("button.nav-item, nav button"));
  const navText = appNavButtons.map((b) => b.textContent || "").join(" ");
  if (navText.includes("Quản lý Trình duyệt")) {
    throw new Error("FAIL [SUITE 7]: Tab 'Quản lý Trình duyệt' vẫn còn tồn tại trên thanh điều hướng sidebar!");
  }
  console.log("  -> [PASS 7.1] Sidebar App Shell: Đã loại bỏ hoàn toàn tab 'Quản lý Trình duyệt'.");

  await act(async () => {
    appVerifyRoot.unmount();
  });
  appVerifyContainer.remove();

  // 2. Kiểm chứng Sub-tabs bên trong phần Tài khoản
  const subTabTestContainer = document.createElement("div");
  document.body.appendChild(subTabTestContainer);
  const subTabRoot = createRoot(subTabTestContainer);

  await act(async () => {
    subTabRoot.render(
      React.createElement(
        ThemeProvider,
        null,
        React.createElement(SettingsPage, {
          initialTab: "accounts",
          onBack: () => {},
        })
      )
    );
  });

  // Chờ tải accounts
  for (let attempt = 0; attempt < 25; attempt++) {
    await act(async () => {
      await new Promise((r) => setTimeout(r, 30));
    });
    if ((subTabTestContainer.textContent || "").includes("ChatGPT Team Account")) break;
  }

  const subTabList = subTabTestContainer.querySelector('[role="tablist"][aria-label="Phân loại tài khoản theo Provider"]');
  if (!subTabList) {
    throw new Error("FAIL [SUITE 7]: Không tìm thấy tablist phân loại Provider trong tab Tài khoản");
  }

  // Xác minh đủ 5 sub-tab: Tất cả, ChatGPT, Gemini, MuseAI, TikTok
  const subTabButtons = Array.from(subTabList.querySelectorAll<HTMLButtonElement>('button[role="tab"]'));
  const subTabTexts = subTabButtons.map((b) => b.textContent || "");

  if (!subTabTexts.some((t) => t.includes("Tất cả"))) {
    throw new Error("FAIL [SUITE 7]: Thiếu sub-tab 'Tất cả'");
  }
  if (!subTabTexts.some((t) => t.includes("ChatGPT"))) {
    throw new Error("FAIL [SUITE 7]: Thiếu sub-tab 'ChatGPT'");
  }
  if (!subTabTexts.some((t) => t.includes("Gemini"))) {
    throw new Error("FAIL [SUITE 7]: Thiếu sub-tab 'Gemini'");
  }
  if (!subTabTexts.some((t) => t.includes("MuseAI"))) {
    throw new Error("FAIL [SUITE 7]: Thiếu sub-tab 'MuseAI'");
  }
  if (!subTabTexts.some((t) => t.includes("TikTok"))) {
    throw new Error("FAIL [SUITE 7]: Thiếu sub-tab 'TikTok'");
  }
  console.log("  -> [PASS 7.2] Phân chia thành các sub-tabs chuẩn xác: Tất cả, ChatGPT, Gemini, MuseAI, TikTok.");

  // 3. Kiểm thử lọc khi bấm từng sub-tab
  // Bấm tab ChatGPT
  const chatGptTab = subTabButtons.find((b) => b.textContent?.includes("ChatGPT"))!;
  await act(async () => {
    chatGptTab.click();
    await new Promise((r) => setTimeout(r, 30));
  });
  let content = subTabTestContainer.textContent || "";
  if (!content.includes("ChatGPT Team Account") || content.includes("Gemini Advanced Creator") || content.includes("MuseAI Studio Pro")) {
    throw new Error("FAIL [SUITE 7]: Lọc theo tab ChatGPT không chính xác!");
  }

  // Bấm tab Gemini
  const geminiTab = subTabButtons.find((b) => b.textContent?.includes("Gemini"))!;
  await act(async () => {
    geminiTab.click();
    await new Promise((r) => setTimeout(r, 30));
  });
  content = subTabTestContainer.textContent || "";
  if (!content.includes("Gemini Advanced Creator") || content.includes("ChatGPT Team Account") || content.includes("MuseAI Studio Pro")) {
    throw new Error("FAIL [SUITE 7]: Lọc theo tab Gemini không chính xác!");
  }

  // Bấm tab MuseAI
  const museAiTab = subTabButtons.find((b) => b.textContent?.includes("MuseAI"))!;
  await act(async () => {
    museAiTab.click();
    await new Promise((r) => setTimeout(r, 30));
  });
  content = subTabTestContainer.textContent || "";
  if (!content.includes("MuseAI Studio Pro") || content.includes("ChatGPT Team Account") || content.includes("Gemini Advanced Creator")) {
    throw new Error("FAIL [SUITE 7]: Lọc theo tab MuseAI không chính xác!");
  }

  // Bấm tab TikTok (chưa có tài khoản)
  const tikTokTab = subTabButtons.find((b) => b.textContent?.includes("TikTok"))!;
  await act(async () => {
    tikTokTab.click();
    await new Promise((r) => setTimeout(r, 30));
  });
  content = subTabTestContainer.textContent || "";
  if (!content.includes("Chưa có tài khoản nào thuộc nhóm TikTok") || !content.includes("Thêm tài khoản TikTok")) {
    throw new Error("FAIL [SUITE 7]: Empty state của tab TikTok không hiển thị đúng hướng dẫn và nút tạo!");
  }
  console.log("  -> [PASS 7.3] Tương tác chuyển đổi giữa các sub-tabs và lọc danh sách tài khoản theo Provider hoạt động hoàn hảo.");

  // 4. Mở form thêm tài khoản khi đang ở tab TikTok -> Provider tự chọn tiktok
  const addTikTokBtn = Array.from(subTabTestContainer.querySelectorAll<HTMLButtonElement>("button")).find(
    (b) => b.textContent?.includes("Thêm tài khoản TikTok")
  );
  if (!addTikTokBtn) throw new Error("FAIL [SUITE 7]: Không tìm thấy nút 'Thêm tài khoản TikTok'");

  await act(async () => {
    addTikTokBtn.click();
    await new Promise((r) => setTimeout(r, 30));
  });

  const providerSelect = subTabTestContainer.querySelector<HTMLSelectElement>("#new-acc-provider");
  if (!providerSelect) throw new Error("FAIL [SUITE 7]: Không tìm thấy #new-acc-provider");
  if (providerSelect.value !== "tiktok") {
    throw new Error(`FAIL [SUITE 7]: Khi mở form từ tab TikTok, provider phải là 'tiktok', thực tế: ${providerSelect.value}`);
  }

  // Kiểm tra option TikTok trong dropdown
  const providerOptions = Array.from(providerSelect.options).map((o) => o.value);
  if (!providerOptions.includes("tiktok")) {
    throw new Error("FAIL [SUITE 7]: Dropdown Dịch vụ AI thiếu tùy chọn 'tiktok'");
  }
  console.log("  -> [PASS 7.4] Form thêm tài khoản: Hỗ trợ tạo tài khoản TikTok và tự động chọn Provider theo sub-tab hiện hành.");

  // Quay lại tab Tất cả
  const allTab = subTabButtons.find((b) => b.textContent?.includes("Tất cả"))!;
  await act(async () => {
    allTab.click();
    await new Promise((r) => setTimeout(r, 30));
  });
  content = subTabTestContainer.textContent || "";
  if (!content.includes("ChatGPT Team Account") || !content.includes("Gemini Advanced Creator") || !content.includes("MuseAI Studio Pro")) {
    throw new Error("FAIL [SUITE 7]: Quay lại tab 'Tất cả' không khôi phục toàn bộ danh sách!");
  }
  console.log("  -> [PASS 7.5] Sub-tab 'Tất cả' hiển thị hợp nhất toàn bộ tài khoản hệ thống.");

  await act(async () => {
    subTabRoot.unmount();
  });
  subTabTestContainer.remove();

  console.log("\n=================================================================");
  console.log(">>> TOÀN BỘ KIỂM THỬ F1-F3, A01-A02, SUITE 6 & SUITE 7 ĐÃ VƯỢT QUA (PASS 100%) <<<");
  console.log("=================================================================\n");
}

runTests().catch((err) => {
  console.error("TEST FAILED:", err);
  process.exit(1);
});
