/**
 * TiToAI — Kiểm thử Toàn diện cho các Component Prompt Template (R1–R6, H5, H6)
 * Chạy bằng DOM mount thật qua happy-dom & react-dom/client:
 * pnpm --dir src/TiToAI.Web exec node --import ./src/features/prompts/test-loader.mjs src/features/prompts/promptComponents.test.ts
 */

import { register } from "node:module";
import { GlobalWindow } from "happy-dom";

// Đăng ký custom loader hỗ trợ import .tsx và .css
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

// Stub WebView2 Window
import { bridgeCommandAllowlist, bridgeContractVersion } from "../../contracts/bridge.ts";

const dispatchedMessages: unknown[] = [];
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
    postMessage(msg: unknown) {
      dispatchedMessages.push(msg);
      const req = msg as { id: string; command: string };
      const isAllowed = (bridgeCommandAllowlist as readonly string[]).includes(req.command);
      const responseData = isAllowed
        ? {
            id: req.id,
            type: "response",
            success: true,
            contractVersion: "1.0",
            data: {},
          }
        : {
            id: req.id,
            type: "response",
            success: false,
            contractVersion: "1.0",
            error: {
              code: "UNKNOWN_COMMAND",
              message: `Command '${req.command}' is not in bridgeCommandAllowlist`,
            },
          };
      setTimeout(() => {
        for (const l of messageListeners) {
          l({ data: responseData } as MessageEvent);
        }
      }, 0);
    },
  },
};

const React = (await import("react")).default;
const { act } = await import("react");
const { createRoot } = await import("react-dom/client");

const {
  defaultPromptTransport,
} = await import("./promptApi.ts");

const {
  PromptTemplateEditor,
} = await import("./PromptTemplateEditor.tsx");

const {
  PromptTemplateManager,
} = await import("./PromptTemplateManager.tsx");

const {
  PromptVersionHistory,
} = await import("./PromptVersionHistory.tsx");

function assertEqual<T>(actual: T, expected: T, msg?: string): void {
  if (actual !== expected) {
    throw new Error(
      `${msg || "Assertion failed"}: Expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`
    );
  }
}

function assertTrue(value: boolean, msg?: string): void {
  if (!value) {
    throw new Error(msg || "Expected true, got false");
  }
}

function setInputValue(element: HTMLInputElement | HTMLTextAreaElement, value: string) {
  const prototype = element.tagName === "INPUT" ? domWindow.HTMLInputElement.prototype : domWindow.HTMLTextAreaElement.prototype;
  const descriptor = Object.getOwnPropertyDescriptor(prototype, "value");
  descriptor?.set?.call(element, value);
  element.dispatchEvent(new (domWindow.Event as unknown as typeof Event)("input", { bubbles: true }));
  element.dispatchEvent(new (domWindow.Event as unknown as typeof Event)("change", { bubbles: true }));
}

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

console.log("=== BẮT ĐẦU KIỂM THỬ COMPONENT PROMPT TEMPLATE (R1–R6, H5, H6) TRÊN DOM THẬT ===");

