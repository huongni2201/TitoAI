import { useState, useEffect, useMemo, useCallback, useRef } from "react";
import { projectApi, type ProjectSummaryDto } from "./projectApi";
import { ProjectForm } from "./ProjectForm";
import type { ErrorDto } from "../../contracts/bridge";
import {
  IconProject,
  IconSearch,
  IconPlus,
  IconRefresh,
  IconInfo,
} from "../../components/Icons";

export interface ProjectListPageProps {
  workspaceId: string;
  onSelectProject: (projectId: string) => void;
  onBackToWorkspaces?: () => void;
}

type StatusFilter = "all" | "Draft" | "InProgress" | "Completed";

export function ProjectListPage({
  workspaceId,
  onSelectProject,
  onBackToWorkspaces,
}: ProjectListPageProps) {
  const [projects, setProjects] = useState<ProjectSummaryDto[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  // Bộ lọc & Tìm kiếm
  const [searchQuery, setSearchQuery] = useState("");
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all");

  // Modals & Menu
  const [isFormOpen, setIsFormOpen] = useState(false);
  const [formMode, setFormMode] = useState<"create" | "edit">("create");
  const [editingProject, setEditingProject] = useState<ProjectSummaryDto | null>(null);

  // Modal Xóa
  const [deletingProject, setDeletingProject] = useState<ProjectSummaryDto | null>(null);
  const [isDeleting, setIsDeleting] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  const [activeMenuProjectId, setActiveMenuProjectId] = useState<string | null>(null);

  const abortControllerRef = useRef<AbortController | null>(null);

  const fetchProjects = useCallback(async () => {
    if (!workspaceId) return;

    if (abortControllerRef.current) {
      abortControllerRef.current.abort();
    }
    const controller = new AbortController();
    abortControllerRef.current = controller;

    setIsLoading(true);
    setErrorMessage(null);

    try {
      const list = await projectApi.listByWorkspace(workspaceId);
      if (controller.signal.aborted) return;
      setProjects(list);
    } catch (err: unknown) {
      if (controller.signal.aborted) return;
      const errorDto = err as ErrorDto;
      console.error("[ProjectListPage] Lỗi tải dự án:", errorDto);
      setErrorMessage(errorDto?.message || "Không thể tải danh sách dự án của kênh.");
    } finally {
      if (!controller.signal.aborted) {
        setIsLoading(false);
      }
    }
  }, [workspaceId]);

  useEffect(() => {
    fetchProjects();
    return () => {
      if (abortControllerRef.current) {
        abortControllerRef.current.abort();
      }
    };
  }, [fetchProjects]);

  // Đóng action menu khi click ngoài
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      const target = e.target as HTMLElement;
      if (!target.closest(".card-action-menu-wrapper")) {
        setActiveMenuProjectId(null);
      }
    };
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  const filteredProjects = useMemo(() => {
    return projects.filter((prj) => {
      if (statusFilter !== "all" && prj.status !== statusFilter) {
        return false;
      }
      if (searchQuery.trim()) {
        const query = searchQuery.trim().toLowerCase();
        const matchName = prj.name.toLowerCase().includes(query);
        const matchDesc = (prj.description || "").toLowerCase().includes(query);
        return matchName || matchDesc;
      }
      return true;
    });
  }, [projects, statusFilter, searchQuery]);

  const handleDelete = async () => {
    if (!deletingProject || isDeleting) return;
    setIsDeleting(true);
    setDeleteError(null);

    try {
      await projectApi.deleteProject(workspaceId, deletingProject.projectId);
      setProjects((prev) => prev.filter((p) => p.projectId !== deletingProject.projectId));
      setDeletingProject(null);
    } catch (err: unknown) {
      const errorDto = err as ErrorDto;
      console.error("[ProjectListPage] Lỗi xóa dự án:", errorDto);
      if (errorDto?.code === "CONCURRENCY_CONFLICT") {
        setDeleteError("Không thể xóa dự án vì có tác vụ đang chạy trong dự án này.");
      } else {
        setDeleteError(errorDto?.message || "Đã xảy ra lỗi khi xóa dự án.");
      }
    } finally {
      setIsDeleting(false);
    }
  };

  return (
    <div className="view-projects" style={{ paddingBottom: "2rem" }}>
      {/* Header */}
      <div
        className="page-header"
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          marginBottom: "1.25rem",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "1rem" }}>
          {onBackToWorkspaces && (
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={onBackToWorkspaces}
              style={{ display: "flex", alignItems: "center", gap: "0.25rem" }}
            >
              ← Danh sách kênh
            </button>
          )}
          <div>
            <h2 className="page-title" style={{ fontSize: "1.5rem", fontWeight: 700, margin: 0 }}>
              Dự Án Video
            </h2>
            <p className="page-desc" style={{ color: "var(--text-secondary)", marginTop: "0.25rem" }}>
              Quản lý kịch bản phân cảnh, prompt chuyển động và video hoàn chỉnh cho kênh.
            </p>
          </div>
        </div>

        <button
          type="button"
          className="btn btn-primary"
          onClick={() => {
            setEditingProject(null);
            setFormMode("create");
            setIsFormOpen(true);
          }}
          style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}
        >
          <IconPlus size={16} /> Tạo Dự Án Mới
        </button>
      </div>

      {/* Filter & Search Bar */}
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
        <div className="tab-group" role="tablist" aria-label="Lọc theo trạng thái dự án">
          <button
            type="button"
            role="tab"
            aria-selected={statusFilter === "all"}
            className={`filter-tab ${statusFilter === "all" ? "active" : ""}`}
            onClick={() => setStatusFilter("all")}
          >
            Tất cả ({projects.length})
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={statusFilter === "InProgress"}
            className={`filter-tab ${statusFilter === "InProgress" ? "active" : ""}`}
            onClick={() => setStatusFilter("InProgress")}
          >
            Đang sản xuất ({projects.filter((p) => p.status === "InProgress").length})
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={statusFilter === "Completed"}
            className={`filter-tab ${statusFilter === "Completed" ? "active" : ""}`}
            onClick={() => setStatusFilter("Completed")}
          >
            Hoàn thành ({projects.filter((p) => p.status === "Completed").length})
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={statusFilter === "Draft"}
            className={`filter-tab ${statusFilter === "Draft" ? "active" : ""}`}
            onClick={() => setStatusFilter("Draft")}
          >
            Nháp ({projects.filter((p) => p.status === "Draft").length})
          </button>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}>
          <div style={{ position: "relative", minWidth: 260 }}>
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
              placeholder="Tìm dự án theo tên hoặc mô tả…"
              style={{ paddingLeft: "2rem" }}
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
            />
          </div>

          <button
            type="button"
            className="btn btn-secondary btn-icon"
            onClick={fetchProjects}
            title="Tải lại danh sách"
            disabled={isLoading}
          >
            <IconRefresh size={16} className={isLoading ? "spin" : ""} />
          </button>
        </div>
      </div>

      {/* Danh sách dự án */}
      {isLoading ? (
        <div className="loading-card">
          <IconRefresh size={28} className="spin" />
          <p>Đang tải danh sách Dự án từ Host…</p>
        </div>
      ) : errorMessage ? (
        <div className="empty-card error-card">
          <h3>Không thể tải danh sách Dự án</h3>
          <p>{errorMessage}</p>
          <button type="button" className="btn btn-secondary" onClick={fetchProjects}>
            <IconRefresh size={16} /> Thử lại
          </button>
        </div>
      ) : projects.length === 0 ? (
        <div className="empty-card">
          <div className="empty-icon">
            <IconProject size={44} />
          </div>
          <h3>Chưa có dự án nào trong kênh này</h3>
          <p>
            Hãy tạo dự án video đầu tiên để bắt đầu xây dựng kịch bản phân cảnh và tự động sinh prompt.
          </p>
          <button
            type="button"
            className="btn btn-primary"
            style={{ marginTop: "1rem" }}
            onClick={() => {
              setEditingProject(null);
              setFormMode("create");
              setIsFormOpen(true);
            }}
          >
            <IconPlus size={16} /> Tạo Dự Án Mới
          </button>
        </div>
      ) : filteredProjects.length === 0 ? (
        <div className="empty-card">
          <h3>Không có dự án phù hợp</h3>
          <p>Không tìm thấy dự án nào khớp với điều kiện lọc hiện tại.</p>
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
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fill, minmax(320px, 1fr))",
            gap: "1.25rem",
          }}
        >
          {filteredProjects.map((prj) => {
            const isMenuOpen = activeMenuProjectId === prj.projectId;
            return (
              <div
                key={prj.projectId}
                style={{
                  backgroundColor: "var(--surface-card)",
                  border: "1px solid var(--border-subtle)",
                  borderRadius: "var(--radius-lg)",
                  padding: "1.25rem",
                  display: "flex",
                  flexDirection: "column",
                  justifyContent: "space-between",
                  boxShadow: "0 2px 8px rgba(0, 0, 0, 0.06)",
                }}
              >
                <div>
                  <div
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "flex-start",
                      gap: "0.5rem",
                      marginBottom: "0.5rem",
                    }}
                  >
                    <h4
                      style={{
                        margin: 0,
                        fontSize: "1.05rem",
                        fontWeight: 700,
                        color: "var(--text-primary)",
                      }}
                    >
                      {prj.name}
                    </h4>

                    <div className="card-action-menu-wrapper" style={{ position: "relative" }}>
                      <button
                        type="button"
                        className="btn-icon"
                        style={{ width: 28, height: 28 }}
                        onClick={(e) => {
                          e.stopPropagation();
                          setActiveMenuProjectId(isMenuOpen ? null : prj.projectId);
                        }}
                        aria-label="Tùy chọn"
                      >
                        ···
                      </button>

                      {isMenuOpen && (
                        <div
                          style={{
                            position: "absolute",
                            right: 0,
                            top: 32,
                            width: 150,
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
                            style={{
                              background: "none",
                              border: "none",
                              padding: "0.5rem 0.85rem",
                              textAlign: "left",
                              fontSize: "0.82rem",
                              color: "var(--text-primary)",
                              cursor: "pointer",
                            }}
                            onClick={() => {
                              setActiveMenuProjectId(null);
                              setEditingProject(prj);
                              setFormMode("edit");
                              setIsFormOpen(true);
                            }}
                          >
                            ✏️ Sửa thông tin
                          </button>
                          <button
                            type="button"
                            style={{
                              background: "none",
                              border: "none",
                              padding: "0.5rem 0.85rem",
                              textAlign: "left",
                              fontSize: "0.82rem",
                              color: "var(--status-error)",
                              cursor: "pointer",
                            }}
                            onClick={() => {
                              setActiveMenuProjectId(null);
                              setDeletingProject(prj);
                              setDeleteError(null);
                            }}
                          >
                            🗑️ Xóa dự án
                          </button>
                        </div>
                      )}
                    </div>
                  </div>

                  <p
                    style={{
                      margin: "0 0 0.85rem 0",
                      fontSize: "0.82rem",
                      color: "var(--text-secondary)",
                      lineHeight: 1.45,
                    }}
                  >
                    {prj.description || "Chưa có mô tả chi tiết."}
                  </p>

                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      justifyContent: "space-between",
                      fontSize: "0.8rem",
                      marginBottom: "1rem",
                    }}
                  >
                    <span className="badge badge-neutral">
                      🎬 {prj.sceneCount} cảnh
                    </span>
                    <span
                      className={`badge ${
                        prj.status === "Completed"
                          ? "badge-success"
                          : prj.status === "InProgress"
                          ? "badge-info"
                          : "badge-neutral"
                      }`}
                    >
                      ● {prj.status}
                    </span>
                  </div>
                </div>

                <div
                  style={{
                    borderTop: "1px solid var(--border-subtle)",
                    paddingTop: "0.75rem",
                    display: "flex",
                    justifyContent: "space-between",
                    alignItems: "center",
                  }}
                >
                  <span style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
                    Cập nhật: {new Date(prj.updatedAtUtc).toLocaleDateString("vi-VN")}
                  </span>

                  <button
                    type="button"
                    className="btn btn-primary btn-sm"
                    onClick={() => onSelectProject(prj.projectId)}
                  >
                    Mở dự án →
                  </button>
                </div>
              </div>
            );
          })}
        </div>
      )}

      {/* Modal Form Tạo/Sửa */}
      <ProjectForm
        isOpen={isFormOpen}
        mode={formMode}
        workspaceId={workspaceId}
        project={editingProject}
        onClose={() => setIsFormOpen(false)}
        onSuccess={() => {
          setIsFormOpen(false);
          fetchProjects();
        }}
      />

      {/* Modal xác nhận xóa */}
      {deletingProject && (
        <div
          className="modal-backdrop"
          role="presentation"
          onClick={(e) => {
            if (e.target === e.currentTarget && !isDeleting) {
              setDeletingProject(null);
            }
          }}
        >
          <div
            className="modal-container"
            role="dialog"
            aria-modal="true"
            aria-labelledby="delete-prj-dialog-title"
            style={{ maxWidth: 460 }}
          >
            <div className="modal-header">
              <h3 id="delete-prj-dialog-title" style={{ margin: 0, color: "var(--status-error)" }}>
                Xác nhận xóa Dự Án
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
                Bạn có chắc chắn muốn xóa dự án <strong>{deletingProject.name}</strong>?
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
                  Toàn bộ các cảnh và tài nguyên phân cảnh trong dự án này sẽ bị xóa.
                </div>
              </div>
            </div>
            <div className="modal-footer" style={{ justifyContent: "flex-end", gap: "0.5rem" }}>
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => setDeletingProject(null)}
                disabled={isDeleting}
              >
                Hủy
              </button>
              <button
                type="button"
                className="btn btn-primary"
                style={{ backgroundColor: "var(--status-error)" }}
                onClick={handleDelete}
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
