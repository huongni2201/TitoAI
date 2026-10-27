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
import { PromptTemplateEditor, type EditorMode } from "./PromptTemplateEditor.tsx";
import {
  getStoredPrompts,
  saveStoredPrompts,
  getActivePromptId,
  setActivePromptId,
  deleteStoredPrompt,
  getDeletedPromptIds,
  DEFAULT_PRESET_PROMPTS,
  type CustomPromptItem,
} from "./promptStorage.ts";

export type { EditorMode };

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

  // Chế độ Editor: "edit" (sửa prompt có sẵn) hoặc "create" (tạo prompt mới)
  const [editorMode, setEditorMode] = useState<EditorMode>("edit");

  // Danh sách templates từ backend T10
  const [templates, setTemplates] = useState<PromptTemplateDto[]>([]);
  const [isLoadingList, setIsLoadingList] = useState(false);
  const [listError, setListError] = useState<string | null>(null);

  // Danh sách custom prompts lưu ở client
  const [storedPrompts, setStoredPrompts] = useState<CustomPromptItem[]>(() =>
    getStoredPrompts(workspaceId)
  );

  // ID của prompt đang được chọn làm active cho AI (Không tự động đổi khi click hoặc chỉnh sửa)
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
        setEditorMode("edit");
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

  // Danh sách prompt IDs đã bị xóa
  const deletedPromptIds = useMemo(() => {
    return getDeletedPromptIds(committedWorkspaceId);
  }, [committedWorkspaceId, storedPrompts]);

  // Preset prompts mặc định nếu chưa bị xóa
  const presetsForPurpose = useMemo(() => {
    const raw = DEFAULT_PRESET_PROMPTS[activePurpose] || [];
    return raw.filter((p) => !deletedPromptIds.has(p.id));
  }, [activePurpose, deletedPromptIds]);

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

    // 1. Thêm các backend templates (loại trừ các ID đã xóa)
    for (const bt of purposeBackendTemplates) {
      if (!deletedPromptIds.has(bt.templateId)) {
        list.push({
          id: bt.templateId,
            name: bt.isOverride ? `Prompt tùy chỉnh theo dự án (${bt.purpose})` : `Prompt mặc định toàn cục (${bt.purpose})`,
          purpose: bt.purpose as PromptPurpose,
          templateText: "",
          isBackend: true,
          backendTemplate: bt,
          isOverride: bt.isOverride,
          versionNumber: bt.currentVersionNumber ?? 1,
          updatedAt: bt.updatedAtUtc,
        });
      }
    }

    // 2. Thêm các custom prompts người dùng đã tạo/sửa
    for (const cp of purposeCustomPrompts) {
      if (!deletedPromptIds.has(cp.id) && !list.some((item) => item.id === cp.id)) {
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

    // 3. Thêm các presets mặc định nếu chưa bị xóa
    for (const preset of presetsForPurpose) {
      if (!deletedPromptIds.has(preset.id) && !list.some((item) => item.id === preset.id)) {
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
  }, [purposeBackendTemplates, purposeCustomPrompts, presetsForPurpose, deletedPromptIds]);

  // Đếm số lượng template cho mỗi purpose tab
  const purposeCounts = useMemo(() => {
    const counts: Record<PromptPurpose, number> = {
      Script: 0,
      Image: 0,
      VideoPrompt: 0,
    };
    for (const p of PROMPT_PURPOSES) {
      const backendCount = templates.filter((t) => t.purpose === p && !deletedPromptIds.has(t.templateId)).length;
      const customCount = storedPrompts.filter((cp) => cp.purpose === p && !deletedPromptIds.has(cp.id)).length;
      const presetCount = (DEFAULT_PRESET_PROMPTS[p] || []).filter((pr) => !deletedPromptIds.has(pr.id)).length;
      counts[p] = backendCount + customCount > 0 ? backendCount + customCount : presetCount;
    }
    return counts;
  }, [templates, storedPrompts, deletedPromptIds]);

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

  // Tự động chọn prompt mặc định khi nạp danh sách ban đầu hoặc khi không có prompt được chọn
  useEffect(() => {
    if (editorMode === "create") return;

    if (allPromptsForPurpose.length === 0) {
      setSelectedTemplate(null);
      setCurrentVersion(null);
      setSelectedPromptId(null);
      setDetailError(null);
      setEditorMode("create");
      return;
    }

    // Nếu đã có selectedPromptId hợp lệ trong danh sách, giữ nguyên
    if (selectedPromptId && allPromptsForPurpose.some((p) => p.id === selectedPromptId)) {
      return;
    }

    // Đọc active ID đã lưu cho purpose này hoặc propSelectedTemplateId
    const savedActiveId = getActivePromptId(activePurpose, committedWorkspaceId);
    let target = propSelectedTemplateId
      ? allPromptsForPurpose.find((p) => p.id === propSelectedTemplateId)
      : allPromptsForPurpose.find((p) => p.id === savedActiveId);

    if (!target) {
      const override = allPromptsForPurpose.find((t) => t.isOverride);
      target = override || allPromptsForPurpose[0];
    }

    if (target) {
      // Nếu chưa có activePromptId nào được chọn, thiết lập mặc định ban đầu
      if (!savedActiveId) {
        setActivePromptId(activePurpose, target.id, committedWorkspaceId);
        setActivePromptIdState(target.id);
      }

      setEditorMode("edit");
      setSelectedPromptId(target.id);
      setCurrentPromptName(target.name);

      if (target.isBackend) {
        if (selectedTemplate?.templateId !== target.id) {
          loadTemplateDetail(target.id);
        }
      } else {
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
    editorMode,
    selectedPromptId,
    loadTemplateDetail,
    selectedTemplate?.templateId,
    propSelectedTemplateId,
  ]);

  // Chọn một Prompt từ danh sách bên trái (Chuyển sang chế độ chỉnh sửa)
  // Quy tắc §2.1: Trạng thái Áp dụng KHÔNG tự đổi khi click hoặc chỉnh sửa!
  const handleSelectPrompt = (promptItem: (typeof allPromptsForPurpose)[0]) => {
    if (promptItem.id === selectedPromptId && editorMode === "edit") return;

    requestSwitch(() => {
      setEditorMode("edit");
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
      setEditorMode("edit");
      setDetailError(null);
      setActivePurpose(purpose);
      setActivePromptIdState(getActivePromptId(purpose, committedWorkspaceId));
    });
  };

  // Tạo mới một template rỗng cho purpose hiện tại (Chuyển sang chế độ tạo mới)
  const handleCreateNewTemplate = () => {
    requestSwitch(() => {
      setEditorMode("create");
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
      setEditorMode("edit");
      setDetailError(null);
      if (allPromptsForPurpose.length > 0) {
        handleSelectPrompt(allPromptsForPurpose[0]);
      }
    });
  };

  // Lưu nội dung prompt: Lưu mẫu gốc (Prompt Template), không ghi đè Rendered Prompt lên Template
  const handleSaveSuccess = (
    savedTemplate: PromptTemplateDto,
    savedVersion: PromptVersionDto,
    customName?: string
  ) => {
    const finalName = customName?.trim() || currentPromptName.trim() || `Prompt ${activePurpose} mới`;

    // Cập nhật vào custom prompts lưu trữ
    const targetId = selectedPromptId || savedTemplate.templateId;
    const existingIndex = storedPrompts.findIndex((p) => p.id === targetId);
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
          id: targetId,
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

    // Lưu ý quy tắc §2.1: Trạng thái Áp dụng không tự đổi khi click hoặc chỉnh sửa!
    // Chỉ tự gán nếu chưa từng có prompt active nào
    if (!activePromptId) {
      setActivePromptId(activePurpose, targetId, committedWorkspaceId);
      setActivePromptIdState(targetId);
    }

    setEditorMode("edit");
    setSelectedTemplate(savedTemplate);
    setCurrentVersion(savedVersion);
    setSelectedPromptId(targetId);
    setCurrentPromptName(finalName);
    setIsEditorDirty(false);
    setDetailError(null);

    fetchTemplates();
  };

  // Callback sau khi xóa Prompt thành công (Fix triệt để bug không xóa được prompt)
  const handleDeleteSuccess = () => {
    const deletedId = selectedPromptId || selectedTemplate?.templateId;
    if (!deletedId) return;

    // 1. Xóa vĩnh viễn khỏi danh sách lưu trữ custom prompts và ghi nhận blacklist
    deleteStoredPrompt(deletedId, committedWorkspaceId);

    // 2. Cập nhật state local
    const nextStored = storedPrompts.filter((p) => p.id !== deletedId);
    setStoredPrompts(nextStored);

    // 3. Optimistic update templates state
    setTemplates((prev) => prev.filter((t) => t.templateId !== deletedId));

    // 4. Nếu prompt bị xóa đang là active prompt, xóa active pointer
    if (activePromptId === deletedId) {
      setActivePromptIdState(null);
    }

    // 5. Reset các error và state editor
    setIsEditorDirty(false);
    setDetailError(null);
    setSelectedTemplate(null);
    setCurrentVersion(null);
    setSelectedPromptId(null);

    // 6. Kiểm tra còn prompt nào khác cho purpose này không
    const remaining = allPromptsForPurpose.filter((p) => p.id !== deletedId);
    if (remaining.length > 0) {
      setEditorMode("edit");
      const nextPrompt = remaining[0];
      setSelectedPromptId(nextPrompt.id);
      setCurrentPromptName(nextPrompt.name);
      if (nextPrompt.isBackend) {
        loadTemplateDetail(nextPrompt.id);
      } else {
        setCurrentVersion({
          versionId: `v-${nextPrompt.id}`,
          templateId: nextPrompt.id,
          versionNumber: 1,
          templateText: nextPrompt.templateText,
          variableDefinitionsJson: "[]",
          changeDescription: null,
          createdAtUtc: nextPrompt.updatedAt || new Date().toISOString(),
        });
      }
    } else {
      setEditorMode("create");
      setCurrentPromptName("");
    }

    fetchTemplates();
  };

  // R1: Xác nhận bỏ qua thay đổi chưa lưu
  const handleConfirmDiscard = () => {
    setShowDirtyConfirmModal(false);
    setIsEditorDirty(false);
    if (pendingSwitchAction) {
      const action = pendingSwitchAction;
      setPendingSwitchAction(null);
      action();
    }
  };

  // R1: Hủy bỏ chuyển đổi, ở lại tiếp tục chỉnh sửa bản nháp
  const handleCancelDiscard = () => {
    setShowDirtyConfirmModal(false);
    setPendingSwitchAction(null);
  };

  const isCreatingNew = editorMode === "create";

  return (
    <div className={cn("flex flex-col w-full h-full min-h-0 bg-background text-foreground", className)}>
      {/* Top Header: Thanh tiêu đề và nút Tạo mới / Tải lại */}
      <div className="flex items-center justify-between gap-4 px-6 py-4 border-b border-border bg-card shrink-0 flex-wrap">
        <div>
          <h1 className="text-xl font-bold tracking-tight text-foreground flex items-center gap-2">
            Quản lý Prompt
          </h1>
          <p className="text-xs text-muted-foreground mt-0.5">
            Quản lý và tùy chỉnh các mẫu prompt AI cho kịch bản, tạo ảnh và video.
          </p>
        </div>

        <div className="flex items-center gap-2">
          <button
            type="button"
            className="inline-flex items-center justify-center gap-1.5 rounded-md text-xs font-medium h-8 px-3 border border-input bg-background shadow-xs hover:bg-accent hover:text-accent-foreground text-foreground transition-colors disabled:opacity-50 cursor-pointer focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
            onClick={() => {
              fetchTemplates(committedWorkspaceId);
              setStoredPrompts(getStoredPrompts(committedWorkspaceId));
            }}
            disabled={isLoadingList}
            title="Tải lại danh sách prompt"
          >
            <RefreshCw className={cn("h-3.5 w-3.5", isLoadingList && "animate-spin")} />
            <span>Tải lại</span>
          </button>

          <button
            type="button"
            className="inline-flex items-center justify-center gap-1.5 rounded-md text-xs font-medium h-8 px-3.5 bg-primary text-primary-foreground shadow-xs hover:bg-primary/90 transition-colors cursor-pointer focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
            onClick={handleCreateNewTemplate}
            title="Tạo prompt mới cho mục đích hiện tại"
          >
            <Plus className="h-3.5 w-3.5" />
            <span>Tạo prompt mới</span>
          </button>
        </div>
      </div>

      {/* Tabs chuyển đổi Mục đích (Purpose Tabs: Script, Image, VideoPrompt) */}
      <div className="flex items-center gap-1.5 px-6 pt-3 pb-0 border-b border-border bg-card shrink-0 overflow-x-auto">
        {PROMPT_PURPOSES.map((p) => {
          const isCurrentActive = p === activePurpose;
          const count = purposeCounts[p] || 0;
          const label =
            p === "Script"
              ? "Kịch bản & Ý tưởng (Script)"
              : p === "Image"
                ? "Tạo ảnh (Image)"
                : "Hướng dẫn AI tạo prompt video";

          return (
            <button
              key={p}
              type="button"
              className={cn(
                "inline-flex items-center gap-2 px-3.5 py-2 text-xs font-medium border-b-2 transition-colors cursor-pointer whitespace-nowrap",
                isCurrentActive
                  ? "border-primary text-primary font-semibold"
                  : "border-transparent text-muted-foreground hover:text-foreground hover:border-border"
              )}
              onClick={() => handleSelectPurpose(p)}
              role="tab"
              aria-selected={isCurrentActive}
            >
              <span>{label}</span>
              <span
                className={cn(
                  "inline-flex items-center justify-center rounded-full px-1.5 py-0.2 text-[10px] font-mono",
                  isCurrentActive
                    ? "bg-primary text-primary-foreground font-bold"
                    : "bg-muted text-muted-foreground"
                )}
              >
                {count}
              </span>
            </button>
          );
        })}
      </div>

      {/* Khung nội dung chính: Cột trái (Danh sách) + Cột phải (Editor & Variables Form) */}
      <div className="flex-1 min-h-0 flex overflow-hidden">
        {/* CỘT TRÁI: Tìm kiếm và Danh sách Prompt */}
        <aside className="w-80 border-r border-border bg-card flex flex-col shrink-0 overflow-hidden">
          {/* Ô tìm kiếm */}
          <div className="p-3 border-b border-border/80">
            <div className="relative">
              <Search className="absolute left-2.5 top-2.5 h-3.5 w-3.5 text-muted-foreground" />
              <input
                type="text"
                className="flex h-8 w-full rounded-md border border-input bg-background pl-8 pr-3 text-xs text-foreground placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring shadow-xs"
                placeholder="Tìm prompt theo tên, nội dung..."
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
              />
            </div>
            {/* Bộ lọc phạm vi */}
            <div className="flex items-center gap-1 mt-2">
              <button
                type="button"
                className={cn(
                  "px-2 py-0.5 rounded text-[11px] font-medium transition-colors cursor-pointer",
                  scopeFilter === "all"
                    ? "bg-secondary text-secondary-foreground font-semibold"
                    : "text-muted-foreground hover:bg-muted"
                )}
                onClick={() => setScopeFilter("all")}
              >
                Tất cả
              </button>
              <button
                type="button"
                className={cn(
                  "px-2 py-0.5 rounded text-[11px] font-medium transition-colors cursor-pointer",
                  scopeFilter === "global"
                    ? "bg-secondary text-secondary-foreground font-semibold"
                    : "text-muted-foreground hover:bg-muted"
                )}
                onClick={() => setScopeFilter("global")}
              >
                Toàn cục
              </button>
              <button
                type="button"
                className={cn(
                  "px-2 py-0.5 rounded text-[11px] font-medium transition-colors cursor-pointer",
                  scopeFilter === "override"
                    ? "bg-secondary text-secondary-foreground font-semibold"
                    : "text-muted-foreground hover:bg-muted"
                )}
                onClick={() => setScopeFilter("override")}
              >
                Tùy chỉnh
              </button>
            </div>
          </div>

          {/* Danh sách các Prompt */}
          <div className="flex-1 overflow-y-auto p-2 flex flex-col gap-1.5">
            {isLoadingList && allPromptsForPurpose.length === 0 && (
              <div className="p-4 text-center text-xs text-muted-foreground flex flex-col items-center gap-2">
                <RefreshCw className="h-4 w-4 animate-spin text-primary" />
                <span>Đang tải danh sách prompt...</span>
              </div>
            )}

            {listError && (
              <div className="p-3 m-1 rounded-md border border-destructive/40 bg-destructive/10 text-xs text-destructive flex items-start gap-2">
                <AlertTriangle className="h-4 w-4 shrink-0 mt-0.5" />
                <div className="flex-1 leading-relaxed">{listError}</div>
              </div>
            )}

            {!isLoadingList && filteredTemplates.length === 0 && (
              <div className="p-6 text-center text-xs text-muted-foreground flex flex-col items-center gap-2">
                <FileText className="h-8 w-8 text-muted-foreground/40" />
                <span>Không tìm thấy prompt nào.</span>
                <button
                  type="button"
                  className="mt-1 text-primary hover:underline text-xs font-medium cursor-pointer"
                  onClick={handleCreateNewTemplate}
                >
                  + Tạo prompt mới ngay
                </button>
              </div>
            )}

            {filteredTemplates.map((item) => {
              const isSelected = selectedPromptId === item.id && !isCreatingNew;
              const isApplied = activePromptId === item.id;

              return (
                <div
                  key={item.id}
                  className={cn(
                    "group relative flex flex-col gap-1 rounded-lg border p-3 text-left text-xs transition-all cursor-pointer shadow-xs",
                    isSelected
                      ? "border-primary bg-primary/5 text-foreground shadow-xs ring-1 ring-primary/40"
                      : "border-border/80 bg-card hover:border-border hover:bg-accent/40 text-muted-foreground hover:text-foreground"
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
                >
                  <div className="flex items-start justify-between gap-1.5">
                    <span className="font-semibold text-foreground line-clamp-1 flex-1 flex items-center gap-1.5">
                      <FileText className="h-3.5 w-3.5 shrink-0 text-muted-foreground" />
                      {item.name}
                    </span>

                    {/* Huy hiệu Áp dụng / Mặc định / Tùy chỉnh */}
                    {isApplied ? (
                      <span className="inline-flex items-center gap-0.5 rounded px-1.5 py-0.2 text-[10px] font-medium bg-emerald-500/15 text-emerald-600 dark:text-emerald-400 border border-emerald-500/30 shrink-0">
                        <CheckCircle className="h-2.5 w-2.5" /> Áp dụng
                      </span>
                    ) : item.isOverride ? (
                      <span className="inline-flex items-center rounded px-1.5 py-0.2 text-[10px] font-medium bg-purple-500/15 text-purple-600 dark:text-purple-400 border border-purple-500/30 shrink-0">
                        Tùy chỉnh
                      </span>
                    ) : (
                      <span className="inline-flex items-center rounded px-1.5 py-0.2 text-[10px] font-medium bg-muted text-muted-foreground border border-border shrink-0">
                        Mặc định
                      </span>
                    )}
                  </div>

                  {item.templateText && (
                    <p className="line-clamp-2 text-[11px] text-muted-foreground/90 font-mono leading-relaxed mt-0.5">
                      {item.templateText}
                    </p>
                  )}

                  <div className="flex items-center justify-between text-[10px] text-muted-foreground/80 mt-1 pt-1 border-t border-border/40 font-mono">
                    <span className="truncate max-w-[140px]" title={item.id}>
                      {item.id}
                    </span>
                    {item.updatedAt && (
                      <span>
                        {new Date(item.updatedAt).toLocaleDateString("vi-VN")}
                      </span>
                    )}
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
                <span>Đang tạo prompt mới cho mục đích: {activePurpose}</span>
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
              mode={editorMode}
              selectedPromptId={selectedPromptId}
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
