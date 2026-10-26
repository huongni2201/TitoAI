import { useState, useEffect, useMemo, useCallback, useRef } from "react";
import { workspaceApi, type WorkspaceSummaryDto } from "./workspaceApi";
import { WorkspaceForm } from "./WorkspaceForm";
import { bridgeClient } from "../../services/bridgeClient";
import type { ErrorDto } from "../../contracts/bridge";
import {
  IconWorkspace,
  IconSearch,
  IconPlus,
  IconRefresh,
  IconInfo,
} from "../../components/Icons";

export interface WorkspaceListPageProps {
  selectedWorkspaceId?: string | null;
  onSelectWorkspace?: (workspaceId: string) => void;
  onOpenOverview?: (workspaceId: string) => void;
  onOpenProjects?: (workspaceId: string) => void;
}

type StatusFilter = "all" | "Active" | "Paused";
type SortOption = "latest" | "oldest" | "name_asc";

// Tạo màu avatar ổn định dựa trên hash chuỗi tên kênh
function getAvatarBgColor(text: string): string {
  const colors = [
    "#6d3af2", // Purple
    "#0284c7", // Sky blue
    "#059669", // Emerald
    "#d97706", // Amber
    "#e11d48", // Rose
    "#7c3aed", // Violet
    "#2563eb", // Blue
    "#db2777", // Pink
  ];
  let hash = 0;
  for (let i = 0; i < text.length; i++) {
    hash = (hash << 5) - hash + text.charCodeAt(i);
    hash |= 0;
  }
  const index = Math.abs(hash) % colors.length;
  return colors[index];
}

// Lấy 2 chữ cái đầu (initials) của tên kênh
function getInitials(name: string): string {
  if (!name) return "WS";
  const words = name.trim().split(/\s+/);
  if (words.length >= 2) {
    return (words[0][0] + words[1][0]).toUpperCase();
  }
  return name.slice(0, 2).toUpperCase();
}

