import React, { useState, useEffect, useRef } from "react";
import {
  projectApi,
  type ProjectDetailDto,
  type ProjectSummaryDto,
} from "./projectApi";
import type { ErrorDto } from "../../contracts/bridge";
import { IconClose, IconRefresh } from "../../components/Icons";

export interface ProjectFormProps {
  isOpen: boolean;
  mode: "create" | "edit";
  workspaceId: string;
  project?: ProjectDetailDto | ProjectSummaryDto | null;
  onClose: () => void;
  onSuccess: (projectId: string) => void;
}

export function ProjectForm({
  isOpen,
  mode,
  workspaceId,
  project,
  onClose,
  onSuccess,
}: ProjectFormProps) {
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [status, setStatus] = useState("Draft");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});

  const nameInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (isOpen) {
      if (mode === "edit" && project) {
        setName(project.name || "");
        setDescription(project.description || "");
        setStatus(project.status || "Draft");
      } else {
        setName("");
        setDescription("");
        setStatus("Draft");
      }
      setErrorMessage(null);
      setFieldErrors({});
      setIsSubmitting(false);

      setTimeout(() => {
        nameInputRef.current?.focus();
      }, 50);
    }
  }, [isOpen, mode, project]);

  // Đóng bằng phím Escape
  useEffect(() => {
    if (!isOpen) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape" && !isSubmitting) {
        onClose();
      }
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [isOpen, isSubmitting, onClose]);

  if (!isOpen) return null;

  const validate = (): boolean => {
    const errors: Record<string, string> = {};
    const trimmed = name.trim();
    if (!trimmed) {
      errors.name = "Vui lòng nhập tên dự án video.";
    } else if (trimmed.length > 150) {
      errors.name = "Tên dự án không được vượt quá 150 ký tự.";
    }

    setFieldErrors(errors);
    return Object.keys(errors).length === 0;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (isSubmitting) return;

    if (!validate()) return;

    setIsSubmitting(true);
    setErrorMessage(null);

    const trimmedName = name.trim();
    const trimmedDesc = description.trim() || null;

    try {
      if (mode === "create") {
        const newProjectId = await projectApi.createProject({
          workspaceId,
          name: trimmedName,
          description: trimmedDesc,
        });
        onSuccess(newProjectId);
      } else if (mode === "edit" && project) {
        const updated = await projectApi.updateProject({
          workspaceId,
          projectId: project.projectId,
          name: trimmedName,
          description: trimmedDesc,
          status,
        });
        onSuccess(updated.projectId);
      }
    } catch (err: unknown) {
      const errorDto = err as ErrorDto;
      console.error("[ProjectForm] Lỗi lưu dự án:", errorDto);
      setErrorMessage(
        errorDto?.message || "Đã xảy ra lỗi khi lưu thông tin dự án. Vui lòng thử lại.",
      );
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div
      className="modal-backdrop"
      role="presentation"
      onClick={(e) => {
        if (e.target === e.currentTarget && !isSubmitting) {
          onClose();
        }
      }}
    >
      <div
        className="modal-container"
        role="dialog"
        aria-modal="true"
        aria-labelledby="project-form-title"
        style={{ maxWidth: 540 }}
      >
        <div className="modal-header">
          <div className="modal-title-group">
            <h2 id="project-form-title">
              {mode === "create" ? "Tạo Dự Án Video Mới" : "Chỉnh Sửa Dự Án"}
            </h2>
            <p className="modal-subtitle">
              {mode === "create"
                ? "Dự án chứa kịch bản phân cảnh và tài nguyên video hoàn chỉnh."
                : `Cập nhật thông tin dự án ${project?.name || ""}.`}
            </p>
          </div>
          <button
            type="button"
            className="btn-icon"
            onClick={onClose}
            disabled={isSubmitting}
            aria-label="Đóng hộp thoại"
          >
            <IconClose size={18} />
          </button>
        </div>

        <form onSubmit={handleSubmit}>
          <div className="modal-body" style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
            {errorMessage && (
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

            <div className="form-group">
              <label htmlFor="prj-name">
                Tiêu đề dự án <span style={{ color: "var(--status-error)" }}>*</span>
              </label>
              <input
                id="prj-name"
                ref={nameInputRef}
                type="text"
                className="form-control"
                placeholder="Ví dụ: Outfit Thu Đông - Áo Len Xinh Xắn"
                value={name}
                maxLength={150}
                disabled={isSubmitting}
                onChange={(e) => {
                  setName(e.target.value);
                  if (fieldErrors.name) setFieldErrors((prev) => ({ ...prev, name: "" }));
                }}
                aria-invalid={!!fieldErrors.name}
              />
              {fieldErrors.name ? (
                <span className="field-hint" style={{ color: "var(--status-error)" }}>
                  {fieldErrors.name}
                </span>
              ) : (
                <span className="field-hint">Tối đa 150 ký tự.</span>
              )}
            </div>

            <div className="form-group">
              <label htmlFor="prj-desc">Mô tả tóm tắt nội dung</label>
              <textarea
                id="prj-desc"
                className="form-control"
                style={{ height: 80, padding: "0.5rem 0.75rem", resize: "vertical" }}
                placeholder="Tóm tắt ý tưởng, chủ đề hoặc thông điệp chính của video…"
                value={description}
                maxLength={500}
                disabled={isSubmitting}
                onChange={(e) => setDescription(e.target.value)}
              />
              <span className="field-hint">Tùy chọn, tối đa 500 ký tự.</span>
            </div>

            {mode === "edit" && (
              <div className="form-group">
                <label htmlFor="prj-status">Trạng thái sản xuất</label>
                <select
                  id="prj-status"
                  className="form-control"
                  value={status}
                  onChange={(e) => setStatus(e.target.value)}
                  disabled={isSubmitting}
                >
                  <option value="Draft">Nháp (Draft)</option>
                  <option value="InProgress">Đang sản xuất (InProgress)</option>
                  <option value="Completed">Hoàn thành (Completed)</option>
                </select>
              </div>
            )}
          </div>

          <div className="modal-footer">
            <span className="footer-status">
              {isSubmitting ? "Đang xử lý…" : "Lưu vào cơ sở dữ liệu SQLite cục bộ."}
            </span>
            <div style={{ display: "flex", gap: "0.5rem" }}>
              <button
                type="button"
                className="btn btn-secondary"
                onClick={onClose}
                disabled={isSubmitting}
              >
                Hủy
              </button>
              <button
                type="submit"
                className="btn btn-primary"
                disabled={isSubmitting}
              >
                {isSubmitting ? (
                  <>
                    <IconRefresh size={16} className="spin" /> Đang lưu…
                  </>
                ) : mode === "create" ? (
                  "Tạo Dự án"
                ) : (
                  "Lưu thay đổi"
                )}
              </button>
            </div>
          </div>
        </form>
      </div>
    </div>
  );
}
