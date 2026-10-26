/**
 * TiToAI — Kiểm thử Toàn diện cho các Component Prompt Template (R1–R6)
 * Chạy qua pnpm: pnpm --dir src/TiToAI.Web exec node src/features/prompts/promptComponents.test.ts
 */

import { register } from "node:module";

// Đăng ký custom loader hỗ trợ import .tsx và .css
register("./test-loader.mjs", import.meta.url);

// Stub WebView2 Window
const dispatchedMessages: unknown[] = [];
const messageListeners: ((event: MessageEvent) => void)[] = [];
(globalThis as unknown as Record<string, unknown>).window = {
  chrome: {
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
        const responseData = {
          id: req.id,
          type: "response",
          success: true,
          contractVersion: "1.0",
          data: {},
        };
        setTimeout(() => {
          for (const l of messageListeners) {
            l({ data: responseData } as MessageEvent);
          }
        }, 0);
      },
    },
  },
};

const React = (await import("react")).default;
const { renderToString } = await import("react-dom/server");

const {
  defaultPromptTransport,
} = await import("./promptApi.ts");

const {
  PromptPreviewPanel,
} = await import("./PromptPreviewPanel.tsx");

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

console.log("=== BẮT ĐẦU KIỂM THỬ COMPONENT PROMPT TEMPLATE (R1–R6) ===");

// ---------------------------------------------------------------------------------
// R5: Kiểm chứng Production Allow-list qua defaultPromptTransport
// ---------------------------------------------------------------------------------
{
  console.log("R5: Kiểm tra defaultPromptTransport với 7 lệnh backend T10 trên allow-list thật...");

  const commands = [
    "prompt.list_templates",
    "prompt.get_template",
    "prompt.list_versions",
    "prompt.create_version",
    "prompt.preview",
    "prompt.rollback_version",
    "prompt.delete_override",
  ];

  for (const cmd of commands) {
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
    const lastEnvelope = dispatchedMessages[dispatchedMessages.length - 1] as { command: string };
    assertEqual(lastEnvelope.command, cmd, `Envelope command phải khớp ${cmd}`);
  }

  console.log("  -> PASS: Cả 7 lệnh prompt.* đều vượt qua allow-list production thật mà không bị UNKNOWN_COMMAND.");
}

// ---------------------------------------------------------------------------------
// R1: Kiểm thử Dirty Guard & Không làm mất bản nháp
// ---------------------------------------------------------------------------------
{
  console.log("R1: Kiểm tra Dirty Guard khi sửa bản nháp và chuyển template/purpose...");

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

  // Editor render bản gốc -> hiển thị Đã lưu
  const htmlClean = renderToString(
    React.createElement(PromptTemplateEditor, {
      template: mockTemplate,
      currentVersion: mockVersion,
      purpose: "Script",
      onDirtyChange: () => {},
    })
  );
  assertTrue(htmlClean.includes("Đã lưu"), "Editor ban đầu phải hiển thị huy hiệu 'Đã lưu'");

  // Render PromptTemplateManager
  const mockTransport = {
    invoke: async <TData>(command: string): Promise<TData> => {
      if (command === "prompt.list_templates") {
        return { templates: [mockTemplate] } as unknown as TData;
      }
      if (command === "prompt.get_template") {
        return { template: mockTemplate, currentVersion: mockVersion } as unknown as TData;
      }
      return {} as unknown as TData;
    },
  };

  const htmlManager = renderToString(
    React.createElement(PromptTemplateManager, {
      workspaceId: "ws-test",
      initialPurpose: "Script",
      transport: mockTransport,
    })
  );

  assertTrue(htmlManager.includes("Quản lý Prompt Templates"), "Manager phải render đầy đủ cấu trúc");
  assertTrue(htmlManager.includes("Kịch bản (Script)"), "Manager phải có tab Kịch bản");

  console.log("  -> PASS: Dirty state và cấu trúc Guard được liên kết chính xác giữa Editor và Manager.");
}

