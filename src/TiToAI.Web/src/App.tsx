import { useEffect, useRef, useState, useCallback } from "react";
import { ThemeProvider, useTheme } from "./context/ThemeContext";
import { bridgeClient } from "./services/bridgeClient";
import type { WorkspaceSummaryDto, WorkspaceListResponseData } from "./contracts/dtos";
import type { ErrorDto } from "./contracts/bridge";
import {
  LayoutDashboard,
  FolderKanban,
  Video,
  Images,
  Settings,
  HardDrive,
  SunMedium,
  Moon,
  ListOrdered,
  Search,
  ChevronDown,
  RefreshCw,
  Check,
  Sparkles,
} from "lucide-react";
import { BrandLogo } from "@/components/BrandLogo";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { SettingsPage } from "./features/settings/SettingsPage";
import { QueueDrawer } from "./components/QueueDrawer";
import { jobApi } from "./features/jobs/jobApi";
import type { JobSummaryDto, JobDetailDto } from "./features/jobs/jobTypes";
import { WorkspaceListPage } from "./features/workspaces/WorkspaceListPage";
import { WorkspaceOverviewPage } from "./features/workspaces/WorkspaceOverviewPage";
import { ProjectListPage } from "./features/projects/ProjectListPage";
import { ProjectPage } from "./features/projects/ProjectPage";
import { PromptTemplateManager } from "./features/prompts";
import { LibraryPage } from "./features/library/LibraryPage";
import { cn } from "@/lib/utils";

export type ActiveRoute =
  | "overview"
  | "workspace-overview"
  | "workspaces"
  | "projects"
  | "project-detail"
  | "browsers"
  | "browser-manager"
  | "library"
  | "prompt-templates"
  | "settings";

