import { useEffect, useRef } from "react";
import { IconClose, IconQueue, IconInfo } from "./Icons";

type QueueDrawerProps = {
  isOpen: boolean;
  onClose: () => void;
  triggerRef: React.RefObject<HTMLButtonElement | null>;
};

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

export function QueueDrawer({ isOpen, onClose, triggerRef }: QueueDrawerProps) {
  const drawerRef = useRef<HTMLDivElement>(null);
  const closeBtnRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!isOpen) return;

    // Focus vào close button ngay sau paint
    const rafId = requestAnimationFrame(() => {
      closeBtnRef.current?.focus();
    });

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        e.stopPropagation();
        onClose();
        return;
      }

      if (e.key === "Tab" && drawerRef.current) {
        const focusableElements = getFocusableElements(drawerRef.current);
        if (focusableElements.length === 0) {
          e.preventDefault();
          return;
        }

        const first = focusableElements[0];
        const last = focusableElements[focusableElements.length - 1];
        const currentIndex = focusableElements.indexOf(
          document.activeElement as HTMLElement,
        );

        if (e.shiftKey) {
          if (currentIndex <= 0) {
            e.preventDefault();
            last.focus();
          }
        } else {
          if (
            currentIndex === -1 ||
            currentIndex >= focusableElements.length - 1
          ) {
            e.preventDefault();
            first.focus();
          }
        }
      }
    };

    const handleFocusIn = (e: FocusEvent) => {
      if (drawerRef.current && !drawerRef.current.contains(e.target as Node)) {
        closeBtnRef.current?.focus();
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
      className="drawer-backdrop"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <aside
        className="queue-drawer"
        ref={drawerRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="queue-drawer-title"
      >
        <div className="drawer-header">
          <div className="drawer-title-group">
            <h2 id="queue-drawer-title">Hàng đợi tác vụ</h2>
            <span className="badge badge-neutral">Chưa kết nối</span>
          </div>
          <button
            ref={closeBtnRef}
            type="button"
            className="btn-icon"
            onClick={onClose}
            aria-label="Đóng hàng đợi"
          >
            <IconClose size={20} />
          </button>
        </div>

        <div
          className="queue-filter-bar"
          role="tablist"
          aria-label="Bộ lọc hàng đợi tác vụ"
        >
          <button
            type="button"
            role="tab"
            aria-selected="true"
            className="queue-tab active"
          >
            Tất cả
          </button>
          <button
            type="button"
            role="tab"
            aria-selected="false"
            className="queue-tab"
            disabled
          >
            Đang chạy
          </button>
          <button
            type="button"
            role="tab"
            aria-selected="false"
            className="queue-tab"
            disabled
          >
            Cần xử lý
          </button>
          <button
            type="button"
            role="tab"
            aria-selected="false"
            className="queue-tab"
            disabled
          >
            Hoàn thành
          </button>
        </div>

        <div
          className="drawer-body"
          tabIndex={0}
          role="region"
          aria-label="Nội dung hàng đợi tác vụ"
        >
          <div className="empty-state">
            <div className="empty-icon-wrapper">
              <IconQueue size={32} />
            </div>
            <h3>Hàng đợi chưa kết nối</h3>
            <p>
              Hệ thống hàng đợi tác vụ đang chờ kết nối dịch vụ. Khi tác vụ được
              khởi tạo, tiến trình thực tế sẽ hiển thị tại đây.
            </p>

            <div className="notice-box info" style={{ marginTop: "1.5rem" }}>
              <IconInfo size={16} />
              <div>
                <strong>Không hiển thị tiến trình giả định:</strong> Hàng đợi bền
                vững và tiến trình thực tế sẽ được hiển thị khi dịch vụ hàng đợi tác vụ được kết nối.
              </div>
            </div>
          </div>
        </div>
      </aside>
    </div>
  );
}