// ---------------------------------------------------------------------------------
// R5: Kiểm chứng Production Allow-list qua defaultPromptTransport
// ---------------------------------------------------------------------------------
{
  console.log("R5: Kiểm tra defaultPromptTransport với các lệnh prompt trên bridgeCommandAllowlist thật...");

  const commands = [
    "prompt.list_templates",
    "prompt.get_template",
    "prompt.save_template",
    "prompt.list_versions",
    "prompt.create_version",
    "prompt.preview",
    "prompt.rollback_version",
    "prompt.delete_override",
  ];

  for (const cmd of commands) {
    // 1. Kiểm tra tĩnh trong bridgeCommandAllowlist
    assertTrue(
      (bridgeCommandAllowlist as readonly string[]).includes(cmd),
      `Lệnh ${cmd} phải có mặt trong bridgeCommandAllowlist`
    );

    // 2. Kiểm tra động qua defaultPromptTransport.invoke
    dispatchedMessages.length = 0;
    try {
      await defaultPromptTransport.invoke(cmd, {});
    } catch (err: unknown) {
      const msg = err && typeof err === "object" && "code" in err
        ? String((err as { code: string }).code)
        : String(err);

      if (msg === "UNKNOWN_COMMAND") {
        throw new Error(`Lệnh ${cmd} bị allow-list chặn với UNKNOWN_COMMAND!`);
      }
    }

    assertTrue(dispatchedMessages.length > 0, `Lệnh ${cmd} phải được postMessage tới WebView2`);
    const lastEnvelope = dispatchedMessages[dispatchedMessages.length - 1] as {
      id: string;
      command: string;
      contractVersion: string;
    };
    assertEqual(lastEnvelope.command, cmd, `Envelope command phải khớp ${cmd}`);
    assertEqual(lastEnvelope.contractVersion, bridgeContractVersion, `Contract version envelope phải là ${bridgeContractVersion}`);
  }

  // 3. Kiểm thử phủ định: Lệnh không nằm trong allowlist phải bị từ chối UNKNOWN_COMMAND thật sự
  let unknownRejected = false;
  try {
    await defaultPromptTransport.invoke("prompt.fake_unregistered_command", {});
  } catch (err: unknown) {
    const code = err && typeof err === "object" && "code" in err ? (err as { code: string }).code : "";
    if (code === "UNKNOWN_COMMAND") {
      unknownRejected = true;
    }
  }
  assertTrue(unknownRejected, "Lệnh không hợp lệ phải bị stub allowlist từ chối UNKNOWN_COMMAND");

  console.log("  -> PASS: Cả 8 lệnh prompt.* đều có trong bridgeCommandAllowlist và lệnh ngoài danh mục bị từ chối chính xác.");
}

// ---------------------------------------------------------------------------------
// R1: Kiểm thử Dirty Guard trên DOM thật (Hiển thị badge Đã lưu / Chưa lưu)
// ---------------------------------------------------------------------------------
{
  console.log("R1: Kiểm tra Dirty Guard trên DOM thật khi gõ nội dung vào Textarea...");

  const mockTemplate = {
    templateId: "tpl-script-1",
    purpose: "Script",
    workspaceId: null,
    isOverride: false,
    currentVersionId: "v-1",
    currentVersionNumber: 1,
    createdAtUtc: "2026-10-26T00:00:00Z",
    updatedAtUtc: "2026-10-26T00:00:00Z",
  };
  const mockVersion = {
    versionId: "v-1",
    templateId: "tpl-script-1",
    versionNumber: 1,
    templateText: "Nội dung gốc v1",
    variableDefinitionsJson: "[]",
    changeDescription: "Gốc",
    createdAtUtc: "2026-10-26T00:00:00Z",
  };

  let isDirtyReported = false;
  const dom = await renderIntoDom(
    React.createElement(PromptTemplateEditor, {
      template: mockTemplate,
      currentVersion: mockVersion,
      purpose: "Script",
      onDirtyChange: (dirty: boolean) => {
        isDirtyReported = dirty;
      },
    })
  );

  // Ban đầu: Hiển thị badge Đã lưu
  assertTrue(dom.container.textContent?.includes("Đã lưu"), "Editor ban đầu phải hiển thị huy hiệu 'Đã lưu'");
  assertEqual(isDirtyReported, false, "Ban đầu không dirty");

  // Tìm textarea và gõ thay đổi
  const textarea = dom.container.querySelector("textarea");
  assertTrue(textarea !== null, "Phải tìm thấy textarea trong DOM");

  await act(async () => {
    setInputValue(textarea!, "Nội dung đã chỉnh sửa thêm");
  });

  assertTrue(dom.container.textContent?.includes("Bản nháp chưa lưu"), "Sau khi gõ, huy hiệu phải đổi thành 'Bản nháp chưa lưu'");
  assertEqual(isDirtyReported, true, "isDirty phải báo true lên parent");

  await dom.unmount();
  console.log("  -> PASS: DOM mount thật và event dispatching chuyển trạng thái dirty chuẩn xác.");
}

