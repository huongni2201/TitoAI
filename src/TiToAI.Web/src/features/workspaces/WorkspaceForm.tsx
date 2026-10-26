import React, { useState, useEffect, useRef } from "react";
import { workspaceApi, type WorkspaceSummaryDto, type WorkspaceDetailDto } from "./workspaceApi";
import type { ErrorDto } from "../../contracts/bridge";
import { IconClose, IconRefresh } from "../../components/Icons";

export interface WorkspaceFormProps {
  isOpen: boolean;
  mode: "create" | "edit";
  workspace?: WorkspaceDetailDto | WorkspaceSummaryDto | null;
  onClose: () => void;
  onSuccess: (savedWorkspaceId: string) => void;
}

export function WorkspaceForm({
  isOpen,
  mode,
  workspace,
  onClose,
  onSuccess,
}: WorkspaceFormProps) {
  const [name, setName] = useState("");
  const [channelHandle, setChannelHandle] = useState("");
  const [topic, setTopic] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});

  const nameInputRef = useRef<HTMLInputElement>(null);
  const dialogRef = useRef<HTMLDivElement>(null);

  // Khởi tạo giá trị khi mở form
  useEffect(() => {
    if (isOpen) {
      if (mode === "edit" && workspace) {
        setName(workspace.name || "");
        setChannelHandle(workspace.channelHandle || "");
        setTopic(workspace.topic || "");
      } else {
        setName("");
        setChannelHandle("");
        setTopic("");
      }
      setErrorMessage(null);
      setFieldErrors({});
      setIsSubmitting(false);

      // Focus vào ô input đầu tiên
      setTimeout(() => {
        nameInputRef.current?.focus();
      }, 50);
    }
  }, [isOpen, mode, workspace]);

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

  // Xử lý tự động chuẩn hóa handle (@...)
  const handleHandleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    let val = e.target.value;
    if (val && !val.startsWith("@")) {
      val = "@" + val;
    }
    setChannelHandle(val);
    if (fieldErrors.channelHandle) {
      setFieldErrors((prev) => ({ ...prev, channelHandle: "" }));
    }
  };

  const handleNameChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setName(e.target.value);
    if (fieldErrors.name) {
      setFieldErrors((prev) => ({ ...prev, name: "" }));
    }
  };

  const validate = (): boolean => {
    const errors: Record<string, string> = {};
    const trimmedName = name.trim();
    const trimmedHandle = channelHandle.trim();

    if (!trimmedName) {
      errors.name = "Vui lòng nhập tên kênh.";
    } else if (trimmedName.length > 100) {
      errors.name = "Tên kênh không được vượt quá 100 ký tự.";
    }

    if (!trimmedHandle || trimmedHandle === "@") {
      errors.channelHandle = "Vui lòng nhập handle kênh (ví dụ: @thoitrangnuxinh).";
    } else if (trimmedHandle.length > 60) {
      errors.channelHandle = "Handle không được vượt quá 60 ký tự.";
    }

    setFieldErrors(errors);
    return Object.keys(errors).length === 0;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (isSubmitting) return;

    if (!validate()) {
      return;
    }

    setIsSubmitting(true);
    setErrorMessage(null);

    const trimmedName = name.trim();
    let normalizedHandle = channelHandle.trim();
    if (!normalizedHandle.startsWith("@")) {
      normalizedHandle = "@" + normalizedHandle;
    }
    const trimmedTopic = topic.trim();

    try {
      if (mode === "create") {
        const result = await workspaceApi.create({
          name: trimmedName,
          channelHandle: normalizedHandle,
          topic: trimmedTopic,
        });
        onSuccess(result.workspaceId);
      } else if (mode === "edit" && workspace) {
        const result = await workspaceApi.update({
          workspaceId: workspace.workspaceId,
          name: trimmedName,
          channelHandle: normalizedHandle,
          topic: trimmedTopic,
        });
        onSuccess(result.workspaceId);
      }
    } catch (err: unknown) {
      const errorDto = err as ErrorDto;
      console.error("[WorkspaceForm] Lỗi lưu Workspace:", errorDto);
      // Giữ nguyên form, không đóng form khi lỗi
      setErrorMessage(
        errorDto?.message || "Đã xảy ra lỗi khi lưu thông tin kênh. Vui lòng thử lại.",
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
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="workspace-form-title"
        style={{ maxWidth: 520 }}
      >
        <div className="modal-header">
          <div className="modal-title-group">
            <h2 id="workspace-form-title">
              {mode === "create" ? "Tạo Workspace Kênh Mới" : "Chỉnh Sửa Workspace"}
            </h2>
            <p className="modal-subtitle">
              {mode === "create"
                ? "Thiết lập kênh TikTok mới để quản lý dự án video và prompt."
                : `Cập nhật thông tin nhận diện cho kênh ${workspace?.name || ""}.`}
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
              <label htmlFor="ws-name">
                Tên kênh hiển thị <span style={{ color: "var(--status-error)" }}>*</span>
              </label>
              <input
                id="ws-name"
                ref={nameInputRef}
                type="text"
                className="form-control"
                placeholder="Ví dụ: Thời Trang Nữ Xinh, Sức Khỏe & Đời Sống"
                value={name}
                maxLength={100}
                disabled={isSubmitting}
                onChange={handleNameChange}
                aria-invalid={!!fieldErrors.name}
              />
              {fieldErrors.name ? (
                <span className="field-hint" style={{ color: "var(--status-error)" }}>
                  {fieldErrors.name}
                </span>
              ) : (
                <span className="field-hint">Tối đa 100 ký tự.</span>
              )}
            </div>

            <div className="form-group">
              <label htmlFor="ws-handle">
                Handle kênh (@) <span style={{ color: "var(--status-error)" }}>*</span>
              </label>
              <input
                id="ws-handle"
                type="text"
                className="form-control"
                placeholder="@thoitrangnuxinh"
                value={channelHandle}
                maxLength={60}
                disabled={isSubmitting}
                onChange={handleHandleChange}
                aria-invalid={!!fieldErrors.channelHandle}
              />
              {fieldErrors.channelHandle ? (
                <span className="field-hint" style={{ color: "var(--status-error)" }}>
                  {fieldErrors.channelHandle}
                </span>
              ) : (
                <span className="field-hint">
                  Định danh TikTok của kênh (tự động thêm tiền tố @ nếu thiếu).
                </span>
              )}
            </div>

            <div className="form-group">
              <label htmlFor="ws-topic">Chủ đề / Niche nội dung</label>
              <input
                id="ws-topic"
                type="text"
                className="form-control"
                placeholder="Ví dụ: Thời trang nữ, review đồ xinh, gia dụng tiện ích"
                value={topic}
                maxLength={150}
                disabled={isSubmitting}
                onChange={(e) => setTopic(e.target.value)}
              />
              <span className="field-hint">
                Chủ đề định hướng để AI hỗ trợ gợi ý kịch bản và prompt phù hợp.
              </span>
            </div>
          </div>

          <div className="modal-footer">
            <span className="footer-status">
              {isSubmitting ? "Đang xử lý…" : "Dữ liệu được lưu trữ cục bộ."}
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
                  "Tạo Workspace"
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
