import React, { useState, useEffect, useMemo, useCallback, useRef } from "react";
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
import { PromptVersionHistory } from "./PromptVersionHistory.tsx";
import { IconRefresh, IconCheck } from "../../components/Icons.tsx";

export interface PromptTemplateEditorProps {
  template: PromptTemplateDto | null;
  currentVersion: PromptVersionDto | null;
  workspaceId?: string | null;
  purpose: PromptPurpose;
  transport?: IPromptTransport;
  onDirtyChange?: (isDirty: boolean) => void;
  onSaveSuccess?: (template: PromptTemplateDto, version: PromptVersionDto) => void;
  onRollbackSuccess?: (template: PromptTemplateDto, version: PromptVersionDto) => void;
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
  transport = defaultPromptTransport,
  onDirtyChange,
  onSaveSuccess,
  onRollbackSuccess,
  onDeleteOverrideSuccess,
  onApplyPreview,
}: PromptTemplateEditorProps) {
  // Trạng thái bản nháp đang chỉnh sửa
  const [templateText, setTemplateText] = useState(() => currentVersion?.templateText || "");
  const [changeDescription, setChangeDescription] = useState("");
  const [isOverrideMode, setIsOverrideMode] = useState(() => template?.isOverride ?? Boolean(workspaceId));

  // Biến số phục vụ preview
  const [variableValues, setVariableValues] = useState<Record<string, string>>({});

  // Trạng thái Preview và metadata nguồn render
  const [previewData, setPreviewData] = useState<PromptPreviewResponseData | null>(null);
  const [previewMeta, setPreviewMeta] = useState<{
    templateId: string | null;
    versionId: string | null;
    renderedSourceText: string;
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

  // Hộp thoại lịch sử phiên bản
  const [isHistoryOpen, setIsHistoryOpen] = useState(false);

  // Đồng bộ dữ liệu khi template hoặc currentVersion thay đổi từ máy chủ
  useEffect(() => {
    if (currentVersion) {
      setTemplateText(currentVersion.templateText || "");
      setChangeDescription("");
      setIsOverrideMode(template?.isOverride ?? false);
    } else {
      setTemplateText("");
      setChangeDescription("");
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

  // R2: Kiểm tra bản xem trước có bị stale không
  const isPreviewStale = useMemo(() => {
    if (!previewData || !previewMeta) return false;
    return (
      previewMeta.renderedSourceText !== templateText ||
      previewMeta.templateId !== (template?.templateId ?? null) ||
      previewMeta.versionId !== (currentVersion?.versionId ?? null)
    );
  }, [previewData, previewMeta, templateText, template?.templateId, currentVersion?.versionId]);

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

    try {
      const res = await promptApi.preview(
        {
          templateId: snapshotTemplateId,
          purpose: snapshotPurpose,
          workspaceId: workspaceId ?? null,
          templateText: snapshotText,
          variables: variableValues,
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

  // Lưu phiên bản mới qua backend T10
  const handleSaveNewVersion = async (e: React.FormEvent) => {
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
          changeDescription: changeDescription.trim() || null,
        },
        transport
      );

      setSaveSuccessNotice(
        `Đã tạo thành công phiên bản v${res.version.versionNumber} (${res.template.isOverride ? "Override Workspace" : "Toàn cục"})`
      );
      setChangeDescription("");

      // R2: Xóa preview cũ sau khi lưu version mới để không gán sai metadata
      setPreviewData(null);
      setPreviewMeta(null);

      if (onSaveSuccess) {
        onSaveSuccess(res.template, res.version);
      }

      setTimeout(() => setSaveSuccessNotice(null), 4000);
    } catch (err: unknown) {
      const msg =
        err && typeof err === "object" && "message" in err
          ? String(err.message)
          : "Lỗi khi lưu phiên bản mới qua backend T10.";
      setSaveError(msg);
    } finally {
      setIsSaving(false);
    }
  };

  // Xóa bản ghi đè Workspace (fallback về Global)
  const handleDeleteOverride = async () => {
    if (!template || !template.isOverride) return;
    setIsDeletingOverride(true);
    setDeleteError(null);

    try {
      await promptApi.deleteOverride(template.templateId, workspaceId, transport);
      setShowDeleteConfirm(false);
      if (onDeleteOverrideSuccess) {
        onDeleteOverrideSuccess(template.templateId);
      }
    } catch (err: unknown) {
      const msg =
        err && typeof err === "object" && "message" in err
          ? String(err.message)
          : "Không thể xóa bản ghi đè Workspace.";
      setDeleteError(msg);
    } finally {
      setIsDeletingOverride(false);
    }
  };

  // Khôi phục bản nháp về bản đã lưu gần nhất
  const handleResetDraft = () => {
    if (currentVersion) {
      setTemplateText(currentVersion.templateText || "");
      setChangeDescription("");
      setSaveError(null);
      // Xóa preview nếu nó được render từ bản nháp khác với bản đã lưu
      if (previewMeta && previewMeta.renderedSourceText !== currentVersion.templateText) {
        setPreviewData(null);
        setPreviewMeta(null);
      }
    }
  };

  // R3: Callback khi rollback thành công từ modal lịch sử
  const handleRollbackSuccess = (
    rolledBackTemplate: PromptTemplateDto,
    rolledBackVersion: PromptVersionDto
  ) => {
    setTemplateText(rolledBackVersion.templateText);
    setChangeDescription("");
    setPreviewData(null);
    setPreviewMeta(null);
    setSaveSuccessNotice(`Đã khôi phục về phiên bản v${rolledBackVersion.versionNumber}`);
    setTimeout(() => setSaveSuccessNotice(null), 4000);

    onRollbackSuccess?.(rolledBackTemplate, rolledBackVersion);
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
    <div className="prompt-editor-pane">
      {/* Topbar của Editor */}
      <div className="prompt-editor-topbar">
        <div className="prompt-editor-meta-group">
          <span style={{ fontWeight: 700, fontSize: "0.95rem", color: "var(--text-primary)" }}>
            {`Mục đích: ${purpose}`}
          </span>

          {template ? (
            <>
              <span
                className={`badge ${template.isOverride ? "badge-override" : "badge-global"}`}
              >
                {template.isOverride ? "Override Workspace" : "Toàn cục (Global)"}
              </span>
              <span
                className="badge badge-neutral"
                title={`Phiên bản hiện tại: v${template.currentVersionNumber ?? currentVersion?.versionNumber ?? 1}`}
              >
                v{template.currentVersionNumber ?? currentVersion?.versionNumber ?? 1}
              </span>
            </>
          ) : (
            <span className="badge badge-neutral">Tạo mới</span>
          )}

          {isDirty ? (
            <span className="badge badge-dirty" title="Bạn có nội dung bản nháp chưa lưu">
              Bản nháp chưa lưu
            </span>
          ) : (
            <span className="badge badge-saved" title="Nội dung đã đồng bộ với máy chủ">
              Đã lưu
            </span>
          )}
        </div>

        <div className="prompt-editor-actions">
          {template && (
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={() => setIsHistoryOpen(true)}
              disabled={isSaving || isDeletingOverride}
              aria-label="Xem lịch sử các phiên bản và rollback"
            >
              Lịch sử phiên bản
            </button>
          )}

          {template?.isOverride && (
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              style={{ color: "var(--status-error)", borderColor: "rgba(239, 68, 68, 0.4)" }}
              onClick={() => setShowDeleteConfirm(true)}
              disabled={isSaving || isDeletingOverride}
              aria-label="Xóa bản ghi đè này để quay lại template toàn cục"
            >
              Xóa bản ghi đè
            </button>
          )}

          {isDirty && (
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={handleResetDraft}
              disabled={isSaving}
              aria-label="Hủy các thay đổi trong bản nháp"
            >
              Khôi phục bản đã lưu
            </button>
          )}

          <button
            type="button"
            className="btn btn-primary btn-sm"
            onClick={handleSaveNewVersion}
            disabled={isSaving || !templateText.trim()}
            aria-label="Lưu bản nháp thành phiên bản mới"
          >
            {isSaving ? (
              <>
                <IconRefresh size={14} className="spin" /> Đang lưu...
              </>
            ) : (
              <>
                <IconCheck size={14} /> Lưu phiên bản mới
              </>
            )}
          </button>
        </div>
      </div>

      {/* Thông báo thành công */}
      {saveSuccessNotice && (
        <div
          className="prompt-alert prompt-alert-info"
          style={{ margin: "0.75rem 1.25rem 0", padding: "0.5rem 1rem" }}
          role="status"
        >
          <div>✅</div>
          <div>{saveSuccessNotice}</div>
        </div>
      )}

      {/* Thông báo lỗi khi lưu (Bảo đảm không mất bản nháp) */}
      {saveError && (
        <div
          className="prompt-alert prompt-alert-error"
          style={{ margin: "0.75rem 1.25rem 0", padding: "0.5rem 1rem" }}
          role="alert"
        >
          <div>⚠️</div>
          <div>
            <div style={{ fontWeight: 600 }}>Lưu phiên bản thất bại</div>
            <div style={{ fontSize: "0.8rem" }}>{saveError}</div>
            <div style={{ fontSize: "0.75rem", color: "var(--text-muted)", marginTop: "0.2rem" }}>
              Nội dung bạn vừa soạn thảo ở dưới không bị mất. Bạn có thể sửa đổi và thử lại.
            </div>
          </div>
        </div>
      )}

      {/* Hộp thoại xác nhận xóa Override */}
      {showDeleteConfirm && (
        <div
          className="prompt-alert prompt-alert-warning"
          style={{ margin: "0.75rem 1.25rem 0" }}
          role="alertdialog"
        >
          <div>⚠️</div>
          <div style={{ flex: 1, display: "flex", flexDirection: "column", gap: "0.4rem" }}>
            <div style={{ fontWeight: 700 }}>Xác nhận xóa bản ghi đè Workspace?</div>
            <div style={{ fontSize: "0.8rem" }}>
              Sau khi xóa, Workspace này sẽ tự động quay về sử dụng template Toàn cục (Global) mặc định
              cho mục đích <strong>{purpose}</strong>.
            </div>
            {deleteError && (
              <div style={{ color: "var(--status-error)", fontSize: "0.8rem" }}>{deleteError}</div>
            )}
            <div style={{ display: "flex", gap: "0.5rem", marginTop: "0.25rem" }}>
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={() => setShowDeleteConfirm(false)}
                disabled={isDeletingOverride}
              >
                Hủy
              </button>
              <button
                type="button"
                className="btn btn-primary btn-sm"
                style={{ backgroundColor: "var(--status-error)", borderColor: "var(--status-error)" }}
                onClick={handleDeleteOverride}
                disabled={isDeletingOverride}
              >
                {isDeletingOverride ? "Đang xóa..." : "Đồng ý xóa bản ghi đè"}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Bố cục hai cột co giãn: Cột trái (Editor) + Cột phải (Variables & Preview) */}
      <div className="prompt-editor-columns">
        {/* CỘT TRÁI: Nội dung Template Text & Mô tả thay đổi */}
        <div className="prompt-col-left">
          <div className="prompt-form-group">
            <div className="prompt-form-label">
              <label htmlFor="prompt-template-textarea">
                Nội dung Template
              </label>
              <span style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
                {templateText.length} ký tự
              </span>
            </div>

            <textarea
              id="prompt-template-textarea"
              className="prompt-textarea"
              value={templateText}
              onChange={(e) => setTemplateText(e.target.value)}
              placeholder="Nhập nội dung template prompt. Dùng {tên_biến} để định nghĩa biến số thay thế..."
              disabled={isSaving}
              aria-label="Nội dung template prompt"
            />
          </div>

          {/* Danh sách chip biến số phát hiện trong template */}
          <div className="prompt-form-group">
            <div className="prompt-form-label">
              <span>Biến số phát hiện trong template ({detectedVariables.length})</span>
            </div>
            {detectedVariables.length > 0 ? (
              <div className="variable-chips-container">
                {detectedVariables.map((v) => (
                  <span key={v} className="variable-chip">
                    &#123;{v}&#125;
                  </span>
                ))}
              </div>
            ) : (
              <span style={{ fontSize: "0.8rem", color: "var(--text-muted)", fontStyle: "italic" }}>
                Chưa có biến số nào được phát hiện trong nội dung. Thêm &#123;tên_biến&#125; vào văn bản ở trên.
              </span>
            )}
          </div>

          {/* Tùy chọn phạm vi lưu (Global vs Workspace Override) */}
          {workspaceId && !template?.isOverride && (
            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: "0.5rem",
                padding: "0.5rem 0.75rem",
                backgroundColor: "var(--surface-card)",
                border: "1px solid var(--border-subtle)",
                borderRadius: "var(--radius-md)",
              }}
            >
              <input
                id="checkbox-override-mode"
                type="checkbox"
                checked={isOverrideMode}
                onChange={(e) => setIsOverrideMode(e.target.checked)}
                disabled={isSaving}
              />
              <label
                htmlFor="checkbox-override-mode"
                style={{ fontSize: "0.85rem", cursor: "pointer", color: "var(--text-primary)" }}
              >
                Lưu thành bản ghi đè (Override) riêng cho Workspace hiện thời
              </label>
            </div>
          )}

          {/* Ghi chú thay đổi (Change Description) */}
          <div className="prompt-form-group">
            <label
              htmlFor="prompt-change-desc-input"
              className="prompt-form-label"
            >
              Mô tả thay đổi cho phiên bản mới (tùy chọn)
            </label>
            <input
              id="prompt-change-desc-input"
              type="text"
              className="prompt-input"
              value={changeDescription}
              onChange={(e) => setChangeDescription(e.target.value)}
              placeholder="Ví dụ: Thêm tham số phong cách cinematic 4k..."
              disabled={isSaving}
              maxLength={200}
            />
          </div>
        </div>

        {/* CỘT PHẢI: Nhập giá trị biến số + Xem trước (Preview Panel) */}
        <div className="prompt-col-right">
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

      {/* Modal Lịch sử phiên bản */}
      {template && (
        <PromptVersionHistory
          templateId={template.templateId}
          currentVersionId={template.currentVersionId}
          currentVersionNumber={template.currentVersionNumber}
          workspaceId={workspaceId}
          transport={transport}
          isOpen={isHistoryOpen}
          onClose={() => setIsHistoryOpen(false)}
          onRollbackSuccess={handleRollbackSuccess}
        />
      )}
    </div>
  );
}
