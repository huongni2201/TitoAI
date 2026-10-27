import { register } from "node:module";
import { GlobalWindow } from "happy-dom";
import assert from "node:assert/strict";
import test from "node:test";

register("../prompts/test-loader.mjs", import.meta.url);

const domWindow = new GlobalWindow({ url: "http://localhost:3000" });
globalThis.window = domWindow as unknown as Window & typeof globalThis;
globalThis.document = domWindow.document as unknown as Document;
globalThis.HTMLElement = domWindow.HTMLElement as unknown as typeof HTMLElement;
globalThis.HTMLButtonElement = domWindow.HTMLButtonElement as unknown as typeof HTMLButtonElement;
globalThis.HTMLDivElement = domWindow.HTMLDivElement as unknown as typeof HTMLDivElement;
globalThis.HTMLInputElement = domWindow.HTMLInputElement as unknown as typeof HTMLInputElement;
globalThis.HTMLSelectElement = domWindow.HTMLSelectElement as unknown as typeof HTMLSelectElement;
globalThis.HTMLTextAreaElement = domWindow.HTMLTextAreaElement as unknown as typeof HTMLTextAreaElement;
globalThis.KeyboardEvent = domWindow.KeyboardEvent as unknown as typeof KeyboardEvent;
globalThis.MouseEvent = domWindow.MouseEvent as unknown as typeof MouseEvent;
globalThis.Event = domWindow.Event as unknown as typeof Event;
globalThis.CustomEvent = domWindow.CustomEvent as unknown as typeof CustomEvent;
globalThis.UIEvent = domWindow.UIEvent as unknown as typeof UIEvent;
globalThis.FocusEvent = domWindow.FocusEvent as unknown as typeof FocusEvent;
globalThis.NodeFilter = (domWindow.NodeFilter || {
  SHOW_ALL: -1,
  SHOW_ELEMENT: 1,
  FILTER_ACCEPT: 1,
  FILTER_REJECT: 2,
  FILTER_SKIP: 3,
}) as unknown as typeof NodeFilter;
globalThis.getComputedStyle = domWindow.getComputedStyle.bind(domWindow) as unknown as typeof getComputedStyle;
globalThis.MutationObserver = domWindow.MutationObserver as unknown as typeof MutationObserver;
globalThis.requestAnimationFrame = (cb) => setTimeout(cb, 0);
globalThis.cancelAnimationFrame = (id) => clearTimeout(id);
(globalThis as unknown as Record<string, unknown>).IS_REACT_ACT_ENVIRONMENT = true;

let lastBridgeCommand: string | null = null;
let lastBridgePayload: Record<string, unknown> | null = null;
let executedCommands: string[] = [];

let mockConfig: Record<string, any> = {
  projectId: "prj_test_01",
  workspaceId: "ws_test_01",
  workflowMode: "ModelAndProduct",
  goalPrompt: "Trang phục dạo phố lụa mềm",
  modelAssetId: "ast_model_01",
  productAssetId: "ast_product_01",
  readyImageAssetId: null,
  foundationImageAssetId: null,
  audioOption: "None",
  audioAssetId: null,
  targetSceneCount: 1,
  targetDurationPerSceneSeconds: 15,
  runState: "Idle",
  currentRunId: null,
  updatedAtUtc: "2026-10-27T01:00:00Z",
};

let mockScenes: Array<Record<string, unknown>> = [];
let mockPromptTemplate: Record<string, any> | null = null;

