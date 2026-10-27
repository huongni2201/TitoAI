import { useState, useEffect, useMemo, useCallback, useRef } from "react";
import { Search, Plus, RefreshCw, Sparkles, AlertTriangle, CheckCircle, FileText } from "lucide-react";
import { cn } from "../../lib/utils.ts";
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
import {
  getStoredPrompts,
  saveStoredPrompts,
  getActivePromptId,
  setActivePromptId,
  DEFAULT_PRESET_PROMPTS,
  type CustomPromptItem,
} from "./promptStorage.ts";

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

  // H5: Workspace ID đã cam kết nội bộ. Khi prop workspaceId đổi từ bên ngoài, chỉ cam kết sau khi vượt qua dirty guard!
  const [committedWorkspaceId, setCommittedWorkspaceId] = useState<string | null | undefined>(workspaceId);

  // Danh sách templates từ backend T10
  const [templates, setTemplates] = useState<PromptTemplateDto[]>([]);
  const [isLoadingList, setIsLoadingList] = useState(false);
  const [listError, setListError] = useState<string | null>(null);

  // Danh sách custom prompts lưu ở client
  const [storedPrompts, setStoredPrompts] = useState<CustomPromptItem[]>(() =>
    getStoredPrompts(workspaceId)
  );

  // ID của prompt đang được chọn làm active cho AI
  const [activePromptId, setActivePromptIdState] = useState<string | null>(() =>
    getActivePromptId(initialPurpose, workspaceId)
  );

  // Prompt / Template đang được hiển thị trong Editor
  const [selectedPromptId, setSelectedPromptId] = useState<string | null>(null);
  const [selectedTemplate, setSelectedTemplate] = useState<PromptTemplateDto | null>(null);
  const [currentVersion, setCurrentVersion] = useState<PromptVersionDto | null>(null);
  const [currentPromptName, setCurrentPromptName] = useState<string>("");

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
  const fetchTemplates = useCallback(async (targetWorkspaceId?: string | null | undefined) => {
    const currentRequestId = ++listRequestIdRef.current;
    setIsLoadingList(true);
    setListError(null);

    const wsToQuery = targetWorkspaceId !== undefined ? targetWorkspaceId : committedWorkspaceId;

    try {
      const result = await promptApi.listTemplates(wsToQuery, transport);

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
  }, [committedWorkspaceId, transport]);

  // H5: Tự động đổi workspace khi prop workspaceId thay đổi NHƯNG PHẢI ĐI QUA dirty guard!
  useEffect(() => {
    if (workspaceId !== committedWorkspaceId) {
      requestSwitch(() => {
        setCommittedWorkspaceId(workspaceId);
        setIsCreatingNew(false);
        setStoredPrompts(getStoredPrompts(workspaceId));
        setActivePromptIdState(getActivePromptId(activePurpose, workspaceId));
        fetchTemplates(workspaceId);
      });
    }
  }, [workspaceId, committedWorkspaceId, requestSwitch, fetchTemplates, activePurpose]);

  // Lần đầu mở: tải danh sách ban đầu
  useEffect(() => {
    fetchTemplates(committedWorkspaceId);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Tải chi tiết template đang chọn từ backend
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
            workspaceId: committedWorkspaceId ?? null,
          },
          transport
        );

        if (currentRequestId !== detailRequestIdRef.current) {
          return;
        }

        setSelectedTemplate(result.template);
        setCurrentVersion(result.currentVersion);
        setSelectedPromptId(result.template.templateId);

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
    [committedWorkspaceId, transport, onSelectTemplate]
  );

  // Danh sách các custom prompts của purpose hiện tại
  const purposeCustomPrompts = useMemo(() => {
    return storedPrompts.filter((p) => p.purpose === activePurpose);
  }, [storedPrompts, activePurpose]);

  // Templates của purpose hiện tại từ backend
  const purposeBackendTemplates = useMemo(() => {
    return templates.filter((t) => t.purpose === activePurpose);
  }, [templates, activePurpose]);

  // Preset prompts mặc định nếu chưa có prompt nào
  const presetsForPurpose = useMemo(() => {
    return DEFAULT_PRESET_PROMPTS[activePurpose] || [];
  }, [activePurpose]);

  // Hợp nhất toàn bộ danh sách prompt hiển thị cho purpose hiện tại
  const allPromptsForPurpose = useMemo(() => {
    const list: Array<{
      id: string;
      name: string;
      purpose: PromptPurpose;
      templateText: string;
      isBackend: boolean;
      backendTemplate?: PromptTemplateDto;
      isOverride?: boolean;
      versionNumber?: number;
      updatedAt?: string;
    }> = [];

    // 1. Thêm các backend templates
    for (const bt of purposeBackendTemplates) {
      list.push({
        id: bt.templateId,
        name: bt.isOverride ? `Prompt tùy chỉnh Workspace (${bt.purpose})` : `Prompt Mặc định Toàn cục (${bt.purpose})`,
        purpose: bt.purpose as PromptPurpose,
        templateText: "", // Sẽ được tải khi load detail
        isBackend: true,
        backendTemplate: bt,
        isOverride: bt.isOverride,
        versionNumber: bt.currentVersionNumber ?? 1,
        updatedAt: bt.updatedAtUtc,
      });
    }

    // 2. Thêm các custom prompts người dùng đã tạo/sửa
    for (const cp of purposeCustomPrompts) {
      // Tránh trùng lặp nếu custom prompt trùng ID với backend template
      if (!list.some((item) => item.id === cp.id)) {
        list.push({
          id: cp.id,
          name: cp.name,
          purpose: cp.purpose,
          templateText: cp.templateText,
          isBackend: false,
          isOverride: true,
          versionNumber: 1,
          updatedAt: cp.updatedAtUtc,
        });
      }
    }

    // 3. Nếu chưa có bất kỳ prompt nào (cả backend lẫn custom), nạp presets mặc định
    if (list.length === 0) {
      for (const preset of presetsForPurpose) {
        list.push({
          id: preset.id,
          name: preset.name,
          purpose: preset.purpose,
          templateText: preset.templateText,
          isBackend: false,
          isOverride: false,
          versionNumber: 1,
          updatedAt: preset.updatedAtUtc,
        });
      }
    }

    return list;
  }, [purposeBackendTemplates, purposeCustomPrompts, presetsForPurpose]);

  // Đếm số lượng template cho mỗi purpose tab
  const purposeCounts = useMemo(() => {
    const counts: Record<PromptPurpose, number> = {
      Script: 0,
      Image: 0,
      VideoPrompt: 0,
    };
    for (const p of PROMPT_PURPOSES) {
      const backendCount = templates.filter((t) => t.purpose === p).length;
      const customCount = storedPrompts.filter((cp) => cp.purpose === p).length;
      const presetCount = DEFAULT_PRESET_PROMPTS[p]?.length || 0;
      counts[p] = backendCount + customCount > 0 ? backendCount + customCount : presetCount;
    }
    return counts;
  }, [templates, storedPrompts]);

  // Lọc theo bộ tìm kiếm và scope filter (Tất cả / Global / Override)
  const filteredTemplates = useMemo(() => {
    return allPromptsForPurpose.filter((t) => {
      if (scopeFilter === "global" && t.isOverride) return false;
      if (scopeFilter === "override" && !t.isOverride) return false;

      if (searchQuery.trim()) {
        const q = searchQuery.toLowerCase();
        const matchesName = t.name.toLowerCase().includes(q);
        const matchesId = t.id.toLowerCase().includes(q);
        const matchesText = t.templateText.toLowerCase().includes(q);
        return matchesName || matchesId || matchesText;
      }
      return true;
    });
  }, [allPromptsForPurpose, scopeFilter, searchQuery]);

  // Tự động chọn prompt mặc định khi chuyển purpose hoặc danh sách tải xong
  useEffect(() => {
    if (isCreatingNew) return;

    if (allPromptsForPurpose.length === 0) {
      setSelectedTemplate(null);
      setCurrentVersion(null);
      setSelectedPromptId(null);
      setDetailError(null);
      return;
    }

    // Đọc active ID đã lưu cho purpose này hoặc propSelectedTemplateId
    const savedActiveId = getActivePromptId(activePurpose, committedWorkspaceId);
    let target = propSelectedTemplateId
      ? allPromptsForPurpose.find((p) => p.id === propSelectedTemplateId)
      : allPromptsForPurpose.find((p) => p.id === savedActiveId);

    if (!target) {
      // Ưu tiên chọn Override trước
      const override = allPromptsForPurpose.find((t) => t.isOverride);
      target = override || allPromptsForPurpose[0];
    }

    if (target) {
      if (!savedActiveId) {
        setActivePromptId(activePurpose, target.id, committedWorkspaceId);
        setActivePromptIdState(target.id);
      }

      // Nếu là backend template thì load detail
      if (target.isBackend) {
        if (selectedTemplate?.templateId !== target.id) {
          loadTemplateDetail(target.id);
        }
      } else {
        // Là custom / preset prompt: gán trực tiếp vào Editor
        setSelectedPromptId(target.id);
        setCurrentPromptName(target.name);
        setSelectedTemplate(null);
        setCurrentVersion({
          versionId: `v-${target.id}`,
          templateId: target.id,
          versionNumber: 1,
          templateText: target.templateText,
          variableDefinitionsJson: "[]",
          changeDescription: null,
          createdAtUtc: target.updatedAt || new Date().toISOString(),
        });
        setDetailError(null);
      }
    }
  }, [
    allPromptsForPurpose,
    activePurpose,
    committedWorkspaceId,
    isCreatingNew,
    loadTemplateDetail,
    selectedTemplate?.templateId,
    propSelectedTemplateId,
  ]);

  // Chọn một Prompt từ danh sách bên trái
  const handleSelectPrompt = (promptItem: (typeof allPromptsForPurpose)[0]) => {
    if (promptItem.id === selectedPromptId && !isCreatingNew) return;

    requestSwitch(() => {
      setIsCreatingNew(false);
      setDetailError(null);
      setSelectedPromptId(promptItem.id);
      setCurrentPromptName(promptItem.name);

      if (promptItem.isBackend) {
        loadTemplateDetail(promptItem.id);
      } else {
        setSelectedTemplate(null);
        setCurrentVersion({
          versionId: `v-${promptItem.id}`,
          templateId: promptItem.id,
          versionNumber: 1,
          templateText: promptItem.templateText,
          variableDefinitionsJson: "[]",
          changeDescription: null,
          createdAtUtc: promptItem.updatedAt || new Date().toISOString(),
        });
      }
    });
  };

  // Chọn prompt này làm Prompt áp dụng (Active) cho tác vụ AI và lưu lại cho lần sau
  const handleSetActivePrompt = (promptIdToActive: string) => {
    setActivePromptId(activePurpose, promptIdToActive, committedWorkspaceId);
    setActivePromptIdState(promptIdToActive);

    // Đồng bộ nội dung sang backend nếu cần
    const target = allPromptsForPurpose.find((p) => p.id === promptIdToActive);
    if (target && target.templateText) {
      promptApi
        .createVersion(
          {
            purpose: activePurpose,
            workspaceId: committedWorkspaceId ?? null,
            templateText: target.templateText,
          },
          transport
        )
        .then(() => fetchTemplates())
        .catch((err) => console.warn("Lỗi đồng bộ prompt active sang backend:", err));
    }
  };

  // Chuyển tab purpose qua dirty guard
  const handleSelectPurpose = (purpose: PromptPurpose) => {
    if (purpose === activePurpose) return;
    requestSwitch(() => {
      setIsCreatingNew(false);
      setDetailError(null);
      setActivePurpose(purpose);
      setActivePromptIdState(getActivePromptId(purpose, committedWorkspaceId));
    });
  };

  // Tạo mới một template rỗng cho purpose hiện tại
  const handleCreateNewTemplate = () => {
    requestSwitch(() => {
      setIsCreatingNew(true);
      setSelectedPromptId(null);
      setSelectedTemplate(null);
      setCurrentVersion(null);
      setCurrentPromptName("");
      setDetailError(null);
    });
  };

  // Hủy chế độ tạo mới
  const handleCancelCreateNew = () => {
    requestSwitch(() => {
      setIsCreatingNew(false);
      setDetailError(null);
      if (allPromptsForPurpose.length > 0) {
        handleSelectPrompt(allPromptsForPurpose[0]);
      }
    });
  };

  // Lưu nội dung prompt hiện hành.
  const handleSaveSuccess = (
    savedTemplate: PromptTemplateDto,
    savedVersion: PromptVersionDto,
    customName?: string
  ) => {
    const finalName = customName?.trim() || currentPromptName.trim() || `Prompt ${activePurpose} mới`;

    // Cập nhật vào custom prompts lưu trữ
    const existingIndex = storedPrompts.findIndex((p) => p.id === savedTemplate.templateId);
    let nextStored: CustomPromptItem[];

    if (existingIndex >= 0) {
      nextStored = [...storedPrompts];
      nextStored[existingIndex] = {
        ...nextStored[existingIndex],
        name: finalName,
        templateText: savedVersion.templateText,
        updatedAtUtc: new Date().toISOString(),
        backendTemplateId: savedTemplate.templateId,
      };
    } else {
      nextStored = [
        ...storedPrompts,
        {
          id: savedTemplate.templateId,
          name: finalName,
          purpose: activePurpose,
          templateText: savedVersion.templateText,
          createdAtUtc: new Date().toISOString(),
          updatedAtUtc: new Date().toISOString(),
          backendTemplateId: savedTemplate.templateId,
        },
      ];
    }

    setStoredPrompts(nextStored);
    saveStoredPrompts(nextStored, committedWorkspaceId);

    // Đặt prompt này làm active prompt và lưu lựa chọn
    setActivePromptId(activePurpose, savedTemplate.templateId, committedWorkspaceId);
    setActivePromptIdState(savedTemplate.templateId);

    setIsCreatingNew(false);
    setSelectedTemplate(savedTemplate);
    setCurrentVersion(savedVersion);
    setSelectedPromptId(savedTemplate.templateId);
    setCurrentPromptName(finalName);
    setDetailError(null);

    fetchTemplates();
  };

  // Callback sau khi xóa Prompt / Override thành công
  const handleDeleteSuccess = () => {
    const deletedId = selectedPromptId || selectedTemplate?.templateId;

    // 1. Xóa khỏi danh sách lưu trữ custom prompts
    if (deletedId) {
      const nextStored = storedPrompts.filter((p) => p.id !== deletedId);
      setStoredPrompts(nextStored);
      saveStoredPrompts(nextStored, committedWorkspaceId);
    }

    // 2. Optimistic update templates state để không bị race condition
    if (deletedId) {
      setTemplates((prev) => prev.filter((t) => t.templateId !== deletedId));
    }

    // 3. Reset các error và state
    setDetailError(null);
    setSelectedTemplate(null);
    setCurrentVersion(null);
    setSelectedPromptId(null);

    // 4. Kiểm tra còn prompt nào khác cho purpose này không
    const remaining = allPromptsForPurpose.filter((p) => p.id !== deletedId);
    if (remaining.length > 0) {
      setIsCreatingNew(false);
      handleSelectPrompt(remaining[0]);
    } else {
      // Khi không còn template nào: Tự động hiển thị form tạo prompt mới như hình 1!
      setIsCreatingNew(true);
      setCurrentPromptName("");
    }

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
    <div className={cn("flex flex-col w-full h-full overflow-hidden bg-background text-foreground", className)}>
      {/* Thanh tiêu đề header */}
      <div className="flex items-center justify-between gap-4 px-6 py-4 bg-card border-b border-border/80 shrink-0 flex-wrap">
        <div>
          <h1 className="text-lg font-bold text-foreground">Quản lý Prompt</h1>
          <p className="mt-0.5 text-xs text-muted-foreground">
            Quản lý và tùy chỉnh các mẫu prompt AI cho Kịch bản, Tạo ảnh và Video.
            {workspaceId ? (
              <span className="ml-2 font-medium text-primary">
                [Ngữ cảnh: Workspace {workspaceId}]
              </span>
            ) : (
              <span className="ml-2 font-medium text-muted-foreground">
                [Ngữ cảnh: Toàn cục]
              </span>
            )}
          </p>
        </div>

        <div className="flex items-center gap-2.5">
          <button
            type="button"
            className="inline-flex items-center justify-center gap-1.5 rounded-md text-xs font-medium h-8 px-3 border border-input bg-background shadow-xs hover:bg-accent hover:text-accent-foreground text-foreground transition-colors disabled:opacity-50 cursor-pointer focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
            onClick={() => fetchTemplates()}
            disabled={isLoadingList}
            aria-label="Tải lại danh sách templates"
          >
            <RefreshCw className={cn("h-3.5 w-3.5", isLoadingList && "animate-spin")} /> Tải lại
          </button>
          <button
            type="button"
            className="inline-flex items-center justify-center gap-1.5 rounded-md text-xs font-medium h-8 px-3.5 bg-primary text-primary-foreground shadow-xs hover:bg-primary/90 transition-colors cursor-pointer focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
            onClick={handleCreateNewTemplate}
            aria-label="Tạo template mới cho mục đích đang chọn"
          >
            <Plus className="h-3.5 w-3.5" /> Tạo prompt mới
          </button>
        </div>
      </div>

      {/* Tabs mục đích (Script / Image / VideoPrompt) */}
      <div
        className="flex items-center gap-2 px-6 py-2 bg-muted/20 border-b border-border/80 shrink-0 overflow-x-auto"
        role="tablist"
        aria-label="Mục đích prompt"
      >
        {PROMPT_PURPOSES.map((purpose) => {
          const isActive = activePurpose === purpose;
          const count = purposeCounts[purpose] || 0;
          return (
            <button
              key={purpose}
              type="button"
              role="tab"
              aria-selected={isActive}
              className={cn(
                "inline-flex items-center gap-2 rounded-md px-3 py-1.5 text-xs font-medium transition-colors cursor-pointer whitespace-nowrap focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring",
                isActive
                  ? "bg-primary text-primary-foreground shadow-xs font-semibold"
                  : "text-muted-foreground hover:bg-muted hover:text-foreground"
              )}
              onClick={() => handleSelectPurpose(purpose)}
            >
              <span>
                {purpose === "Script"
                  ? "Kịch bản & Ý tưởng (Script)"
                  : purpose === "Image"
                    ? "Tạo ảnh (Image)"
                    : "Hướng dẫn AI tạo prompt video"}
              </span>
              <span
                className={cn(
                  "rounded-full px-1.5 py-0.2 text-[10px] font-semibold",
                  isActive
                    ? "bg-primary-foreground/20 text-primary-foreground"
                    : "bg-muted text-muted-foreground"
                )}
              >
                {count}
              </span>
            </button>
          );
        })}
      </div>

      {/* Thân giao diện chính: Danh sách bên trái + Editor hai cột bên phải */}
      <div className="flex flex-1 min-h-0 overflow-hidden">
        {/* Danh sách prompts theo purpose */}
        <aside
          className="w-80 shrink-0 flex flex-col bg-card/40 border-r border-border/80 overflow-hidden"
          aria-label="Danh sách template"
        >
          <div className="p-3 border-b border-border/70 flex flex-col gap-2">
            <div className="relative flex items-center">
              <Search className="absolute left-2.5 h-3.5 w-3.5 text-muted-foreground pointer-events-none" />
              <input
                type="text"
                className="flex h-8 w-full rounded-md border border-input bg-background pl-8 pr-3 py-1 text-xs text-foreground shadow-xs placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
                placeholder="Tìm prompt theo tên, nội dung..."
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                aria-label="Tìm kiếm template"
              />
            </div>

            <div className="flex items-center gap-1.5">
              <button
                type="button"
                className={cn(
                  "rounded px-2.5 py-0.5 text-[11px] font-medium border transition-colors cursor-pointer focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring",
                  scopeFilter === "all"
                    ? "bg-background text-foreground border-border shadow-xs font-semibold"
                    : "border-transparent text-muted-foreground hover:text-foreground hover:bg-muted/60"
                )}
                onClick={() => setScopeFilter("all")}
              >
                Tất cả
              </button>
              <button
                type="button"
                className={cn(
                  "rounded px-2.5 py-0.5 text-[11px] font-medium border transition-colors cursor-pointer focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring",
                  scopeFilter === "global"
                    ? "bg-background text-foreground border-border shadow-xs font-semibold"
                    : "border-transparent text-muted-foreground hover:text-foreground hover:bg-muted/60"
                )}
                onClick={() => setScopeFilter("global")}
              >
                Toàn cục
              </button>
              <button
                type="button"
                className={cn(
                  "rounded px-2.5 py-0.5 text-[11px] font-medium border transition-colors cursor-pointer focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring",
                  scopeFilter === "override"
                    ? "bg-background text-foreground border-border shadow-xs font-semibold"
                    : "border-transparent text-muted-foreground hover:text-foreground hover:bg-muted/60"
                )}
                onClick={() => setScopeFilter("override")}
              >
                Tùy chỉnh
              </button>
            </div>
          </div>

          <div className="flex-1 overflow-y-auto p-2 flex flex-col gap-1.5">
            {listError && (
              <div
                className="flex items-start gap-2 rounded-lg border border-destructive/40 bg-destructive/10 p-2.5 text-xs text-destructive shadow-xs"
                role="alert"
              >
                <AlertTriangle className="h-4 w-4 shrink-0 mt-0.5" />
                <span>{listError}</span>
              </div>
            )}

            {isLoadingList && (
              <div className="text-center py-8 text-muted-foreground text-xs flex flex-col items-center gap-2">
                <RefreshCw className="h-5 w-5 animate-spin" />
                <div>Đang tải danh sách prompt...</div>
              </div>
            )}

            {!isLoadingList && filteredTemplates.length === 0 && (
              <div className="text-center py-8 px-4 text-muted-foreground text-xs flex flex-col items-center gap-2">
                <p className="leading-relaxed">
                  Chưa có template nào cho mục đích {activePurpose}.
                </p>
                <button
                  type="button"
                  className="inline-flex items-center justify-center gap-1.5 rounded-md text-xs font-medium h-7 px-3 border border-input bg-background shadow-xs hover:bg-accent hover:text-accent-foreground text-foreground transition-colors cursor-pointer focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
                  onClick={handleCreateNewTemplate}
                >
                  <Plus className="h-3 w-3" /> Tạo template đầu tiên
                </button>
              </div>
            )}

            {filteredTemplates.map((item) => {
              const isSelected = selectedPromptId === item.id && !isCreatingNew;
              const isActive = activePromptId === item.id;

              return (
                <div
                  key={item.id}
                  className={cn(
                    "flex flex-col gap-1.5 rounded-lg border p-2.5 transition-colors cursor-pointer shadow-xs text-left focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring",
                    isSelected
                      ? "border-primary bg-primary/10 shadow-xs"
                      : "border-border/70 bg-card hover:border-border hover:bg-accent/40"
                  )}
                  onClick={() => handleSelectPrompt(item)}
                  role="button"
                  tabIndex={0}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" || e.key === " ") {
                      e.preventDefault();
                      handleSelectPrompt(item);
                    }
                  }}
                  aria-pressed={isSelected}
                  aria-label={`Chọn prompt ${item.name}`}
                >
                  <div className="flex items-center justify-between gap-2">
                    <span className="font-semibold text-xs text-foreground truncate flex items-center gap-1.5">
                      <FileText className="h-3.5 w-3.5 text-muted-foreground shrink-0" />
                      <span className="truncate">{item.name}</span>
                    </span>
                    {isActive ? (
                      <span className="inline-flex items-center gap-0.5 rounded px-1.5 py-0.2 text-[10px] font-semibold border border-emerald-500/40 bg-emerald-500/15 text-emerald-600 dark:text-emerald-400 shrink-0">
                        <CheckCircle className="h-2.5 w-2.5" /> Áp dụng
                      </span>
                    ) : (
                      <span
                        className={cn(
                          "inline-flex items-center rounded px-1.5 py-0.2 text-[10px] font-medium border shadow-2xs shrink-0",
                          item.isOverride
                            ? "border-purple-500/30 bg-purple-500/10 text-purple-600 dark:text-purple-400"
                            : "border-border bg-muted/60 text-muted-foreground"
                        )}
                      >
                        {item.isOverride ? "Tùy chỉnh" : "Mặc định"}
                      </span>
                    )}
                  </div>

                  {item.templateText && (
                    <div className="text-[11px] text-muted-foreground line-clamp-2 leading-relaxed">
                      {item.templateText}
                    </div>
                  )}

                  <div className="flex items-center gap-1.5 text-[10px] text-muted-foreground font-mono mt-0.5">
                    <span className="truncate max-w-[140px] text-muted-foreground/80">{item.id}</span>
                    <span>·</span>
                    <span>
                      {item.updatedAt ? new Date(item.updatedAt).toLocaleDateString("vi-VN") : "—"}
                    </span>
                  </div>
                </div>
              );
            })}
          </div>
        </aside>

        {/* Khung Editor chi tiết */}
        <main className="flex-1 flex flex-col min-w-0 overflow-hidden bg-background">
          {isCreatingNew && (
            <div className="flex items-center justify-between px-5 py-2.5 bg-primary/10 border-b border-primary/30 shrink-0">
              <div className="text-xs font-semibold text-primary flex items-center gap-1.5">
                <Sparkles className="h-3.5 w-3.5" />
                <span>Đang tạo template mới cho mục đích: {activePurpose}</span>
              </div>
              <button
                type="button"
                className="inline-flex items-center justify-center rounded-md text-xs font-medium h-7 px-2.5 border border-input bg-background shadow-xs hover:bg-accent hover:text-accent-foreground text-foreground transition-colors cursor-pointer focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
                onClick={handleCancelCreateNew}
              >
                Hủy tạo mới
              </button>
            </div>
          )}

          {isLoadingDetail ? (
            <div className="m-auto text-center text-muted-foreground text-xs flex flex-col items-center gap-2">
              <RefreshCw className="h-6 w-6 animate-spin" />
              <div>Đang tải nội dung prompt...</div>
            </div>
          ) : detailError && !isCreatingNew && allPromptsForPurpose.length > 0 ? (
            <div className="m-auto max-w-md p-4 flex flex-col gap-3">
              <div
                className="flex items-start gap-2.5 rounded-lg border border-destructive/40 bg-destructive/10 p-3.5 text-xs text-destructive shadow-xs"
                role="alert"
              >
                <AlertTriangle className="h-4 w-4 shrink-0 mt-0.5" />
                <div>{detailError}</div>
              </div>
              <button
                type="button"
                className="inline-flex items-center justify-center gap-1.5 rounded-md text-xs font-medium h-8 px-3 border border-input bg-background shadow-xs hover:bg-accent hover:text-accent-foreground text-foreground transition-colors cursor-pointer"
                onClick={handleCreateNewTemplate}
              >
                <Plus className="h-3.5 w-3.5" /> Tạo prompt mới
              </button>
            </div>
          ) : (
            <PromptTemplateEditor
              template={isCreatingNew ? null : selectedTemplate}
              currentVersion={isCreatingNew ? null : currentVersion}
              workspaceId={committedWorkspaceId}
              purpose={activePurpose}
              promptName={currentPromptName}
              onNameChange={setCurrentPromptName}
              isActivePrompt={Boolean(selectedPromptId && activePromptId === selectedPromptId)}
              onSetActivePrompt={
                selectedPromptId ? () => handleSetActivePrompt(selectedPromptId) : undefined
              }
              onDeletePrompt={handleDeleteSuccess}
              transport={transport}
              onDirtyChange={setIsEditorDirty}
              onSaveSuccess={handleSaveSuccess}
              onDeleteOverrideSuccess={handleDeleteSuccess}
              onApplyPreview={onApplyPreview}
            />
          )}
        </main>
      </div>

      {/* R1 & H5: Modal xác nhận rời bản nháp khi có thay đổi chưa lưu */}
      {showDirtyConfirmModal && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-xs"
          role="alertdialog"
          aria-modal="true"
          aria-labelledby="dirty-confirm-title"
        >
          <div className="w-full max-w-md rounded-xl border border-border bg-card p-5 shadow-xl flex flex-col gap-3.5 text-card-foreground">
            <div
              id="dirty-confirm-title"
              className="font-bold text-base text-foreground flex items-center gap-2"
            >
              <AlertTriangle className="h-5 w-5 text-amber-500 shrink-0" />
              Nội dung bản nháp chưa được lưu!
            </div>
            <div className="text-xs text-muted-foreground leading-relaxed">
              Bạn đang có các thay đổi chưa lưu trong bản nháp. Nếu chuyển sang template khác hoặc thay đổi mục đích,
              các thay đổi này sẽ bị hủy bỏ.
            </div>
            <div className="flex items-center justify-end gap-2 pt-2">
              <button
                type="button"
                className="inline-flex items-center justify-center rounded-md text-xs font-medium h-8 px-3.5 border border-input bg-background shadow-xs hover:bg-accent hover:text-accent-foreground text-foreground transition-colors cursor-pointer focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
                onClick={handleCancelDiscard}
              >
                Ở lại chỉnh sửa
              </button>
              <button
                type="button"
                className="inline-flex items-center justify-center rounded-md text-xs font-medium h-8 px-3.5 bg-destructive text-destructive-foreground shadow-xs hover:bg-destructive/90 transition-colors cursor-pointer focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
                onClick={handleConfirmDiscard}
              >
                Rời đi (Bỏ qua thay đổi)
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
