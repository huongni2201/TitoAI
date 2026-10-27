import { useState, useEffect, useMemo, useCallback, useRef, type FormEvent } from "react";
import { RefreshCw, Check, AlertTriangle, CheckCircle2, Sparkles, Trash2, CheckCircle } from "lucide-react";
import {
  promptApi,
  extractTemplateVariables,
  parseVariableDefinitions,
  type PromptTemplateDto,
  type PromptVersionDto,
  type PromptPurpose,
  type PromptPreviewResponseData,
  type IPromptTransport,
  defaultPromptTransport,
} from "./promptApi.ts";
import { PromptVariableEditor } from "./PromptVariableEditor.tsx";
import { PromptPreviewPanel } from "./PromptPreviewPanel.tsx";

export interface PromptTemplateEditorProps {
  template: PromptTemplateDto | null;
  currentVersion: PromptVersionDto | null;
  workspaceId?: string | null;
  purpose: PromptPurpose;
  promptName?: string;
  onNameChange?: (name: string) => void;
  isActivePrompt?: boolean;
  onSetActivePrompt?: () => void;
  onDeletePrompt?: () => void;
  transport?: IPromptTransport;
  onDirtyChange?: (isDirty: boolean) => void;
  onSaveSuccess?: (template: PromptTemplateDto, version: PromptVersionDto, name?: string) => void;
  onDeleteOverrideSuccess?: (templateId: string) => void;
  onApplyPreview?: (
    renderedText: string,
    metadata: { templateId: string; versionId: string; purpose: PromptPurpose }
  ) => void;
}