window.chrome = {
  webview: {
    postMessage: (envelope: unknown) => {
      const req = (typeof envelope === "string" ? JSON.parse(envelope) : envelope) as {
        id: string;
        type: string;
        command: string;
        payload: Record<string, unknown>;
        contractVersion: string;
      };

      lastBridgeCommand = req.command;
      lastBridgePayload = req.payload;
      executedCommands.push(req.command);

      setTimeout(() => {
        let responseData: unknown = null;
        let success = true;

        switch (req.command) {
          case "prompt.get_template":
            responseData = mockPromptTemplate ?? {
              template: {
                templateId: "tpl_script_custom",
                purpose: "Script",
                workspaceId: req.payload?.workspaceId || "ws_test_01",
                isOverride: true,
                currentVersionId: "ver_01",
                currentVersionNumber: 1,
                createdAtUtc: "2026-10-27T00:00:00Z",
                updatedAtUtc: "2026-10-27T00:00:00Z",
              },
              currentVersion: {
                versionId: "ver_01",
                templateId: "tpl_script_custom",
                versionNumber: 1,
                templateText: "Kịch bản video TikTok về {chu_de}.",
                variableDefinitionsJson: "[]",
                changeDescription: null,
                createdAtUtc: "2026-10-27T00:00:00Z",
              },
            };
            break;
          case "workflow.get_config":
            responseData = mockConfig;
            break;
          case "workflow.list_scenes":
            responseData = { scenes: mockScenes };
            break;
          case "workflow.save_config":
            mockConfig = { ...mockConfig, ...req.payload };
            responseData = mockConfig;
            break;
          case "workflow.start":
            mockConfig = { ...mockConfig, runState: "Running" };
            mockScenes = [
              {
                sceneId: "scn_01",
                projectId: "prj_test_01",
                workspaceId: "ws_test_01",
                sequenceOrder: 1,
                name: "Clip 1",
                targetDurationSeconds: mockConfig.targetDurationPerSceneSeconds || 15,
                workflowStatus: "PreparingPrompt",
                imageStrategy: "ReuseFoundation",
                sourceImageAssetId: "ast_model_01",
                selectedImageAssetId: "ast_model_01",
                motionPromptText: "Cinematic shot",
                audioOption: "None",
                audioAssetId: null,
                videoAssetId: null,
                isStale: false,
                staleReason: null,
                revision: "rev_01",
              },
            ];
            responseData = {
              runId: "run_01",
              runState: "Running",
              createdSceneCount: 1,
              message: "Đã bắt đầu quy trình tạo video.",
            };
            break;
          case "workflow.preview_ai_prompt":
            responseData = {
              templateId: "tpl_video_prompt_default",
              templateName: "Mẫu Prompt Video Chuẩn",
              versionId: "v1",
              isOverride: false,
              userBrief: mockConfig.goalPrompt || "",
              composedPrompt: `[HƯỚNG DẪN TỪ TEMPLATE VIDEO PROMPT ĐÃ CHỌN]\nViết prompt chuyển động...\n\n[NHIỆM VỤ]\nViết prompt cho clip_1...\n\n[DỮ LIỆU LẦN TẠO — do app serialize]\nUSER_BRIEF: "${mockConfig.goalPrompt}"\nCLIP_CONTEXT: clip_1, thời lượng 15s\n\n[OUTPUT_CONTRACT]\nChỉ trả về prompt chuyển động camera`,
              outputContractVersion: 1,
            };
            break;
          case "asset.upload":
            responseData = {
              asset: {
                assetId: "ast_uploaded_direct",
                workspaceId: req.payload?.workspaceId,
                projectId: req.payload?.projectId,
                assetType: req.payload?.assetType || "Image",
                relativePath: `media/${req.payload?.assetType || "image"}.png`,
                byteSize: 1024,
                sha256Hash: "hash123",
                status: "Linked",
                createdAtUtc: new Date().toISOString(),
              },
              canceled: false,
            };
            break;
          default:
            responseData = null;
        }

        const respEnvelope = {
          id: req.id,
          type: "response",
          success,
          data: responseData,
          error: null,
          serverTimestampUtc: new Date().toISOString(),
          contractVersion: req.contractVersion || "1.0.4",
        };

        const event = new domWindow.MessageEvent("message", {
          data: JSON.stringify(respEnvelope),
        });
        window.dispatchEvent(event as unknown as Event);
      }, 5);
    },
    addEventListener: (type: string, listener: (ev: MessageEvent) => void) => {
      window.addEventListener(type, listener as EventListener);
    },
    removeEventListener: (type: string, listener: (ev: MessageEvent) => void) => {
      window.removeEventListener(type, listener as EventListener);
    },
  },
} as any;