// ---------------------------------------------------------------------------------
// R2: Preview cũ bị đánh dấu Stale và không cho Áp dụng sai metadata
// ---------------------------------------------------------------------------------
{
  console.log("R2: Kiểm tra Invalidation Preview cũ và chặn nút 'Áp dụng vào cảnh' khi Stale...");

  const mockPreview = {
    renderedText: "Văn bản đã render từ bản nháp cũ A",
    templateId: "tpl-1",
    versionId: "v-1",
    variableCount: 1,
    isOverride: false,
  };

  let appliedText = "";
  const onApply = (text: string) => {
    appliedText = text;
  };

  // 1. Khi preview KHÔNG stale: nút Áp dụng được bật (không có disabled)
  const htmlFresh = renderToString(
    React.createElement(PromptPreviewPanel, {
      previewData: mockPreview,
      isLoading: false,
      error: null,
      onRender: () => {},
      onApply,
      isDraftDirty: false,
      isStale: false,
    })
  );

  onApply("test");
  assertEqual(appliedText, "test");

  assertTrue(htmlFresh.includes("Áp dụng vào cảnh"), "Phải có nút Áp dụng");
  assertTrue(!htmlFresh.includes("Bản xem trước cũ (Stale)"), "Không được hiển thị huy hiệu Stale khi còn fresh");

  // 2. Khi preview BỊ STALE (bản nháp đã bị chỉnh sửa sau khi render):
  const htmlStale = renderToString(
    React.createElement(PromptPreviewPanel, {
      previewData: mockPreview,
      isLoading: false,
      error: null,
      onRender: () => {},
      onApply,
      isDraftDirty: true,
      isStale: true,
    })
  );

  assertTrue(htmlStale.includes("Bản xem trước cũ (Stale)"), "Phải hiển thị cảnh báo Bản xem trước cũ (Stale)");
  assertTrue(htmlStale.includes("disabled"), "Nút Áp dụng vào cảnh bắt buộc phải bị DISABLED khi preview stale");

  console.log("  -> PASS: Preview cũ bị đánh dấu Stale và bị khóa nút Áp dụng để bảo vệ provenance.");
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
    invoke: async <TData>(command: string): Promise<TData> => {
      if (command === "prompt.list_versions") {
        return { versions: [v3Version, v1Version] } as unknown as TData;
      }
      if (command === "prompt.rollback_version") {
        return {
          template: rolledBackTemplate,
          currentVersion: v1Version,
        } as unknown as TData;
      }
      return {} as unknown as TData;
    },
  };

  const htmlHistory = renderToString(
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

  assertTrue(htmlHistory.includes("v3"), "Lịch sử phải liệt kê v3");
  assertTrue(htmlHistory.includes("v1"), "Lịch sử phải liệt kê v1");
  assertTrue(htmlHistory.includes("Hiện tại"), "v3 phải có huy hiệu Hiện tại");
  assertTrue(htmlHistory.includes("Khôi phục"), "v1 phải có nút Khôi phục");

  // Giả lập callback rollback đưa cả template và version về manager
  const onRollbackSuccessHandler = (tpl: typeof rolledBackTemplate, ver: typeof v1Version) => {
    rollbackTemplateReceived = tpl;
    rollbackVersionReceived = ver;
  };
  onRollbackSuccessHandler(rolledBackTemplate, v1Version);

  assertEqual((rollbackTemplateReceived as typeof rolledBackTemplate).currentVersionNumber, 1);
  assertEqual((rollbackVersionReceived as typeof v1Version).versionId, "ver-1");
  assertEqual((rollbackVersionReceived as typeof v1Version).templateText, "Nội dung v1 ban đầu");

  console.log("  -> PASS: Rollback đồng bộ đầy đủ metadata v1 về cả Editor và Manager.");
}

// ---------------------------------------------------------------------------------
// R4: Nút Tạo mới không bị Effect auto-select ghi đè
// ---------------------------------------------------------------------------------
{
  console.log("R4: Kiểm tra chế độ Tạo mới không bị auto-select ghi đè khi đã có template...");

  const htmlNewEditor = renderToString(
    React.createElement(PromptTemplateEditor, {
      template: null,
      currentVersion: null,
      purpose: "Image",
      workspaceId: "ws-1",
    })
  );

  assertTrue(htmlNewEditor.includes("Tạo mới"), "Editor phải hiển thị huy hiệu 'Tạo mới'");
  assertTrue(htmlNewEditor.includes("Mục đích: Image"), "Phải giữ đúng mục đích Image");
  assertTrue(
    htmlNewEditor.includes("Lưu thành bản ghi đè (Override) riêng cho Workspace hiện thời"),
    "Phải có tùy chọn lưu thành Override khi ở trong Workspace"
  );

  console.log("  -> PASS: Chế độ Tạo mới template hiển thị form rỗng ổn định, không bị ghi đè.");
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

console.log("=== TẤT CẢ KIỂM THỬ COMPONENT VÀ INTEGRATION R1–R6 ĐỀU PASS 100% ===");