function AppContent() {
  const { preference, effectiveTheme, toggleTheme } = useTheme();

  const [activeRoute, setActiveRoute] = useState<ActiveRoute>("workspaces");
  const [workspaces, setWorkspaces] = useState<WorkspaceSummaryDto[]>([]);
  const [selectedWorkspaceId, setSelectedWorkspaceId] = useState<string | null>(null);
  const [selectedProjectId, setSelectedProjectId] = useState<string | null>(null);
  const [isLoadingWorkspaces, setIsLoadingWorkspaces] = useState<boolean>(true);
  const [workspaceError, setWorkspaceError] = useState<ErrorDto | null>(null);

  const [isWorkspaceDropdownOpen, setIsWorkspaceDropdownOpen] = useState(false);
  const [isQueueOpen, setIsQueueOpen] = useState(false);

  // References cho việc phục hồi focus sau khi đóng drawer và quản lý lifecycle
  const settingsBtnRef = useRef<HTMLButtonElement>(null);
  const queueBtnRef = useRef<HTMLButtonElement>(null);
  const workspaceDropdownRef = useRef<HTMLDivElement>(null);
  const abortControllerRef = useRef<AbortController | null>(null);

  // Gọi workspace.list bằng typed bridge client kèm guard chống stale state / race conditions
  const fetchWorkspaces = useCallback(async () => {
    // Hủy request trước đó nếu còn đang chờ phản hồi
    if (abortControllerRef.current) {
      abortControllerRef.current.abort();
    }
    const controller = new AbortController();
    abortControllerRef.current = controller;

    setIsLoadingWorkspaces(true);
    setWorkspaceError(null);

    try {
      const response = await bridgeClient.invoke<WorkspaceListResponseData>(
        "workspace.list",
        {},
        { signal: controller.signal },
      );

      if (controller.signal.aborted) return;

      const list = response?.workspaces ?? [];
      setWorkspaces(list);

      setSelectedWorkspaceId((currentId) => {
        if (list.length > 0) {
          const stillExists = list.some((ws) => ws.workspaceId === currentId);
          if (!currentId || !stillExists) {
            bridgeClient.setActiveWorkspaceId(list[0].workspaceId);
            return list[0].workspaceId;
          }
          return currentId;
        } else {
          bridgeClient.setActiveWorkspaceId(null);
          return null;
        }
      });
    } catch (err) {
      if (controller.signal.aborted) return;
      console.warn("[TiToAI Web] Lỗi tải danh sách Workspace:", err);
      setWorkspaceError(err as ErrorDto);
    } finally {
      if (!controller.signal.aborted) {
        setIsLoadingWorkspaces(false);
      }
    }
  }, []);

  useEffect(() => {
    fetchWorkspaces();
    return () => {
      if (abortControllerRef.current) {
        abortControllerRef.current.abort();
      }
    };
  }, [fetchWorkspaces]);

  // Đóng dropdown khi click ra ngoài hoặc bấm Escape
  useEffect(() => {
    if (!isWorkspaceDropdownOpen) return;

    const handleClickOutside = (e: MouseEvent) => {
      if (
        workspaceDropdownRef.current &&
        !workspaceDropdownRef.current.contains(e.target as Node)
      ) {
        setIsWorkspaceDropdownOpen(false);
      }
    };

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        setIsWorkspaceDropdownOpen(false);
        const btn =
          workspaceDropdownRef.current?.querySelector<HTMLButtonElement>(
            ".workspace-selector-btn",
          );
        btn?.focus();
      }
    };

    document.addEventListener("mousedown", handleClickOutside);
    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("mousedown", handleClickOutside);
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [isWorkspaceDropdownOpen]);

  const selectedWorkspace = workspaces.find(
    (w) => w.workspaceId === selectedWorkspaceId,
  );

  const handleSelectWorkspace = (id: string) => {
    setSelectedWorkspaceId(id);
    bridgeClient.setActiveWorkspaceId(id);
    setIsWorkspaceDropdownOpen(false);
  };

  // Quản lý số lượng tác vụ đang hoạt động thực tế trên Topbar
  const [activeJobCount, setActiveJobCount] = useState<number>(0);

  const fetchActiveJobCount = useCallback(async () => {
    try {
      const activeJobs = await jobApi.listActive();
      const count = activeJobs.filter(
        (j) =>
          j.state === "Running" ||
          j.state === "Queued" ||
          j.state === "Reserved" ||
          j.state === "AwaitingUser",
      ).length;
      setActiveJobCount(count);
    } catch {
      // Nuốt lỗi kết nối nền
    }
  }, []);

  useEffect(() => {
    fetchActiveJobCount();
    const unsubState = jobApi.onStateChanged(() => {
      fetchActiveJobCount();
    });
    return () => {
      unsubState();
    };
  }, [fetchActiveJobCount]);

  const [isSettingsDirty, setIsSettingsDirty] = useState<boolean>(false);
  const [pendingRoute, setPendingRoute] = useState<ActiveRoute | null>(null);

  const handleOpenSettings = () => {
    setIsQueueOpen(false);
    setIsWorkspaceDropdownOpen(false);
    setActiveRoute("settings");
  };

  const handleNavigate = (targetRoute: ActiveRoute) => {
    if (activeRoute === targetRoute) return;
    if (targetRoute === "browsers" || targetRoute === "browser-manager") {
      handleOpenSettings();
      return;
    }
    if (activeRoute === "settings" && isSettingsDirty) {
      setPendingRoute(targetRoute);
      return;
    }
    if (targetRoute === "settings") {
      handleOpenSettings();
    } else {
      setActiveRoute(targetRoute);
    }
  };

  const handleConfirmDiscardSettings = () => {
    setIsSettingsDirty(false);
    if (pendingRoute) {
      setActiveRoute(pendingRoute);
      setPendingRoute(null);
    }
  };

  const handleCancelDiscardSettings = () => {
    setPendingRoute(null);
  };

  const handleOpenQueue = () => {
    setIsWorkspaceDropdownOpen(false);
    setIsQueueOpen(true);
  };

  const handleNavigateToJob = useCallback(
    (job: JobSummaryDto | JobDetailDto) => {
      setIsQueueOpen(false);
      if (job.workspaceId && job.workspaceId !== selectedWorkspaceId) {
        handleSelectWorkspace(job.workspaceId);
      }
      if (job.projectId) {
        setSelectedProjectId(job.projectId);
        setActiveRoute("project-detail");
      }
    },
    [selectedWorkspaceId],
  );

  const handleNavigateToSettingsAccounts = useCallback(() => {
    setIsQueueOpen(false);
    handleNavigate("settings");
  }, [handleNavigate]);

  const isModalActive = isQueueOpen;

  const getBreadcrumbTitle = () => {
    switch (activeRoute) {
      case "overview":
      case "workspace-overview":
        return "Tổng quan";
      case "workspaces":
        return "Danh sách Workspace";
      case "projects":
      case "project-detail":
        return "Dự án";
      case "browsers":
      case "browser-manager":
        return "Quản lý Trình duyệt";
      case "prompt-templates":
        return "Prompt";
      case "library":
        return "Tài nguyên";
      case "settings":
        return "Cài đặt";
      default:
        return "TiToAI";
    }
  };

  const isOverviewActive =
    activeRoute === "overview" || activeRoute === "workspace-overview";
  const isWorkspacesActive = activeRoute === "workspaces";
  const isProjectsActive =
    activeRoute === "projects" || activeRoute === "project-detail";
  const isPromptTemplatesActive = activeRoute === "prompt-templates";
  const isLibraryActive = activeRoute === "library";
  const isSettingsActive = activeRoute === "settings";

  return (
    <div className="flex h-screen w-screen overflow-hidden bg-background text-foreground antialiased font-sans select-none">
      {/* 1. SIDEBAR TRÁI CỐ ĐỊNH (Khóa tương tác khi modal/drawer mở) */}
      <aside
        className="w-60 min-w-60 border-r border-sidebar-border bg-sidebar flex flex-col h-screen text-sidebar-foreground select-none shrink-0"
        aria-label="Điều hướng chính"
        inert={isModalActive}
        aria-hidden={isModalActive ? true : undefined}
      >
        {/* Brand Logo trên Sidebar */}
        <div className="h-14 flex items-center gap-3 px-4 border-b border-sidebar-border shrink-0">
          <BrandLogo size={24} className="text-primary shrink-0" />
          <div className="flex flex-col min-w-0">
            <span className="font-bold text-foreground tracking-tight text-base leading-none">
              TiToAI
            </span>
            <span className="text-[10px] text-muted-foreground font-medium mt-1 leading-none">
              Desktop Studio
            </span>
          </div>
        </div>

        {/* Sidebar Navigation items */}
        <nav
          className="sidebar-nav flex flex-col gap-1 px-3 py-4 flex-1 overflow-y-auto"
          aria-label="Menu chức năng"
        >
          <button
            type="button"
            className={cn(
              "nav-item flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm transition-colors text-left font-normal cursor-pointer",
              isOverviewActive
                ? "bg-primary/10 text-primary font-medium border-l-2 border-primary"
                : "text-sidebar-foreground hover:bg-muted/60 hover:text-foreground",
            )}
            onClick={() => handleNavigate("overview")}
            aria-current={isOverviewActive ? "page" : undefined}
          >
            <LayoutDashboard className="size-4 shrink-0" />
            <span>Tổng quan</span>
          </button>

          <button
            type="button"
            className={cn(
              "nav-item flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm transition-colors text-left font-normal cursor-pointer",
              isWorkspacesActive
                ? "bg-primary/10 text-primary font-medium border-l-2 border-primary"
                : "text-sidebar-foreground hover:bg-muted/60 hover:text-foreground",
            )}
            onClick={() => handleNavigate("workspaces")}
            aria-current={isWorkspacesActive ? "page" : undefined}
          >
            <FolderKanban className="size-4 shrink-0" />
            <span>Workspaces</span>
            {!isLoadingWorkspaces && !workspaceError && workspaces.length > 0 && (
              <span className="ml-auto rounded-full bg-primary/20 px-2 py-0.5 text-xs font-semibold text-primary">
                {workspaces.length}
              </span>
            )}
          </button>

          <button
            type="button"
            className={cn(
              "nav-item flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm transition-colors text-left font-normal cursor-pointer",
              isProjectsActive
                ? "bg-primary/10 text-primary font-medium border-l-2 border-primary"
                : "text-sidebar-foreground hover:bg-muted/60 hover:text-foreground",
            )}
            onClick={() => handleNavigate("projects")}
            aria-current={isProjectsActive ? "page" : undefined}
          >
            <Video className="size-4 shrink-0" />
            <span>Dự án</span>
          </button>

          <button
            type="button"
            className={cn(
              "nav-item flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm transition-colors text-left font-normal cursor-pointer",
              isPromptTemplatesActive
                ? "bg-primary/10 text-primary font-medium border-l-2 border-primary"
                : "text-sidebar-foreground hover:bg-muted/60 hover:text-foreground",
            )}
            onClick={() => handleNavigate("prompt-templates")}
            aria-current={isPromptTemplatesActive ? "page" : undefined}
          >
            <Sparkles className="size-4 shrink-0" />
            <span>Prompt</span>
          </button>

          <button
            type="button"
            className={cn(
              "nav-item flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm transition-colors text-left font-normal cursor-pointer",
              isLibraryActive
                ? "bg-primary/10 text-primary font-medium border-l-2 border-primary"
                : "text-sidebar-foreground hover:bg-muted/60 hover:text-foreground",
            )}
            onClick={() => handleNavigate("library")}
            aria-current={isLibraryActive ? "page" : undefined}
          >
            <Images className="size-4 shrink-0" />
            <span>Tài nguyên</span>
          </button>

          <div className="my-2 border-t border-sidebar-border" />

          <button
            ref={settingsBtnRef}
            type="button"
            className={cn(
              "nav-item flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm transition-colors text-left font-normal cursor-pointer",
              isSettingsActive
                ? "bg-primary/10 text-primary font-medium border-l-2 border-primary"
                : "text-sidebar-foreground hover:bg-muted/60 hover:text-foreground",
            )}
            onClick={() => handleNavigate("settings")}
            aria-current={isSettingsActive ? "page" : undefined}
          >
            <Settings className="size-4 shrink-0" />
            <span>Cài đặt</span>
          </button>
        </nav>

        {/* Footer của Sidebar */}
        <div className="p-3 border-t border-sidebar-border mt-auto flex flex-col gap-2 shrink-0">
          <div className="flex items-center gap-2.5 p-2.5 rounded-lg bg-card/60 border border-sidebar-border/80">
            <HardDrive className="size-4 text-muted-foreground shrink-0" />
            <div className="flex flex-col min-w-0">
              <span className="text-[10px] text-muted-foreground font-medium leading-tight">
                Lưu trữ cục bộ
              </span>
              <span className="text-xs text-foreground font-semibold truncate leading-tight">
                Sẵn sàng
              </span>
            </div>
          </div>
          <div className="text-[10px] text-center text-muted-foreground/60 font-medium py-0.5">
            TiToAI v0.1.0
          </div>
        </div>
      </aside>

      {/* 2. KHU VỰC CHÍNH (TOPBAR + NỘI DUNG) */}
      <div
        className="flex-1 flex flex-col overflow-hidden bg-background"
        inert={isModalActive}
        aria-hidden={isModalActive ? true : undefined}
      >
        {/* TOPBAR */}
        <header className="h-14 border-b border-border bg-card/50 backdrop-blur-sm px-6 flex items-center justify-between shrink-0">
          <div className="flex items-center gap-4">
            {/* Breadcrumb điều hướng phân cấp */}
            <nav aria-label="Breadcrumb" className="flex items-center gap-1.5 text-xs">
              <span className="text-muted-foreground font-medium">TiToAI</span>
              <span className="text-muted-foreground/40 font-light">/</span>
              <span className="text-foreground font-semibold">
                {getBreadcrumbTitle()}
              </span>
            </nav>

            {/* BỘ CHỌN WORKSPACE TRÊN TOPBAR */}
            <div className="workspace-selector-wrapper relative" ref={workspaceDropdownRef}>
              <button
                type="button"
                className="workspace-selector-btn flex items-center gap-2 px-2.5 py-1.5 rounded-lg border border-border bg-card/80 hover:bg-muted/60 transition-colors text-left cursor-pointer"
                onClick={() => setIsWorkspaceDropdownOpen((prev) => !prev)}
                aria-haspopup="listbox"
                aria-expanded={isWorkspaceDropdownOpen}
                aria-label="Chọn Workspace"
              >
                <div className="size-5 rounded-full bg-primary/20 text-primary flex items-center justify-center text-[10px] font-bold shrink-0">
                  {selectedWorkspace?.name
                    ? selectedWorkspace.name.charAt(0).toUpperCase()
                    : "W"}
                </div>
                <div className="flex flex-col min-w-0">
                  <span className="text-[10px] text-muted-foreground font-medium leading-none">
                    Workspace
                  </span>
                  <span className="text-xs font-semibold text-foreground truncate max-w-[130px] leading-snug">
                    {isLoadingWorkspaces
                      ? "Đang tải…"
                      : workspaceError
                        ? "Lỗi kết nối Host"
                        : selectedWorkspace
                          ? selectedWorkspace.name
                          : "Chưa có Workspace"}
                  </span>
                </div>
                <ChevronDown className="size-3 text-muted-foreground shrink-0 ml-1" />
              </button>

              {isWorkspaceDropdownOpen && (
                <div
                  className="workspace-dropdown-panel absolute left-0 top-full mt-1.5 z-50 w-72 rounded-lg border border-border bg-popover p-1 text-popover-foreground shadow-lg animate-in fade-in-0 zoom-in-95"
                  role="listbox"
                  aria-label="Danh sách Workspace"
                  tabIndex={-1}
                >
                  <div className="px-2 py-1.5 text-xs font-semibold text-muted-foreground">
                    Danh sách Workspace
                  </div>
                  <div className="my-1 border-t border-border" />
                  {isLoadingWorkspaces ? (
                    <div className="px-3 py-4 text-center text-xs text-muted-foreground">
                      Đang tải danh sách…
                    </div>
                  ) : workspaceError ? (
                    <div className="p-3 text-center flex flex-col items-center gap-2 text-xs">
                      <span className="text-destructive font-medium">
                        Lỗi tải Workspace
                      </span>
                      <Button
                        variant="outline"
                        size="sm"
                        className="h-7 text-xs gap-1.5 cursor-pointer"
                        onClick={fetchWorkspaces}
                      >
                        <RefreshCw className="size-3" /> Thử lại
                      </Button>
                    </div>
                  ) : workspaces.length === 0 ? (
                    <div className="px-3 py-4 text-center text-xs text-muted-foreground">
                      Chưa có Workspace nào.
                    </div>
                  ) : (
                    <div className="flex flex-col gap-0.5 max-h-60 overflow-y-auto">
                      {workspaces.map((ws) => (
                        <button
                          key={ws.workspaceId}
                          type="button"
                          className={cn(
                            "dropdown-item flex items-center justify-between w-full px-2.5 py-2 rounded-md text-xs text-left transition-colors cursor-pointer",
                            ws.workspaceId === selectedWorkspaceId
                              ? "bg-accent text-accent-foreground font-medium"
                              : "hover:bg-muted/60 text-foreground",
                          )}
                          onClick={() => handleSelectWorkspace(ws.workspaceId)}
                          role="option"
                          aria-selected={ws.workspaceId === selectedWorkspaceId}
                        >
                          <div className="flex flex-col min-w-0 pr-2">
                            <span className="item-title font-medium truncate">
                              {ws.name}
                            </span>
                            <span className="item-handle text-[11px] text-muted-foreground truncate">
                              {ws.channelHandle}
                            </span>
                          </div>
                          {ws.workspaceId === selectedWorkspaceId && (
                            <Check className="size-3.5 text-primary shrink-0" />
                          )}
                        </button>
                      ))}
                    </div>
                  )}
                  <div className="my-1 border-t border-border" />
                  <button
                    type="button"
                    className="flex items-center gap-2 w-full px-2.5 py-1.5 rounded-md text-xs text-muted-foreground hover:text-foreground hover:bg-muted/60 transition-colors cursor-pointer"
                    onClick={() => {
                      setIsWorkspaceDropdownOpen(false);
                      handleNavigate("workspaces");
                    }}
                  >
                    <FolderKanban className="size-3.5" />
                    <span>Quản lý Workspace</span>
                  </button>
                </div>
              )}
            </div>
          </div>

          {/* Search box ở giữa */}
          <div className="flex-1 max-w-md mx-6">
            <div className="relative flex items-center">
              <Search className="absolute left-3 size-3.5 text-muted-foreground pointer-events-none" />
              <Input
                type="search"
                placeholder={
                  activeRoute === "workspaces"
                    ? "Tìm Workspace…"
                    : "Tìm kiếm trong TiToAI…"
                }
                className="pl-8 h-8 text-xs bg-background/80"
                aria-label={
                  activeRoute === "workspaces"
                    ? "Tìm kiếm Workspace"
                    : "Tìm kiếm trong TiToAI"
                }
              />
            </div>
          </div>

          {/* Actions bên phải */}
          <div className="flex items-center gap-2">
            {/* Quick theme toggle button */}
            <Button
              type="button"
              variant="ghost"
              size="icon"
              className="size-8 rounded-lg text-muted-foreground hover:text-foreground"
              onClick={toggleTheme}
              title={`Giao diện: ${preference} (Hiện tại: ${effectiveTheme})`}
              aria-label="Chuyển nhanh giao diện sáng/tối"
            >
              {effectiveTheme === "dark" ? (
                <Moon className="size-4" />
              ) : (
                <SunMedium className="size-4" />
              )}
            </Button>

            {/* Nút mở hàng đợi tác vụ kèm badge */}
            <Button
              ref={queueBtnRef}
              type="button"
              variant="ghost"
              size="icon"
              className="queue-btn relative size-8 rounded-lg text-muted-foreground hover:text-foreground"
              onClick={handleOpenQueue}
              title="Hàng đợi tác vụ"
              aria-label="Mở hàng đợi tác vụ"
              aria-haspopup="dialog"
              aria-expanded={isQueueOpen}
            >
              <ListOrdered className="size-4" />
              {activeJobCount > 0 && (
                <span className="absolute -top-1 -right-1 flex size-4 items-center justify-center rounded-full bg-primary text-[10px] font-bold text-primary-foreground leading-none animate-in zoom-in-50">
                  {activeJobCount > 99 ? "99+" : activeJobCount}
                </span>
              )}
            </Button>
          </div>
        </header>

        {/* VÙNG NỘI DUNG CHÍNH (BẢO TOÀN DRAFT KHI ĐỔI VIEW/ROUTE) */}
        <main className="flex-1 overflow-y-auto bg-background p-6">
          {/* MÀN HÌNH CÀI ĐẶT (TRANG TRONG APP SHELL 4 TAB CON) */}
          <div className={activeRoute === "settings" ? "block" : "hidden"}>
            <SettingsPage
              onDirtyChange={setIsSettingsDirty}
            />
          </div>

          {/* MÀN HÌNH WORKSPACES */}
          <div className={activeRoute === "workspaces" ? "block" : "hidden"}>
            <WorkspaceListPage
              selectedWorkspaceId={selectedWorkspaceId}
              onSelectWorkspace={(wsId) => {
                setSelectedWorkspaceId(wsId);
                bridgeClient.setActiveWorkspaceId(wsId);
                fetchWorkspaces();
              }}
              onOpenOverview={(wsId) => {
                setSelectedWorkspaceId(wsId);
                bridgeClient.setActiveWorkspaceId(wsId);
                setSelectedProjectId(null);
                setActiveRoute("overview");
                fetchWorkspaces();
              }}
              onOpenProjects={(wsId) => {
                setSelectedWorkspaceId(wsId);
                bridgeClient.setActiveWorkspaceId(wsId);
                setSelectedProjectId(null);
                setActiveRoute("projects");
                fetchWorkspaces();
              }}
            />
          </div>

          {/* MÀN HÌNH TỔNG QUAN (OVERVIEW / WORKSPACE-OVERVIEW) */}
          <div
            className={
              activeRoute === "overview" || activeRoute === "workspace-overview"
                ? "block"
                : "hidden"
            }
          >
            {selectedWorkspaceId ? (
              <WorkspaceOverviewPage
                workspaceId={selectedWorkspaceId}
                onNavigateToProjects={() => {
                  setSelectedProjectId(null);
                  setActiveRoute("projects");
                }}
                onSelectProject={(prjId) => {
                  setSelectedProjectId(prjId);
                  setActiveRoute("projects");
                }}
              />
            ) : (
              <div className="rounded-xl border border-dashed border-border p-12 text-center flex flex-col items-center justify-center gap-3 mt-6">
                <div className="size-12 rounded-full bg-muted/60 flex items-center justify-center text-muted-foreground">
                  <LayoutDashboard className="size-6" />
                </div>
                <h3 className="font-semibold text-foreground text-sm">
                  Chưa chọn Workspace
                </h3>
                <p className="text-xs text-muted-foreground max-w-md leading-relaxed">
                  Vui lòng chọn một Workspace từ danh sách hoặc tạo kênh mới để xem báo cáo tổng quan.
                </p>
                <Button
                  type="button"
                  onClick={() => setActiveRoute("workspaces")}
                  className="gap-2 mt-2"
                >
                  <FolderKanban className="size-4" /> Đến danh sách Workspace
                </Button>
              </div>
            )}
          </div>

          {/* MÀN HÌNH DỰ ÁN & PHÂN CẢNH (PROJECTS & PROJECT-DETAIL) */}
          <div
            className={
              activeRoute === "projects" || activeRoute === "project-detail"
                ? "block"
                : "hidden"
            }
          >
            {selectedWorkspaceId ? (
              selectedProjectId ? (
                <ProjectPage
                  workspaceId={selectedWorkspaceId}
                  projectId={selectedProjectId}
                  onBack={() => setSelectedProjectId(null)}
                />
              ) : (
                <ProjectListPage
                  workspaceId={selectedWorkspaceId}
                  onSelectProject={(prjId) => setSelectedProjectId(prjId)}
                  onBackToWorkspaces={() => setActiveRoute("workspaces")}
                />
              )
            ) : (
              <div className="rounded-xl border border-dashed border-border p-12 text-center flex flex-col items-center justify-center gap-3 mt-6">
                <div className="size-12 rounded-full bg-muted/60 flex items-center justify-center text-muted-foreground">
                  <Video className="size-6" />
                </div>
                <h3 className="font-semibold text-foreground text-sm">
                  Chưa chọn Workspace
                </h3>
                <p className="text-xs text-muted-foreground max-w-md leading-relaxed">
                  Vui lòng chọn hoặc tạo một Workspace trước khi quản lý các dự án và phân cảnh.
                </p>
                <Button
                  type="button"
                  onClick={() => setActiveRoute("workspaces")}
                  className="gap-2 mt-2"
                >
                  <FolderKanban className="size-4" /> Đến danh sách Workspace
                </Button>
              </div>
            )}
          </div>

          {/* MÀN HÌNH PROMPT (PROMPT-TEMPLATES) */}
          <div
            className={
              activeRoute === "prompt-templates"
                ? "block"
                : "hidden"
            }
          >
            <div className="flex flex-col gap-4">
              <div>
                <h2 className="text-xl font-bold text-foreground tracking-tight">
                  Prompt
                </h2>
                <p className="text-xs text-muted-foreground mt-0.5">
                  Quản lý mẫu lời dẫn AI (Script / Image / Video Prompt) và các biến số theo ngữ cảnh Workspace
                </p>
              </div>

              <PromptTemplateManager workspaceId={selectedWorkspaceId} />
            </div>
          </div>

          {/* MÀN HÌNH TÀI NGUYÊN (LIBRARY) */}
          <div
            className={
              activeRoute === "library"
                ? "block"
                : "hidden"
            }
          >
            <div className="flex flex-col gap-4">
              <div>
                <h2 className="text-xl font-bold text-foreground tracking-tight">
                  Tài nguyên
                </h2>
                <p className="text-xs text-muted-foreground mt-0.5">
                  Quản lý tài nguyên hình ảnh, âm thanh, video đa phương tiện
                </p>
              </div>

              <LibraryPage workspaceId={selectedWorkspaceId} />
            </div>
          </div>
        </main>
      </div>

      {/* DIALOG XÁC NHẬN MẤT BẢN NHÁP DÙNG SHADCN / RADIX VỚI ROLE ALERTDIALOG */}
      {pendingRoute && (
        <div
          role="dialog"
          aria-modal="true"
          aria-labelledby="app-dirty-confirm-title"
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/80 backdrop-blur-xs animate-in fade-in-0"
        >
          <div
            role="alertdialog"
            aria-labelledby="app-dirty-confirm-title"
            aria-describedby="app-dirty-confirm-desc"
            className="w-full max-w-md rounded-xl border border-border bg-card p-6 shadow-xl flex flex-col gap-4 animate-in zoom-in-95"
          >
            <div className="flex flex-col gap-1.5">
              <h3
                id="app-dirty-confirm-title"
                className="text-base font-semibold text-foreground"
              >
                Cài đặt chưa được lưu!
              </h3>
              <p
                id="app-dirty-confirm-desc"
                className="text-sm text-muted-foreground leading-relaxed"
              >
                Bạn đang có các thay đổi chưa lưu trong Cài đặt. Bạn có muốn bỏ thay đổi để chuyển trang không?
              </p>
            </div>
            <div className="flex items-center justify-end gap-2 pt-2">
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={handleCancelDiscardSettings}
              >
                Ở lại
              </Button>
              <Button
                type="button"
                variant="destructive"
                size="sm"
                onClick={handleConfirmDiscardSettings}
              >
                Bỏ thay đổi
              </Button>
            </div>
          </div>
        </div>
      )}

      {/* QUEUE DRAWER */}
      <QueueDrawer
        isOpen={isQueueOpen}
        onClose={() => setIsQueueOpen(false)}
        triggerRef={queueBtnRef}
        onNavigateToJob={handleNavigateToJob}
        onNavigateToSettingsAccounts={handleNavigateToSettingsAccounts}
      />
    </div>
  );
}

export default function App() {
  return (
    <ThemeProvider>
      <AppContent />
    </ThemeProvider>
  );
}