const React = await import("react");
const ReactDOMClient = await import("react-dom/client");
const { VideoProductionTab } = await import("./VideoProductionTab");

test("VideoProductionTab: CD01 & CD02 - Nhãn 'Số clip cần tạo', bộ chọn duration theo capability & tóm tắt", async () => {
  executedCommands = [];
  mockConfig = {
    projectId: "prj_test_01",
    workspaceId: "ws_test_01",
    workflowMode: "ModelAndProduct",
    goalPrompt: "BST Váy lụa cao cấp",
    modelAssetId: "ast_model_01",
    productAssetId: "ast_product_01",
    readyImageAssetId: null,
    foundationImageAssetId: null,
    audioOption: "None",
    audioAssetId: null,
    targetSceneCount: 1, // CD01: default 1 clip cho cấu hình mới
    targetDurationPerSceneSeconds: 15, // CD01: ưu tiên 15s nếu hỗ trợ
    runState: "Idle",
    currentRunId: null,
    updatedAtUtc: "2026-10-27T01:00:00Z",
  };
  mockScenes = [];

  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = ReactDOMClient.createRoot(container);

  await React.act(async () => {
    root.render(
      React.createElement(VideoProductionTab, {
        workspaceId: "ws_test_01",
        projectId: "prj_test_01",
      })
    );
  });

  await React.act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 50));
  });

  const fullText = container.textContent || "";

  // CD02: Nhãn 'Số clip cần tạo', không còn 'Số cảnh tự động lập'
  assert(fullText.includes("Số clip cần tạo"), "Phải có nhãn 'Số clip cần tạo'.");
  assert(!fullText.includes("Số cảnh tự động lập"), "Không được dùng nhãn cũ 'Số cảnh tự động lập'.");

  // CD02: Input số tự do của duration bị thay thế bằng select
  const durationSelect = container.querySelector("#target-duration") as HTMLSelectElement;
  assert(durationSelect, "Phải có dropdown select cho '#target-duration'.");
  assert(durationSelect.tagName.toLowerCase() === "select", "Thời lượng phải là thẻ <select>, không phải <input type='number'>");

  const options = Array.from(durationSelect.querySelectorAll("option")).map((o) => o.value);
  assert(options.includes("15"), "Bộ chọn phải có option 15s.");
  assert(options.includes("10"), "Bộ chọn phải có option 10s.");
  assert(!options.includes("30"), "CD05: Option 30s không được xuất hiện khi chưa verified direct.");

  // CD02: Tóm tắt 'Tạo N clip riêng × T giây' và giải thích AI tự chia phân cảnh
  assert(fullText.includes("Tạo 1 clip riêng × 15 giây"), "Phải hiển thị tóm tắt 'Tạo 1 clip riêng × 15 giây'.");
  assert(fullText.includes("AI tự chia các phân cảnh trong từng clip theo nội dung"), "Phải có giải thích AI tự chia phân cảnh.");

  // Click Bắt đầu tạo video khi duration và brief hợp lệ
  const startButton = Array.from(container.querySelectorAll("button")).find((b) =>
    b.textContent?.includes("Bắt đầu tạo video")
  );
  assert(startButton, "Phải có nút 'Bắt đầu tạo video'.");

  await React.act(async () => {
    startButton.click();
  });
  await React.act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 80));
  });

  assert(executedCommands.includes("workflow.start"), "Duration 15s hợp lệ phải trigger 'workflow.start'.");

  root.unmount();
  container.remove();
});

