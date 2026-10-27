import { useState, useEffect, useCallback, useRef } from "react";
import { X, RefreshCw, Clock, AlertTriangle } from "lucide-react";
import { cn } from "../../lib/utils.ts";
import {
  promptApi,
  type PromptTemplateDto,
  type PromptVersionDto,
  type IPromptTransport,
  defaultPromptTransport,
} from "./promptApi.ts";

export interface PromptVersionHistoryProps {
  templateId: string;
  currentVersionId: string | null;
  currentVersionNumber?: number | null;
  workspaceId?: string | null;
  transport?: IPromptTransport;
  initialVersions?: PromptVersionDto[];
  isOpen: boolean;
  onClose: () => void;
  onRollbackSuccess: (rolledBackTemplate: PromptTemplateDto, rolledBackVersion: PromptVersionDto) => void;
}

export function PromptVersionHistory({
  templateId,
  currentVersionId,
  workspaceId,
  transport = defaultPromptTransport,
  initialVersions,
  isOpen,
  onClose,
  onRollbackSuccess,
}: PromptVersionHistoryProps) {
  const [versions, setVersions] = useState<PromptVersionDto[]>(initialVersions ?? []);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Xem chi tiết một phiên bản cũ
  const [viewingVersion, setViewingVersion] = useState<PromptVersionDto | null>(null);

  // Trạng thái rollback
  const [rollbackTarget, setRollbackTarget] = useState<PromptVersionDto | null>(null);
  const [isRollingBack, setIsRollingBack] = useState(false);
  const [rollbackError, setRollbackError] = useState<string | null>(null);

  const modalRef = useRef<HTMLDivElement>(null);

  const fetchVersions = useCallback(async () => {
    if (!templateId || !isOpen) return;
    setIsLoading(true);
    setError(null);
    try {
      const list = await promptApi.listVersions(templateId, workspaceId, transport);
      setVersions(list);
    } catch (err: unknown) {
      const msg =
        err && typeof err === "object" && "message" in err
          ? String(err.message)
          : "Không thể tải danh sách phiên bản.";
      setError(msg);
    } finally {
      setIsLoading(false);
    }
  }, [templateId, workspaceId, transport, isOpen]);

  useEffect(() => {
    if (isOpen) {
      fetchVersions();
      setViewingVersion(null);
      setRollbackTarget(null);
      setRollbackError(null);
    }
  }, [isOpen, fetchVersions]);

  // Phím Escape đóng modal
  useEffect(() => {
    if (!isOpen) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape" && !isRollingBack) {
        if (rollbackTarget) {
          setRollbackTarget(null);
        } else if (viewingVersion) {
          setViewingVersion(null);
        } else {
          onClose();
        }
      }
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [isOpen, isRollingBack, rollbackTarget, viewingVersion, onClose]);

  if (!isOpen) return null;

  const handleConfirmRollback = async () => {
    if (!rollbackTarget) return;
    setIsRollingBack(true);
    setRollbackError(null);
    try {
      const res = await promptApi.rollbackVersion(
        {
          templateId,
          targetVersionId: rollbackTarget.versionId,
        },
        workspaceId,
        transport
      );
      onRollbackSuccess(res.template, res.currentVersion);
      setRollbackTarget(null);
      onClose();
    } catch (err: unknown) {
      const msg =
        err && typeof err === "object" && "message" in err
          ? String(err.message)
          : "Khôi phục phiên bản thất bại.";
      setRollbackError(msg);
    } finally {
      setIsRollingBack(false);
    }
  };

  const formatDate = (isoStr: string) => {
    try {
      const d = new Date(isoStr);
      return d.toLocaleString("vi-VN", {
        year: "numeric",
        month: "2-digit",
        day: "2-digit",
        hour: "2-digit",
        minute: "2-digit",
        second: "2-digit",
      });
    } catch {
      return isoStr;
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-xs"
      role="dialog"
      aria-modal="true"
      aria-labelledby="version-history-title"
    >
      <div
        ref={modalRef}
        className="flex flex-col w-full max-w-2xl max-h-[88vh] rounded-xl border border-border bg-card text-card-foreground shadow-xl overflow-hidden"
      >
        {/* Header */}
        <div className="flex items-center justify-between border-b border-border/80 px-5 py-4 bg-card shrink-0">
          <div>
            <h2
              id="version-history-title"
              className="text-base font-bold text-foreground"
            >
              Lịch sử phiên bản template
            </h2>
            <p className="mt-0.5 text-xs text-muted-foreground">
              ID: {templateId} · Các phiên bản được lưu trữ bất biến đơn điệu
            </p>
          </div>
          <button
            type="button"
            className="inline-flex h-8 w-8 items-center justify-center rounded-md text-muted-foreground hover:bg-accent hover:text-foreground transition-colors cursor-pointer focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
            onClick={onClose}
            disabled={isRollingBack}
            aria-label="Đóng hộp thoại lịch sử"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        {/* Content body */}
        <div className="p-5 overflow-y-auto flex flex-col gap-3 flex-1">
          {error && (
            <div
              className="flex items-start gap-2.5 rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-xs text-destructive shadow-xs"
              role="alert"
            >
              <AlertTriangle className="h-4 w-4 shrink-0 mt-0.5" />
              <div className="flex flex-col gap-2">
                <span>{error}</span>
                <button
                  type="button"
                  className="inline-flex items-center gap-1.5 self-start rounded-md border border-input bg-background px-2.5 py-1 text-xs font-medium text-foreground shadow-2xs hover:bg-accent hover:text-accent-foreground transition-colors cursor-pointer focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
                  onClick={fetchVersions}
                >
                  <RefreshCw className="h-3 w-3" /> Thử lại
                </button>
              </div>
            </div>
          )}

          {isLoading ? (
            <div className="text-center py-8 text-muted-foreground text-xs flex flex-col items-center gap-2">
              <RefreshCw className="h-5 w-5 animate-spin" />
              <div>Đang tải danh sách phiên bản...</div>
            </div>
          ) : versions.length === 0 ? (
            <div className="text-center py-8 text-muted-foreground text-xs">
              Chưa có phiên bản nào được ghi nhận cho template này.
            </div>
          ) : (
            <div className="flex flex-col gap-2">
              {versions.map((ver) => {
                const isCurrent = ver.versionId === currentVersionId;
                return (
                  <div
                    key={ver.versionId}
                    className={cn(
                      "flex items-center justify-between gap-3 rounded-lg border p-3 transition-colors shadow-xs",
                      isCurrent
                        ? "border-primary/50 bg-primary/5 dark:bg-primary/10"
                        : "border-border/80 bg-card hover:border-border hover:bg-accent/40"
                    )}
                  >
                    <div className="flex flex-col gap-1 min-w-0">
                      <div className="flex items-center gap-2 flex-wrap">
                        <span className="font-mono text-sm font-bold text-foreground">
                          v{ver.versionNumber}
                        </span>
                        {isCurrent && (
                          <span className="inline-flex items-center rounded-full border border-emerald-500/40 bg-emerald-500/15 px-2 py-0.5 text-[11px] font-semibold text-emerald-600 dark:text-emerald-400">
                            Hiện tại
                          </span>
                        )}
                        <span className="text-xs text-muted-foreground flex items-center gap-1">
                          <Clock className="h-3 w-3" />
                          {formatDate(ver.createdAtUtc)}
                        </span>
                      </div>
                      <div className="text-xs text-muted-foreground truncate">
                        {ver.changeDescription || "(Không có mô tả thay đổi)"}
                      </div>
                    </div>

                    <div className="flex items-center gap-2 shrink-0">
                      <button
                        type="button"
                        className="inline-flex items-center justify-center rounded-md text-xs font-medium h-7 px-2.5 border border-input bg-background shadow-2xs hover:bg-accent hover:text-accent-foreground text-foreground transition-colors cursor-pointer focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
                        onClick={() =>
                          setViewingVersion(viewingVersion?.versionId === ver.versionId ? null : ver)
                        }
                        aria-label={`Xem nội dung phiên bản v${ver.versionNumber}`}
                      >
                        {viewingVersion?.versionId === ver.versionId ? "Thu gọn" : "Xem"}
                      </button>

                      {!isCurrent && (
                        <button
                          type="button"
                          className="inline-flex items-center justify-center rounded-md text-xs font-medium h-7 px-2.5 border border-amber-500/40 text-amber-600 dark:text-amber-400 bg-amber-500/10 hover:bg-amber-500/20 shadow-2xs transition-colors disabled:opacity-50 cursor-pointer focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
                          onClick={() => setRollbackTarget(ver)}
                          disabled={isRollingBack}
                          aria-label={`Khôi phục về phiên bản v${ver.versionNumber}`}
                        >
                          Khôi phục
                        </button>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
          )}

          {/* Chi tiết nội dung của phiên bản được chọn xem */}
          {viewingVersion && (
            <div className="mt-2 rounded-lg border border-border bg-muted/30 p-3.5 flex flex-col gap-2 shadow-xs">
              <div className="flex items-center justify-between text-xs font-semibold text-muted-foreground">
                <span>Nội dung template phiên bản v{viewingVersion.versionNumber}:</span>
                <button
                  type="button"
                  className="h-5 w-5 inline-flex items-center justify-center rounded hover:bg-accent hover:text-foreground text-muted-foreground cursor-pointer focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
                  onClick={() => setViewingVersion(null)}
                  aria-label="Đóng xem trước phiên bản"
                >
                  <X className="h-3.5 w-3.5" />
                </button>
              </div>
              <pre className="m-0 p-3 rounded-md bg-background border border-border/80 font-mono text-xs text-foreground leading-relaxed whitespace-pre-wrap break-words max-h-44 overflow-y-auto shadow-2xs">
                {viewingVersion.templateText}
              </pre>
            </div>
          )}

          {/* Modal xác nhận Rollback */}
          {rollbackTarget && (
            <div
              className="mt-2 rounded-lg border border-amber-500/50 bg-amber-500/10 p-4 flex flex-col gap-2.5 shadow-xs"
              role="alertdialog"
              aria-labelledby="rollback-confirm-title"
            >
              <div
                id="rollback-confirm-title"
                className="font-bold text-xs text-amber-600 dark:text-amber-400 flex items-center gap-1.5"
              >
                <AlertTriangle className="h-4 w-4 shrink-0" />
                Xác nhận khôi phục phiên bản về v{rollbackTarget.versionNumber}?
              </div>
              <div className="text-xs text-foreground leading-relaxed">
                Thao tác này sẽ chuyển con trỏ phiên bản hiện thời của template sang phiên bản v
                {rollbackTarget.versionNumber} ({formatDate(rollbackTarget.createdAtUtc)}).
                Các job trong hàng đợi đã snapshot sẽ không bị ảnh hưởng.
              </div>

              {rollbackError && (
                <div className="flex items-center gap-1.5 text-xs text-destructive font-medium">
                  <AlertTriangle className="size-3.5 shrink-0" />
                  <span>{rollbackError}</span>
                </div>
              )}

              <div className="flex items-center justify-end gap-2 pt-1">
                <button
                  type="button"
                  className="inline-flex items-center justify-center rounded-md text-xs font-medium h-7 px-3 border border-input bg-background shadow-2xs hover:bg-accent hover:text-accent-foreground text-foreground transition-colors disabled:opacity-50 cursor-pointer focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
                  onClick={() => setRollbackTarget(null)}
                  disabled={isRollingBack}
                >
                  Hủy bỏ
                </button>
                <button
                  type="button"
                  className="inline-flex items-center justify-center rounded-md text-xs font-medium h-7 px-3 bg-amber-500 hover:bg-amber-600 text-white shadow-2xs transition-colors disabled:opacity-50 cursor-pointer focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
                  onClick={handleConfirmRollback}
                  disabled={isRollingBack}
                >
                  {isRollingBack ? "Đang khôi phục..." : "Đồng ý khôi phục"}
                </button>
              </div>
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="flex items-center justify-end border-t border-border/80 px-5 py-3 bg-muted/20 shrink-0">
          <button
            type="button"
            className="inline-flex items-center justify-center rounded-md text-xs font-medium h-8 px-4 border border-input bg-background shadow-xs hover:bg-accent hover:text-accent-foreground text-foreground transition-colors cursor-pointer focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
            onClick={onClose}
            disabled={isRollingBack}
          >
            Đóng
          </button>
        </div>
      </div>
    </div>
  );
}
