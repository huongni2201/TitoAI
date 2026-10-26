import { useState } from "react";
import type { PromptPreviewResponseData } from "./promptApi.ts";
import { IconCheck, IconRefresh } from "../../components/Icons.tsx";

export interface PromptPreviewPanelProps {
  previewData: PromptPreviewResponseData | null;
  isLoading: boolean;
  error: string | null;
  onRender: () => void;
  onApply?: (renderedText: string) => void;
  isDraftDirty?: boolean;
  isStale?: boolean;
}

export function PromptPreviewPanel({
  previewData,
  isLoading,
  error,
  onRender,
  onApply,
  isDraftDirty = false,
  isStale = false,
}: PromptPreviewPanelProps) {
  const [copied, setCopied] = useState(false);

  const handleCopy = async () => {
    if (!previewData?.renderedText) return;
    try {
      await navigator.clipboard.writeText(previewData.renderedText);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      const textArea = document.createElement("textarea");
      textArea.value = previewData.renderedText;
      document.body.appendChild(textArea);
      textArea.select();
      document.execCommand("copy");
      document.body.removeChild(textArea);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    }
  };

  const charCount = previewData?.renderedText ? previewData.renderedText.length : 0;
  const wordCount = previewData?.renderedText
    ? previewData.renderedText.trim().split(/\s+/).filter(Boolean).length
    : 0;

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "0.75rem" }}>
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
        <div style={{ display: "flex", alignItems: "center", gap: "0.5rem", flexWrap: "wrap" }}>
          <span style={{ fontWeight: 600, fontSize: "0.9rem", color: "var(--text-primary)" }}>
            Bản xem trước render
          </span>
          {previewData && (
            <span
              className={`badge ${previewData.isOverride ? "badge-override" : "badge-global"}`}
              style={{ fontSize: "0.7rem" }}
            >
              {previewData.isOverride ? "Override Workspace" : "Global Template"}
            </span>
          )}
          {isStale ? (
            <span
              className="badge badge-dirty"
              style={{ fontSize: "0.7rem", backgroundColor: "rgba(245, 158, 11, 0.15)", color: "var(--status-warning)", borderColor: "var(--status-warning)" }}
              title="Bản xem trước cũ, không khớp với nội dung bản nháp vừa sửa"
            >
              Bản xem trước cũ (Stale)
            </span>
          ) : isDraftDirty ? (
            <span
              className="badge badge-dirty"
              style={{ fontSize: "0.7rem" }}
              title="Nội dung bản nháp đã thay đổi so với bản preview trước"
            >
              Bản nháp mới
            </span>
          ) : null}
        </div>

        <button
          type="button"
          className="btn btn-secondary btn-sm"
          onClick={onRender}
          disabled={isLoading}
          aria-label="Render xem trước qua backend T10"
        >
          <IconRefresh size={14} className={isLoading ? "spin" : ""} />
          {isLoading ? "Đang render..." : "Render xem trước"}
        </button>
      </div>

      {/* Cảnh báo bản xem trước cũ không khớp với bản nháp hiện tại */}
      {isStale && previewData && (
        <div
          className="prompt-alert prompt-alert-warning"
          style={{ fontSize: "0.8rem", padding: "0.5rem 0.75rem" }}
          role="status"
        >
          <div>⚠️</div>
          <div>
            Bản xem trước này thuộc về nội dung cũ trước khi chỉnh sửa. Vui lòng nhấn &quot;Render xem trước&quot;
            để cập nhật lại trước khi áp dụng.
          </div>
        </div>
      )}

      {/* Hiển thị lỗi nếu backend trả validation error hoặc lỗi kết nối */}
      {error && (
        <div className="prompt-alert prompt-alert-error" role="alert">
          <div style={{ flexShrink: 0 }}>⚠️</div>
          <div style={{ display: "flex", flexDirection: "column", gap: "0.25rem" }}>
            <span style={{ fontWeight: 600 }}>Lỗi render xem trước</span>
            <span style={{ fontSize: "0.8rem", lineHeight: 1.4 }}>{error}</span>
            <span style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
              (Nội dung bản nháp bạn đang nhập vẫn được giữ nguyên đầy đủ).
            </span>
          </div>
        </div>
      )}

      {/* Container hiển thị nội dung kết quả xem trước */}
      <div className="preview-output-container">
        <div className="preview-output-header">
          <span>
            {isLoading
              ? "Đang xử lý render qua Prompt Engine..."
              : previewData
                ? `Đã render thành công (${previewData.variableCount} biến số)`
                : "Chưa render xem trước"}
          </span>
          {previewData && (
            <div style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}>
              <span>{charCount} ký tự / {wordCount} từ</span>
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                style={{ padding: "0.15rem 0.5rem", minHeight: "24px", fontSize: "0.7rem" }}
                onClick={handleCopy}
                disabled={isLoading || !previewData.renderedText}
                aria-label="Sao chép kết quả render"
              >
                {copied ? (
                  <>
                    <IconCheck size={12} style={{ color: "var(--status-success)" }} /> Đã chép
                  </>
                ) : (
                  "Sao chép"
                )}
              </button>
            </div>
          )}
        </div>

        <div className="preview-output-body">
          {isLoading ? (
            <span style={{ color: "var(--text-muted)", fontStyle: "italic" }}>
              Đang phân tích cú pháp template và thay thế biến số qua backend T10...
            </span>
          ) : previewData ? (
            previewData.renderedText || (
              <span style={{ color: "var(--text-muted)", fontStyle: "italic" }}>
                (Kết quả render trả về chuỗi rỗng)
              </span>
            )
          ) : (
            <span style={{ color: "var(--text-muted)", fontStyle: "italic" }}>
              Nhấn &quot;Render xem trước&quot; để kiểm tra cú pháp và kết quả thay thế biến số qua backend T10.
            </span>
          )}
        </div>
      </div>

      {/* Nút áp dụng vào scene editor nếu được cung cấp */}
      {onApply && previewData && (
        <div style={{ display: "flex", justifyContent: "flex-end" }}>
          <button
            type="button"
            className="btn btn-primary btn-sm"
            onClick={() => onApply(previewData.renderedText)}
            disabled={isLoading || !previewData.renderedText || isStale}
            title={isStale ? "Bản xem trước cũ, cần render lại trước khi áp dụng" : "Áp dụng nội dung này vào cảnh hiện tại"}
            aria-label="Áp dụng nội dung này vào cảnh hiện tại"
          >
            <IconCheck size={14} /> Áp dụng vào cảnh
          </button>
        </div>
      )}
    </div>
  );
}