export function WorkspaceListPage({
  selectedWorkspaceId,
  onSelectWorkspace,
  onOpenOverview,
  onOpenProjects,
}: WorkspaceListPageProps) {
  const [workspaces, setWorkspaces] = useState<WorkspaceSummaryDto[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  // Bộ lọc & Tìm kiếm
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all");
  const [searchQuery, setSearchQuery] = useState<string>("");
  const [sortOption, setSortOption] = useState<SortOption>("latest");

  // State các Dialog / Modals
  const [isFormOpen, setIsFormOpen] = useState(false);
  const [formMode, setFormMode] = useState<"create" | "edit">("create");
  const [editingWorkspace, setEditingWorkspace] = useState<WorkspaceSummaryDto | null>(null);


  // Modal xác nhận xóa
  const [deletingWorkspace, setDeletingWorkspace] = useState<WorkspaceSummaryDto | null>(null);
  const [isDeleting, setIsDeleting] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  // Menu popover dropdown cho từng card
  const [activeMenuWorkspaceId, setActiveMenuWorkspaceId] = useState<string | null>(null);

  const abortControllerRef = useRef<AbortController | null>(null);

  // Tải danh sách Workspace
  const fetchWorkspaces = useCallback(async () => {
    if (abortControllerRef.current) {
      abortControllerRef.current.abort();
    }
    const controller = new AbortController();
    abortControllerRef.current = controller;

    setIsLoading(true);
    setErrorMessage(null);

    try {
      const list = await workspaceApi.list();
      if (controller.signal.aborted) return;
      setWorkspaces(list);
    } catch (err: unknown) {
      if (controller.signal.aborted) return;
      const errorDto = err as ErrorDto;
      console.error("[WorkspaceListPage] Lỗi tải Workspace:", errorDto);
      setErrorMessage(errorDto?.message || "Không thể tải danh sách Workspace từ Host.");
    } finally {
      if (!controller.signal.aborted) {
        setIsLoading(false);
      }
    }
  }, []);

  useEffect(() => {
    fetchWorkspaces();

    // Lắng nghe sự kiện thay đổi trạng thái workspace từ host
    const unsubscribe = workspaceApi.onStatusChanged((event) => {
      const { workspaceId, status } = event.payload;
      setWorkspaces((prev) =>
        prev.map((ws) => (ws.workspaceId === workspaceId ? { ...ws, status } : ws)),
      );
    });

    return () => {
      unsubscribe();
      if (abortControllerRef.current) {
        abortControllerRef.current.abort();
      }
    };
  }, [fetchWorkspaces]);

  // Đóng popover khi click ra ngoài
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      const target = e.target as HTMLElement;
      if (!target.closest(".card-action-menu-wrapper")) {
        setActiveMenuWorkspaceId(null);
      }
    };
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  // Lọc và sắp xếp danh sách
  const filteredWorkspaces = useMemo(() => {
    return workspaces
      .filter((ws) => {
        if (statusFilter !== "all" && ws.status !== statusFilter) {
          return false;
        }
        if (searchQuery.trim()) {
          const query = searchQuery.trim().toLowerCase();
          const matchName = ws.name.toLowerCase().includes(query);
          const matchHandle = ws.channelHandle.toLowerCase().includes(query);
          const matchTopic = (ws.topic || "").toLowerCase().includes(query);
          return matchName || matchHandle || matchTopic;
        }
        return true;
      })
      .sort((a, b) => {
        if (sortOption === "name_asc") {
          return a.name.localeCompare(b.name, "vi");
        }
        if (sortOption === "oldest") {
          return a.workspaceId.localeCompare(b.workspaceId);
        }
        // latest: mặc định đảo ngược theo id hoặc giữ thứ tự
        return b.workspaceId.localeCompare(a.workspaceId);
      });
  }, [workspaces, statusFilter, searchQuery, sortOption]);

  const activeCount = useMemo(
    () => workspaces.filter((w) => w.status === "Active").length,
    [workspaces],
  );
  const pausedCount = useMemo(
    () => workspaces.filter((w) => w.status === "Paused").length,
    [workspaces],
  );

  // Thao tác Tạm dừng / Tiếp tục
  const handleToggleStatus = async (ws: WorkspaceSummaryDto) => {
    setActiveMenuWorkspaceId(null);
    const newStatus = ws.status === "Active" ? "Paused" : "Active";
    try {
      if (newStatus === "Paused") {
        await workspaceApi.pause(ws.workspaceId);
      } else {
        await workspaceApi.resume(ws.workspaceId);
      }
      setWorkspaces((prev) =>
        prev.map((item) =>
          item.workspaceId === ws.workspaceId ? { ...item, status: newStatus } : item,
        ),
      );
    } catch (err: unknown) {
      const errorDto = err as ErrorDto;
      alert(`Không thể chuyển trạng thái Workspace: ${errorDto?.message || "Lỗi nội bộ"}`);
    }
  };

  // Thao tác chọn và mở kênh
  const handleSelectWorkspace = (ws: WorkspaceSummaryDto) => {
    bridgeClient.setActiveWorkspaceId(ws.workspaceId);
    onSelectWorkspace?.(ws.workspaceId);
    if (onOpenOverview) {
      onOpenOverview(ws.workspaceId);
    } else if (onOpenProjects) {
      onOpenProjects(ws.workspaceId);
    }
  };

  // Mở modal tạo mới
  const handleOpenCreateForm = () => {
    setEditingWorkspace(null);
    setFormMode("create");
    setIsFormOpen(true);
  };

  // Mở modal chỉnh sửa
  const handleOpenEditForm = (ws: WorkspaceSummaryDto) => {
    setActiveMenuWorkspaceId(null);
    setEditingWorkspace(ws);
    setFormMode("edit");
    setIsFormOpen(true);
  };

  // Xóa Workspace
  const handleConfirmDelete = async () => {
    if (!deletingWorkspace || isDeleting) return;
    setIsDeleting(true);
    setDeleteError(null);

    try {
      await workspaceApi.delete(deletingWorkspace.workspaceId);
      setWorkspaces((prev) => prev.filter((w) => w.workspaceId !== deletingWorkspace.workspaceId));
      if (selectedWorkspaceId === deletingWorkspace.workspaceId) {
        bridgeClient.setActiveWorkspaceId(null);
      }
      setDeletingWorkspace(null);
    } catch (err: unknown) {
      const errorDto = err as ErrorDto;
      console.error("[WorkspaceListPage] Lỗi xóa Workspace:", errorDto);
      if (errorDto?.code === "CONCURRENCY_CONFLICT") {
        setDeleteError("Không thể xóa do đang có tác vụ xử lý video đang chạy trong kênh này.");
      } else {
        setDeleteError(errorDto?.message || "Đã xảy ra lỗi khi xóa Workspace.");
      }
    } finally {
      setIsDeleting(false);
    }
  };

  return (
    <div className="view-workspaces" style={{ paddingBottom: "2rem" }}>
      {/* Tiêu đề trang */}
      <div className="page-header" style={{ marginBottom: "1.25rem" }}>
        <div>
          <h2 className="page-title" style={{ fontSize: "1.5rem", fontWeight: 700, margin: 0 }}>
            Danh sách Workspace
          </h2>
          <p className="page-desc" style={{ color: "var(--text-secondary)", marginTop: "0.25rem" }}>
            Quản lý các kênh TikTok của bạn. Mỗi workspace là một tài khoản TikTok độc lập.
          </p>
        </div>
        <button
          type="button"
          className="btn btn-primary"
          onClick={handleOpenCreateForm}
          style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}
        >
          <IconPlus size={16} /> Tạo workspace
        </button>
      </div>

      {/* Thanh công cụ: Bộ lọc, Tìm kiếm, Sắp xếp */}
      <div
        className="filter-bar"
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          flexWrap: "wrap",
          gap: "1rem",
          marginBottom: "1.5rem",
        }}
      >
        {/* Filter Tabs */}
        <div className="tab-group" role="tablist" aria-label="Bộ lọc trạng thái">
          <button
            type="button"
            role="tab"
            aria-selected={statusFilter === "all"}
            className={`filter-tab ${statusFilter === "all" ? "active" : ""}`}
            onClick={() => setStatusFilter("all")}
          >
            Tất cả ({workspaces.length})
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={statusFilter === "Active"}
            className={`filter-tab ${statusFilter === "Active" ? "active" : ""}`}
            onClick={() => setStatusFilter("Active")}
          >
            Đang hoạt động ({activeCount})
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={statusFilter === "Paused"}
            className={`filter-tab ${statusFilter === "Paused" ? "active" : ""}`}
            onClick={() => setStatusFilter("Paused")}
          >
            Tạm dừng ({pausedCount})
          </button>
        </div>

        {/* Search & Sort Controls */}
        <div style={{ display: "flex", alignItems: "center", gap: "0.75rem", flexWrap: "wrap" }}>
          <div style={{ position: "relative", minWidth: 240 }}>
            <IconSearch
              size={16}
              style={{
                position: "absolute",
                left: 10,
                top: "50%",
                transform: "translateY(-50%)",
                color: "var(--text-muted)",
              }}
            />
            <input
              type="text"
              className="form-control"
              placeholder="Tìm workspace theo tên, handle…"
              style={{ paddingLeft: "2rem" }}
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
            />
          </div>

          <select
            className="form-control"
            style={{ width: "auto" }}
            value={sortOption}
            onChange={(e) => setSortOption(e.target.value as SortOption)}
          >
            <option value="latest">Mới nhất</option>
            <option value="oldest">Cũ nhất</option>
            <option value="name_asc">Tên (A-Z)</option>
          </select>

          <button
            type="button"
            className="btn btn-secondary btn-icon"
            onClick={fetchWorkspaces}
            title="Làm mới danh sách"
            disabled={isLoading}
          >
            <IconRefresh size={16} className={isLoading ? "spin" : ""} />
          </button>
        </div>
      </div>

      {/* Nội dung danh sách */}
      {isLoading ? (
        <div className="loading-card">
          <IconRefresh size={28} className="spin" />
          <p>Đang tải danh sách Workspace từ Host…</p>
        </div>
      ) : errorMessage ? (
        <div className="empty-card error-card">
          <h3>Không thể tải danh sách Workspace</h3>
          <p>{errorMessage}</p>
          <button type="button" className="btn btn-secondary" onClick={fetchWorkspaces}>
            <IconRefresh size={16} /> Thử lại
          </button>
        </div>
      ) : workspaces.length === 0 ? (
        <div className="empty-card">
          <div className="empty-icon">
            <IconWorkspace size={44} />
          </div>
          <h3>Chưa có Workspace nào</h3>
          <p>
            Bạn chưa tạo kênh TikTok nào. Hãy bắt đầu bằng cách tạo Workspace đầu tiên để
            quản lý kịch bản, prompt và render video.
          </p>
          <button
            type="button"
            className="btn btn-primary"
            onClick={handleOpenCreateForm}
            style={{ marginTop: "1rem" }}
          >
            <IconPlus size={16} /> Tạo Workspace Đầu Tiên
          </button>
        </div>
      ) : filteredWorkspaces.length === 0 ? (
        <div className="empty-card">
          <h3>Không tìm thấy kết quả phù hợp</h3>
          <p>Không có workspace nào khớp với bộ lọc hoặc từ khóa tìm kiếm của bạn.</p>
          <button
            type="button"
            className="btn btn-secondary"
            onClick={() => {
              setStatusFilter("all");
              setSearchQuery("");
            }}
          >
            Xóa bộ lọc tìm kiếm
          </button>
        </div>
      ) : (
        /* Grid các card Workspace (3 cột / 2 cột / 1 cột co giãn) */
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fill, minmax(320px, 1fr))",
            gap: "1.25rem",
          }}
        >
          {filteredWorkspaces.map((ws) => {
            const isSelected = selectedWorkspaceId === ws.workspaceId;
            const isActive = ws.status === "Active";
            const isMenuOpen = activeMenuWorkspaceId === ws.workspaceId;
            const avatarColor = getAvatarBgColor(ws.name);
            const initials = getInitials(ws.name);

            return (
              <div
                key={ws.workspaceId}
                className="workspace-card"
                style={{
                  backgroundColor: "var(--surface-card)",
                  border: isSelected
                    ? "2px solid var(--accent)"
                    : "1px solid var(--border-subtle)",
                  borderRadius: "var(--radius-lg)",
                  padding: "1.25rem",
                  display: "flex",
                  flexDirection: "column",
                  justifyContent: "space-between",
                  boxShadow: "0 2px 8px rgba(0, 0, 0, 0.08)",
                  position: "relative",
                  transition: "transform 150ms ease, border-color 150ms ease",
                }}
              >
                {/* Header Card: Avatar, Thông tin, Menu 3 chấm */}
                <div>
                  <div
                    style={{
                      display: "flex",
                      alignItems: "flex-start",
                      justifyContent: "space-between",
                      gap: "0.75rem",
                      marginBottom: "0.75rem",
                    }}
                  >
                    <div style={{ display: "flex", gap: "0.75rem", alignItems: "center" }}>
                      <div
                        style={{
                          width: 44,
                          height: 44,
                          borderRadius: "50%",
                          backgroundColor: avatarColor,
                          color: "#ffffff",
                          display: "flex",
                          alignItems: "center",
                          justifyContent: "center",
                          fontWeight: 700,
                          fontSize: "1rem",
                          flexShrink: 0,
                          boxShadow: "0 2px 4px rgba(0,0,0,0.15)",
                        }}
                      >
                        {initials}
                      </div>
                      <div style={{ overflow: "hidden" }}>
                        <h4
                          style={{
                            margin: 0,
                            fontSize: "1.05rem",
                            fontWeight: 700,
                            color: "var(--text-primary)",
                            whiteSpace: "nowrap",
                            overflow: "hidden",
                            textOverflow: "ellipsis",
                          }}
                          title={ws.name}
                        >
                          {ws.name}
                        </h4>
                        <div
                          style={{
                            fontSize: "0.8rem",
                            color: "var(--text-muted)",
                            whiteSpace: "nowrap",
                            overflow: "hidden",
                            textOverflow: "ellipsis",
                          }}
                        >
                          {ws.channelHandle}
                        </div>
                      </div>
                    </div>

                    {/* Nút hành động 3 chấm và popover */}
                    <div className="card-action-menu-wrapper" style={{ position: "relative" }}>
                      <button
                        type="button"
                        className="btn-icon"
                        style={{ width: 28, height: 28 }}
                        onClick={(e) => {
                          e.stopPropagation();
                          setActiveMenuWorkspaceId(isMenuOpen ? null : ws.workspaceId);
                        }}
                        aria-label="Tùy chọn thao tác"
                      >
                        ···
                      </button>

                      {isMenuOpen && (
                        <div
                          className="action-dropdown"
                          style={{
                            position: "absolute",
                            right: 0,
                            top: 32,
                            width: 170,
                            backgroundColor: "var(--surface-card)",
                            border: "1px solid var(--border)",
                            borderRadius: "var(--radius-md)",
                            boxShadow: "var(--shadow-dropdown)",
                            zIndex: 20,
                            display: "flex",
                            flexDirection: "column",
                            padding: "0.25rem 0",
                          }}
                        >
                          <button
                            type="button"
                            className="dropdown-item"
                            style={{
                              background: "transparent",
                              border: "none",
                              textAlign: "left",
                              padding: "0.5rem 0.85rem",
                              fontSize: "0.82rem",
                              color: "var(--text-primary)",
                              cursor: "pointer",
                            }}
                            onClick={() => handleOpenEditForm(ws)}
                          >
                            ✏️ Sửa thông tin
                          </button>
                          <button
                            type="button"
                            className="dropdown-item"
                            style={{
                              background: "transparent",
                              border: "none",
                              textAlign: "left",
                              padding: "0.5rem 0.85rem",
                              fontSize: "0.82rem",
                              color: "var(--text-primary)",
                              cursor: "pointer",
                            }}
                            onClick={() => handleToggleStatus(ws)}
                          >
                            {isActive ? "⏸️ Tạm dừng kênh" : "▶️ Kích hoạt lại"}
                          </button>
                          <hr
                            style={{
                              border: "none",
                              borderTop: "1px solid var(--border-subtle)",
                              margin: "0.25rem 0",
                            }}
                          />
                          <button
                            type="button"
                            className="dropdown-item"
                            style={{
                              background: "transparent",
                              border: "none",
                              textAlign: "left",
                              padding: "0.5rem 0.85rem",
                              fontSize: "0.82rem",
                              color: "var(--status-error)",
                              cursor: "pointer",
                            }}
                            onClick={() => {
                              setActiveMenuWorkspaceId(null);
                              setDeletingWorkspace(ws);
                              setDeleteError(null);
                            }}
                          >
                            🗑️ Xóa kênh
                          </button>
                        </div>
                      )}
                    </div>
                  </div>

                  {/* Chủ đề / Niche & Trạng thái */}
                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      justifyContent: "space-between",
                      marginBottom: "0.75rem",
                    }}
                  >
                    <span
                      style={{
                        fontSize: "0.8rem",
                        color: "var(--text-secondary)",
                        backgroundColor: "var(--surface-card-subtle)",
                        padding: "0.2rem 0.5rem",
                        borderRadius: "var(--radius-sm)",
                      }}
                    >
                      {ws.topic || "Chưa đặt chủ đề"}
                    </span>

                    <span
                      className={`badge ${isActive ? "badge-success" : "badge-neutral"}`}
                      style={{ display: "flex", alignItems: "center", gap: "0.3rem" }}
                    >
                      <span>●</span> {isActive ? "Đang hoạt động" : "Tạm dừng"}
                    </span>
                  </div>
                </div>

                {/* Phần liên kết tài khoản và Footer */}
                <div style={{ marginTop: "0.75rem", borderTop: "1px solid var(--border-subtle)", paddingTop: "0.75rem" }}>

                  {/* Nút hành động chính: Mở workspace */}
                  <button
                    type="button"
                    className="btn btn-primary"
                    style={{ width: "100%", justifyContent: "center" }}
                    onClick={() => handleSelectWorkspace(ws)}
                  >
                    Mở workspace
                  </button>
                </div>
              </div>
            );
          })}
        </div>
      )}

      {/* Form Tạo/Sửa Workspace Modal */}
      <WorkspaceForm
        isOpen={isFormOpen}
        mode={formMode}
        workspace={editingWorkspace}
        onClose={() => setIsFormOpen(false)}
        onSuccess={async (savedId) => {
          setIsFormOpen(false);
          await fetchWorkspaces();
          bridgeClient.setActiveWorkspaceId(savedId);
          onSelectWorkspace?.(savedId);
        }}
      />

      {/* Modal Xác Nhận Xóa Workspace */}
      {deletingWorkspace && (
        <div
          className="modal-backdrop"
          role="presentation"
          onClick={(e) => {
            if (e.target === e.currentTarget && !isDeleting) {
              setDeletingWorkspace(null);
            }
          }}
        >
          <div
            className="modal-container"
            role="dialog"
            aria-modal="true"
            aria-labelledby="delete-dialog-title"
            style={{ maxWidth: 460 }}
          >
            <div className="modal-header">
              <h3 id="delete-dialog-title" style={{ margin: 0, color: "var(--status-error)" }}>
                Xác nhận xóa Workspace
              </h3>
            </div>
            <div className="modal-body" style={{ display: "flex", flexDirection: "column", gap: "0.75rem" }}>
              {deleteError && (
                <div
                  className="notice-box"
                  style={{
                    backgroundColor: "rgba(239, 68, 68, 0.12)",
                    border: "1px solid var(--status-error)",
                    color: "var(--status-error)",
                  }}
                  role="alert"
                >
                  <span>⚠️</span>
                  <div>{deleteError}</div>
                </div>
              )}
              <p style={{ margin: 0 }}>
                Bạn có chắc chắn muốn xóa kênh <strong>{deletingWorkspace.name}</strong> (
                {deletingWorkspace.channelHandle})?
              </p>
              <div
                className="notice-box info"
                style={{
                  backgroundColor: "rgba(239, 68, 68, 0.08)",
                  border: "1px solid var(--border)",
                  color: "var(--text-secondary)",
                }}
              >
                <IconInfo size={16} />
                <div>
                  Hành động này sẽ xóa các dự án, cảnh và tệp cục bộ liên quan của Workspace. Không thể hoàn tác.
                </div>
              </div>
            </div>
            <div className="modal-footer" style={{ justifyContent: "flex-end", gap: "0.5rem" }}>
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => setDeletingWorkspace(null)}
                disabled={isDeleting}
              >
                Hủy
              </button>
              <button
                type="button"
                className="btn btn-primary"
                style={{ backgroundColor: "var(--status-error)" }}
                onClick={handleConfirmDelete}
                disabled={isDeleting}
              >
                {isDeleting ? (
                  <>
                    <IconRefresh size={16} className="spin" /> Đang xóa…
                  </>
                ) : (
                  "Xác nhận xóa"
                )}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
