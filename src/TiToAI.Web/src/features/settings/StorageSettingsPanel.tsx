import React, { useState, useEffect, useCallback } from "react";
import { IconInfo, IconCheck } from "../../components/Icons";
import {
  storageSettingsApi,
  type SettingsDto,
  type SettingsSaveRequest,
} from "./storageSettingsApi";

export interface StorageSettingsPanelProps {
  onClose?: () => void;
}

export function StorageSettingsPanel({ onClose }: StorageSettingsPanelProps) {
  const [savedSettings, setSavedSettings] = useState<SettingsDto | null>(null);
  const [documentsPath, setDocumentsPath] = useState<string>("");
  const [imagesPath, setImagesPath] = useState<string>("");
  const [videosPath, setVideosPath] = useState<string>("");

  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});
  const [generalError, setGeneralError] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [isSaving, setIsSaving] = useState<boolean>(false);
  const [isBrowsing, setIsBrowsing] = useState<string | null>(null);

  // Nạp cấu hình cài đặt ban đầu từ Host
  const loadSettings = useCallback(async () => {
    setIsLoading(true);
    setGeneralError(null);
    try {
      const data = await storageSettingsApi.getSettings();
      setSavedSettings(data);
      setDocumentsPath(data.documentsPath);
      setImagesPath(data.imagesPath);
      setVideosPath(data.videosPath);
      setFieldErrors({});
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : String(err);
      setGeneralError(`Không thể nạp cấu hình cài đặt: ${message}`);
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadSettings();
  }, [loadSettings]);

  // Kiểm tra xem người dùng có thay đổi đường dẫn nào so với bản đã lưu hay không
  const isDirty =
    savedSettings !== null &&
    (documentsPath !== savedSettings.documentsPath ||
      imagesPath !== savedSettings.imagesPath ||
      videosPath !== savedSettings.videosPath);

  // Mở hộp thoại chọn thư mục Windows an toàn
  const handleBrowse = async (target: "documents" | "images" | "videos") => {
    setIsBrowsing(target);
    setGeneralError(null);
    setSuccessMessage(null);
    try {
      const currentPath =
        target === "documents"
          ? documentsPath
          : target === "images"
            ? imagesPath
            : videosPath;

      const selected = await storageSettingsApi.browseFolder(target, currentPath);
      if (selected) {
        if (target === "documents") setDocumentsPath(selected);
        if (target === "images") setImagesPath(selected);
        if (target === "videos") setVideosPath(selected);

        // Xóa thông báo lỗi của trường vừa chọn lại thư mục
        const fieldKey =
          target === "documents"
            ? "documentsPath"
            : target === "images"
              ? "imagesPath"
              : "videosPath";

        setFieldErrors((prev) => {
          const next = { ...prev };
          delete next[fieldKey];
          return next;
        });
      }
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : String(err);
      setGeneralError(`Lỗi khi mở hộp thoại chọn thư mục: ${message}`);
    } finally {
      setIsBrowsing(null);
    }
  };

  // Xác thực nhanh và Lưu cài đặt đồng thời vào Host
  const handleSave = async (e?: React.FormEvent) => {
    if (e) e.preventDefault();
    if (isSaving || isLoading) return;

    setIsSaving(true);
    setGeneralError(null);
    setSuccessMessage(null);
    setFieldErrors({});

    try {
      const request: SettingsSaveRequest = {
        documentsPath: documentsPath.trim(),
        imagesPath: imagesPath.trim(),
        videosPath: videosPath.trim(),
        imageProvider: savedSettings?.imageProvider ?? null,
        scriptProvider: savedSettings?.scriptProvider ?? null,
        videoPromptProvider: savedSettings?.videoPromptProvider ?? null,
      };

      const updated = await storageSettingsApi.saveSettings(request);
      setSavedSettings(updated);
      setDocumentsPath(updated.documentsPath);
      setImagesPath(updated.imagesPath);
      setVideosPath(updated.videosPath);
      setFieldErrors({});
      setSuccessMessage("Đã lưu thành công cấu hình thư mục lưu trữ!");
    } catch (err: any) {
      // Bắt lỗi từ Envelope Error nếu Host trả về VALIDATION_FAILED
      if (err && typeof err === "object" && err.fieldErrors) {
        setFieldErrors(err.fieldErrors);
      } else {
        const message = err instanceof Error ? err.message : String(err);
        setGeneralError(message || "Không thể lưu cài đặt. Vui lòng kiểm tra lại quyền ghi hoặc đường dẫn.");
      }
    } finally {
      setIsSaving(false);
    }
  };

  // Hủy bản nháp và hoàn tác về cấu hình đã lưu gần nhất
  const handleCancel = () => {
    if (savedSettings) {
      setDocumentsPath(savedSettings.documentsPath);
      setImagesPath(savedSettings.imagesPath);
      setVideosPath(savedSettings.videosPath);
    }
    setFieldErrors({});
    setGeneralError(null);
    setSuccessMessage(null);
  };

  return (
    <div
      className="settings-section"
      role="tabpanel"
      id="panel-storage"
      aria-labelledby="tab-storage"
    >
      <div className="section-header">
        <h3>Thư mục lưu trữ độc lập</h3>
        <p>
          Cấu hình ba thư mục riêng biệt trên ổ đĩa để lưu trữ tài liệu xuất, ảnh AI
          và video thành phẩm.
        </p>
      </div>

      {isLoading ? (
        <div style={{ padding: "2rem", textAlign: "center", color: "var(--text-muted)" }}>
          Đang nạp thông tin cấu hình lưu trữ...
        </div>
      ) : (
        <form onSubmit={handleSave} noValidate>
          {/* Thông báo thành công */}
          {successMessage && (
            <div
              className="notice-box"
              style={{
                backgroundColor: "var(--success-subtle, rgba(34, 197, 94, 0.1))",
                borderColor: "var(--success, #22c55e)",
                marginBottom: "1rem",
              }}
              role="status"
            >
              <IconCheck size={18} style={{ color: "var(--success, #22c55e)" }} />
              <div>
                <strong>Thành công:</strong> {successMessage}
              </div>
            </div>
          )}

          {/* Thông báo lỗi chung */}
          {generalError && (
            <div
              className="notice-box"
              style={{
                backgroundColor: "rgba(239, 68, 68, 0.1)",
                borderColor: "var(--error, #ef4444)",
                marginBottom: "1rem",
              }}
              role="alert"
            >
              <IconInfo size={18} style={{ color: "var(--error, #ef4444)" }} />
              <div>
                <strong>Lỗi cài đặt:</strong> {generalError}
              </div>
            </div>
          )}

          {/* 1. Thư mục Tài liệu */}
          <div className="form-group">
            <label htmlFor="docs-path">1. Thư mục Tài liệu (Documents)</label>
            <div className="input-group">
              <input
                id="docs-path"
                type="text"
                value={documentsPath}
                onChange={(e) => {
                  setDocumentsPath(e.target.value);
                  setSuccessMessage(null);
                }}
                placeholder="Ví dụ: C:\TiToAI\documents"
                className={`form-control ${fieldErrors["documentsPath"] ? "input-error" : ""}`}
                style={fieldErrors["documentsPath"] ? { borderColor: "var(--error, #ef4444)" } : undefined}
                disabled={isSaving}
              />
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => handleBrowse("documents")}
                disabled={isSaving || isBrowsing === "documents"}
                aria-label="Chọn thư mục lưu trữ tài liệu"
              >
                {isBrowsing === "documents" ? "Đang chọn…" : "Chọn thư mục…"}
              </button>
            </div>
            <span className="field-hint">
              Lưu trữ kịch bản, prompt và tài liệu được xuất thành tệp văn bản.
            </span>
            {fieldErrors["documentsPath"] && (
              <div
                className="field-error-message"
                style={{ color: "var(--error, #ef4444)", fontSize: "0.8125rem", marginTop: "0.25rem" }}
                role="alert"
              >
                {fieldErrors["documentsPath"].join(" ")}
              </div>
            )}
          </div>

          {/* 2. Thư mục Ảnh AI */}
          <div className="form-group" style={{ marginTop: "1rem" }}>
            <label htmlFor="imgs-path">2. Thư mục Ảnh AI (Images)</label>
            <div className="input-group">
              <input
                id="imgs-path"
                type="text"
                value={imagesPath}
                onChange={(e) => {
                  setImagesPath(e.target.value);
                  setSuccessMessage(null);
                }}
                placeholder="Ví dụ: C:\TiToAI\images"
                className={`form-control ${fieldErrors["imagesPath"] ? "input-error" : ""}`}
                style={fieldErrors["imagesPath"] ? { borderColor: "var(--error, #ef4444)" } : undefined}
                disabled={isSaving}
              />
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => handleBrowse("images")}
                disabled={isSaving || isBrowsing === "images"}
                aria-label="Chọn thư mục lưu trữ ảnh AI"
              >
                {isBrowsing === "images" ? "Đang chọn…" : "Chọn thư mục…"}
              </button>
            </div>
            <span className="field-hint">
              Lưu ảnh do AI tạo ra và ảnh tham chiếu được nhập vào thư viện.
            </span>
            {fieldErrors["imagesPath"] && (
              <div
                className="field-error-message"
                style={{ color: "var(--error, #ef4444)", fontSize: "0.8125rem", marginTop: "0.25rem" }}
                role="alert"
              >
                {fieldErrors["imagesPath"].join(" ")}
              </div>
            )}
          </div>

          {/* 3. Thư mục Video AI */}
          <div className="form-group" style={{ marginTop: "1rem" }}>
            <label htmlFor="vids-path">3. Thư mục Video AI (Videos)</label>
            <div className="input-group">
              <input
                id="vids-path"
                type="text"
                value={videosPath}
                onChange={(e) => {
                  setVideosPath(e.target.value);
                  setSuccessMessage(null);
                }}
                placeholder="Ví dụ: C:\TiToAI\videos"
                className={`form-control ${fieldErrors["videosPath"] ? "input-error" : ""}`}
                style={fieldErrors["videosPath"] ? { borderColor: "var(--error, #ef4444)" } : undefined}
                disabled={isSaving}
              />
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => handleBrowse("videos")}
                disabled={isSaving || isBrowsing === "videos"}
                aria-label="Chọn thư mục lưu trữ video AI"
              >
                {isBrowsing === "videos" ? "Đang chọn…" : "Chọn thư mục…"}
              </button>
            </div>
            <span className="field-hint">
              Lưu video thành phẩm được tạo hoặc tải về từ các dịch vụ sinh video.
            </span>
            {fieldErrors["videosPath"] && (
              <div
                className="field-error-message"
                style={{ color: "var(--error, #ef4444)", fontSize: "0.8125rem", marginTop: "0.25rem" }}
                role="alert"
              >
                {fieldErrors["videosPath"].join(" ")}
              </div>
            )}
          </div>

          {/* Banner lưu ý đổi thư mục theo SETTINGS_PLAN §3 */}
          <div className="notice-box info" style={{ marginTop: "1.25rem" }}>
            <IconInfo size={18} />
            <div>
              <strong>Quy tắc lưu trữ:</strong> Đổi thư mục chỉ áp dụng cho tài liệu và
              tác vụ mới; các tệp đã tạo trước đó được giữ nguyên tại vị trí cũ.
            </div>
          </div>

          {/* Thanh hành động Lưu, Hủy & Đóng */}
          <div
            style={{
              display: "flex",
              justifyContent: "flex-end",
              gap: "0.75rem",
              marginTop: "1.5rem",
              paddingTop: "1rem",
              borderTop: "1px solid var(--border)",
            }}
          >
            {onClose && (
              <button
                type="button"
                className="btn btn-secondary"
                onClick={onClose}
                disabled={isSaving}
              >
                Đóng
              </button>
            )}
            <button
              type="button"
              className="btn btn-secondary"
              onClick={handleCancel}
              disabled={isSaving || !isDirty}
            >
              Hủy thay đổi
            </button>
            <button
              type="submit"
              className="btn btn-primary"
              disabled={isSaving || !isDirty}
            >
              {isSaving ? "Đang lưu cấu hình…" : "Lưu cài đặt"}
            </button>
          </div>
        </form>
      )}
    </div>
  );
}
