import { useEffect, useRef, useState, useCallback } from "react";
import { ThemeProvider, useTheme } from "./context/ThemeContext";
import { bridgeClient } from "./services/bridgeClient";
import type { WorkspaceSummaryDto, WorkspaceListResponseData } from "./contracts/dtos";
import type { ErrorDto } from "./contracts/bridge";
import {
  IconOverview,
  IconWorkspace,
  IconProject,
  IconBrowser,
  IconLibrary,
  IconSettings,
  IconQueue,
  IconTheme,
  IconSearch,
  IconRefresh,
  IconHardDrive,
  IconChevronDown,
} from "./components/Icons";
import { SettingsModal } from "./components/SettingsModal";
import { QueueDrawer } from "./components/QueueDrawer";
import { BrowserManagerPage } from "./features/browsers/BrowserManagerPage";
import { WorkspaceListPage } from "./features/workspaces/WorkspaceListPage";
import { WorkspaceOverviewPage } from "./features/workspaces/WorkspaceOverviewPage";
import { ProjectListPage } from "./features/projects/ProjectListPage";
import { ProjectPage } from "./features/projects/ProjectPage";
import { PromptTemplateManager } from "./features/prompts";

type ActiveRoute = "overview" | "workspaces" | "projects" | "browsers" | "library";

