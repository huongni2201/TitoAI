import React, { useState, useEffect, useRef } from "react";
import { projectApi, type SceneDto } from "./projectApi";
import type { ErrorDto } from "../../contracts/bridge";
import { IconClose, IconRefresh } from "../../components/Icons";

export interface SceneFormProps {
  isOpen: boolean;
  mode: "create" | "edit";
  workspaceId: string;
  projectId: string;
  scene?: SceneDto | null;
  onClose: () => void;
  onSuccess: () => void;
}

export function SceneForm({
  isOpen,
  mode,
  workspaceId,
  projectId,
  scene,
  onClose,
  onSuccess,
}: SceneFormProps) {
  const [name, setName] = useState("");
  const [promptText, setPromptText] = useState("");
  const [targetDurationSeconds, setTargetDurationSeconds] = useState<number>(15);
  const [status, setStatus] = useState("Draft");

  // Lưu trữ revision phục vụ kiểm soát xung đột đồng thời (optimistic concurrency)
  const [initialRevision, setInitialRevision] = useState<string | null>(null);

  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isReloading, setIsReloading] = useState(false);
  const [isConflict, setIsConflict] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});

  const nameInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (isOpen) {
      if (mode === "edit" && scene) {
        setName(scene.name || "");
        setPromptText(scene.promptText || "");
        setTargetDurationSeconds(scene.targetDurationSeconds || 15);
        setStatus(scene.status || "Draft");
        setInitialRevision(scene.revision);
      } else {
        setName("");
        setPromptText("");
        setTargetDurationSeconds(15);
        setStatus("Draft");
        setInitialRevision(null);
      }
      setIsConflict(false);
      setErrorMessage(null);
      setFieldErrors({});
      setIsSubmitting(false);

      setTimeout(() => {
        nameInputRef.current?.focus();
      }, 50);
    }
  }, [isOpen, mode, scene]);

  // Phím Escape
  useEffect(() => {
    if (!isOpen) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape" && !isSubmitting && !isReloading) {
        onClose();
      }
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [isOpen, isSubmitting, isReloading, onClose]);

  if (!isOpen) return null;

  const validate = (): boolean => {
    const errors: Record<string, string> = {};
    const trimmed = name.trim();
    if (!trimmed) {
      errors.name = "Vui lòng nhập tên phân cảnh.";
    } else if (trimmed.length > 100) {
      errors.name = "Tên phân cảnh không được vượt quá 100 ký tự.";
    }

    if (!targetDurationSeconds || targetDurationSeconds < 1 || targetDurationSeconds > 600) {
      errors.duration = "Thời lượng phân cảnh hợp lệ từ 1 đến 600 giây.";
    }

    setFieldErrors(errors);
    return Object.keys(errors).length === 0;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (isSubmitting || isReloading) return;

    if (!validate()) return;

    setIsSubmitting(true);
    setErrorMessage(null);
    setIsConflict(false);

    const trimmedName = name.trim();
    const trimmedPrompt = promptText.trim() || null;

    try {
      if (mode === "create") {
        await projectApi.createScene({
          workspaceId,
          projectId,
          name: trimmedName,
          promptText: trimmedPrompt,
          targetDurationSeconds,
        });
        onSuccess();
      } else if (mode === "edit" && scene) {
        // GỬI KÈM expectedRevision: revision ban đầu
        await projectApi.updateScene({
          workspaceId,
          projectId,
          sceneId: scene.sceneId,
          expectedRevision: initialRevision,
          name: trimmedName,
          promptText: trimmedPrompt,
          targetDurationSeconds,
          status,
        });
        onSuccess();
      }
    } catch (err: unknown) {
      const errorDto = err as ErrorDto;
      console.error("[SceneForm] Lỗi lưu Cảnh:", errorDto);

      if (errorDto?.code === "CONCURRENCY_CONFLICT") {
        // QUY TẮC BẮT BUỘC: Hiển thị cảnh báo xung đột phiên, GIỮ NGUYÊN FORM INPUT
        setIsConflict(true);
        setErrorMessage(
          "Cảnh này đã bị chỉnh sửa bởi một phiên khác. Vui lòng tải lại dữ liệu mới nhất trước khi lưu.",
        );
      } else {
        setErrorMessage(
          errorDto?.message || "Đã xảy ra lỗi khi lưu thông tin phân cảnh. Vui lòng thử lại.",
        );
      }
    } finally {
      setIsSubmitting(false);
    }
  };

  // Nạp lại phiên bản mới nhất từ Host để giải quyết xung đột mà không mất nội dung đã nhập
  const handleReloadLatest = async () => {
    if (!scene) return;
    setIsReloading(true);
    setErrorMessage(null);

    try {
      const latestScene = await projectApi.getScene(workspaceId, projectId, scene.sceneId);
      // Cập nhật lại initialRevision thành revision mới nhất từ server
      setInitialRevision(latestScene.revision);
      setIsConflict(false);
      // Hiển thị thông báo hướng dẫn người dùng
      setErrorMessage(null);
    } catch (err: unknown) {
      const errorDto = err as ErrorDto;
      setErrorMessage(
        `Không thể nạp dữ liệu mới nhất: ${errorDto?.message || "Lỗi kết nối máy chủ"}`,
      );
    } finally {
      setIsReloading(false);
    }
  };

  return (
    <div
      className="modal-backdrop"
      role="presentation"
      onClick={(e) => {
        if (e.target === e.currentTarget && !isSubmitting && !isReloading) {
          onClose();
        }
      }}
    >
      <div
        className="modal-container"
        role="dialog"
        aria-modal="true"
        aria-labelledby="scene-form-title"
        style={{ maxWidth: 580 }}
      >
        <div className="modal-header">
          <div className="modal-title-group">
            <h2 id="scene-form-title">
              {mode === "create" ? "Thêm Phân Cảnh Mới" : `Chỉnh Sửa Phân Cảnh #${scene?.sceneSequence || ""}`}
            </h2>
            <p className="modal-subtitle">
              {mode === "create"
                ? "Thiết lập kịch bản lời dẫn, thời lượng và prompt tạo video."
                : `Revision hiện thời: ${initialRevision || "—"}`}
            </p>
          </div>
          <button
            type="button"
            className="btn-icon"
            onClick={onClose}
            disabled={isSubmitting || isReloading}
            aria-label="Đóng"
          >
            <IconClose size={18} />
          </button>
        </div>

        <form onSubmit={handleSubmit}>
          <div className="modal-body" style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
            {/* Banner Cảnh Báo Xung Đột Đồng Thời (CONCURRENCY_CONFLICT) */}
            {isConflict && (
              <div
                className="notice-box"
                style={{
                  backgroundColor: "rgba(239, 68, 68, 0.15)",
                  border: "1px solid var(--status-error)",
                  color: "var(--status-error)",
                  flexDirection: "column",
                  gap: "0.5rem",
                }}
                role="alert"
              >
                <div style={{ display: "flex", alignItems: "center", gap: "0.5rem", fontWeight: 700 }}>
                  <span>⚠️</span> Xung đột phiên chỉnh sửa đồng thời (Concurrency Conflict)!
                </div>
                <div style={{ fontSize: "0.85rem", lineHeight: 1.4 }}>
                  Cảnh này đã bị chỉnh sửa bởi một phiên khác. Vui lòng tải lại dữ liệu mới nhất trước khi lưu.
                  (Nội dung bạn vừa nhập ở dưới vẫn được giữ nguyên đầy đủ).
                </div>
                <div>
                  <button
                    type="button"
                    className="btn btn-secondary btn-sm"
                    style={{ backgroundColor: "var(--surface-card)", color: "var(--text-primary)" }}
                    onClick={handleReloadLatest}
                    disabled={isReloading}
                  >
                    <IconRefresh size={14} className={isReloading ? "spin" : ""} /> Tải lại dữ liệu mới nhất từ máy chủ
                  </button>
                </div>
              </div>
            )}

            {/* Thông báo lỗi thông thường */}
            {errorMessage && !isConflict && (
              <div
                className="notice-box"
                style={{
                  backgroundColor: "rgba(239, 68, 68, 0.12)",
                  border: "1px solid var(--status-error)",
                  color: "var(--status-error)",
                }}
                role="alert"
              >
                <span>⚠️</span>
                <div>{errorMessage}</div>
              </div>
            )}

            <div style={{ display: "grid", gridTemplateColumns: "1fr 140px", gap: "0.75rem" }}>
              <div className="form-group">
                <label htmlFor="scene-name">
                  Tiêu đề phân cảnh <span style={{ color: "var(--status-error)" }}>*</span>
                </label>
                <input
                  id="scene-name"
                  ref={nameInputRef}
                  type="text"
                  className="form-control"
                  placeholder="Ví dụ: Giới thiệu sản phẩm, Cận cảnh áo len…"
                  value={name}
                  maxLength={100}
                  disabled={isSubmitting || isReloading}
                  onChange={(e) => {
                    setName(e.target.value);
                    if (fieldErrors.name) setFieldErrors((prev) => ({ ...prev, name: "" }));
                  }}
                  aria-invalid={!!fieldErrors.name}
                />
                {fieldErrors.name && (
                  <span className="field-hint" style={{ color: "var(--status-error)" }}>
                    {fieldErrors.name}
                  </span>
                )}
              </div>

              <div className="form-group">
                <label htmlFor="scene-duration">
                  Thời lượng (giây) <span style={{ color: "var(--status-error)" }}>*</span>
                </label>
                <input
                  id="scene-duration"
                  type="number"
                  min={1}
                  max={600}
                  className="form-control"
                  placeholder="15"
                  value={targetDurationSeconds}
                  disabled={isSubmitting || isReloading}
                  onChange={(e) => {
                    setTargetDurationSeconds(Number(e.target.value));
                    if (fieldErrors.duration) setFieldErrors((prev) => ({ ...prev, duration: "" }));
                  }}
                  aria-invalid={!!fieldErrors.duration}
                />
                {fieldErrors.duration && (
                  <span className="field-hint" style={{ color: "var(--status-error)" }}>
                    {fieldErrors.duration}
                  </span>
                )}
              </div>
            </div>

            <div className="form-group">
              <label htmlFor="scene-prompt">
                Lời dẫn (Narration) / Prompt nháp cho cảnh
              </label>
              <textarea
                id="scene-prompt"
                className="form-control"
                style={{ height: 110, padding: "0.5rem 0.75rem", resize: "vertical" }}
                placeholder="Nhập lời dẫn thu âm hoặc mô tả hành động nhân vật cho AI sinh prompt video…"
                value={promptText}
                maxLength={2000}
                disabled={isSubmitting || isReloading}
                onChange={(e) => setPromptText(e.target.value)}
              />
              <div style={{ display: "flex", justifyContent: "space-between", marginTop: "0.25rem" }}>
                <span className="field-hint">Mô tả càng chi tiết sẽ giúp sinh video chuyển động mượt mà hơn.</span>
                <span className="field-hint">{promptText.length} / 2000</span>
              </div>
            </div>

            {mode === "edit" && (
              <div className="form-group">
                <label htmlFor="scene-status">Trạng thái phân cảnh</label>
                <select
                  id="scene-status"
                  className="form-control"
                  value={status}
                  onChange={(e) => setStatus(e.target.value)}
                  disabled={isSubmitting || isReloading}
                >
                  <option value="Draft">Chưa bắt đầu (Draft)</option>
                  <option value="PromptReady">Đã có Prompt (PromptReady)</option>
                  <option value="Generating">Đang tạo video (Generating)</option>
                  <option value="Completed">Hoàn thành (Completed)</option>
                </select>
              </div>
            )}
          </div>

          <div className="modal-footer">
            <span className="footer-status">
              {isSubmitting
                ? "Đang lưu cảnh…"
                : isReloading
                ? "Đang nạp dữ liệu mới…"
                : "Bảo vệ xung đột đồng thời an toàn."}
            </span>
            <div style={{ display: "flex", gap: "0.5rem" }}>
              <button
                type="button"
                className="btn btn-secondary"
                onClick={onClose}
                disabled={isSubmitting || isReloading}
              >
                Hủy
              </button>
              <button
                type="submit"
                className="btn btn-primary"
                disabled={isSubmitting || isReloading}
              >
                {isSubmitting ? (
                  <>
                    <IconRefresh size={16} className="spin" /> Đang lưu…
                  </>
                ) : mode === "create" ? (
                  "Thêm Phân Cảnh"
                ) : (
                  "Lưu Phân Cảnh"
                )}
              </button>
            </div>
          </div>
        </form>
      </div>
    </div>
  );
}