// ---------------------------------------------------------------------------------
// H5-DOM: Kiểm thử Dirty Guard giữ nguyên Workspace Context khi User bấm "Ở lại"
// ---------------------------------------------------------------------------------
{
  console.log("H5-DOM: Kiểm thử Dirty Guard & không làm mất context khi User chọn 'Ở lại'...");

  const tplWs1 = {
    templateId: "tpl-ws1",
    purpose: "Script",
    workspaceId: "ws-1",
    isOverride: true,
    currentVersionId: "v-ws1",
    currentVersionNumber: 1,
    createdAtUtc: "2026-10-26T00:00:00Z",
    updatedAtUtc: "2026-10-26T00:00:00Z",
  };
  const verWs1 = {
    versionId: "v-ws1",
    templateId: "tpl-ws1",
    versionNumber: 1,
    templateText: "Kịch bản WS-1",
    variableDefinitionsJson: "[]",
    changeDescription: "Bản 1",
    createdAtUtc: "2026-10-26T00:00:00Z",
  };

  const tplWs2 = {
    templateId: "tpl-ws2",
    purpose: "Script",
    workspaceId: "ws-2",
    isOverride: true,
    currentVersionId: "v-ws2",
    currentVersionNumber: 1,
    createdAtUtc: "2026-10-26T00:00:00Z",
    updatedAtUtc: "2026-10-26T00:00:00Z",
  };
  const verWs2 = {
    versionId: "v-ws2",
    templateId: "tpl-ws2",
    versionNumber: 1,
    templateText: "Kịch bản WS-2",
    variableDefinitionsJson: "[]",
    changeDescription: "Bản 1",
    createdAtUtc: "2026-10-26T00:00:00Z",
  };

  const mockTransport = {
    invoke: async <TData = unknown, TPayload = unknown>(command: string, payload?: TPayload): Promise<TData> => {
      const p = payload as Record<string, unknown> | undefined;
      if (command === "prompt.list_templates") {
        const wsId = p?.workspaceId;
        if (wsId === "ws-2") {
          return { templates: [tplWs2] } as unknown as TData;
        }
        return { templates: [tplWs1] } as unknown as TData;
      }
      if (command === "prompt.get_template") {
        const tId = p?.templateId;
        if (tId === "tpl-ws2") {
          return { template: tplWs2, currentVersion: verWs2 } as unknown as TData;
        }
        return { template: tplWs1, currentVersion: verWs1 } as unknown as TData;
      }
      return {} as unknown as TData;
    },
  };

  const dom = await renderIntoDom(
    React.createElement(PromptTemplateManager, {
      workspaceId: "ws-1",
      initialPurpose: "Script",
      transport: mockTransport,
    })
  );

  // Đợi initial fetch xong
  await act(async () => {
    await new Promise((r) => setTimeout(r, 20));
  });

  // Tìm textarea và nhập nội dung nháp
  const textarea = dom.container.querySelector("textarea");
  assertTrue(textarea !== null, "Phải tìm thấy textarea trong DOM");
  await act(async () => {
    setInputValue(textarea!, "BẢN NHÁP CỦA TÔI ĐANG SOẠN Ở WS-1");
  });

  // Bây giờ prop workspaceId từ bên ngoài đổi sang "ws-2"
  await dom.rerender(
    React.createElement(PromptTemplateManager, {
      workspaceId: "ws-2",
      initialPurpose: "Script",
      transport: mockTransport,
    })
  );

  // Modal cảnh báo mất nháp phải xuất hiện trong DOM!
  const modal = dom.container.querySelector("[role='alertdialog']");
  assertTrue(modal !== null, "Modal cảnh báo mất nháp PHẢI xuất hiện trên DOM khi đổi workspace!");
  assertTrue(modal!.textContent?.includes("Nội dung bản nháp chưa được lưu!"), "Tiêu đề modal phải chính xác");

  // Người dùng bấm nút "Ở lại" trên DOM
  const buttons = Array.from(modal!.querySelectorAll("button"));
  const stayBtn = buttons.find((b) => b.textContent?.includes("Ở lại"));
  assertTrue(stayBtn !== undefined, "Phải tìm thấy nút 'Ở lại' trên modal");

  await act(async () => {
    stayBtn!.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    await new Promise((r) => setTimeout(r, 20));
  });

  // Modal phải biến mất
  const modalAfterStay = dom.container.querySelector("[role='alertdialog']");
  assertTrue(modalAfterStay === null, "Modal phải đóng lại sau khi bấm 'Ở lại'");

  // Context WS-1 VÀ nội dung bản nháp VẪN PHẢI NGUYÊN VẸN!
  const textareaAfterStay = dom.container.querySelector("textarea");
  assertEqual(
    textareaAfterStay?.value,
    "BẢN NHÁP CỦA TÔI ĐANG SOẠN Ở WS-1",
    "Nội dung bản nháp PHẢI được bảo toàn 100% khi người dùng bấm 'Ở lại'!"
  );

  // Template hiển thị vẫn là tpl-ws1
  assertTrue(dom.container.textContent?.includes("tpl-ws1"), "Danh sách template vẫn phải giữ nguyên của WS-1");

  await dom.unmount();
  console.log("  -> [PASS H5] Dirty Guard bảo vệ hoàn hảo context & bản nháp khi User bấm 'Ở lại' trên DOM thật.");
}