test("VideoProductionTab: CD04 & CD08 - Dữ liệu cũ 5s hiển thị cảnh báo và bị chặn trước khi Bắt đầu", async () => {
  executedCommands = [];
  mockConfig = {
    projectId: "prj_legacy_01",
    workspaceId: "ws_test_01",
    workflowMode: "ModelAndProduct",
    goalPrompt: "Dự án cũ 3 clip 5s",
    modelAssetId: "ast_model_01",
    productAssetId: "ast_product_01",
    readyImageAssetId: null,
    foundationImageAssetId: null,
    audioOption: "None",
    audioAssetId: null,
    targetSceneCount: 3,
    targetDurationPerSceneSeconds: 5, // CD08: Dữ liệu cũ 5s
    runState: "Idle",
    currentRunId: null,
    updatedAtUtc: "2026-10-27T01:00:00Z",
  };
  mockScenes = [];

  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = ReactDOMClient.createRoot(container);

  await React.act(async () => {
    root.render(
      React.createElement(VideoProductionTab, {
        workspaceId: "ws_test_01",
        projectId: "prj_legacy_01",
      })
    );
  });

  await React.act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 50));
  });

  const fullText = container.textContent || "";
  // Phải bảo toàn hiển thị 3 clip x 5s cũ, kèm cảnh báo
  assert(fullText.includes("5 giây"), "Dữ liệu cũ 5s phải mở được và hiển thị trong form.");
  assert(
    fullText.includes("chưa được nhà cung cấp hỗ trợ") || fullText.includes("Cần chọn lại thời lượng"),
    "Phải hiển thị cảnh báo yêu cầu chọn lại thời lượng được hỗ trợ."
  );

  // Click Bắt đầu phải bị chặn, không gửi workflow.start
  const startButton = Array.from(container.querySelectorAll("button")).find((b) =>
    b.textContent?.includes("Bắt đầu tạo video")
  );
  assert(startButton);

  await React.act(async () => {
    startButton.click();
  });
  await React.act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 50));
  });

  assert(!executedCommands.includes("workflow.start"), "CD04: Thời lượng 5s không được hỗ trợ phải bị chặn chạy.");
  const errorText = container.textContent || "";
  assert(errorText.includes("không được nhà cung cấp video hỗ trợ"), "Phải có thông báo lỗi cụ thể.");

  root.unmount();
  container.remove();
});

test("VideoProductionTab: CD13 - Brief (GoalPrompt) trống bị chặn tại UI", async () => {
  executedCommands = [];
  mockConfig = {
    projectId: "prj_test_02",
    workspaceId: "ws_test_01",
    workflowMode: "ModelAndProduct",
    goalPrompt: "   ", // Brief trống sau trim
    modelAssetId: "ast_model_01",
    productAssetId: "ast_product_01",
    readyImageAssetId: null,
    foundationImageAssetId: null,
    audioOption: "None",
    audioAssetId: null,
    targetSceneCount: 1,
    targetDurationPerSceneSeconds: 15,
    runState: "Idle",
    currentRunId: null,
    updatedAtUtc: "2026-10-27T01:00:00Z",
  };
  mockScenes = [];

  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = ReactDOMClient.createRoot(container);

  await React.act(async () => {
    root.render(
      React.createElement(VideoProductionTab, {
        workspaceId: "ws_test_01",
        projectId: "prj_test_02",
      })
    );
  });

  await React.act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 50));
  });

  const startButton = Array.from(container.querySelectorAll("button")).find((b) =>
    b.textContent?.includes("Bắt đầu tạo video")
  );
  assert(startButton);

  await React.act(async () => {
    startButton.click();
  });
  await React.act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 50));
  });

  assert(!executedCommands.includes("workflow.start"), "Brief trống phải bị chặn, không gọi 'workflow.start'.");
  const errorText = container.textContent || "";
  assert(errorText.includes("Vui lòng nhập yêu cầu nội dung"), "Phải thông báo yêu cầu nhập nội dung.");

  root.unmount();
  container.remove();
});