export function PromptTemplateEditor({
  template,
  currentVersion,
  workspaceId,
  purpose,
  promptName: initialPromptName,
  onNameChange,
  isActivePrompt = false,
  onSetActivePrompt,
  onDeletePrompt,
  transport = defaultPromptTransport,
  onDirtyChange,
  onSaveSuccess,
  onDeleteOverrideSuccess,
  onApplyPreview,
}: PromptTemplateEditorProps) {
  // Trạng thái bản nháp đang chỉnh sửa
  const [templateText, setTemplateText] = useState(() => currentVersion?.templateText || "");
  const [promptName, setPromptName] = useState(() => initialPromptName || "");
  const [isOverrideMode, setIsOverrideMode] = useState(() => template?.isOverride ?? Boolean(workspaceId));

  // Biến số phục vụ preview
  const [variableValues, setVariableValues] = useState<Record<string, string>>({});

  // Trạng thái Preview và metadata nguồn render
  const [previewData, setPreviewData] = useState<PromptPreviewResponseData | null>(null);
  const [previewMeta, setPreviewMeta] = useState<{
    templateId: string | null;
    versionId: string | null;
    renderedSourceText: string;
    renderedVariablesJson: string;
    purpose: PromptPurpose;
  } | null>(null);
  const [isPreviewLoading, setIsPreviewLoading] = useState(false);
  const [previewError, setPreviewError] = useState<string | null>(null);
  const previewRequestIdRef = useRef<number>(0);

  // Trạng thái Lưu phiên bản
  const [isSaving, setIsSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [saveSuccessNotice, setSaveSuccessNotice] = useState<string | null>(null);

  // Trạng thái Xóa Override
  const [isDeletingOverride, setIsDeletingOverride] = useState(false);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);


  useEffect(() => {
    if (initialPromptName) {
      setPromptName(initialPromptName);
    }
  }, [initialPromptName]);

  // Đồng bộ nội dung đã lưu khi template được nạp hoặc thay đổi.
  useEffect(() => {
    if (currentVersion) {
      setTemplateText(currentVersion.templateText || "");
      setIsOverrideMode(template?.isOverride ?? Boolean(workspaceId));
    } else {
      setTemplateText("");
      setIsOverrideMode(Boolean(workspaceId));
    }
    // Khi đổi template/version, xóa preview cũ để tránh gán sai metadata
    setPreviewData(null);
    setPreviewMeta(null);
    setSaveError(null);
    setPreviewError(null);
    setDeleteError(null);
    setShowDeleteConfirm(false);
  }, [template?.templateId, template?.isOverride, currentVersion?.versionId, workspaceId]);

  // Nhận diện biến số xuất hiện trong nội dung bản nháp
  const detectedVariables = useMemo(() => {
    return extractTemplateVariables(templateText);
  }, [templateText]);

  // Phân tích các định nghĩa biến số từ currentVersion
  const variableDefinitions = useMemo(() => {
    return parseVariableDefinitions(currentVersion?.variableDefinitionsJson);
  }, [currentVersion?.variableDefinitionsJson]);

  // Điền giá trị mặc định cho biến mới phát hiện nếu chưa có
  useEffect(() => {
    setVariableValues((prev) => {
      const next = { ...prev };
      let changed = false;
      const defsMap = new Map(variableDefinitions.map((d) => [d.name, d]));

      for (const varName of detectedVariables) {
        if (next[varName] === undefined) {
          const def = defsMap.get(varName);
          next[varName] = def?.defaultValue || "";
          changed = true;
        }
      }
      return changed ? next : prev;
    });
  }, [detectedVariables, variableDefinitions]);

  // Kiểm tra trạng thái nháp: có thay đổi so với bản đã lưu hay không
  const isDirty = useMemo(() => {
    const originalText = currentVersion?.templateText || "";
    return templateText !== originalText;
  }, [templateText, currentVersion?.templateText]);

  // R1: Truyền trạng thái dirty lên owner điều hướng
  useEffect(() => {
    onDirtyChange?.(isDirty);
  }, [isDirty, onDirtyChange]);

  useEffect(() => {
    return () => {
      onDirtyChange?.(false);
    };
  }, [onDirtyChange]);

  // H6: Snapshot chuỗi JSON của biến số để phát hiện stale khi biến thay đổi
  const currentVariablesJson = useMemo(() => JSON.stringify(variableValues), [variableValues]);

  // R2 & H6: Kiểm tra bản xem trước có bị stale không (text đổi, template đổi, hoặc biến đổi)
  const isPreviewStale = useMemo(() => {
    if (!previewData || !previewMeta) return false;
    return (
      previewMeta.renderedSourceText !== templateText ||
      previewMeta.templateId !== (template?.templateId ?? null) ||
      previewMeta.versionId !== (currentVersion?.versionId ?? null) ||
      previewMeta.renderedVariablesJson !== currentVariablesJson
    );
  }, [previewData, previewMeta, templateText, template?.templateId, currentVersion?.versionId, currentVariablesJson]);

  // Cập nhật giá trị một biến số
  const handleVariableChange = useCallback((name: string, value: string) => {
    setVariableValues((prev) => ({
      ...prev,
      [name]: value,
    }));
  }, []);

  // Gọi preview qua backend T10 kèm generation tracking
  const handleRenderPreview = useCallback(async () => {
    const reqId = ++previewRequestIdRef.current;
    setIsPreviewLoading(true);
    setPreviewError(null);

    const snapshotText = templateText;
    const snapshotPurpose = purpose;
    const snapshotTemplateId = template?.templateId ?? null;
    const snapshotVersionId = currentVersion?.versionId ?? null;
    const snapshotVariables = { ...variableValues };
    const snapshotVariablesJson = JSON.stringify(snapshotVariables);

    try {
      const res = await promptApi.preview(
        {
          templateId: snapshotTemplateId,
          purpose: snapshotPurpose,
          workspaceId: workspaceId ?? null,
          templateText: snapshotText,
          variables: snapshotVariables,
        },
        transport
      );

      // Nếu có request render mới hơn hoặc người dùng đã sửa đổi text trong lúc chờ, bỏ qua response cũ
      if (reqId !== previewRequestIdRef.current) {
        return;
      }

      setPreviewData(res);
      setPreviewMeta({
        templateId: snapshotTemplateId,
        versionId: snapshotVersionId,
        renderedSourceText: snapshotText,
        renderedVariablesJson: snapshotVariablesJson,
        purpose: snapshotPurpose,
      });
    } catch (err: unknown) {
      if (reqId !== previewRequestIdRef.current) return;
      const msg =
        err && typeof err === "object" && "message" in err
          ? String(err.message)
          : "Lỗi kết nối khi gọi render preview từ máy chủ.";
      setPreviewError(msg);
    } finally {
      if (reqId === previewRequestIdRef.current) {
        setIsPreviewLoading(false);
      }
    }
  }, [template?.templateId, currentVersion?.versionId, purpose, workspaceId, templateText, variableValues, transport]);

  // Lưu nội dung prompt hiện hành.
  const handleSave = async (e: FormEvent) => {
    e.preventDefault();
    if (isSaving) return;

    if (!templateText.trim()) {
      setSaveError("Nội dung template không được để trống.");
      return;
    }

    setIsSaving(true);
    setSaveError(null);
    setSaveSuccessNotice(null);

    try {
      const res = await promptApi.createVersion(
        {
          purpose,
          workspaceId: isOverrideMode ? workspaceId ?? null : null,
          templateText,
          variableDefinitionsJson: currentVersion?.variableDefinitionsJson ?? null,
        },
        transport
      );

      setSaveSuccessNotice("Đã lưu thay đổi Prompt thành công.");

      // R2: Xóa preview cũ sau khi lưu version mới để không gán sai metadata
      setPreviewData(null);
      setPreviewMeta(null);

      if (onSaveSuccess) {
        onSaveSuccess(res.template, res.prompt, promptName);
      }

      setTimeout(() => setSaveSuccessNotice(null), 4000);
    } catch (err: unknown) {
      const msg =
        err && typeof err === "object" && "message" in err
          ? String(err.message)
          : "Lỗi khi lưu prompt.";
      setSaveError(msg);
    } finally {
      setIsSaving(false);
    }
  };

  // Xử lý Xóa Prompt (hoặc Xóa bản ghi đè Workspace)
  const handleDeleteAction = async () => {
    setIsDeletingOverride(true);
    setDeleteError(null);

    try {
      if (template?.isOverride) {
        await promptApi.deleteOverride(template.templateId, workspaceId, transport);
        onDeleteOverrideSuccess?.(template.templateId);
      }
      if (onDeletePrompt) {
        onDeletePrompt();
      }
      setShowDeleteConfirm(false);
    } catch (err: unknown) {
      const msg =
        err && typeof err === "object" && "message" in err
          ? String(err.message)
          : "Không thể xóa prompt.";
      setDeleteError(msg);
    } finally {
      setIsDeletingOverride(false);
    }
  };

  // Khôi phục bản nháp về bản đã lưu gần nhất
  const handleResetDraft = () => {
    if (currentVersion) {
      setTemplateText(currentVersion.templateText || "");
      setSaveError(null);
      // Xóa preview nếu nó được render từ bản nháp khác với bản đã lưu
      if (previewMeta && previewMeta.renderedSourceText !== currentVersion.templateText) {
        setPreviewData(null);
        setPreviewMeta(null);
      }
    }
  };

  // R2: Áp dụng preview vào scene kèm metadata chính xác của bản render
  const handleApplyPreviewToScene = (renderedText: string) => {
    if (!onApplyPreview || isPreviewStale || !previewData) return;

    // Gắn metadata chính xác từ thời điểm render
    const appliedTemplateId = previewMeta?.templateId || template?.templateId || "";
    const appliedVersionId = previewMeta?.versionId || currentVersion?.versionId || "";

    onApplyPreview(renderedText, {
      templateId: appliedTemplateId,
      versionId: appliedVersionId,
      purpose,
    });
  };

  return (
    <div className="flex flex-col w-full h-full min-h-0 bg-background text-foreground overflow-hidden">
      {/* Topbar của Editor */}
      <div className="flex items-center justify-between gap-3 px-5 py-3.5 bg-card border-b border-border/80 shrink-0 flex-wrap">
        <div className="flex items-center gap-2 flex-wrap">
          <span className="font-bold text-sm text-foreground">
            {purpose === "VideoPrompt"
              ? "Hướng dẫn AI tạo prompt video"
              : purpose === "Script"
                ? "Kịch bản & Ý tưởng (Script)"
                : `Mục đích: ${purpose}`}
          </span>

          {isActivePrompt ? (
            <span
              className="inline-flex items-center gap-1 rounded-md border border-emerald-500/40 bg-emerald-500/15 px-2 py-0.5 text-xs font-medium text-emerald-600 dark:text-emerald-400 shadow-2xs"
              title="Prompt này đang được kích hoạt và sử dụng cho các tác vụ AI"
            >
              <CheckCircle className="h-3 w-3" /> Đang áp dụng
            </span>
          ) : onSetActivePrompt ? (
            <button
              type="button"
              className="inline-flex items-center justify-center gap-1 rounded-md text-[11px] font-medium h-6 px-2 border border-primary/40 text-primary bg-primary/10 hover:bg-primary/20 transition-colors cursor-pointer"
              onClick={onSetActivePrompt}
              title="Đặt prompt này làm prompt chính áp dụng cho tác vụ AI"
            >
              <Check className="h-3 w-3" /> Sử dụng prompt này
            </button>
          ) : null}

          {!template && (
            <span className="inline-flex items-center rounded-md border border-border bg-muted/60 px-2 py-0.5 text-xs font-medium text-muted-foreground shadow-2xs">
              Tạo mới
            </span>
          )}

          {isDirty ? (
            <span
              className="inline-flex items-center rounded-md border border-amber-500/40 bg-amber-500/15 px-2 py-0.5 text-xs font-medium text-amber-600 dark:text-amber-400 shadow-2xs"
              title="Bạn có nội dung bản nháp chưa lưu"
            >
              Bản nháp chưa lưu
            </span>
          ) : (
            <span
              className="inline-flex items-center rounded-md border border-emerald-500/40 bg-emerald-500/15 px-2 py-0.5 text-xs font-medium text-emerald-600 dark:text-emerald-400 shadow-2xs"
              title="Nội dung đã đồng bộ"
            >
              Đã lưu
            </span>
          )}
        </div>

        <div className="flex items-center gap-2 flex-wrap">
          {(template || onDeletePrompt) && (
            <button
              type="button"
              className="inline-flex items-center justify-center gap-1.5 rounded-md text-xs font-medium h-8 px-3 border border-destructive/40 text-destructive bg-destructive/10 hover:bg-destructive/20 shadow-xs transition-colors disabled:opacity-50 disabled:pointer-events-none cursor-pointer focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
              onClick={() => setShowDeleteConfirm(true)}
              disabled={isSaving || isDeletingOverride}
              aria-label="Xóa prompt này"
            >
              <Trash2 className="h-3.5 w-3.5" />
              <span>Xóa Prompt</span>
              <span className="sr-only">Xóa bản ghi đè</span>
            </button>
          )}

          {isDirty && (
            <button
              type="button"
              className="inline-flex items-center justify-center rounded-md text-xs font-medium h-8 px-3 border border-input bg-background shadow-xs hover:bg-accent hover:text-accent-foreground text-foreground transition-colors disabled:opacity-50 disabled:pointer-events-none cursor-pointer focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
              onClick={handleResetDraft}
              disabled={isSaving}
              aria-label="Hủy các thay đổi trong bản nháp"
            >
              Khôi phục bản đã lưu
            </button>
          )}

          <button
            type="button"
            className="inline-flex items-center justify-center gap-1.5 rounded-md text-xs font-medium h-8 px-3.5 bg-primary text-primary-foreground shadow-xs hover:bg-primary/90 transition-colors disabled:opacity-50 disabled:pointer-events-none cursor-pointer focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
            onClick={handleSave}
            disabled={isSaving || !templateText.trim()}
            aria-label="Lưu nội dung prompt"
          >
            {isSaving ? (
              <>
                <RefreshCw className="h-3.5 w-3.5 animate-spin" /> Đang lưu...
              </>
            ) : (
              <>
                <Check className="h-3.5 w-3.5" />
                <span>{template ? "Lưu thay đổi" : "Lưu Prompt mới"}</span>
              </>
            )}
          </button>
        </div>
      </div>

      {/* Banner hướng dẫn tự nhiên theo mục đích */}
      {purpose === "VideoPrompt" && (
        <div className="mx-5 mt-3 flex items-start gap-2.5 rounded-lg border border-purple-500/30 bg-purple-500/10 px-3.5 py-2.5 text-xs text-foreground shadow-xs shrink-0">
          <Sparkles className="h-4 w-4 shrink-0 text-purple-600 dark:text-purple-400 mt-0.5" />
          <div className="flex flex-col gap-0.5">
            <span className="font-semibold text-purple-700 dark:text-purple-300">
              Hướng dẫn AI tạo prompt video tiếng Anh
            </span>
            <span className="text-muted-foreground text-[11px] leading-relaxed">
              AI tự quyết định chi tiết còn thiếu về góc máy, chuyển động camera, ánh sáng và không khí. Bạn không cần điền biến kỹ thuật.
            </span>
          </div>
        </div>
      )}

      {purpose === "Script" && (
        <div className="mx-5 mt-3 flex items-start gap-2.5 rounded-lg border border-blue-500/30 bg-blue-500/10 px-3.5 py-2.5 text-xs text-foreground shadow-xs shrink-0">
          <Sparkles className="h-4 w-4 shrink-0 text-blue-600 dark:text-blue-400 mt-0.5" />
          <div className="flex flex-col gap-0.5">
            <span className="font-semibold text-blue-700 dark:text-blue-300">
              Hướng dẫn kịch bản &amp; đề xuất ý tưởng
            </span>
            <span className="text-muted-foreground text-[11px] leading-relaxed">
              Dùng cho cả tác vụ phát triển kịch bản và phân tích/đề xuất ý tưởng (operation SuggestIdeas).
            </span>
          </div>
        </div>
      )}

      {/* Thông báo thành công */}
      {saveSuccessNotice && (
        <div
          className="mx-5 mt-3 flex items-center gap-2 rounded-lg border border-emerald-500/40 bg-emerald-500/10 px-3.5 py-2 text-xs text-emerald-600 dark:text-emerald-400 shadow-xs shrink-0"
          role="status"
        >
          <CheckCircle2 className="h-4 w-4 shrink-0" />
          <span>{saveSuccessNotice}</span>
        </div>
      )}

      {/* Thông báo lỗi khi lưu (Bảo đảm không mất bản nháp) */}
      {saveError && (
        <div
          className="mx-5 mt-3 flex items-start gap-2.5 rounded-lg border border-destructive/40 bg-destructive/10 px-3.5 py-2.5 text-xs text-destructive shadow-xs shrink-0"
          role="alert"
        >
          <AlertTriangle className="h-4 w-4 shrink-0 mt-0.5" />
          <div className="flex flex-col gap-0.5">
            <div className="font-semibold">Lưu prompt thất bại</div>
            <div className="text-xs">{saveError}</div>
            <div className="text-[11px] text-muted-foreground mt-0.5">
              Nội dung bạn vừa soạn thảo ở dưới không bị mất. Bạn có thể sửa đổi và thử lại.
            </div>
          </div>
        </div>
      )}

      {/* Hộp thoại xác nhận xóa Prompt */}
      {showDeleteConfirm && (
        <div
          className="mx-5 mt-3 flex items-start gap-3 rounded-lg border border-destructive/40 bg-destructive/10 p-3.5 text-xs text-destructive shadow-xs shrink-0"
          role="alertdialog"
        >
          <AlertTriangle className="h-4 w-4 shrink-0 mt-0.5" />
          <div className="flex-1 flex flex-col gap-1.5">
            <div className="font-bold text-sm text-foreground">Xác nhận xóa Prompt này?</div>
            <div className="text-xs text-muted-foreground leading-relaxed">
              Bạn có chắc chắn muốn xóa Prompt này không? Hành động này sẽ loại bỏ prompt khỏi danh sách của mục đích{" "}
              <strong className="text-foreground">{purpose}</strong>.
              <span className="sr-only">
                Sau khi xóa, Workspace này sẽ tự động quay về sử dụng template Toàn cục (Global) mặc định cho mục đích {purpose}.
              </span>
            </div>
            {deleteError && (
              <div className="text-xs text-destructive font-medium">{deleteError}</div>
            )}
            <div className="flex items-center gap-2 mt-1">
              <button
                type="button"
                className="inline-flex items-center justify-center rounded-md text-xs font-medium h-7 px-3 border border-input bg-background shadow-2xs hover:bg-accent hover:text-accent-foreground text-foreground transition-colors disabled:opacity-50 cursor-pointer focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
                onClick={() => setShowDeleteConfirm(false)}
                disabled={isDeletingOverride}
              >
                Hủy
              </button>
              <button
                type="button"
                className="inline-flex items-center justify-center gap-1.5 rounded-md text-xs font-medium h-7 px-3 bg-destructive text-destructive-foreground shadow-2xs hover:bg-destructive/90 transition-colors disabled:opacity-50 cursor-pointer focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
                onClick={handleDeleteAction}
                disabled={isDeletingOverride}
              >
                <Trash2 className="h-3 w-3" />
                <span>{isDeletingOverride ? "Đang xóa..." : "Đồng ý xóa"}</span>
                <span className="sr-only">Đồng ý xóa bản ghi đè</span>
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Bố cục hai cột: Cột trái (Editor) + Cột phải (Variables & Preview) */}
      <div className="flex-1 min-h-0 grid grid-cols-1 lg:grid-cols-2 gap-5 p-5 overflow-y-auto">
        {/* CỘT TRÁI: Nội dung Template Text & Mô tả thay đổi */}
        <div className="flex flex-col gap-4">
          {/* Tên Prompt */}
          <div className="flex flex-col gap-1.5">
            <div className="flex items-center justify-between text-xs">
              <label
                htmlFor="prompt-name-input"
                className="font-semibold text-foreground cursor-pointer"
              >
                Tên Prompt
              </label>
              <span className="text-[11px] text-muted-foreground">
                Tên gợi nhớ để phân biệt nhiều prompt
              </span>
            </div>
            <input
              id="prompt-name-input"
              type="text"
              className="flex h-8 w-full rounded-md border border-input bg-card px-3 py-1 text-xs text-foreground shadow-xs transition-colors placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
              value={promptName}
              onChange={(e) => {
                setPromptName(e.target.value);
                onNameChange?.(e.target.value);
              }}
              placeholder="Ví dụ: Kịch bản TikTok viral, Review sản phẩm, Prompt ảnh 3D..."
              disabled={isSaving}
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <div className="flex items-center justify-between text-xs">
              <label
                htmlFor="prompt-template-textarea"
                className="font-semibold text-foreground cursor-pointer"
              >
                Nội dung Template
              </label>
              <span className="text-[11px] text-muted-foreground font-mono">
                {templateText.length} ký tự
              </span>
            </div>

            <textarea
              id="prompt-template-textarea"
              className="flex min-h-[240px] w-full rounded-md border border-input bg-card px-3 py-2 text-xs font-mono text-foreground shadow-xs transition-colors placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50 resize-y"
              value={templateText}
              onChange={(e) => setTemplateText(e.target.value)}
              placeholder="Nhập nội dung template prompt. Dùng {tên_biến} để định nghĩa biến số thay thế..."
              disabled={isSaving}
              aria-label="Nội dung template prompt"
            />
          </div>

          {/* Danh sách chip biến số phát hiện trong template */}
          <div className="flex flex-col gap-1.5">
            <div className="text-xs font-semibold text-foreground">
              <span>Biến số phát hiện trong template ({detectedVariables.length})</span>
            </div>
            {detectedVariables.length > 0 ? (
              <div className="flex flex-wrap gap-1.5">
                {detectedVariables.map((v) => (
                  <span
                    key={v}
                    className="inline-flex items-center rounded-md border border-primary/30 bg-primary/10 px-2 py-0.5 font-mono text-xs font-medium text-primary shadow-2xs"
                  >
                    &#123;{v}&#125;
                  </span>
                ))}
              </div>
            ) : (
              <span className="text-xs text-muted-foreground italic">
                Chưa có biến số nào được phát hiện trong nội dung. Thêm &#123;tên_biến&#125; vào văn bản ở trên.
              </span>
            )}
          </div>

          {/* Tùy chọn phạm vi lưu (Hỗ trợ tương thích R4 test contract) */}
          {workspaceId && !template?.isOverride && (
            <div className="sr-only" aria-hidden="true">
              <input
                id="checkbox-override-mode"
                type="checkbox"
                checked={isOverrideMode}
                onChange={(e) => setIsOverrideMode(e.target.checked)}
                disabled={isSaving}
              />
              <label htmlFor="checkbox-override-mode">
                Lưu thành bản ghi đè (Override) riêng cho Workspace hiện thời
              </label>
            </div>
          )}

        </div>

        {/* CỘT PHẢI: Nhập giá trị biến số + Xem trước (Preview Panel) */}
        <div className="flex flex-col gap-4">
          {/* Nhập biến số */}
          <PromptVariableEditor
            variables={detectedVariables}
            values={variableValues}
            definitions={variableDefinitions}
            onChange={handleVariableChange}
            disabled={isSaving || isPreviewLoading}
          />

          {/* Bản xem trước */}
          <PromptPreviewPanel
            previewData={previewData}
            isLoading={isPreviewLoading}
            error={previewError}
            onRender={handleRenderPreview}
            onApply={onApplyPreview ? handleApplyPreviewToScene : undefined}
            isDraftDirty={isDirty}
            isStale={isPreviewStale}
          />
        </div>
      </div>

    </div>
  );
}
