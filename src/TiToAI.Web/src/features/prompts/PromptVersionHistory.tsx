import { useState, useEffect, useCallback, useRef } from "react";
import {
  promptApi,
  type PromptTemplateDto,
  type PromptVersionDto,
  type IPromptTransport,
  defaultPromptTransport,
} from "./promptApi.ts";
import { IconClose, IconRefresh } from "../../components/Icons.tsx";

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
      const msg = err && typeof err === "object" && "message" in err
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
      const msg = err && typeof err === "object" && "message" in err
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
      className="modal-backdrop"
      style={{
        position: "fixed",
        inset: 0,
        backgroundColor: "var(--backdrop-color)",
        backdropFilter: "blur(2px)",
        zIndex: 1000,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        padding: "1rem",
      }}
      role="dialog"
      aria-modal="true"
      aria-labelledby="version-history-title"
    >
      <div
        ref={modalRef}
        className="modal-container"
        style={{
          width: "100%",
          maxWidth: "680px",
          maxHeight: "90vh",
          backgroundColor: "var(--surface-card)",
          border: "1px solid var(--border)",
          borderRadius: "var(--radius-lg)",
          boxShadow: "var(--shadow-modal)",
          display: "flex",
          flexDirection: "column",
          overflow: "hidden",
        }}
      >
        {/* Header */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            padding: "1rem 1.25rem",
            borderBottom: "1px solid var(--border-subtle)",
          }}
        >
          <div>
            <h2
              id="version-history-title"
              style={{ margin: 0, fontSize: "1.1rem", fontWeight: 700, color: "var(--text-primary)" }}
            >
              Lịch sử phiên bản template
            </h2>
            <p style={{ margin: "0.25rem 0 0 0", fontSize: "0.8rem", color: "var(--text-muted)" }}>
              ID: {templateId} · Các phiên bản được lưu trữ bất biến đơn điệu
            </p>
          </div>
          <button
            type="button"
            className="btn-icon"
            onClick={onClose}
            disabled={isRollingBack}
            aria-label="Đóng hộp thoại lịch sử"
          >
            <IconClose size={18} />
          </button>
        </div>

        {/* Content body */}
        <div
          style={{
            padding: "1.25rem",
            overflowY: "auto",
            display: "flex",
            flexDirection: "column",
            gap: "1rem",
            flex: 1,
          }}
        >
          {error && (
            <div className="prompt-alert prompt-alert-error" role="alert">
              <div>⚠️</div>
              <div style={{ display: "flex", flexDirection: "column", gap: "0.25rem" }}>
                <span>{error}</span>
                <button
                  type="button"
                  className="btn btn-secondary btn-sm"
                  style={{ alignSelf: "flex-start" }}
                  onClick={fetchVersions}
                >
                  <IconRefresh size={12} /> Thử lại
                </button>
              </div>
            </div>
          )}

          {isLoading ? (
            <div style={{ textAlign: "center", padding: "2rem", color: "var(--text-muted)" }}>
              <IconRefresh size={20} className="spin" style={{ marginBottom: "0.5rem" }} />
              <div>Đang tải danh sách phiên bản...</div>
            </div>
          ) : versions.length === 0 ? (
            <div style={{ textAlign: "center", padding: "2rem", color: "var(--text-muted)" }}>
              Chưa có phiên bản nào được ghi nhận cho template này.
            </div>
          ) : (
            <div style={{ display: "flex", flexDirection: "column", gap: "0.5rem" }}>
              {versions.map((ver) => {
                const isCurrent = ver.versionId === currentVersionId;
                return (
                  <div
                    key={ver.versionId}
                    className={`version-item ${isCurrent ? "current" : ""}`}
                  >
                    <div className="version-meta">
                      <div className="version-header-line">
                        <span className="version-num">v{ver.versionNumber}</span>
                        {isCurrent && (
                          <span
                            className="badge badge-saved"
                            style={{ fontSize: "0.7rem", padding: "0.1rem 0.45rem" }}
                          >
                            Hiện tại
                          </span>
                        )}
                        <span className="version-time">{formatDate(ver.createdAtUtc)}</span>
                      </div>
                      <div className="version-desc">
                        {ver.changeDescription || "(Không có mô tả thay đổi)"}
                      </div>
                    </div>

                    <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
                      <button
                        type="button"
                        className="btn btn-secondary btn-sm"
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
                          className="btn btn-secondary btn-sm"
                          style={{
                            color: "var(--status-warning)",
                            borderColor: "rgba(245, 158, 11, 0.4)",
                          }}
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
            <div
              style={{
                marginTop: "0.5rem",
                padding: "0.85rem",
                backgroundColor: "var(--surface-card-subtle)",
                border: "1px solid var(--border)",
                borderRadius: "var(--radius-md)",
                display: "flex",
                flexDirection: "column",
                gap: "0.5rem",
              }}
            >
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                  fontSize: "0.8rem",
                  fontWeight: 600,
                  color: "var(--text-secondary)",
                }}
              >
                <span>Nội dung template phiên bản v{viewingVersion.versionNumber}:</span>
                <button
                  type="button"
                  className="btn-icon"
                  style={{ width: "24px", height: "24px" }}
                  onClick={() => setViewingVersion(null)}
                  aria-label="Đóng xem trước phiên bản"
                >
                  <IconClose size={14} />
                </button>
              </div>
              <pre
                style={{
                  margin: 0,
                  padding: "0.75rem",
                  backgroundColor: "var(--surface-card)",
                  border: "1px solid var(--border-subtle)",
                  borderRadius: "var(--radius-sm)",
                  fontFamily: "monospace",
                  fontSize: "0.8rem",
                  lineHeight: 1.45,
                  whiteSpace: "pre-wrap",
                  wordBreak: "break-word",
                  maxHeight: "180px",
                  overflowY: "auto",
                  color: "var(--text-primary)",
                }}
              >
                {viewingVersion.templateText}
              </pre>
            </div>
          )}

          {/* Modal xác nhận Rollback */}
          {rollbackTarget && (
            <div
              style={{
                marginTop: "0.5rem",
                padding: "1rem",
                backgroundColor: "rgba(245, 158, 11, 0.08)",
                border: "1px solid var(--status-warning)",
                borderRadius: "var(--radius-md)",
                display: "flex",
                flexDirection: "column",
                gap: "0.65rem",
              }}
              role="alertdialog"
              aria-labelledby="rollback-confirm-title"
            >
              <div
                id="rollback-confirm-title"
                style={{ fontWeight: 700, color: "var(--status-warning)", fontSize: "0.9rem" }}
              >
                Xác nhận khôi phục phiên bản về v{rollbackTarget.versionNumber}?
              </div>
              <div style={{ fontSize: "0.8rem", color: "var(--text-primary)", lineHeight: 1.4 }}>
                Thao tác này sẽ chuyển con trỏ phiên bản hiện thời của template sang phiên bản v
                {rollbackTarget.versionNumber} ({formatDate(rollbackTarget.createdAtUtc)}).
                Các job trong hàng đợi đã snapshot sẽ không bị ảnh hưởng.
              </div>

              {rollbackError && (
                <div style={{ color: "var(--status-error)", fontSize: "0.8rem" }}>
                  ⚠️ {rollbackError}
                </div>
              )}

              <div style={{ display: "flex", alignItems: "center", gap: "0.5rem", justifyContent: "flex-end" }}>
                <button
                  type="button"
                  className="btn btn-secondary btn-sm"
                  onClick={() => setRollbackTarget(null)}
                  disabled={isRollingBack}
                >
                  Hủy bỏ
                </button>
                <button
                  type="button"
                  className="btn btn-primary btn-sm"
                  style={{ backgroundColor: "var(--status-warning)", borderColor: "var(--status-warning)" }}
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
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "flex-end",
            padding: "0.75rem 1.25rem",
            borderTop: "1px solid var(--border-subtle)",
            backgroundColor: "var(--surface-card-subtle)",
          }}
        >
          <button
            type="button"
            className="btn btn-secondary btn-sm"
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