test("VideoProductionTab: CD11 & CD13 - Xem trước yêu cầu gửi AI writer (Preview AI Prompt Modal)", async () => {
  executedCommands = [];
  mockConfig = {
    projectId: "prj_test_03",
    workspaceId: "ws_test_01",
    workflowMode: "ModelAndProduct",
    goalPrompt: "UNIQUE_BRIEF_TOKEN_9988 Áo vest công sở lịch lãm",
    modelAssetId: "ast_model_01",
    productAssetId: "ast_product_01",
    readyImageAssetId: null,
    foundationImageAssetId: null,
    audioOption: "None",
    audioAssetId: null,
    targetSceneCount: 1,
    targetDurationPerSceneSeconds: 15,
    runState: "Idle",
    currentRunId: null,
    updatedAtUtc: "2026-10-27T01:00:00Z",
  };
  mockScenes = [];

  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = ReactDOMClient.createRoot(container);

  await React.act(async () => {
    root.render(
      React.createElement(VideoProductionTab, {
        workspaceId: "ws_test_01",
        projectId: "prj_test_03",
      })
    );
  });

  await React.act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 50));
  });

  // Tìm nút 'Xem yêu cầu gửi AI'
  const previewButton = Array.from(container.querySelectorAll("button")).find((b) =>
    b.textContent?.includes("Xem yêu cầu gửi AI")
  );
  assert(previewButton, "Phải có nút 'Xem yêu cầu gửi AI'.");

  await React.act(async () => {
    previewButton.click();
  });
  await React.act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 50));
  });

  assert(executedCommands.includes("workflow.preview_ai_prompt"), "Click nút preview phải gọi bridge 'workflow.preview_ai_prompt'.");

  // Kiểm tra nội dung Dialog hiển thị
  const bodyText = document.body.textContent || "";
  assert(bodyText.includes("Xem yêu cầu gửi AI viết prompt chuyển động"), "Dialog tiêu đề xem trước phải xuất hiện.");
  assert(bodyText.includes("Mẫu Prompt Video Chuẩn"), "Dialog phải hiển thị tên template.");
  assert(bodyText.includes("UNIQUE_BRIEF_TOKEN_9988"), "Dialog phải hiển thị đúng user brief của người dùng.");

  root.unmount();
  container.remove();
});

