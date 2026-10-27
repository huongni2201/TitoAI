import { useEffect, useRef, useState, useCallback } from "react";
import { X, ListTodo, RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { JobList } from "@/features/jobs/JobList";
import { useQueueState } from "@/features/jobs/useQueueState";
import type {
  JobSummaryDto,
  JobDetailDto,
  QueueTabFilter,
  JobReconcileResponseData,
} from "@/features/jobs/jobTypes";

export interface QueueDrawerProps {
  isOpen: boolean;
  onClose: () => void;
  triggerRef: React.RefObject<HTMLButtonElement | null>;
  onNavigateToJob?: (job: JobSummaryDto | JobDetailDto) => void;
  onNavigateToSettingsAccounts?: () => void;
}

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

export function QueueDrawer({
  isOpen,
  onClose,
  triggerRef,
  onNavigateToJob,
  onNavigateToSettingsAccounts,
}: QueueDrawerProps) {
  const drawerRef = useRef<HTMLDivElement>(null);
  const closeBtnRef = useRef<HTMLButtonElement>(null);
  const [actionLoadingJobId, setActionLoadingJobId] = useState<string | null>(null);

  // Hook quản trị hàng đợi kết nối trực tiếp với backend qua Bridge & Realtime Events
  const queueState = useQueueState({ isOpen });

  useEffect(() => {
    if (!isOpen) return;

    // Focus vào close button ngay sau khi mount
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
      const target = e.target as HTMLElement | null;
      // Bảo vệ nested dialogs: Nếu focus đang nằm trong dialog con (JobDetailsDialog, JobReconcileDialog qua Portal), không can thiệp
      if (target?.closest?.('[role="dialog"]') && !drawerRef.current?.contains(target)) {
        return;
      }
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
      // Trả lại focus cho trigger button khi đóng
      requestAnimationFrame(() => {
        triggerRef.current?.focus();
      });
    };
  }, [isOpen, onClose, triggerRef]);

  const handleAction = useCallback(
    async (
      actionType: "login" | "navigate_settings_accounts" | "resume" | "retry" | "cancel",
      job: JobSummaryDto | JobDetailDto,
    ) => {
      setActionLoadingJobId(job.id);
      try {
        if (actionType === "navigate_settings_accounts") {
          onClose();
          onNavigateToSettingsAccounts?.();
          return;
        }

        if (actionType === "login") {
          // T17-R4: Lấy profileId thực tế từ job, TUYỆT ĐỐI không dùng providerKey làm profileId
          const profileId = job.profileId?.trim() ?? "";
          if (!profileId) {
            // Chưa liên kết hồ sơ trình duyệt -> điều hướng rõ ràng sang Cài đặt Tài khoản
            onClose();
            onNavigateToSettingsAccounts?.();
            return;
          }
          await queueState.handleRequestLogin(profileId);
        } else if (actionType === "resume") {
          await queueState.handleResume(job);
        } else if (actionType === "cancel") {
          await queueState.handleCancel(job);
        } else if (actionType === "retry") {
          await queueState.handleRetry(job);
        }
      } finally {
        setActionLoadingJobId(null);
      }
    },
    [queueState, onClose, onNavigateToSettingsAccounts],
  );

  const handleReconcileSuccess = useCallback(
    (_job: JobSummaryDto | JobDetailDto, _result: JobReconcileResponseData) => {
      queueState.refresh();
    },
    [queueState],
  );

  if (!isOpen) return null;

  const activeCount = queueState.tabCounts.running + queueState.tabCounts.awaiting;

  return (
    <div
      className="bg-black/60 backdrop-blur-sm z-50 fixed inset-0 flex justify-end animate-in fade-in-0 duration-200"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <aside
        className="fixed inset-y-0 right-0 w-full max-w-[540px] sm:max-w-[560px] bg-card border-l border-border z-50 shadow-2xl flex flex-col transition-transform duration-300 animate-in slide-in-from-right-full"
        ref={drawerRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="queue-drawer-title"
      >
        {/* Header hàng đợi */}
        <div className="flex items-center justify-between px-5 py-3.5 border-b border-border bg-card/80 backdrop-blur-xs shrink-0">
          <div className="flex items-center gap-2.5">
            <div className="flex items-center justify-center w-8 h-8 rounded-lg bg-primary/10 text-primary">
              <ListTodo className="w-4 h-4" />
            </div>
            <div>
              <h2
                id="queue-drawer-title"
                className="text-sm font-semibold leading-none text-foreground flex items-center gap-2"
              >
                <span>Hàng đợi tác vụ</span>
                {queueState.isLoading && (
                  <span className="text-[10px] font-normal text-muted-foreground animate-pulse">
                    (Đang cập nhật...)
                  </span>
                )}
              </h2>
              <p className="text-xs text-muted-foreground mt-1">
                {activeCount > 0 ? (
                  <span className="text-primary font-medium">
                    {activeCount} tác vụ đang chờ / chạy
                  </span>
                ) : (
                  <span>Không có tác vụ đang chạy</span>
                )}
              </p>
            </div>
          </div>
          <div className="flex items-center gap-1">
            <Button
              variant="ghost"
              size="icon"
              onClick={() => queueState.refresh()}
              disabled={queueState.isLoading}
              className="h-8 w-8 rounded-md text-muted-foreground hover:text-foreground hover:bg-muted"
              title="Làm mới hàng đợi"
              aria-label="Làm mới hàng đợi"
            >
              <RefreshCw
                className={`h-3.5 w-3.5 ${queueState.isLoading ? "animate-spin" : ""}`}
              />
            </Button>
            <Button
              ref={closeBtnRef}
              variant="ghost"
              size="icon"
              onClick={onClose}
              className="h-8 w-8 rounded-md text-muted-foreground hover:text-foreground hover:bg-muted"
              aria-label="Đóng hàng đợi"
            >
              <X className="h-4 w-4" />
            </Button>
          </div>
        </div>

        {/* Thân danh sách tác vụ */}
        <div className="flex-1 min-h-0 flex flex-col">
          <JobList
            jobs={queueState.jobs}
            isLoading={queueState.isLoading}
            error={queueState.error ? queueState.error.message : null}
            onRefresh={queueState.refresh}
            onNavigateToJob={onNavigateToJob}
            onAction={handleAction}
            onReconcileSuccess={handleReconcileSuccess}
            actionLoadingJobId={actionLoadingJobId}
          />
        </div>
      </aside>
    </div>
  );
}

// Export các kiểu tương thích ngược
export type { JobSummaryDto as QueueJob };
export type { QueueTabFilter };