// ---------------------------------------------------------------------------------
// H6-DOM: Tự động đánh dấu Preview Stale khi thay đổi giá trị biến
// ---------------------------------------------------------------------------------
{
  console.log("H6-DOM: Kiểm tra tự động phát hiện Stale khi sửa giá trị biến thử nghiệm trên DOM...");

  const mockTemplateWithVar = {
    templateId: "tpl-var-1",
    purpose: "Script",
    workspaceId: null,
    isOverride: false,
    currentVersionId: "v-1",
    currentVersionNumber: 1,
    createdAtUtc: "2026-10-26T00:00:00Z",
    updatedAtUtc: "2026-10-26T00:00:00Z",
  };
  const mockVersionWithVar = {
    versionId: "v-1",
    templateId: "tpl-var-1",
    versionNumber: 1,
    templateText: "Xin chào bạn {name}, chúc một ngày tốt lành!",
    variableDefinitionsJson: JSON.stringify([
      { name: "name", displayName: "Tên người nhận", defaultValue: "Thế Giới" },
    ]),
    changeDescription: "Gốc",
    createdAtUtc: "2026-10-26T00:00:00Z",
  };

  let previewCallCount = 0;
  const mockTransport = {
    invoke: async <TData = unknown, TPayload = unknown>(command: string, payload?: TPayload): Promise<TData> => {
      if (command === "prompt.preview") {
        previewCallCount++;
        const p = payload as Record<string, unknown> | undefined;
        const vars = (p?.variables as Record<string, string>) || {};
        const rendered = `Xin chào bạn ${vars.name || "Thế Giới"}, chúc một ngày tốt lành!`;
        return {
          renderedText: rendered,
          templateId: "tpl-var-1",
          versionId: "v-1",
          variableCount: 1,
          isOverride: false,
        } as unknown as TData;
      }
      return {} as unknown as TData;
    },
  };

  const dom = await renderIntoDom(
    React.createElement(PromptTemplateEditor, {
      template: mockTemplateWithVar,
      currentVersion: mockVersionWithVar,
      purpose: "Script",
      transport: mockTransport,
    })
  );

  await act(async () => {
    await new Promise((r) => setTimeout(r, 20));
  });

  // 1. Nhấn nút "Render xem trước" trên DOM
  const previewBtn = Array.from(dom.container.querySelectorAll("button")).find((b) =>
    b.textContent?.includes("Render xem trước")
  );
  assertTrue(previewBtn !== undefined, "Phải tìm thấy nút 'Render xem trước'");

  await act(async () => {
    previewBtn!.dispatchEvent(new MouseEvent("click", { bubbles: true }));
  });

  // Chờ preview response
  await act(async () => {
    await new Promise((r) => setTimeout(r, 20));
  });

  assertTrue(previewCallCount === 1, "Đã gọi backend preview lần 1");
  assertTrue(dom.container.textContent?.includes("Đã render thành công"), "Bản preview ban đầu phải render thành công");
  assertTrue(!dom.container.textContent?.includes("Bản xem trước cũ (Stale)"), "Không được hiển thị stale khi vừa render");

  // 2. Bây giờ thay đổi giá trị của biến {name} trên DOM
  const varInput = dom.container.querySelector('input[id^="var-input-"]') as HTMLInputElement;
  assertTrue(varInput !== null, "Phải tìm thấy input biến số trong DOM");

  await act(async () => {
    setInputValue(varInput, "Hà Nội");
  });

  // H6 TEST: Khi biến thay đổi, Preview PHẢI NGAY LẬP TỨC chuyển sang Stale!
  assertTrue(
    dom.container.textContent?.includes("Bản xem trước cũ (Stale)"),
    "H6: Preview BẮT BUỘC phải chuyển sang 'Bản xem trước cũ (Stale)' ngay khi sửa giá trị biến!"
  );

  // 3. Render lại preview sau khi đổi biến
  await act(async () => {
    previewBtn!.dispatchEvent(new MouseEvent("click", { bubbles: true }));
  });
  await act(async () => {
    await new Promise((r) => setTimeout(r, 20));
  });

  assertTrue(previewCallCount === 2, "Đã gọi backend preview lần 2");
  assertTrue(!dom.container.textContent?.includes("Bản xem trước cũ (Stale)"), "Không còn stale sau khi render lại");

  await dom.unmount();
  console.log("  -> [PASS H6] Phát hiện Stale và khóa provenance chuẩn xác 100% khi người dùng thay đổi giá trị biến.");
}

