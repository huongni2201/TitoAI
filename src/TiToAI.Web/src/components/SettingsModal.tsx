import { useEffect, useRef, useState, useCallback } from "react";
import { IconClose, IconInfo, IconCheck } from "./Icons";
import { useTheme } from "../context/ThemeContext";
import { StorageSettingsPanel } from "../features/settings/StorageSettingsPanel";

type SettingsModalProps = {
  isOpen: boolean;
  onClose: () => void;
  triggerRef: React.RefObject<HTMLButtonElement | null>;
};

type SettingsTab = "storage" | "ai" | "appearance";

function getFocusableElements(container: HTMLElement): HTMLElement[] {
  const elements = Array.from(
    container.querySelectorAll<HTMLElement>(
      'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"]):not([disabled])',
    ),
  );
  return elements.filter((el) => {
    if (el.hasAttribute("disabled")) return false;
    if ("disabled" in el && (el as HTMLButtonElement).disabled) return false;
    if (el.getAttribute("aria-hidden") === "true") return false;
    if (el.closest('[aria-hidden="true"]')) return false;
    if (el.tabIndex === -1) return false;
    if (typeof window !== "undefined") {
      const style = window.getComputedStyle(el);
      if (style.display === "none" || style.visibility === "hidden") return false;
    }
    return true;
  });
}