function AppContent() {
  const { preference, effectiveTheme, toggleTheme } = useTheme();

  const [activeRoute, setActiveRoute] = useState<ActiveRoute>("workspaces");
  const [workspaces, setWorkspaces] = useState<WorkspaceSummaryDto[]>([]);
  const [selectedWorkspaceId, setSelectedWorkspaceId] = useState<string | null>(null);
  const [selectedProjectId, setSelectedProjectId] = useState<string | null>(null);
  const [isLoadingWorkspaces, setIsLoadingWorkspaces] = useState<boolean>(true);
  const [workspaceError, setWorkspaceError] = useState<ErrorDto | null>(null);

  const [isWorkspaceDropdownOpen, setIsWorkspaceDropdownOpen] = useState(false);
  const [isSettingsOpen, setIsSettingsOpen] = useState(false);
  const [isQueueOpen, setIsQueueOpen] = useState(false);
  const [libraryTab, setLibraryTab] = useState<"prompts" | "media">("prompts");

  // References cho việc phục hồi focus sau khi đóng modal/drawer và quản lý lifecycle
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

  const handleOpenSettings = () => {
    setIsQueueOpen(false);
    setIsWorkspaceDropdownOpen(false);
    setIsSettingsOpen(true);
  };

  const handleOpenQueue = () => {
    setIsSettingsOpen(false);
    setIsWorkspaceDropdownOpen(false);
    setIsQueueOpen(true);
  };

  const isModalActive = isSettingsOpen || isQueueOpen;

  const getBreadcrumbTitle = () => {
    switch (activeRoute) {
      case "overview":
        return "Tổng quan";
      case "workspaces":
        return "Danh sách Workspace";
      case "projects":
        return "Dự án";
      case "browsers":
        return "Trình duyệt AI";
      case "library":
        return "Thư viện";
      default:
        return "TiToAI";
    }
  };

  return (
    <div className="app-layout">
      {/* 1. SIDEBAR TRÁI CỐ ĐỊNH (Khóa tương tác khi modal/drawer mở) */}
      <aside
        className="app-sidebar"
        aria-label="Điều hướng chính"
        inert={isModalActive}
        aria-hidden={isModalActive ? true : undefined}
      >
        <div className="sidebar-brand">
          <div className="brand-logo" aria-hidden="true">
            <svg width="24" height="24" viewBox="0 0 24 24" fill="none">
              <path
                d="M12 2L2 7L12 12L22 7L12 2Z"
                fill="var(--accent)"
              />
              <path
                d="M2 17L12 22L22 17"
                stroke="var(--accent)"
                strokeWidth="2"
                strokeLinecap="round"
                strokeLinejoin="round"
              />
              <path
                d="M2 12L12 17L22 12"
                stroke="var(--accent)"
                strokeWidth="2"
                strokeLinecap="round"
                strokeLinejoin="round"
              />
            </svg>
          </div>
          <div className="brand-text">
            <h1 className="brand-title">TiToAI</h1>
            <span className="brand-subtitle">Desktop Studio</span>
          </div>
        </div>

        <nav className="sidebar-nav" aria-label="Menu chức năng">
          <button
            type="button"
            className={`nav-item ${activeRoute === "overview" ? "active" : ""}`}
            onClick={() => setActiveRoute("overview")}
            aria-current={activeRoute === "overview" ? "page" : undefined}
          >
            <IconOverview size={18} />
            <span>Tổng quan</span>
          </button>

          <button
            type="button"
            className={`nav-item ${activeRoute === "workspaces" ? "active" : ""}`}
            onClick={() => setActiveRoute("workspaces")}
            aria-current={activeRoute === "workspaces" ? "page" : undefined}
          >
            <IconWorkspace size={18} />
            <span>Workspaces</span>
            {!isLoadingWorkspaces && !workspaceError && workspaces.length > 0 && (
              <span className="nav-badge">{workspaces.length}</span>
            )}
          </button>

          <button
            type="button"
            className={`nav-item ${activeRoute === "projects" ? "active" : ""}`}
            onClick={() => setActiveRoute("projects")}
            aria-current={activeRoute === "projects" ? "page" : undefined}
          >
            <IconProject size={18} />
            <span>Dự án</span>
          </button>

          <button
            type="button"
            className={`nav-item ${activeRoute === "browsers" ? "active" : ""}`}
            onClick={() => setActiveRoute("browsers")}
            aria-current={activeRoute === "browsers" ? "page" : undefined}
          >
            <IconBrowser size={18} />
            <span>Trình duyệt AI</span>
          </button>

          <button
            type="button"
            className={`nav-item ${activeRoute === "library" ? "active" : ""}`}
            onClick={() => setActiveRoute("library")}
            aria-current={activeRoute === "library" ? "page" : undefined}
          >
            <IconLibrary size={18} />
            <span>Thư viện</span>
          </button>

          <div className="nav-divider" />

          <button
            ref={settingsBtnRef}
            type="button"
            className="nav-item"
            onClick={handleOpenSettings}
            aria-haspopup="dialog"
            aria-expanded={isSettingsOpen}
          >
            <IconSettings size={18} />
            <span>Cài đặt</span>
          </button>
        </nav>

        <div className="sidebar-footer">
          <div className="storage-status-card">
            <div className="storage-icon">
              <IconHardDrive size={16} />
            </div>
            <div className="storage-info">
              <span className="storage-label">Lưu trữ cục bộ</span>
              <span className="storage-val">Chưa xác định</span>
            </div>
          </div>
          <div className="version-tag">TiToAI v0.1.0</div>
        </div>
      </aside>

      {/* 2. KHU VỰC CHÍNH (TOPBAR + NỘI DUNG) (Khóa tương tác khi modal/drawer mở) */}
      <div
        className="app-main"
        inert={isModalActive}
        aria-hidden={isModalActive ? true : undefined}
      >
        {/* TOPBAR / CONTEXT */}
        <header className="app-topbar">
          <div className="topbar-left">
            <div className="breadcrumb">
              <span className="crumb-root">TiToAI</span>
              <span className="crumb-sep">/</span>
              <span className="crumb-current">{getBreadcrumbTitle()}</span>
            </div>

            {/* WORKSPACE SELECTOR */}
            <div className="workspace-selector-wrapper" ref={workspaceDropdownRef}>
              <button
                type="button"
                className="workspace-selector-btn"
                onClick={() => setIsWorkspaceDropdownOpen((prev) => !prev)}
                aria-haspopup="listbox"
                aria-expanded={isWorkspaceDropdownOpen}
                aria-label="Chọn Workspace"
              >
                <div className="ws-btn-text">
                  <span className="ws-label">Workspace:</span>
                  <span className="ws-name">
                    {isLoadingWorkspaces
                      ? "Đang tải…"
                      : workspaceError
                        ? "Lỗi kết nối Host"
                        : selectedWorkspace
                          ? selectedWorkspace.name
                          : "Chưa có Workspace"}
                  </span>
                </div>
                <IconChevronDown size={14} />
              </button>

              {isWorkspaceDropdownOpen && (
                <div
                  className="workspace-dropdown-panel"
                  role="listbox"
                  aria-label="Danh sách Workspace"
                  tabIndex={-1}
                >
                  {isLoadingWorkspaces ? (
                    <div className="dropdown-status">Đang tải danh sách…</div>
                  ) : workspaceError ? (
                    <div className="dropdown-error">
                      <span>Lỗi tải Workspace</span>
                      <button
                        type="button"
                        className="btn btn-secondary btn-sm"
                        onClick={fetchWorkspaces}
                      >
                        <IconRefresh size={12} /> Thử lại
                      </button>
                    </div>
                  ) : workspaces.length === 0 ? (
                    <div className="dropdown-empty">
                      <p>Chưa có Workspace nào.</p>
                    </div>
                  ) : (
                    <div className="dropdown-list">
                      {workspaces.map((ws) => (
                        <button
                          key={ws.workspaceId}
                          type="button"
                          className={`dropdown-item ${
                            ws.workspaceId === selectedWorkspaceId ? "selected" : ""
                          }`}
                          onClick={() => handleSelectWorkspace(ws.workspaceId)}
                          role="option"
                          aria-selected={ws.workspaceId === selectedWorkspaceId}
                        >
                          <div className="item-title">{ws.name}</div>
                          <div className="item-handle">{ws.channelHandle}</div>
                        </button>
                      ))}
                    </div>
                  )}
                </div>
              )}
            </div>
          </div>

          <div className="topbar-center">
            <div className="search-box">
              <IconSearch size={14} className="search-icon" />
              <input
                type="search"
                placeholder={
                  activeRoute === "workspaces"
                    ? "Tìm Workspace…"
                    : "Tìm kiếm trong TiToAI…"
                }
                className="search-input"
                aria-label={
                  activeRoute === "workspaces"
                    ? "Tìm kiếm Workspace"
                    : "Tìm kiếm trong TiToAI"
                }
              />
            </div>
          </div>

          <div className="topbar-right">
            {/* Quick theme toggle */}
            <button
              type="button"
              className="btn-icon"
              onClick={toggleTheme}
              title={`Giao diện: ${preference} (Hiện tại: ${effectiveTheme})`}
              aria-label="Chuyển nhanh giao diện sáng/tối"
            >
              <IconTheme size={18} />
            </button>

            {/* Queue drawer trigger */}
            <button
              ref={queueBtnRef}
              type="button"
              className="btn-icon queue-btn"
              onClick={handleOpenQueue}
              title="Hàng đợi tác vụ"
              aria-label="Mở hàng đợi tác vụ"
              aria-haspopup="dialog"
              aria-expanded={isQueueOpen}
            >
              <IconQueue size={18} />
            </button>
          </div>
        </header>

        {/* VÙNG NỘI DUNG CHÍNH */}
        <main className="content-container">
          {/* MÀN HÌNH WORKSPACES */}
          {activeRoute === "workspaces" && (
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
          )}

          {/* MÀN HÌNH TỔNG QUAN (OVERVIEW) */}
          {activeRoute === "overview" && (
            selectedWorkspaceId ? (
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
              <div className="empty-card" style={{ marginTop: "1.5rem" }}>
                <div className="empty-icon">
                  <IconOverview size={40} />
                </div>
                <h3>Chưa chọn Workspace</h3>
                <p>
                  Vui lòng chọn một Workspace từ danh sách hoặc tạo kênh mới để xem báo cáo tổng quan.
                </p>
                <button
                  type="button"
                  className="btn btn-primary"
                  onClick={() => setActiveRoute("workspaces")}
                  style={{ marginTop: "1rem" }}
                >
                  <IconWorkspace size={16} /> Đến danh sách Workspace
                </button>
              </div>
            )
          )}

          {/* MÀN HÌNH DỰ ÁN & PHÂN CẢNH (PROJECTS & SCENES) */}
          {activeRoute === "projects" && (
            selectedWorkspaceId ? (
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
              <div className="empty-card" style={{ marginTop: "1.5rem" }}>
                <div className="empty-icon">
                  <IconProject size={40} />
                </div>
                <h3>Chưa chọn Workspace</h3>
                <p>
                  Vui lòng chọn hoặc tạo một Workspace trước khi quản lý các dự án và phân cảnh.
                </p>
                <button
                  type="button"
                  className="btn btn-primary"
                  onClick={() => setActiveRoute("workspaces")}
                  style={{ marginTop: "1rem" }}
                >
                  <IconWorkspace size={16} /> Đến danh sách Workspace
                </button>
              </div>
            )
          )}

          {activeRoute === "browsers" && (
            <BrowserManagerPage
              currentWorkspaceId={selectedWorkspaceId}
              workspaces={workspaces}
            />
          )}

          {activeRoute === "library" && (
            <div className="library-view" style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
              <div
                className="page-header"
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                  flexWrap: "wrap",
                  gap: "0.75rem",
                }}
              >
                <div>
                  <h2 className="page-title">Thư viện &amp; Mẫu Prompt</h2>
                  <p
                    className="page-subtitle"
                    style={{ color: "var(--text-muted)", fontSize: "0.85rem", margin: "0.2rem 0 0" }}
                  >
                    Quản lý mẫu lời dẫn AI (Script / Image / Video Prompt) và tài nguyên media đa phương tiện
                  </p>
                </div>
                <div style={{ display: "flex", gap: "0.5rem" }}>
                  <button
                    type="button"
                    className={`btn btn-sm ${libraryTab === "prompts" ? "btn-primary" : "btn-secondary"}`}
                    onClick={() => setLibraryTab("prompts")}
                  >
                    Mẫu Prompt (AI Templates)
                  </button>
                  <button
                    type="button"
                    className={`btn btn-sm ${libraryTab === "media" ? "btn-primary" : "btn-secondary"}`}
                    onClick={() => setLibraryTab("media")}
                  >
                    Tài nguyên Media
                  </button>
                </div>
              </div>

              {libraryTab === "prompts" ? (
                <PromptTemplateManager workspaceId={selectedWorkspaceId} />
              ) : (
                <div className="empty-card" style={{ marginTop: "1rem" }}>
                  <div className="empty-icon">
                    <IconLibrary size={40} />
                  </div>
                  <h3>Thư viện media</h3>
                  <p>
                    Quản lý ảnh AI, video thành phẩm và tệp âm thanh gốc theo ba
                    thư mục độc lập (Documents, Images, Videos). Tính năng quản lý
                    thư viện media sẽ được tích hợp cùng bộ Pipeline Orchestration.
                  </p>
                </div>
              )}
            </div>
          )}

        </main>
      </div>

      {/* POPUP CÀI ĐẶT BA NHÓM */}
      <SettingsModal
        isOpen={isSettingsOpen}
        onClose={() => setIsSettingsOpen(false)}
        triggerRef={settingsBtnRef}
      />

      {/* QUEUE DRAWER */}
      <QueueDrawer
        isOpen={isQueueOpen}
        onClose={() => setIsQueueOpen(false)}
        triggerRef={queueBtnRef}
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