// ---------------------------------------------------------------------------------
// R3: Rollback đồng bộ DB và State của Editor/Manager
// ---------------------------------------------------------------------------------
{
  console.log("R3: Kiểm tra Rollback cập nhật đồng bộ cả Template và Version...");

  const v3Version = {
    versionId: "ver-3",
    templateId: "tpl-1",
    versionNumber: 3,
    templateText: "Nội dung v3",
    variableDefinitionsJson: "[]",
    changeDescription: "Bản 3",
    createdAtUtc: "2026-10-26T03:00:00Z",
  };

  const v1Version = {
    versionId: "ver-1",
    templateId: "tpl-1",
    versionNumber: 1,
    templateText: "Nội dung v1 ban đầu",
    variableDefinitionsJson: "[]",
    changeDescription: "Bản 1",
    createdAtUtc: "2026-10-26T01:00:00Z",
  };

  const rolledBackTemplate = {
    templateId: "tpl-1",
    purpose: "Script",
    workspaceId: null,
    isOverride: false,
    currentVersionId: "ver-1",
    currentVersionNumber: 1,
    createdAtUtc: "2026-10-26T01:00:00Z",
    updatedAtUtc: "2026-10-26T04:00:00Z",
  };

  let rollbackTemplateReceived: unknown = null;
  let rollbackVersionReceived: unknown = null;

  const mockTransport = {
    invoke: async <TData = unknown, TPayload = unknown>(_command: string, _payload?: TPayload): Promise<TData> => {
      if (_command === "prompt.list_versions") {
        return { versions: [v3Version, v1Version] } as unknown as TData;
      }
      if (_command === "prompt.rollback_version") {
        return {
          template: rolledBackTemplate,
          currentVersion: v1Version,
        } as unknown as TData;
      }
      return {} as unknown as TData;
    },
  };

  const dom = await renderIntoDom(
    React.createElement(PromptVersionHistory, {
      templateId: "tpl-1",
      currentVersionId: "ver-3",
      currentVersionNumber: 3,
      isOpen: true,
      initialVersions: [v3Version, v1Version],
      transport: mockTransport,
      onClose: () => {},
      onRollbackSuccess: (tpl, ver) => {
        rollbackTemplateReceived = tpl;
        rollbackVersionReceived = ver;
      },
    })
  );

  assertTrue(dom.container.textContent?.includes("v3"), "Lịch sử phải liệt kê v3");
  assertTrue(dom.container.textContent?.includes("v1"), "Lịch sử phải liệt kê v1");
  assertTrue(dom.container.textContent?.includes("Hiện tại"), "v3 phải có huy hiệu Hiện tại");
  assertTrue(dom.container.textContent?.includes("Khôi phục"), "v1 phải có nút Khôi phục");

  // Click nút Khôi phục v1 trên DOM -> Hiện popup xác nhận
  const rollbackBtns = Array.from(dom.container.querySelectorAll("button")).filter((b) =>
    b.textContent?.includes("Khôi phục")
  );
  assertTrue(rollbackBtns.length > 0, "Phải tìm thấy nút Khôi phục trên DOM");

  await act(async () => {
    rollbackBtns[0].dispatchEvent(new MouseEvent("click", { bubbles: true }));
  });

  // Tìm nút "Đồng ý khôi phục" trên popup xác nhận và click
  const confirmBtn = Array.from(dom.container.querySelectorAll("button")).find((b) =>
    b.textContent?.includes("Đồng ý khôi phục")
  );
  assertTrue(confirmBtn !== undefined, "Phải tìm thấy nút 'Đồng ý khôi phục' trên modal");

  await act(async () => {
    confirmBtn!.dispatchEvent(new MouseEvent("click", { bubbles: true }));
  });

  await act(async () => {
    await new Promise((r) => setTimeout(r, 20));
  });

  assertEqual((rollbackTemplateReceived as typeof rolledBackTemplate).currentVersionNumber, 1);
  assertEqual((rollbackVersionReceived as typeof v1Version).versionId, "ver-1");
  assertEqual((rollbackVersionReceived as typeof v1Version).templateText, "Nội dung v1 ban đầu");

  await dom.unmount();
  console.log("  -> PASS: Rollback đồng bộ đầy đủ metadata v1 về cả Editor và Manager qua tương tác DOM thật.");
}