export function SettingsModal({
  isOpen,
  onClose,
  triggerRef,
}: SettingsModalProps) {
  const [activeTab, setActiveTab] = useState<SettingsTab>("storage");
  const modalRef = useRef<HTMLDivElement>(null);
  const closeButtonRef = useRef<HTMLButtonElement>(null);

  const {
    preference,
    persistenceWarning,
    retryPersistence,
    setThemePreference,
  } = useTheme();

  // Điều hướng bằng phím mũi tên giữa các Tab theo chuẩn WAI-ARIA
  const handleTabListKeyDown = useCallback(
    (e: React.KeyboardEvent, currentTab: SettingsTab) => {
      const tabs: SettingsTab[] = ["storage", "ai", "appearance"];
      const currentIndex = tabs.indexOf(currentTab);
      let nextIndex = -1;

      if (e.key === "ArrowRight" || e.key === "ArrowDown") {
        e.preventDefault();
        nextIndex = (currentIndex + 1) % tabs.length;
      } else if (e.key === "ArrowLeft" || e.key === "ArrowUp") {
        e.preventDefault();
        nextIndex = (currentIndex - 1 + tabs.length) % tabs.length;
      } else if (e.key === "Home") {
        e.preventDefault();
        nextIndex = 0;
      } else if (e.key === "End") {
        e.preventDefault();
        nextIndex = tabs.length - 1;
      }

      if (nextIndex !== -1) {
        const nextTab = tabs[nextIndex];
        setActiveTab(nextTab);
        const tabBtn = modalRef.current?.querySelector<HTMLButtonElement>(
          `#tab-${nextTab}`,
        );
        tabBtn?.focus();
      }
    },
    [],
  );

  // Điều hướng bằng phím mũi tên trong nhóm Radio Theme
  const handleThemeKeyDown = useCallback(
    (e: React.KeyboardEvent, currentTheme: "system" | "light" | "dark") => {
      const themes: Array<"system" | "light" | "dark"> = [
        "system",
        "light",
        "dark",
      ];
      const currentIndex = themes.indexOf(currentTheme);
      let nextIndex = -1;

      if (e.key === "ArrowRight" || e.key === "ArrowDown") {
        e.preventDefault();
        nextIndex = (currentIndex + 1) % themes.length;
      } else if (e.key === "ArrowLeft" || e.key === "ArrowUp") {
        e.preventDefault();
        nextIndex = (currentIndex - 1 + themes.length) % themes.length;
      }

      if (nextIndex !== -1) {
        const nextTheme = themes[nextIndex];
        setThemePreference(nextTheme);
        const card = modalRef.current?.querySelector<HTMLButtonElement>(
          `#theme-option-${nextTheme}`,
        );
        card?.focus();
      }
    },
    [setThemePreference],
  );

  // Quản lý focus trap, Escape và focus return
  useEffect(() => {
    if (!isOpen) return;

    // Focus vào close button ngay sau paint
    const rafId = requestAnimationFrame(() => {
      closeButtonRef.current?.focus();
    });

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        e.stopPropagation();
        onClose();
        return;
      }

      if (e.key === "Tab" && modalRef.current) {
        const focusableElements = getFocusableElements(modalRef.current);
        if (focusableElements.length === 0) {
          e.preventDefault();
          return;
        }

        const firstElement = focusableElements[0];
        const lastElement = focusableElements[focusableElements.length - 1];
        const currentIndex = focusableElements.indexOf(
          document.activeElement as HTMLElement,
        );

        if (e.shiftKey) {
          if (currentIndex <= 0) {
            e.preventDefault();
            lastElement.focus();
          }
        } else {
          if (
            currentIndex === -1 ||
            currentIndex >= focusableElements.length - 1
          ) {
            e.preventDefault();
            firstElement.focus();
          }
        }
      }
    };

    const handleFocusIn = (e: FocusEvent) => {
      if (modalRef.current && !modalRef.current.contains(e.target as Node)) {
        closeButtonRef.current?.focus();
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    document.addEventListener("focusin", handleFocusIn);

    return () => {
      cancelAnimationFrame(rafId);
      window.removeEventListener("keydown", handleKeyDown);
      document.removeEventListener("focusin", handleFocusIn);
      // Trả lại focus cho trigger button khi đóng qua rAF để đảm bảo React đã gỡ inert
      requestAnimationFrame(() => {
        triggerRef.current?.focus();
      });
    };
  }, [isOpen, onClose, triggerRef]);

  if (!isOpen) return null;

  return (
    <div
      className="modal-backdrop"
      style={{ padding: "1rem" }}
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div
        className="modal-container"
        ref={modalRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="settings-modal-title"
      >
        <div className="modal-header">
          <div className="modal-title-group">
            <h2 id="settings-modal-title">Cài đặt TiToAI</h2>
            <p className="modal-subtitle">
              Cấu hình lưu trữ cục bộ, dịch vụ AI và giao diện ứng dụng
            </p>
          </div>
          <button
            ref={closeButtonRef}
            type="button"
            className="btn-icon"
            onClick={onClose}
            aria-label="Đóng cài đặt"
          >
            <IconClose size={20} />
          </button>
        </div>

        <div
          className="settings-tabs"
          role="tablist"
          aria-label="Các nhóm cài đặt"
        >
          <button
            type="button"
            role="tab"
            id="tab-storage"
            aria-controls="panel-storage"
            aria-selected={activeTab === "storage"}
            tabIndex={0}
            className={`settings-tab-btn ${activeTab === "storage" ? "active" : ""}`}
            onClick={() => setActiveTab("storage")}
            onKeyDown={(e) => handleTabListKeyDown(e, "storage")}
          >
            1. Lưu trữ
          </button>
          <button
            type="button"
            role="tab"
            id="tab-ai"
            aria-controls="panel-ai"
            aria-selected={activeTab === "ai"}
            tabIndex={0}
            className={`settings-tab-btn ${activeTab === "ai" ? "active" : ""}`}
            onClick={() => setActiveTab("ai")}
            onKeyDown={(e) => handleTabListKeyDown(e, "ai")}
          >
            2. AI &amp; Prompt
          </button>
          <button
            type="button"
            role="tab"
            id="tab-appearance"
            aria-controls="panel-appearance"
            aria-selected={activeTab === "appearance"}
            tabIndex={0}
            className={`settings-tab-btn ${activeTab === "appearance" ? "active" : ""}`}
            onClick={() => setActiveTab("appearance")}
            onKeyDown={(e) => handleTabListKeyDown(e, "appearance")}
          >
            3. Giao diện
          </button>
        </div>

        <div className="modal-body">
          {activeTab === "storage" && (
            <div
              className="settings-section"
              role="tabpanel"
              id="panel-storage"
              aria-labelledby="tab-storage"
            >
              <StorageSettingsPanel onClose={onClose} />
            </div>
          )}

          {activeTab === "ai" && (
            <div
              className="settings-section"
              role="tabpanel"
              id="panel-ai"
              aria-labelledby="tab-ai"
              tabIndex={0}
              aria-label="Nhóm cài đặt AI & Prompt"
            >
              <div className="section-header">
                <h3>Lựa chọn Provider AI theo tác vụ</h3>
                <p>
                  Hỗ trợ cấu hình độc lập Gemini Web hoặc ChatGPT Web cho từng
                  tác vụ riêng biệt.
                </p>
              </div>

              <div className="form-group">
                <label htmlFor="script-provider">Tác vụ Tạo kịch bản (Script)</label>
                <select id="script-provider" className="form-control" disabled value="">
                  <option value="">Chưa chọn (Mặc định)</option>
                  <option value="gemini-web">Gemini Web</option>
                  <option value="chatgpt-web">ChatGPT Web</option>
                </select>
                <span className="field-hint">Sinh nội dung kịch bản và phân rã các cảnh.</span>
              </div>

              <div className="form-group">
                <label htmlFor="image-provider">Tác vụ Tạo ảnh (Image)</label>
                <select id="image-provider" className="form-control" disabled value="">
                  <option value="">Chưa chọn (Mặc định)</option>
                  <option value="gemini-web">Gemini Web</option>
                  <option value="chatgpt-web">ChatGPT Web</option>
                </select>
                <span className="field-hint">Tạo hình ảnh cảnh quay theo mô tả kịch bản.</span>
              </div>

              <div className="form-group">
                <label htmlFor="video-prompt-provider">Tác vụ Tạo prompt video (Video Prompt)</label>
                <select id="video-prompt-provider" className="form-control" disabled value="">
                  <option value="">Chưa chọn (Mặc định)</option>
                  <option value="gemini-web">Gemini Web</option>
                  <option value="chatgpt-web">ChatGPT Web</option>
                </select>
                <span className="field-hint">Sinh prompt chuyển động và góc quay cho MuseAI.</span>
              </div>

              <div className="notice-box info">
                <IconInfo size={18} />
                <div>
                  <strong>Trạng thái tích hợp:</strong> Cấu hình dịch vụ AI sẽ được
                  kích hoạt khi hệ thống điều phối Provider sẵn sàng. Video Render
                  cố định qua MuseAI theo tài khoản kênh.
                </div>
              </div>
            </div>
          )}

          {activeTab === "appearance" && (
            <div
              className="settings-section"
              role="tabpanel"
              id="panel-appearance"
              aria-labelledby="tab-appearance"
            >
              <div className="section-header">
                <h3>Giao diện hiển thị</h3>
                <p>
                  Áp dụng ngay qua giao diện, hỗ trợ Theo hệ thống (System), Sáng
                  (Light) và Tối (Dark).
                </p>
              </div>

              <div
                className="theme-options-grid"
                role="radiogroup"
                aria-label="Tùy chọn giao diện ứng dụng"
              >
                <button
                  type="button"
                  role="radio"
                  id="theme-option-system"
                  aria-checked={preference === "system"}
                  className={`theme-option-card ${preference === "system" ? "selected" : ""}`}
                  onClick={() => setThemePreference("system")}
                  onKeyDown={(e) => handleThemeKeyDown(e, "system")}
                >
                  <div className="theme-card-header">
                    <strong>Theo hệ thống (System)</strong>
                    {preference === "system" && <IconCheck size={16} />}
                  </div>
                  <p>Tự động đồng bộ theo cấu hình sáng/tối của Windows.</p>
                </button>

                <button
                  type="button"
                  role="radio"
                  id="theme-option-light"
                  aria-checked={preference === "light"}
                  className={`theme-option-card ${preference === "light" ? "selected" : ""}`}
                  onClick={() => setThemePreference("light")}
                  onKeyDown={(e) => handleThemeKeyDown(e, "light")}
                >
                  <div className="theme-card-header">
                    <strong>Giao diện Sáng (Light)</strong>
                    {preference === "light" && <IconCheck size={16} />}
                  </div>
                  <p>Tông màu sáng, nền sạch, độ tương phản cao.</p>
                </button>

                <button
                  type="button"
                  role="radio"
                  id="theme-option-dark"
                  aria-checked={preference === "dark"}
                  className={`theme-option-card ${preference === "dark" ? "selected" : ""}`}
                  onClick={() => setThemePreference("dark")}
                  onKeyDown={(e) => handleThemeKeyDown(e, "dark")}
                >
                  <div className="theme-card-header">
                    <strong>Giao diện Tối (Dark)</strong>
                    {preference === "dark" && <IconCheck size={16} />}
                  </div>
                  <p>Dark navy nhiều lớp, accent tím theo bố cục tham khảo.</p>
                </button>
              </div>

              {persistenceWarning && (
                <div className="notice-box info" role="alert" aria-live="polite">
                  <IconInfo size={18} />
                  <div>
                    <strong>Giao diện chưa được lưu.</strong>{" "}
                    {persistenceWarning === "read"
                      ? "Ứng dụng đang dùng Theo hệ thống; cấu hình đã lưu chưa bị ghi đè."
                      : "Giao diện đang có hiệu lực trong phiên này nhưng chưa lưu được."}
                    <button
                      type="button"
                      className="btn btn-secondary"
                      onClick={retryPersistence}
                    >
                      Thử lại
                    </button>
                  </div>
                </div>
              )}

              <div className="notice-box info">
                <IconInfo size={18} />
                <div>
                  <strong>Giao diện:</strong> Thay đổi có hiệu lực ngay và được tự lưu.
                </div>
              </div>
            </div>
          )}
        </div>

        <div className="modal-footer">
          <span className="footer-status">
            {activeTab === "appearance"
              ? "Giao diện được áp dụng ngay và tự lưu."
              : "Cấu hình sẽ được lưu bền vững khi dịch vụ lưu trữ và AI sẵn sàng."}
          </span>
          <button type="button" className="btn btn-secondary" onClick={onClose}>
            Đóng
          </button>
        </div>
      </div>
    </div>
  );
}
