/**
 * TiToAI — Kiểm thử Toàn diện: Dynamic Variable Form, Chuẩn hóa Tên biến, Phân biệt Template vs Rendered & Sửa lỗi Xóa Prompt
 * Chạy trực tiếp qua Node.js: node --import ./src/features/prompts/test-loader.mjs src/features/prompts/promptDynamicFormAndDeletion.test.ts
 */

import { register } from "node:module";
import { GlobalWindow } from "happy-dom";
import assert from "node:assert/strict";

register("./test-loader.mjs", import.meta.url);

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
  normalizeVariableName,
  isValidVariableName,
  detectNonStandardVariables,
  normalizeTemplateText,
  renderPromptTemplateClient,
} = await import("./promptApi.ts");

const {
  deleteStoredPrompt,
  getDeletedPromptIds,
  getStoredPrompts,
  saveStoredPrompts,
  getActivePromptId,
  setActivePromptId,
  DEFAULT_PRESET_PROMPTS,
} = await import("./promptStorage.ts");

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

async function runTests() {
  console.log("=== BẮT ĐẦU KIỂM THỬ: DYNAMIC VARIABLE FORM, CHUẨN HÓA TÊN BIẾN, RENDERED PROMPT & FIX BUG XÓA ===");

  // TEST 1: Chuẩn hóa tên biến không dấu (§2.2) với đúng 5 mẫu của đề xuất
  console.log("\n[TEST 1] Kiểm tra hàm chuẩn hóa tên biến (§2.2)...");
  {
    assert.strictEqual(normalizeVariableName("chủ_đề"), "chu_de");
    assert.strictEqual(normalizeVariableName("sản phẩm"), "san_pham");
    assert.strictEqual(normalizeVariableName("Đối Tượng Khách Hàng"), "doi_tuong_khach_hang");
    assert.strictEqual(normalizeVariableName("phong cách video"), "phong_cach_video");
    assert.strictEqual(normalizeVariableName("thời_lượng"), "thoi_luong");

    assert.strictEqual(isValidVariableName("chu_de"), true);
    assert.strictEqual(isValidVariableName("san_pham"), true);
    assert.strictEqual(isValidVariableName("doi_tuong_khach_hang"), true);
    assert.strictEqual(isValidVariableName("phong_cach_video"), true);
    assert.strictEqual(isValidVariableName("thoi_luong"), true);

    // Ký tự đầu phải là chữ cái, không khoảng trắng, không dấu
    assert.strictEqual(isValidVariableName("1_chu_de"), false);
    assert.strictEqual(isValidVariableName("chu de"), false);
    assert.strictEqual(isValidVariableName("chủ_đề"), false);

    console.log("  -> PASS [TEST 1]: Chuẩn hóa tên biến 100% khớp quy tắc §2.2.");
  }

  // TEST 2: Dynamic Variable Form — Tự sinh field và cập nhật tức thì theo template (§3)
  console.log("\n[TEST 2] Dynamic Variable Form — Phát hiện 4 biến, thêm biến thứ 5, xóa biến...");
  {
    const initialTemplateText =
      "Bạn là chuyên gia TikTok Affiliate.\n" +
      "Hãy viết kịch bản giới thiệu {san_pham}.\n" +
      "Đối tượng khách hàng: {doi_tuong}.\n" +
      "Phong cách video: {phong_cach}.\n" +
      "Thời lượng: {thoi_luong} giây.";

    const dom = await renderIntoDom(
      React.createElement(PromptTemplateEditor, {
        template: null,
        currentVersion: {
          versionId: "v-dyn",
          templateId: "tpl-dyn",
          versionNumber: 1,
          templateText: initialTemplateText,
          variableDefinitionsJson: "[]",
          changeDescription: null,
          createdAtUtc: new Date().toISOString(),
        },
        purpose: "Script",
        promptName: "Kịch bản Affiliate Mẫu",
      })
    );

    // 1. Phải sinh đúng 4 input cho 4 biến
    let varInputs = dom.container.querySelectorAll('input[id^="var-input-"]');
    assert.strictEqual(varInputs.length, 4, "Ban đầu phải có đúng 4 input cho 4 biến");
    assert.ok(dom.container.querySelector('input#var-input-san_pham'), "Phải có input {san_pham}");
    assert.ok(dom.container.querySelector('input#var-input-doi_tuong'), "Phải có input {doi_tuong}");
    assert.ok(dom.container.querySelector('input#var-input-phong_cach'), "Phải có input {phong_cach}");
    assert.ok(dom.container.querySelector('input#var-input-thoi_luong'), "Phải có input {thoi_luong}");

    // Nhập giá trị cho {san_pham}
    const sanPhamInput = dom.container.querySelector('input#var-input-san_pham') as HTMLInputElement;
    await act(async () => {
      setInputValue(sanPhamInput, "Áo sơ mi lụa cao cấp");
    });

    // 2. Thêm {boi_canh} vào template -> tự sinh input thứ 5
    const textarea = dom.container.querySelector("textarea#prompt-template-textarea") as HTMLTextAreaElement;
    await act(async () => {
      setInputValue(
        textarea,
        initialTemplateText + "\nBối cảnh quay: {boi_canh}."
      );
    });

    varInputs = dom.container.querySelectorAll('input[id^="var-input-"]');
    assert.strictEqual(varInputs.length, 5, "Sau khi thêm {boi_canh}, màn hình tự xuất hiện input thứ 5!");
    assert.ok(dom.container.querySelector('input#var-input-boi_canh'), "Phải xuất hiện input cho {boi_canh}");

    // Giá trị đã nhập của {san_pham} phải được bảo toàn!
    const sanPhamInputAfter = dom.container.querySelector('input#var-input-san_pham') as HTMLInputElement;
    assert.strictEqual(
      sanPhamInputAfter.value,
      "Áo sơ mi lụa cao cấp",
      "Giá trị đã nhập của {san_pham} phải được bảo toàn khi thêm biến mới"
    );

    // 3. Xóa {doi_tuong} khỏi template -> input tương ứng biến mất
    await act(async () => {
      setInputValue(
        textarea,
        "Bạn là chuyên gia TikTok Affiliate.\nHãy viết kịch bản giới thiệu {san_pham} trong {thoi_luong} giây."
      );
    });

    varInputs = dom.container.querySelectorAll('input[id^="var-input-"]');
    assert.strictEqual(varInputs.length, 2, "Sau khi xóa các biến khác, chỉ còn lại 2 input");
    assert.strictEqual(
      dom.container.querySelector('input#var-input-doi_tuong'),
      null,
      "Input {doi_tuong} phải biến mất"
    );

    await dom.unmount();
    console.log("  -> PASS [TEST 2]: Dynamic Variable Form tự sinh/xóa field theo template chuẩn xác 100%.");
  }

  // TEST 3: Phân biệt Prompt Template vs Rendered Prompt (§1)
  console.log("\n[TEST 3] Phân biệt rõ Prompt Template và Rendered Prompt...");
  {
    const templateText = "Giới thiệu {san_pham} cho đối tượng {doi_tuong} trong {thoi_luong}s.";
    const variables = {
      san_pham: "Kem chống nắng",
      doi_tuong: "Học sinh sinh viên",
      thoi_luong: "30",
    };

    const result = renderPromptTemplateClient(templateText, variables);

    // Rendered Prompt đã thay thế toàn bộ biến số
    assert.strictEqual(
      result.renderedText,
      "Giới thiệu Kem chống nắng cho đối tượng Học sinh sinh viên trong 30s."
    );

    // Prompt Template gốc vẫn giữ nguyên hoàn toàn các biến số {san_pham}, v.v.
    assert.ok(templateText.includes("{san_pham}"));
    assert.ok(templateText.includes("{doi_tuong}"));
    assert.ok(templateText.includes("{thoi_luong}"));

    console.log("  -> PASS [TEST 3]: Phân biệt rành mạch Template gốc và Rendered Prompt.");
  }

  // TEST 4: Fix bug prompt hiện không xóa được
  console.log("\n[TEST 4] Kiểm tra sửa lỗi Xóa Prompt (kể cả preset prompt)...");
  {
    const wsId = "ws_delete_test";

    // Khởi tạo storage rỗng cho ws này
    saveStoredPrompts([], wsId);

    const mockTransport = {
      invoke: async <TData = unknown, TPayload = unknown>(command: string): Promise<TData> => {
        if (command === "prompt.list_templates") {
          return { templates: [] } as unknown as TData;
        }
        return {} as unknown as TData;
      },
    };

    const dom = await renderIntoDom(
      React.createElement(PromptTemplateManager, {
        workspaceId: wsId,
        initialPurpose: "Script",
        transport: mockTransport,
      })
    );

    await act(async () => {
      await new Promise((r) => setTimeout(r, 30));
    });

    // Lúc này danh sách nạp 3 preset: TikTok chuẩn, Phân tích ý tưởng, Review sản phẩm
    const promptCards = dom.container.querySelectorAll("[role='button']");
    assert.strictEqual(promptCards.length, 3, "Ban đầu có 3 prompt");

    // Chọn prompt thứ 2 (Phân tích dữ liệu & Đề xuất 5 ý tưởng video)
    await act(async () => {
      promptCards[1].dispatchEvent(new MouseEvent("click", { bubbles: true }));
      await new Promise((r) => setTimeout(r, 20));
    });

    // Bấm nút "Xóa Prompt"
    const deleteBtn = Array.from(dom.container.querySelectorAll("button")).find((b) =>
      b.textContent?.includes("Xóa Prompt")
    );
    assert.ok(deleteBtn, "Phải tìm thấy nút Xóa Prompt");

    await act(async () => {
      deleteBtn!.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });

    // Modal xác nhận xuất hiện
    const alertDialog = dom.container.querySelector("[role='alertdialog']");
    assert.ok(alertDialog, "Phải xuất hiện modal xác nhận xóa");

    // Bấm "Đồng ý xóa"
    const confirmDeleteBtn = Array.from(alertDialog!.querySelectorAll("button")).find((b) =>
      b.textContent?.includes("Đồng ý xóa")
    );
    assert.ok(confirmDeleteBtn, "Phải tìm thấy nút 'Đồng ý xóa'");

    await act(async () => {
      confirmDeleteBtn!.dispatchEvent(new MouseEvent("click", { bubbles: true }));
      await new Promise((r) => setTimeout(r, 40));
    });

    // Prompt đã xóa PHẢI biến mất, danh sách chỉ còn 2 prompt!
    const cardsAfterDelete = dom.container.querySelectorAll("[role='button']");
    assert.strictEqual(cardsAfterDelete.length, 2, "Sau khi xóa, danh sách chỉ còn lại 2 prompt!");

    // Tuyệt đối không còn xuất hiện "Phân tích dữ liệu & Đề xuất 5 ý tưởng video"
    assert.strictEqual(
      dom.container.textContent?.includes("Phân tích dữ liệu & Đề xuất 5 ý tưởng video"),
      false,
      "Prompt đã xóa tuyệt đối không được xuất hiện lại trong danh sách!"
    );

    // Kiểm tra blacklist vĩnh viễn đã ghi nhận
    const deletedIds = getDeletedPromptIds(wsId);
    assert.ok(
      deletedIds.has("preset-script-ideas-analysis"),
      "ID của preset đã xóa phải được lưu vào blacklist vĩnh viễn"
    );

    await dom.unmount();
    console.log("  -> PASS [TEST 4]: Bug xóa prompt đã được khắc phục triệt để, không bị tái sinh.");
  }

  // TEST 5: Trạng thái Áp dụng không tự đổi khi click hoặc chỉnh sửa (§2.1)
  console.log("\n[TEST 5] Kiểm tra Trạng thái Áp dụng không tự đổi khi click hoặc chỉnh sửa...");
  {
    const wsId = "ws_active_guard";
    setActivePromptId("Script", "preset-script-tiktok-standard", wsId);

    const mockTransport = {
      invoke: async <TData = unknown>(): Promise<TData> => {
        return { templates: [] } as unknown as TData;
      },
    };

    const dom = await renderIntoDom(
      React.createElement(PromptTemplateManager, {
        workspaceId: wsId,
        initialPurpose: "Script",
        transport: mockTransport,
      })
    );

    await act(async () => {
      await new Promise((r) => setTimeout(r, 30));
    });

    // Prompt 1 đang có badge "Áp dụng"
    assert.strictEqual(getActivePromptId("Script", wsId), "preset-script-tiktok-standard");

    // Click chọn Prompt 3 (Review sản phẩm)
    const promptCards = dom.container.querySelectorAll("[role='button']");
    await act(async () => {
      promptCards[2].dispatchEvent(new MouseEvent("click", { bubbles: true }));
      await new Promise((r) => setTimeout(r, 20));
    });

    // QUY TẮC §2.1: Active prompt VẪN PHẢI LÀ preset-script-tiktok-standard!
    assert.strictEqual(
      getActivePromptId("Script", wsId),
      "preset-script-tiktok-standard",
      "Click chọn prompt khác KHÔNG ĐƯỢC tự ý đổi trạng thái Áp dụng!"
    );

    // Chỉ khi bấm nút "Sử dụng prompt này", active prompt mới đổi
    const useThisBtn = Array.from(dom.container.querySelectorAll("button")).find((b) =>
      b.textContent?.includes("Sử dụng prompt này")
    );
    assert.ok(useThisBtn, "Phải có nút 'Sử dụng prompt này'");

    await act(async () => {
      useThisBtn!.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });

    assert.strictEqual(
      getActivePromptId("Script", wsId),
      "preset-script-product-review",
      "Chỉ đổi active prompt khi người dùng chủ động bấm 'Sử dụng prompt này'!"
    );

    await dom.unmount();
    console.log("  -> PASS [TEST 5]: Trạng thái Áp dụng bảo toàn đúng quy tắc §2.1.");
  }

  console.log("\n=================================================================");
  console.log(">>> TOÀN BỘ 5 TEST SUITE ĐẶC TẢ NÂNG CẤP ĐÃ VƯỢT QUA (PASS 100%) <<<");
  console.log("=================================================================");
}

runTests().catch((err) => {
  console.error("TEST FAILED:", err);
  process.exit(1);
});
