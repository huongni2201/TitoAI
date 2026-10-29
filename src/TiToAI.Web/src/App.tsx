import { lazy, Suspense, useEffect, useRef, useState, useCallback } from "react";
import { ThemeProvider, useTheme } from "./context/ThemeContext";
import { bridgeClient } from "./services/bridgeClient";
import {
  Video,
  Settings,
  HardDrive,
  SunMedium,
  Moon,
  ListOrdered,
  Search,
  Sparkles,
  Images,
  BarChart3,
} from "lucide-react";
import { BrandLogo } from "@/components/BrandLogo";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { QueueDrawer } from "./components/QueueDrawer";
import { jobApi } from "./features/jobs/jobApi";
import type { JobSummaryDto, JobDetailDto } from "./features/jobs/jobTypes";
import { ProjectListPage } from "./features/projects/ProjectListPage";
import { ProjectPage } from "./features/projects/ProjectPage";
import { PromptTemplateManager } from "./features/prompts";
import { LibraryPage } from "./features/library/LibraryPage";
import { JobProgressPage } from "./features/jobs/JobProgressPage";
import { PublishingNavItem } from "./features/publishing/PublishingNavItem";
import { createPublishingApi } from "./features/publishing/publishingApi";
import { projectApi } from "./features/projects/projectApi";
import { cn } from "@/lib/utils";

export type ActiveRoute =
  | "projects"
  | "project-detail"
  | "prompt-templates"
  | "library"
  | "analytics"
  | "publishing"
  | "job-progress"
  | "settings";

const publishingApi = createPublishingApi((command, payload, options) =>
  bridgeClient.invoke(command, payload, options));

// Các màn hình ít dùng chỉ tải khi mở lần đầu, giảm JS khởi động của trang Dự án.
const SettingsPage = lazy(() => import("./features/settings/SettingsPage").then(m => ({ default: m.SettingsPage })));
const AnalyticsPage = lazy(() => import("./features/analytics/AnalyticsPage").then(m => ({ default: m.AnalyticsPage })));
const PublishingPage = lazy(() => import("./features/publishing/PublishingPage").then(m => ({ default: m.PublishingPage })));

