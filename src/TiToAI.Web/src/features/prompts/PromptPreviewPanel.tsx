import { useState } from "react";
import { RefreshCw, Check, Copy, AlertTriangle } from "lucide-react";
import { cn } from "../../lib/utils.ts";
import type { PromptPreviewResponseData } from "./promptApi.ts";

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
    <div className="flex flex-col gap-3">
      {/* Header Panel */}
      <div className="flex items-center justify-between gap-2 flex-wrap">
        <div className="flex items-center gap-2 flex-wrap">
          <span className="font-semibold text-xs text-foreground">
            Bản xem trước render
          </span>
          {previewData && (
            <span
              className={cn(
                "inline-flex items-center rounded-md border px-2 py-0.5 text-[11px] font-medium shadow-2xs",
                previewData.isOverride
                  ? "border-purple-500/30 bg-purple-500/10 text-purple-600 dark:text-purple-400"
                  : "border-border bg-muted/60 text-muted-foreground"
              )}
            >
              {previewData.isOverride ? "Override Workspace" : "Global Template"}
            </span>
          )}
          {isStale ? (
            <span
              className="inline-flex items-center rounded-md border border-amber-500/40 bg-amber-500/15 px-2 py-0.5 text-[11px] font-medium text-amber-600 dark:text-amber-400 shadow-2xs"
              title="Bản xem trước cũ, không khớp với nội dung bản nháp vừa sửa"
            >
              Bản xem trước cũ (Stale)
            </span>
          ) : isDraftDirty ? (
            <span
              className="inline-flex items-center rounded-md border border-amber-500/40 bg-amber-500/15 px-2 py-0.5 text-[11px] font-medium text-amber-600 dark:text-amber-400 shadow-2xs"
              title="Nội dung bản nháp đã thay đổi so với bản preview trước"
            >
              Bản nháp mới
            </span>
          ) : null}
        </div>

        <button
          type="button"
          className="inline-flex items-center justify-center gap-1.5 rounded-md text-xs font-medium h-8 px-3 border border-input bg-background shadow-xs hover:bg-accent hover:text-accent-foreground text-foreground transition-colors disabled:opacity-50 disabled:pointer-events-none cursor-pointer focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
          onClick={onRender}
          disabled={isLoading}
          aria-label="Xem yêu cầu gửi AI (Render xem trước)"
        >
          <RefreshCw className={cn("h-3.5 w-3.5", isLoading && "animate-spin")} />
          {isLoading ? "Đang render..." : "Xem yêu cầu gửi AI (Render xem trước)"}
        </button>
      </div>

      <div className="rounded-md border border-border/60 bg-muted/30 px-3 py-1.5 text-[11px] text-muted-foreground leading-relaxed flex flex-col gap-1">
        <div>
          <strong>Phân biệt mẫu & bản hoàn chỉnh:</strong> Mẫu gốc (Prompt Template) chứa biến số được bảo toàn để tái sử dụng; nội dung hiển thị dưới đây là <strong>Rendered Prompt</strong> đã thay thế các biến bằng giá trị thực tế để gọi AI hoặc lưu theo lần chạy.
        </div>
        <div className="text-[10px] text-muted-foreground/80">
          Thao tác render xem trước chỉ kiểm tra cú pháp và kết nối dữ liệu tại frontend; hoàn toàn không gọi AI và không tốn lượt gửi. Thao tác gửi AI được thực hiện tại luồng Tạo nội dung trong Dự án.
        </div>
      </div>

      {/* Cảnh báo bản xem trước cũ không khớp với bản nháp hiện tại */}
      {isStale && previewData && (
        <div
          className="flex items-start gap-2.5 rounded-lg border border-amber-500/40 bg-amber-500/10 p-3 text-xs text-amber-600 dark:text-amber-400 shadow-xs"
          role="status"
        >
          <AlertTriangle className="h-4 w-4 shrink-0 mt-0.5" />
          <div className="leading-relaxed">
            Bản xem trước này thuộc về nội dung cũ trước khi chỉnh sửa. Vui lòng nhấn &quot;Render xem trước&quot;
            để cập nhật lại trước khi áp dụng.
          </div>
        </div>
      )}

      {/* Hiển thị lỗi nếu backend trả validation error hoặc lỗi kết nối */}
      {error && (
        <div
          className="flex items-start gap-2.5 rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-xs text-destructive shadow-xs"
          role="alert"
        >
          <AlertTriangle className="h-4 w-4 shrink-0 mt-0.5" />
          <div className="flex flex-col gap-1">
            <span className="font-semibold">Lỗi render xem trước</span>
            <span className="leading-relaxed">{error}</span>
            <span className="text-[11px] text-muted-foreground">
              (Nội dung bản nháp bạn đang nhập vẫn được giữ nguyên đầy đủ).
            </span>
          </div>
        </div>
      )}

      {/* Card hiển thị nội dung kết quả xem trước */}
      <div className="flex flex-col rounded-xl border border-border/80 bg-card text-card-foreground overflow-hidden shadow-xs">
        <div className="flex items-center justify-between border-b border-border/70 bg-muted/30 px-3.5 py-2 text-xs font-medium text-muted-foreground">
          <span>
            {isLoading
              ? "Đang xử lý render qua Prompt Engine..."
              : previewData
                ? `Đã render thành công (${previewData.variableCount} biến số)`
                : "Chưa render xem trước"}
          </span>
          {previewData && (
            <div className="flex items-center gap-2">
              <span className="text-[11px] text-muted-foreground font-mono">
                {charCount} ký tự / {wordCount} từ
              </span>
              <button
                type="button"
                className="inline-flex items-center gap-1 rounded-md border border-input bg-background px-2 py-0.5 text-[11px] font-medium text-foreground shadow-2xs hover:bg-accent hover:text-accent-foreground transition-colors disabled:opacity-50 cursor-pointer focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
                onClick={handleCopy}
                disabled={isLoading || !previewData.renderedText}
                aria-label="Sao chép kết quả render"
              >
                {copied ? (
                  <>
                    <Check className="h-3 w-3 text-emerald-500" /> Đã chép
                  </>
                ) : (
                  <>
                    <Copy className="h-3 w-3" /> Sao chép
                  </>
                )}
              </button>
            </div>
          )}
        </div>

        <div className="p-3.5 font-mono text-xs text-foreground leading-relaxed whitespace-pre-wrap break-words max-h-64 overflow-y-auto min-h-[110px] bg-background/50">
          {isLoading ? (
            <span className="text-muted-foreground italic">
              Đang phân tích cú pháp template và thay thế biến số qua backend T10...
            </span>
          ) : previewData ? (
            previewData.renderedText || (
              <span className="text-muted-foreground italic">
                (Kết quả render trả về chuỗi rỗng)
              </span>
            )
          ) : (
            <span className="text-muted-foreground italic">
              Nhấn &quot;Render xem trước&quot; để kiểm tra cú pháp và kết quả thay thế biến số qua backend T10.
            </span>
          )}
        </div>
      </div>

      {/* Nút áp dụng vào scene editor nếu được cung cấp */}
      {onApply && previewData && (
        <div className="flex justify-end pt-1">
          <button
            type="button"
            className="inline-flex items-center justify-center gap-1.5 rounded-md text-xs font-medium h-8 px-3.5 bg-primary text-primary-foreground shadow-xs hover:bg-primary/90 transition-colors disabled:opacity-50 disabled:pointer-events-none cursor-pointer focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
            onClick={() => onApply(previewData.renderedText)}
            disabled={isLoading || !previewData.renderedText || isStale}
            title={isStale ? "Bản xem trước cũ, cần render lại trước khi áp dụng" : "Áp dụng nội dung này vào cảnh hiện tại"}
            aria-label="Áp dụng nội dung này vào cảnh hiện tại"
          >
            <Check className="h-3.5 w-3.5" /> Áp dụng vào cảnh
          </button>
        </div>
      )}
    </div>
  );
}
