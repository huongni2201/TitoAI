import { useState, useEffect, useMemo, useCallback, useRef } from "react";
import {
  promptApi,
  PROMPT_PURPOSES,
  type PromptPurpose,
  type PromptTemplateDto,
  type PromptVersionDto,
  type IPromptTransport,
  defaultPromptTransport,
} from "./promptApi.ts";
import { PromptTemplateEditor } from "./PromptTemplateEditor.tsx";
import { IconSearch, IconPlus, IconRefresh } from "../../components/Icons.tsx";
import "./promptStyles.css";

export interface PromptTemplateManagerProps {
  workspaceId?: string | null;
  initialPurpose?: PromptPurpose;
  selectedTemplateId?: string | null;
  onSelectTemplate?: (template: PromptTemplateDto) => void;
  onApplyPreview?: (
    renderedText: string,
    metadata: { templateId: string; versionId: string; purpose: PromptPurpose }
  ) => void;
  className?: string;
  transport?: IPromptTransport;
}

export function PromptTemplateManager({
  workspaceId,
  initialPurpose = "Script",
  selectedTemplateId: propSelectedTemplateId,
  onSelectTemplate,
  onApplyPreview,
  className = "",
  transport = defaultPromptTransport,
}: PromptTemplateManagerProps) {
  // Purpose đang chọn
  const [activePurpose, setActivePurpose] = useState<PromptPurpose>(initialPurpose);

  // Danh sách templates
  const [templates, setTemplates] = useState<PromptTemplateDto[]>([]);
  const [isLoadingList, setIsLoadingList] = useState(false);
  const [listError, setListError] = useState<string | null>(null);

  // Template đang chọn để chỉnh sửa
  const [selectedTemplate, setSelectedTemplate] = useState<PromptTemplateDto | null>(null);
  const [currentVersion, setCurrentVersion] = useState<PromptVersionDto | null>(null);
  const [isLoadingDetail, setIsLoadingDetail] = useState(false);
  const [detailError, setDetailError] = useState<string | null>(null);

  // R4: Chế độ tạo mới template tường minh (không bị effect auto-select ghi đè)
  const [isCreatingNew, setIsCreatingNew] = useState(false);

  // Bộ lọc phạm vi (Tất cả / Toàn cục / Workspace Override)
  const [scopeFilter, setScopeFilter] = useState<"all" | "global" | "override">("all");
  const [searchQuery, setSearchQuery] = useState("");

  // Chống race condition / stale response khi đổi Workspace hoặc chọn template nhanh
  const listRequestIdRef = useRef<number>(0);
  const detailRequestIdRef = useRef<number>(0);

  // R1: Trạng thái dirty của editor và guard khi chuyển đổi
  const [isEditorDirty, setIsEditorDirty] = useState(false);
  const [pendingSwitchAction, setPendingSwitchAction] = useState<(() => void) | null>(null);
  const [showDirtyConfirmModal, setShowDirtyConfirmModal] = useState(false);

  // R1: Hàm yêu cầu chuyển đổi an toàn qua dirty guard
  const requestSwitch = useCallback((action: () => void) => {
    if (isEditorDirty) {
      setPendingSwitchAction(() => action);
      setShowDirtyConfirmModal(true);
    } else {
      action();
    }
  }, [isEditorDirty]);

  // Tải danh sách templates từ backend T10
  const fetchTemplates = useCallback(async () => {
    const currentRequestId = ++listRequestIdRef.current;
    setIsLoadingList(true);
    setListError(null);

    try {
      const result = await promptApi.listTemplates(workspaceId, transport);

      // Nếu đã có request mới hơn (người dùng đổi workspace nhanh), bỏ qua response cũ
      if (currentRequestId !== listRequestIdRef.current) {
        return;
      }

      setTemplates(result);
    } catch (err: unknown) {
      if (currentRequestId !== listRequestIdRef.current) return;
      const msg =
        err && typeof err === "object" && "message" in err
          ? String(err.message)
          : "Không thể tải danh sách template từ máy chủ.";
      setListError(msg);
    } finally {
      if (currentRequestId === listRequestIdRef.current) {
        setIsLoadingList(false);
      }
    }
  }, [workspaceId, transport]);

  // Tự động tải lại danh sách khi workspaceId thay đổi qua dirty guard
  const prevWorkspaceIdRef = useRef<string | null | undefined>(workspaceId);
  useEffect(() => {
    if (prevWorkspaceIdRef.current !== workspaceId) {
      prevWorkspaceIdRef.current = workspaceId;
      requestSwitch(() => {
        setIsCreatingNew(false);
        fetchTemplates();
      });
    }
  }, [workspaceId, fetchTemplates, requestSwitch]);

  // Lần đầu mở: tải danh sách
  useEffect(() => {
    fetchTemplates();
  }, [fetchTemplates]);

  // Tải chi tiết template đang chọn
  const loadTemplateDetail = useCallback(
    async (templateId: string) => {
      const currentRequestId = ++detailRequestIdRef.current;
      setIsLoadingDetail(true);
      setDetailError(null);

      try {
        const result = await promptApi.getTemplate(
          {
            templateId,
            purpose: null,
            workspaceId: workspaceId ?? null,
          },
          transport
        );

        if (currentRequestId !== detailRequestIdRef.current) {
          return;
        }

        setSelectedTemplate(result.template);
        setCurrentVersion(result.currentVersion);

        if (onSelectTemplate) {
          onSelectTemplate(result.template);
        }
      } catch (err: unknown) {
        if (currentRequestId !== detailRequestIdRef.current) return;
        const msg =
          err && typeof err === "object" && "message" in err
            ? String(err.message)
            : "Không thể tải chi tiết phiên bản template.";
        setDetailError(msg);
      } finally {
        if (currentRequestId === detailRequestIdRef.current) {
          setIsLoadingDetail(false);
        }
      }
    },
    [workspaceId, transport, onSelectTemplate]
  );

  // Lọc templates theo purpose đang chọn
  const purposeTemplates = useMemo(() => {
    return templates.filter((t) => t.purpose === activePurpose);
  }, [templates, activePurpose]);

  // Đếm số lượng template cho mỗi purpose tab
  const purposeCounts = useMemo(() => {
    const counts: Record<PromptPurpose, number> = {
      Script: 0,
      Image: 0,
      VideoPrompt: 0,
    };
    for (const t of templates) {
      if (t.purpose in counts) {
        counts[t.purpose as PromptPurpose]++;
      }
    }
    return counts;
  }, [templates]);

  // Lọc theo bộ tìm kiếm và scope filter (Tất cả / Global / Override)
  const filteredTemplates = useMemo(() => {
    return purposeTemplates.filter((t) => {
      if (scopeFilter === "global" && t.isOverride) return false;
      if (scopeFilter === "override" && !t.isOverride) return false;

      if (searchQuery.trim()) {
        const q = searchQuery.toLowerCase();
        const matchesId = t.templateId.toLowerCase().includes(q);
        const matchesPurpose = t.purpose.toLowerCase().includes(q);
        return matchesId || matchesPurpose;
      }
      return true;
    });
  }, [purposeTemplates, scopeFilter, searchQuery]);

  // Tự động chọn template mặc định khi chuyển purpose hoặc khi danh sách tải xong
  useEffect(() => {
    // R4: Nếu người dùng đang chủ động ở chế độ tạo template mới, tuyệt đối không tự ghi đè chọn template cũ
    if (isCreatingNew) {
      return;
    }

    if (propSelectedTemplateId) {
      const match = purposeTemplates.find((t) => t.templateId === propSelectedTemplateId);
      if (match) {
        loadTemplateDetail(match.templateId);
        return;
      }
    }

    if (purposeTemplates.length > 0) {
      // Ưu tiên chọn Override trước (resolution precedence: Override > Global)
      const currentSelectedExists = purposeTemplates.some(
        (t) => t.templateId === selectedTemplate?.templateId
      );
      if (!currentSelectedExists) {
        const override = purposeTemplates.find((t) => t.isOverride);
        const target = override || purposeTemplates[0];
        loadTemplateDetail(target.templateId);
      }
    } else {
      setSelectedTemplate(null);
      setCurrentVersion(null);
    }
  }, [purposeTemplates, propSelectedTemplateId, loadTemplateDetail, selectedTemplate?.templateId, isCreatingNew]);

  // R1: Xử lý chuyển chọn template card qua dirty guard
  const handleSelectTemplateCard = (template: PromptTemplateDto) => {
    if (template.templateId === selectedTemplate?.templateId && !isCreatingNew) return;
    requestSwitch(() => {
      setIsCreatingNew(false);
      loadTemplateDetail(template.templateId);
    });
  };

  // R1: Xử lý chuyển tab purpose qua dirty guard
  const handleSelectPurpose = (purpose: PromptPurpose) => {
    if (purpose === activePurpose) return;
    requestSwitch(() => {
      setIsCreatingNew(false);
      setActivePurpose(purpose);
    });
  };

  // R4: Tạo mới một template rỗng cho purpose hiện tại qua dirty guard
  const handleCreateNewTemplate = () => {
    requestSwitch(() => {
      setIsCreatingNew(true);
      setSelectedTemplate(null);
      setCurrentVersion(null);
    });
  };

  // R4: Hủy chế độ tạo mới
  const handleCancelCreateNew = () => {
    setIsCreatingNew(false);
    if (purposeTemplates.length > 0) {
      const override = purposeTemplates.find((t) => t.isOverride);
      const target = override || purposeTemplates[0];
      loadTemplateDetail(target.templateId);
    }
  };

  // Callback sau khi lưu phiên bản mới thành công
  const handleSaveSuccess = (savedTemplate: PromptTemplateDto, savedVersion: PromptVersionDto) => {
    setIsCreatingNew(false);
    setSelectedTemplate(savedTemplate);
    setCurrentVersion(savedVersion);
    fetchTemplates();
  };

  // R3: Callback sau khi rollback thành công: cập nhật template và currentVersion của manager
  const handleRollbackSuccess = (
    rolledBackTemplate: PromptTemplateDto,
    rolledBackVersion: PromptVersionDto
  ) => {
    setSelectedTemplate(rolledBackTemplate);
    setCurrentVersion(rolledBackVersion);
    fetchTemplates(); // Cập nhật lại danh sách template cards
  };

  // Callback sau khi xóa Override thành công
  const handleDeleteOverrideSuccess = () => {
    setIsCreatingNew(false);
    setSelectedTemplate(null);
    setCurrentVersion(null);
    fetchTemplates();
  };

  // R1: Xác nhận bỏ qua thay đổi chưa lưu
  const handleConfirmDiscard = () => {
    setShowDirtyConfirmModal(false);
    setIsEditorDirty(false);
    if (pendingSwitchAction) {
      pendingSwitchAction();
      setPendingSwitchAction(null);
    }
  };

  // R1: Hủy bỏ việc chuyển đổi, giữ nguyên bản nháp
  const handleCancelDiscard = () => {
    setShowDirtyConfirmModal(false);
    setPendingSwitchAction(null);
  };

  return (
    <div className={`prompt-manager-container ${className}`}>
      {/* Thanh tiêu đề header */}
      <div className="prompt-header-bar">
        <div className="prompt-title-group">
          <h1>Quản lý Prompt Templates</h1>
          <p>
            Định nghĩa template bất biến, quản lý phiên bản đơn điệu và ghi đè Workspace theo T10.
            {workspaceId ? (
              <span style={{ marginLeft: "0.5rem", color: "var(--accent)" }}>
                [Ngữ cảnh: Workspace {workspaceId}]
              </span>
            ) : (
              <span style={{ marginLeft: "0.5rem", color: "var(--text-muted)" }}>
                [Ngữ cảnh: Toàn cục]
              </span>
            )}
          </p>
        </div>

        <div className="prompt-header-actions">
          <button
            type="button"
            className="btn btn-secondary btn-sm"
            onClick={fetchTemplates}
            disabled={isLoadingList}
            aria-label="Tải lại danh sách templates"
          >
            <IconRefresh size={14} className={isLoadingList ? "spin" : ""} /> Tải lại
          </button>
          <button
            type="button"
            className="btn btn-primary btn-sm"
            onClick={handleCreateNewTemplate}
            aria-label="Tạo template mới cho mục đích đang chọn"
          >
            <IconPlus size={14} /> Tạo template mới
          </button>
        </div>
      </div>

      {/* Tabs mục đích (Script / Image / VideoPrompt) */}
      <div className="prompt-purpose-tabs" role="tablist" aria-label="Mục đích prompt">
        {PROMPT_PURPOSES.map((purpose) => {
          const isActive = activePurpose === purpose;
          const count = purposeCounts[purpose] || 0;
          return (
            <button
              key={purpose}
              type="button"
              role="tab"
              aria-selected={isActive}
              className={`prompt-tab-btn ${isActive ? "active" : ""}`}
              onClick={() => handleSelectPurpose(purpose)}
            >
              <span>{purpose === "Script" ? "Kịch bản (Script)" : purpose === "Image" ? "Tạo ảnh (Image)" : "Prompt video (VideoPrompt)"}</span>
              <span className="prompt-tab-count">{count}</span>
            </button>
          );
        })}
      </div>

      {/* Thân giao diện chính: Danh sách bên trái + Editor hai cột bên phải */}
      <div className="prompt-body-layout">
        {/* Danh sách templates theo purpose */}
        <aside className="prompt-sidebar-list" aria-label="Danh sách template">
          <div className="prompt-search-bar">
            <div className="prompt-search-input-wrap">
              <span className="prompt-search-icon">
                <IconSearch size={14} />
              </span>
              <input
                type="text"
                className="prompt-search-input"
                placeholder="Tìm template theo ID..."
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                aria-label="Tìm kiếm template"
              />
            </div>

            <div className="prompt-scope-filter">
              <button
                type="button"
                className={`prompt-filter-chip ${scopeFilter === "all" ? "active" : ""}`}
                onClick={() => setScopeFilter("all")}
              >
                Tất cả
              </button>
              <button
                type="button"
                className={`prompt-filter-chip ${scopeFilter === "global" ? "active" : ""}`}
                onClick={() => setScopeFilter("global")}
              >
                Toàn cục
              </button>
              <button
                type="button"
                className={`prompt-filter-chip ${scopeFilter === "override" ? "active" : ""}`}
                onClick={() => setScopeFilter("override")}
              >
                Override
              </button>
            </div>
          </div>

          <div className="prompt-list-scroll">
            {listError && (
              <div className="prompt-alert prompt-alert-error" style={{ margin: "0.5rem" }}>
                <span>{listError}</span>
              </div>
            )}

            {isLoadingList && (
              <div style={{ textAlign: "center", padding: "1.5rem", color: "var(--text-muted)" }}>
                <IconRefresh size={18} className="spin" style={{ marginBottom: "0.4rem" }} />
                <div>Đang tải templates...</div>
              </div>
            )}

            {!isLoadingList && filteredTemplates.length === 0 && (
              <div style={{ textAlign: "center", padding: "2rem 1rem", color: "var(--text-muted)" }}>
                <p style={{ margin: "0 0 0.5rem 0", fontSize: "0.85rem" }}>
                  Chưa có template nào cho mục đích {activePurpose}.
                </p>
                <button
                  type="button"
                  className="btn btn-secondary btn-sm"
                  onClick={handleCreateNewTemplate}
                >
                  <IconPlus size={12} /> Tạo template đầu tiên
                </button>
              </div>
            )}

            {filteredTemplates.map((t) => {
              const isSelected = selectedTemplate?.templateId === t.templateId && !isCreatingNew;
              return (
                <div
                  key={t.templateId}
                  className={`prompt-template-card ${isSelected ? "selected" : ""}`}
                  onClick={() => handleSelectTemplateCard(t)}
                  role="button"
                  tabIndex={0}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" || e.key === " ") {
                      e.preventDefault();
                      handleSelectTemplateCard(t);
                    }
                  }}
                  aria-pressed={isSelected}
                  aria-label={`Chọn template ${t.templateId}`}
                >
                  <div className="prompt-card-top">
                    <span className="prompt-card-title">{t.templateId}</span>
                    <span
                      className={`badge ${t.isOverride ? "badge-override" : "badge-global"}`}
                      style={{ fontSize: "0.65rem", padding: "0.1rem 0.4rem" }}
                    >
                      {t.isOverride ? "Override" : "Global"}
                    </span>
                  </div>

                  <div className="prompt-card-meta">
                    <span>v{t.currentVersionNumber ?? 1}</span>
                    <span>·</span>
                    <span>
                      {t.updatedAtUtc ? new Date(t.updatedAtUtc).toLocaleDateString("vi-VN") : "—"}
                    </span>
                  </div>
                </div>
              );
            })}
          </div>
        </aside>

        {/* Khung Editor chi tiết */}
        <main className="prompt-editor-pane" style={{ flex: 1, display: "flex", flexDirection: "column" }}>
          {isCreatingNew && (
            <div
              style={{
                display: "flex",
                alignItems: "center",
                justifyContent: "space-between",
                padding: "0.6rem 1.25rem",
                backgroundColor: "var(--accent-subtle)",
                borderBottom: "1px solid var(--accent)",
              }}
            >
              <div style={{ fontSize: "0.85rem", fontWeight: 600, color: "var(--accent)" }}>
                ✨ Đang tạo template mới cho mục đích: {activePurpose}
              </div>
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={handleCancelCreateNew}
              >
                Hủy tạo mới
              </button>
            </div>
          )}

          {isLoadingDetail ? (
            <div style={{ margin: "auto", textAlign: "center", color: "var(--text-muted)" }}>
              <IconRefresh size={24} className="spin" style={{ marginBottom: "0.5rem" }} />
              <div>Đang tải chi tiết phiên bản template...</div>
            </div>
          ) : detailError ? (
            <div style={{ margin: "auto", maxWidth: "400px" }}>
              <div className="prompt-alert prompt-alert-error" role="alert">
                <div>⚠️ {detailError}</div>
              </div>
            </div>
          ) : (
            <PromptTemplateEditor
              template={isCreatingNew ? null : selectedTemplate}
              currentVersion={isCreatingNew ? null : currentVersion}
              workspaceId={workspaceId}
              purpose={activePurpose}
              transport={transport}
              onDirtyChange={setIsEditorDirty}
              onSaveSuccess={handleSaveSuccess}
              onRollbackSuccess={handleRollbackSuccess}
              onDeleteOverrideSuccess={handleDeleteOverrideSuccess}
              onApplyPreview={onApplyPreview}
            />
          )}
        </main>
      </div>

      {/* R1: Modal xác nhận rời bản nháp khi có thay đổi chưa lưu */}
      {showDirtyConfirmModal && (
        <div
          className="modal-backdrop"
          style={{
            position: "fixed",
            inset: 0,
            backgroundColor: "var(--backdrop-color)",
            backdropFilter: "blur(2px)",
            zIndex: 1100,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            padding: "1rem",
          }}
          role="alertdialog"
          aria-modal="true"
          aria-labelledby="dirty-confirm-title"
        >
          <div
            style={{
              width: "100%",
              maxWidth: "440px",
              backgroundColor: "var(--surface-card)",
              border: "1px solid var(--border)",
              borderRadius: "var(--radius-lg)",
              boxShadow: "var(--shadow-modal)",
              padding: "1.25rem",
              display: "flex",
              flexDirection: "column",
              gap: "1rem",
            }}
          >
            <div
              id="dirty-confirm-title"
              style={{ fontWeight: 700, fontSize: "1.05rem", color: "var(--text-primary)" }}
            >
              Nội dung bản nháp chưa được lưu!
            </div>
            <div style={{ fontSize: "0.85rem", color: "var(--text-secondary)", lineHeight: 1.5 }}>
              Bạn đang có các thay đổi chưa lưu trong bản nháp. Nếu chuyển sang template khác hoặc thay đổi mục đích,
              các thay đổi này sẽ bị hủy bỏ.
            </div>
            <div style={{ display: "flex", justifyContent: "flex-end", gap: "0.5rem" }}>
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={handleCancelDiscard}
              >
                Ở lại chỉnh sửa
              </button>
              <button
                type="button"
                className="btn btn-primary btn-sm"
                style={{ backgroundColor: "var(--status-error)", borderColor: "var(--status-error)" }}
                onClick={handleConfirmDiscard}
              >
                Bỏ qua thay đổi
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