function AppContent() {
  const { preference, effectiveTheme, toggleTheme } = useTheme();

  const [activeRoute, setActiveRoute] = useState<ActiveRoute>("projects");
  // Đã mở một lần thì giữ mounted (ẩn bằng class) để bảo toàn bản nháp, như trước khi lazy-load.
  const [loadedRoutes, setLoadedRoutes] = useState<ReadonlySet<ActiveRoute>>(() => new Set());
  useEffect(() => {
    setLoadedRoutes(prev => (prev.has(activeRoute) ? prev : new Set(prev).add(activeRoute)));
  }, [activeRoute]);
  const isLoaded = (route: ActiveRoute) => activeRoute === route || loadedRoutes.has(route);
  const [selectedProjectId, setSelectedProjectId] = useState<string | null>(null);
  const [isQueueOpen, setIsQueueOpen] = useState(false);
  const [publishTarget, setPublishTarget] = useState<{ projectId: string; revision: number } | null>(null);
  const [accountsNavigationSignal, setAccountsNavigationSignal] = useState(0);

  // References cho việc phục hồi focus sau khi đóng drawer.
  const settingsBtnRef = useRef<HTMLButtonElement>(null);
  const queueBtnRef = useRef<HTMLButtonElement>(null);

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
    setActiveRoute("settings");
  };

  const handleNavigate = (targetRoute: ActiveRoute) => {
    if (activeRoute === targetRoute) return;
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
    setIsQueueOpen(true);
  };

  const handleNavigateToJob = useCallback(
    (job: JobSummaryDto | JobDetailDto) => {
      setIsQueueOpen(false);
      if (job.projectId) {
        setSelectedProjectId(job.projectId);
        setActiveRoute("project-detail");
      }
    },
    [],
  );

  const handleNavigateToSettingsAccounts = useCallback(() => {
    setIsQueueOpen(false);
    setAccountsNavigationSignal(value => value + 1);
    handleNavigate("settings");
  }, [handleNavigate]);

  const handleOpenPublishing = (projectId: string) => {
    setPublishTarget(current => ({ projectId, revision: (current?.revision ?? 0) + 1 }));
    handleNavigate("publishing");
  };

  const handleReviewPublishedVideo = async (projectId: string) => {
    const project = (await projectApi.listAll()).find(item => item.projectId === projectId);
    if (!project) throw new Error("Dự án không còn tồn tại. Hãy tải lại danh sách video.");
    setSelectedProjectId(projectId);
    handleNavigate("project-detail");
  };

  const isModalActive = isQueueOpen;

  const getBreadcrumbTitle = () => {
    switch (activeRoute) {
      case "projects":
      case "project-detail":
        return "Dự án";
      case "prompt-templates":
        return "Prompt";
      case "library":
        return "Tài nguyên";
      case "analytics":
        return "TikTok Analytics";
      case "publishing":
        return "Đăng video";
      case "job-progress":
        return "Tiến trình";
      case "settings":
        return "Cài đặt";
      default:
        return "TiToAI";
    }
  };

  const isProjectsActive =
    activeRoute === "projects" || activeRoute === "project-detail";
  const isPromptTemplatesActive = activeRoute === "prompt-templates";
  const isLibraryActive = activeRoute === "library";
  const isAnalyticsActive = activeRoute === "analytics";
  const isSettingsActive = activeRoute === "settings";
  const isJobProgressActive = activeRoute === "job-progress";

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

          <button
            type="button"
            className={cn(
              "nav-item flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm transition-colors text-left font-normal cursor-pointer",
              isAnalyticsActive
                ? "bg-primary/10 text-primary font-medium border-l-2 border-primary"
                : "text-sidebar-foreground hover:bg-muted/60 hover:text-foreground",
            )}
            onClick={() => handleNavigate("analytics")}
            aria-current={isAnalyticsActive ? "page" : undefined}
          >
            <BarChart3 className="size-4 shrink-0" />
            <span>TikTok Analytics</span>
          </button>

          <PublishingNavItem active={activeRoute === "publishing"} onClick={() => handleNavigate("publishing")} />

          <button
            type="button"
            className={cn(
              "nav-item flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm transition-colors text-left font-normal cursor-pointer",
              isJobProgressActive
                ? "bg-primary/10 text-primary font-medium border-l-2 border-primary"
                : "text-sidebar-foreground hover:bg-muted/60 hover:text-foreground",
            )}
            onClick={() => handleNavigate("job-progress")}
            aria-current={isJobProgressActive ? "page" : undefined}
          >
            <ListOrdered className="size-4 shrink-0" />
            <span>Tiến trình</span>
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

          </div>

                    {/* Search box ở giữa */}
          <div className="flex-1 max-w-md mx-6">
            <div className="relative flex items-center">
              <Search className="absolute left-3 size-3.5 text-muted-foreground pointer-events-none" />
              <Input
                type="search"
                placeholder="Tìm kiếm trong TiToAI…"
                className="pl-8 h-8 text-xs bg-background/80"
                aria-label="Tìm kiếm trong TiToAI"
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
            {isLoaded("settings") && (
              <Suspense fallback={null}>
                <SettingsPage
                  onDirtyChange={setIsSettingsDirty}
                  accountsNavigationSignal={accountsNavigationSignal}
                />
              </Suspense>
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
            {selectedProjectId ? (
              <ProjectPage
                projectId={selectedProjectId}
                onBack={() => setSelectedProjectId(null)}
                onOpenPublishing={handleOpenPublishing}
              />
            ) : (
              <ProjectListPage
                onSelectProject={(projectId) => {
                  setSelectedProjectId(projectId);
                }}
              />
            )}
          </div>

          <div className={activeRoute === "prompt-templates" ? "block" : "hidden"}>
            <div className="flex flex-col gap-4">
              <div>
                <h2 className="text-xl font-bold text-foreground tracking-tight">Prompt</h2>
                <p className="text-xs text-muted-foreground mt-0.5">
                  Quản lý các mẫu lời dẫn AI dùng cho video.
                </p>
              </div>
              <PromptTemplateManager />
            </div>
          </div>

          <div className={activeRoute === "library" ? "block" : "hidden"}>
            <div className="flex flex-col gap-4">
              <div>
                <h2 className="text-xl font-bold text-foreground tracking-tight">Tài nguyên</h2>
                <p className="text-xs text-muted-foreground mt-0.5">
                  Quản lý hình ảnh, âm thanh và video dùng trong các dự án.
                </p>
              </div>
              <LibraryPage />
            </div>
          </div>

          <div className={activeRoute === "analytics" ? "block" : "hidden"}>
            {isLoaded("analytics") && (
              <Suspense fallback={null}>
                <AnalyticsPage active={activeRoute === "analytics"} />
              </Suspense>
            )}
          </div>

          <div className={activeRoute === "publishing" ? "block" : "hidden"}>
            {isLoaded("publishing") && (
              <Suspense fallback={null}>
                <PublishingPage
                  active={activeRoute === "publishing"}
                  api={publishingApi}
                  initialProjectId={publishTarget?.projectId}
                  selectionRevision={publishTarget?.revision}
                  onOpenAccounts={handleNavigateToSettingsAccounts}
                  onReviewVideo={handleReviewPublishedVideo}
                />
              </Suspense>
            )}
          </div>

          <div className={activeRoute === "job-progress" ? "block" : "hidden"}>
            <JobProgressPage active={isJobProgressActive} />
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