test("VideoProductionTab: Render biến ở frontend và chỉ lưu prompt text", async () => {
  executedCommands = [];
  localStorage.removeItem("titoai_workflow_prompt_values_ws_test_01_prj_test_dyn_vars");
  mockPromptTemplate = {
    template: {
      templateId: "tpl_script_fashion",
      purpose: "Script",
      workspaceId: "ws_test_01",
      isOverride: true,
      currentVersionId: "ver_01",
      currentVersionNumber: 1,
      createdAtUtc: "2026-10-27T00:00:00Z",
      updatedAtUtc: "2026-10-27T00:00:00Z",
    },
    currentVersion: {
      versionId: "ver_01",
      templateId: "tpl_script_fashion",
      versionNumber: 1,
      templateText: "Tạo kịch bản video TikTok về chủ đề {chu_de} cho sản phẩm {san_pham}.",
      variableDefinitionsJson: "[]",
      changeDescription: null,
      createdAtUtc: "2026-10-27T00:00:00Z",
    },
  };
  mockConfig = {
    projectId: "prj_test_dyn_vars",
    workspaceId: "ws_test_01",
    workflowMode: "ModelAndProduct",
    goalPrompt: "",
    modelAssetId: "ast_model_01",
    productAssetId: "ast_product_01",
    readyImageAssetId: null,
    foundationImageAssetId: null,
    audioOption: "None",
    audioAssetId: null,
    targetSceneCount: 1,
    targetDurationPerSceneSeconds: 15,
    runState: "Idle",
    currentRunId: null,
    updatedAtUtc: "2026-10-27T01:00:00Z",
    scriptPromptText: null,
    videoPromptText: null,
  };
  mockScenes = [];

  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = ReactDOMClient.createRoot(container);

  await React.act(async () => {
    root.render(
      React.createElement(VideoProductionTab, {
        workspaceId: "ws_test_01",
        projectId: "prj_test_dyn_vars",
      })
    );
  });

  await React.act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 60));
  });

  // 1. Kiểm tra badge/thông báo mẫu prompt áp dụng
  const badgeArea = container.textContent || "";
  assert(badgeArea.includes("Mẫu prompt áp dụng"), "Phải có thông báo mẫu prompt áp dụng.");
  assert(badgeArea.includes("Có mẫu kênh tùy chỉnh"), "Phải hiển thị badge có mẫu kênh tùy chỉnh.");

  // 2. Kiểm tra ô nhập dynamic variable {chu_de} có id 'var-input-chu_de'
  const chuDeInput = container.querySelector("#var-input-chu_de") as HTMLInputElement;
  assert(chuDeInput, "Phải hiển thị ô nhập liệu id='var-input-chu_de' cho biến {chu_de}.");

  // Kiểm tra ô nhập {san_pham}
  const sanPhamInput = container.querySelector("#var-input-san_pham") as HTMLInputElement;
  assert(sanPhamInput, "Phải hiển thị ô nhập liệu id='var-input-san_pham' cho biến {san_pham}.");

  await React.act(async () => {
    const nativeInputValueSetter = Object.getOwnPropertyDescriptor(
      window.HTMLInputElement.prototype,
      "value"
    )?.set;
    nativeInputValueSetter?.call(sanPhamInput, "Áo lụa cao cấp");
    sanPhamInput.dispatchEvent(new Event("input", { bubbles: true }));
  });

  // 3. Nhập giá trị cho {chu_de}
  await React.act(async () => {
    const nativeInputValueSetter = Object.getOwnPropertyDescriptor(
      window.HTMLInputElement.prototype,
      "value"
    )?.set;
    nativeInputValueSetter?.call(chuDeInput, "Thời trang dạo phố mùa hè");
    chuDeInput.dispatchEvent(new Event("input", { bubbles: true }));
  });

  await React.act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 50));
  });

  // 4. Bấm nút "Lưu cấu hình"
  const saveButton = Array.from(container.querySelectorAll("button")).find((b) =>
    b.textContent?.includes("Lưu cấu hình")
  );
  assert(saveButton, "Phải có nút 'Lưu cấu hình'.");
  assert(!saveButton.disabled, "Nút Lưu cấu hình phải enabled khi có thay đổi.");

  await React.act(async () => {
    saveButton.click();
  });

  await React.act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 50));
  });

  // Kiểm tra lệnh workflow.save_config chỉ gửi prompt text đã render
  assert(executedCommands.includes("workflow.save_config"), "Bấm lưu phải gọi 'workflow.save_config'.");
  assert(lastBridgePayload, "Phải có payload gửi sang bridge.");
  assert.strictEqual((lastBridgePayload as any)?.scriptPromptText,
    "Tạo kịch bản video TikTok về chủ đề Thời trang dạo phố mùa hè cho sản phẩm Áo lụa cao cấp.",
    "Payload phải chứa prompt Script đã render.");
  assert.strictEqual((lastBridgePayload as any)?.videoPromptText, (lastBridgePayload as any)?.scriptPromptText,
    "Mỗi tác vụ lưu text đã render thay vì gửi dictionary biến.");
  assert.strictEqual("promptVariables" in (lastBridgePayload as any), false,
    "Payload workflow không còn trường promptVariables.");

  // 5. Bấm bắt đầu dùng prompt text đã lưu và khởi chạy workflow
  const startButton = Array.from(container.querySelectorAll("button")).find((b) =>
    b.textContent?.includes("Bắt đầu tạo video")
  );
  assert(startButton, "Phải có nút 'Bắt đầu tạo video'.");

  await React.act(async () => {
    startButton.click();
  });

  await React.act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 50));
  });

  assert(executedCommands.includes("workflow.start"), "Bấm bắt đầu phải kích hoạt 'workflow.start'.");
  assert.strictEqual(mockConfig.scriptPromptText,
    "Tạo kịch bản video TikTok về chủ đề Thời trang dạo phố mùa hè cho sản phẩm Áo lụa cao cấp.",
    "Config phải giữ prompt Script đã render sau khi workflow bắt đầu.");

  root.unmount();
  container.remove();
  mockPromptTemplate = null;

  root.unmount();
  container.remove();
});