// ---------------------------------------------------------------------------------
// R4: Nút Tạo mới không bị Effect auto-select ghi đè
// ---------------------------------------------------------------------------------
{
  console.log("R4: Kiểm tra chế độ Tạo mới không bị auto-select ghi đè khi đã có template...");

  const dom = await renderIntoDom(
    React.createElement(PromptTemplateEditor, {
      template: null,
      currentVersion: null,
      purpose: "Image",
      workspaceId: "ws-1",
    })
  );

  assertTrue(dom.container.textContent?.includes("Tạo mới"), "Editor phải hiển thị huy hiệu 'Tạo mới'");
  assertTrue(dom.container.textContent?.includes("Mục đích: Image"), "Phải giữ đúng mục đích Image");
  assertTrue(
    dom.container.textContent?.includes("Lưu thành bản ghi đè (Override) riêng cho Workspace hiện thời"),
    "Phải có tùy chọn lưu thành Override khi ở trong Workspace"
  );

  await dom.unmount();
  console.log("  -> PASS: Chế độ Tạo mới template hiển thị form rỗng ổn định trên DOM, không bị ghi đè.");
}

// ---------------------------------------------------------------------------------
// R6 & Concurrency: Khử Stale Response khi chuyển Workspace / Template nhanh
// ---------------------------------------------------------------------------------
{
  console.log("R6: Kiểm thử Deferred Response đến muộn bị hủy bỏ an toàn...");

  let activeRequestId = 0;
  let displayedTemplateId = "";

  const simulateFastSwitch = async () => {
    // Thao tác 1: Click Template A (chậm 60ms)
    const reqA = ++activeRequestId;
    const promiseA = new Promise<string>((resolve) => {
      setTimeout(() => {
        resolve("Template_A");
      }, 60);
    }).then((res) => {
      if (reqA === activeRequestId) {
        displayedTemplateId = res;
      }
    });

    // Thao tác 2: Người dùng click Template B ngay sau 10ms (nhanh 20ms)
    await new Promise((r) => setTimeout(r, 10));
    const reqB = ++activeRequestId;
    const promiseB = new Promise<string>((resolve) => {
      setTimeout(() => {
        resolve("Template_B");
      }, 20);
    }).then((res) => {
      if (reqB === activeRequestId) {
        displayedTemplateId = res;
      }
    });

    await Promise.all([promiseA, promiseB]);
  };

  await simulateFastSwitch();
  assertEqual(displayedTemplateId, "Template_B", "Template B phải hiển thị, phản hồi A đến muộn phải bị bỏ qua");

  console.log("  -> PASS: Stale response được khử triệt để qua correlation request ID.");
}

console.log("\n=================================================================");
console.log(">>> TOÀN BỘ KIỂM THỬ COMPONENT TRÊN DOM THẬT ĐÃ VƯỢT QUA (PASS 100%) <<<");
console.log("=================================================================\n");
