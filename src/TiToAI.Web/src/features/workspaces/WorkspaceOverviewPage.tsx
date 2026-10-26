import { useState, useEffect, useCallback, useRef } from "react";
import { workspaceApi, type WorkspaceDetailDto } from "./workspaceApi";
import { projectApi, type ProjectSummaryDto } from "../projects/projectApi";
import { WorkspaceForm } from "./WorkspaceForm";
import { ProjectForm } from "../projects/ProjectForm";
import type { ErrorDto } from "../../contracts/bridge";
import {
  IconProject,
  IconPlus,
  IconRefresh,
} from "../../components/Icons";

export interface WorkspaceOverviewPageProps {
  workspaceId: string;
  onNavigateToProjects?: () => void;
  onSelectProject?: (projectId: string) => void;
}

// Tạo màu avatar ổn định theo tên
function getAvatarBgColor(text: string): string {
  const colors = [
    "#6d3af2", "#0284c7", "#059669", "#d97706",
    "#e11d48", "#7c3aed", "#2563eb", "#db2777",
  ];
  let hash = 0;
  for (let i = 0; i < text.length; i++) {
    hash = (hash << 5) - hash + text.charCodeAt(i);
    hash |= 0;
  }
  return colors[Math.abs(hash) % colors.length];
}

function getInitials(name: string): string {
  if (!name) return "WS";
  const words = name.trim().split(/\s+/);
  if (words.length >= 2) {
    return (words[0][0] + words[1][0]).toUpperCase();
  }
  return name.slice(0, 2).toUpperCase();
}

export function WorkspaceOverviewPage({
  workspaceId,
  onNavigateToProjects,
  onSelectProject,
}: WorkspaceOverviewPageProps) {
  const [workspace, setWorkspace] = useState<WorkspaceDetailDto | null>(null);
  const [projects, setProjects] = useState<ProjectSummaryDto[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  // Dialogs
  const [isEditFormOpen, setIsEditFormOpen] = useState(false);
  const [isCreateProjectOpen, setIsCreateProjectOpen] = useState(false);

  const abortControllerRef = useRef<AbortController | null>(null);

  const fetchData = useCallback(async () => {
    if (!workspaceId) return;

    if (abortControllerRef.current) {
      abortControllerRef.current.abort();
    }
    const controller = new AbortController();
    abortControllerRef.current = controller;

    setIsLoading(true);
    setErrorMessage(null);

    try {
      const [wsDetail, prjList] = await Promise.all([
        workspaceApi.get(workspaceId),
        projectApi.listByWorkspace(workspaceId).catch(() => [] as ProjectSummaryDto[]),
      ]);

      if (controller.signal.aborted) return;

      setWorkspace(wsDetail);
      setProjects(prjList);
    } catch (err: unknown) {
      if (controller.signal.aborted) return;
      const errorDto = err as ErrorDto;
      console.error("[WorkspaceOverviewPage] Lỗi tải tổng quan:", errorDto);
      setErrorMessage(errorDto?.message || "Không thể tải dữ liệu tổng quan của Workspace.");
    } finally {
      if (!controller.signal.aborted) {
        setIsLoading(false);
      }
    }
  }, [workspaceId]);

  useEffect(() => {
    fetchData();

    // Lắng nghe sự kiện đổi trạng thái từ host
    const unsubscribe = workspaceApi.onStatusChanged((evt) => {
      if (evt.payload.workspaceId === workspaceId) {
        setWorkspace((prev) => (prev ? { ...prev, status: evt.payload.status } : null));
      }
    });

    return () => {
      unsubscribe();
      if (abortControllerRef.current) {
        abortControllerRef.current.abort();
      }
    };
  }, [workspaceId, fetchData]);

  if (isLoading) {
    return (
      <div className="loading-card" style={{ marginTop: "2rem" }}>
        <IconRefresh size={32} className="spin" />
        <p>Đang tải dữ liệu tổng quan kênh…</p>
      </div>
    );
  }

  if (errorMessage || !workspace) {
    return (
      <div className="empty-card error-card" style={{ marginTop: "2rem" }}>
        <h3>Không thể tải thông tin kênh</h3>
        <p>{errorMessage || "Kênh không tồn tại hoặc đã bị xóa."}</p>
        <button type="button" className="btn btn-secondary" onClick={fetchData}>
          <IconRefresh size={16} /> Thử lại
        </button>
      </div>
    );
  }

  const totalScenes = projects.reduce((acc, p) => acc + (p.sceneCount || 0), 0);
  const recentProjects = projects.slice(0, 3);
  const avatarColor = getAvatarBgColor(workspace.name);
  const initials = getInitials(workspace.name);
  const isActive = workspace.status === "Active";

  return (
    <div className="view-overview" style={{ paddingBottom: "2.5rem" }}>
      {/* Header kênh theo Panel 02 mockup */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          flexWrap: "wrap",
          gap: "1.25rem",
          backgroundColor: "var(--surface-card)",
          border: "1px solid var(--border-subtle)",
          borderRadius: "var(--radius-xl)",
          padding: "1.5rem",
          marginBottom: "1.5rem",
          boxShadow: "0 2px 10px rgba(0, 0, 0, 0.05)",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "1.25rem" }}>
          {/* Avatar to */}
          <div
            style={{
              width: 64,
              height: 64,
              borderRadius: "50%",
              backgroundColor: avatarColor,
              color: "#ffffff",
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              fontSize: "1.5rem",
              fontWeight: 800,
              boxShadow: "0 4px 12px rgba(0, 0, 0, 0.2)",
              flexShrink: 0,
            }}
          >
            {initials}
          </div>

          <div>
            <div style={{ display: "flex", alignItems: "center", gap: "0.75rem", flexWrap: "wrap" }}>
              <h2 style={{ margin: 0, fontSize: "1.4rem", fontWeight: 700, color: "var(--text-primary)" }}>
                {workspace.name}
              </h2>
              <span className={`badge ${isActive ? "badge-success" : "badge-neutral"}`}>
                ● {isActive ? "Đang hoạt động" : "Tạm dừng"}
              </span>
            </div>

            <div style={{ display: "flex", alignItems: "center", gap: "1rem", marginTop: "0.35rem", fontSize: "0.85rem", color: "var(--text-secondary)" }}>
              <span>{workspace.channelHandle}</span>
              <span>•</span>
              <span>{workspace.topic || "Chưa thiết lập chủ đề"}</span>
            </div>
          </div>
        </div>

        {/* Nút hành động */}
        <div style={{ display: "flex", gap: "0.75rem", alignItems: "center" }}>
          <button
            type="button"
            className="btn btn-secondary"
            onClick={() => setIsEditFormOpen(true)}
            style={{ display: "flex", alignItems: "center", gap: "0.4rem" }}
          >
            ✏️ Chỉnh sửa kênh
          </button>
        </div>
      </div>

      {/* Hàng 4 thẻ chỉ số thống kê (KPI Cards) */}
      <div className="kpi-grid" style={{ marginBottom: "1.75rem" }}>
        <div className="kpi-card">
          <span className="kpi-label">Tổng số dự án</span>
          <span className="kpi-val">{projects.length}</span>
          <span className="kpi-sub">Dự án video đang quản lý</span>
        </div>

        <div className="kpi-card">
          <span className="kpi-label">Tổng số cảnh (Scenes)</span>
          <span className="kpi-val">{totalScenes}</span>
          <span className="kpi-sub">Cảnh được tạo trong kênh</span>
        </div>

        <div className="kpi-card">
          <span className="kpi-label">Tác vụ đang chạy (Jobs)</span>
          <span className="kpi-val" style={{ fontSize: "1.2rem", color: "var(--text-muted)" }}>
            Chưa có dữ liệu
          </span>
          <span className="kpi-sub">Chờ kết nối hàng đợi xử lý</span>
        </div>

        <div className="kpi-card">
          <span className="kpi-label">Tác vụ chờ xử lý</span>
          <span className="kpi-val" style={{ fontSize: "1.2rem", color: "var(--text-muted)" }}>
            Chưa có dữ liệu
          </span>
          <span className="kpi-sub">Cần người dùng can thiệp</span>
        </div>
      </div>

      {/* 2 cột nội dung: Cột trái "Dự án gần đây", Cột phải "Quy trình sản xuất" */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "minmax(0, 1.4fr) minmax(0, 1fr)",
          gap: "1.5rem",
        }}
      >
        {/* CỘT TRÁI: DỰ ÁN GẦN ĐÂY */}
        <div
          style={{
            backgroundColor: "var(--surface-card)",
            border: "1px solid var(--border-subtle)",
            borderRadius: "var(--radius-lg)",
            padding: "1.25rem",
            display: "flex",
            flexDirection: "column",
            gap: "1rem",
          }}
        >
          <div
            style={{
              display: "flex",
              alignItems: "center",
              justifyContent: "space-between",
            }}
          >
            <div>
              <h3 style={{ margin: 0, fontSize: "1.1rem", fontWeight: 700, color: "var(--text-primary)" }}>
                Dự án hiện tại
              </h3>
              <span style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
                Các dự án video đang tiến hành trong Workspace này
              </span>
            </div>

            <div style={{ display: "flex", gap: "0.5rem" }}>
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={() => setIsCreateProjectOpen(true)}
              >
                <IconPlus size={14} /> Dự án mới
              </button>
              {projects.length > 0 && onNavigateToProjects && (
                <button
                  type="button"
                  className="btn btn-secondary btn-sm"
                  onClick={onNavigateToProjects}
                  style={{ color: "var(--accent)" }}
                >
                  Xem tất cả ({projects.length}) →
                </button>
              )}
            </div>
          </div>

          {projects.length === 0 ? (
            <div
              style={{
                padding: "2rem 1rem",
                textAlign: "center",
                backgroundColor: "var(--surface-card-subtle)",
                borderRadius: "var(--radius-md)",
                border: "1px dashed var(--border)",
              }}
            >
              <IconProject size={36} style={{ color: "var(--text-muted)", marginBottom: "0.5rem" }} />
              <p style={{ margin: 0, fontSize: "0.9rem", color: "var(--text-secondary)" }}>
                Chưa có dự án nào trong kênh này.
              </p>
              <button
                type="button"
                className="btn btn-primary btn-sm"
                style={{ marginTop: "0.75rem" }}
                onClick={() => setIsCreateProjectOpen(true)}
              >
                <IconPlus size={14} /> Bắt đầu dự án đầu tiên
              </button>
            </div>
          ) : (
            <div style={{ display: "flex", flexDirection: "column", gap: "0.85rem" }}>
              {recentProjects.map((prj) => (
                <div
                  key={prj.projectId}
                  style={{
                    backgroundColor: "var(--surface-card-subtle)",
                    border: "1px solid var(--border-subtle)",
                    borderRadius: "var(--radius-md)",
                    padding: "1rem",
                    display: "flex",
                    flexDirection: "column",
                    gap: "0.75rem",
                  }}
                >
                  <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start" }}>
                    <div>
                      <h4 style={{ margin: 0, fontSize: "0.95rem", fontWeight: 700, color: "var(--text-primary)" }}>
                        {prj.name}
                      </h4>
                      <p
                        style={{
                          margin: "0.25rem 0 0 0",
                          fontSize: "0.8rem",
                          color: "var(--text-secondary)",
                          lineHeight: 1.4,
                        }}
                      >
                        {prj.description || "Không có mô tả chi tiết."}
                      </p>
                    </div>
                    <span className="badge badge-neutral" style={{ fontSize: "0.75rem" }}>
                      {prj.sceneCount} cảnh
                    </span>
                  </div>

                  <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                    <span style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
                      Trạng thái: <strong>{prj.status}</strong>
                    </span>

                    <button
                      type="button"
                      className="btn btn-primary btn-sm"
                      onClick={() => {
                        if (onSelectProject) {
                          onSelectProject(prj.projectId);
                        } else if (onNavigateToProjects) {
                          onNavigateToProjects();
                        }
                      }}
                    >
                      Tiếp tục làm việc →
                    </button>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>

        {/* CỘT PHẢI: QUY TRÌNH SẢN XUẤT NỘI DUNG (PIPELINE MAP) */}
        <div
          style={{
            backgroundColor: "var(--surface-card)",
            border: "1px solid var(--border-subtle)",
            borderRadius: "var(--radius-lg)",
            padding: "1.25rem",
            display: "flex",
            flexDirection: "column",
            gap: "1rem",
          }}
        >
          <div>
            <h3 style={{ margin: 0, fontSize: "1.1rem", fontWeight: 700, color: "var(--text-primary)" }}>
              Quy trình sản xuất nội dung
            </h3>
            <span style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
              Chu trình chuyển đổi từ ý tưởng kịch bản đến video TikTok
            </span>
          </div>

          <div style={{ display: "flex", flexDirection: "column", gap: "0.75rem" }}>
            {/* Bước 1: Kịch bản */}
            <div
              style={{
                display: "flex",
                alignItems: "center",
                justifyContent: "space-between",
                padding: "0.75rem 1rem",
                backgroundColor: "var(--surface-card-subtle)",
                borderRadius: "var(--radius-md)",
                border: "1px solid var(--border-subtle)",
              }}
            >
              <div style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}>
                <div
                  style={{
                    width: 28,
                    height: 28,
                    borderRadius: "50%",
                    backgroundColor: "var(--accent-subtle)",
                    color: "var(--accent)",
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "center",
                    fontWeight: 700,
                    fontSize: "0.8rem",
                  }}
                >
                  1
                </div>
                <div>
                  <div style={{ fontWeight: 600, fontSize: "0.85rem", color: "var(--text-primary)" }}>
                    Kịch bản & Lời dẫn
                  </div>
                  <div style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
                    Phân đoạn cảnh và soạn thảo nội dung
                  </div>
                </div>
              </div>
              <span className="badge badge-success">Sẵn sàng</span>
            </div>

            {/* Bước 2: Tạo ảnh AI */}
            <div
              style={{
                display: "flex",
                alignItems: "center",
                justifyContent: "space-between",
                padding: "0.75rem 1rem",
                backgroundColor: "var(--surface-card-subtle)",
                borderRadius: "var(--radius-md)",
                border: "1px solid var(--border-subtle)",
              }}
            >
              <div style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}>
                <div
                  style={{
                    width: 28,
                    height: 28,
                    borderRadius: "50%",
                    backgroundColor: "var(--accent-subtle)",
                    color: "var(--accent)",
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "center",
                    fontWeight: 700,
                    fontSize: "0.8rem",
                  }}
                >
                  2
                </div>
                <div>
                  <div style={{ fontWeight: 600, fontSize: "0.85rem", color: "var(--text-primary)" }}>
                    Tạo ảnh AI minh họa
                  </div>
                  <div style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
                    Sinh hình ảnh nhân vật & bối cảnh
                  </div>
                </div>
              </div>
              <span className="badge badge-neutral">Tự động</span>
            </div>

            {/* Bước 3: Prompt chuyển động */}
            <div
              style={{
                display: "flex",
                alignItems: "center",
                justifyContent: "space-between",
                padding: "0.75rem 1rem",
                backgroundColor: "var(--surface-card-subtle)",
                borderRadius: "var(--radius-md)",
                border: "1px solid var(--border-subtle)",
              }}
            >
              <div style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}>
                <div
                  style={{
                    width: 28,
                    height: 28,
                    borderRadius: "50%",
                    backgroundColor: "var(--accent-subtle)",
                    color: "var(--accent)",
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "center",
                    fontWeight: 700,
                    fontSize: "0.8rem",
                  }}
                >
                  3
                </div>
                <div>
                  <div style={{ fontWeight: 600, fontSize: "0.85rem", color: "var(--text-primary)" }}>
                    Prompt chuyển động video
                  </div>
                  <div style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
                    Đặc tả camera và hành động nhân vật
                  </div>
                </div>
              </div>
              <span className="badge badge-neutral">Tự động</span>
            </div>

            {/* Bước 4: Âm thanh gốc & Video AI */}
            <div
              style={{
                display: "flex",
                alignItems: "center",
                justifyContent: "space-between",
                padding: "0.75rem 1rem",
                backgroundColor: "var(--surface-card-subtle)",
                borderRadius: "var(--radius-md)",
                border: "1px solid var(--border-subtle)",
              }}
            >
              <div style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}>
                <div
                  style={{
                    width: 28,
                    height: 28,
                    borderRadius: "50%",
                    backgroundColor: "var(--accent-subtle)",
                    color: "var(--accent)",
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "center",
                    fontWeight: 700,
                    fontSize: "0.8rem",
                  }}
                >
                  4
                </div>
                <div>
                  <div style={{ fontWeight: 600, fontSize: "0.85rem", color: "var(--text-primary)" }}>
                    Âm thanh gốc & Render Video
                  </div>
                  <div style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
                    Sinh video AI qua MuseAI
                  </div>
                </div>
              </div>
              <span className="badge badge-neutral">MuseAI</span>
            </div>

            {/* Bước 5: Đăng tải TikTok */}
            <div
              style={{
                display: "flex",
                alignItems: "center",
                justifyContent: "space-between",
                padding: "0.75rem 1rem",
                backgroundColor: "var(--surface-card-subtle)",
                borderRadius: "var(--radius-md)",
                border: "1px solid var(--border-subtle)",
              }}
            >
              <div style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}>
                <div
                  style={{
                    width: 28,
                    height: 28,
                    borderRadius: "50%",
                    backgroundColor: "var(--accent-subtle)",
                    color: "var(--accent)",
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "center",
                    fontWeight: 700,
                    fontSize: "0.8rem",
                  }}
                >
                  5
                </div>
                <div>
                  <div style={{ fontWeight: 600, fontSize: "0.85rem", color: "var(--text-primary)" }}>
                    Thư viện & Đăng TikTok
                  </div>
                  <div style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
                    Lưu trữ asset và lên lịch phát hành
                  </div>
                </div>
              </div>
              <span className="badge badge-neutral">TikTok</span>
            </div>
          </div>
        </div>
      </div>

      {/* Modal Sửa Workspace */}
      <WorkspaceForm
        isOpen={isEditFormOpen}
        mode="edit"
        workspace={workspace}
        onClose={() => setIsEditFormOpen(false)}
        onSuccess={() => {
          setIsEditFormOpen(false);
          fetchData();
        }}
      />

      {/* Modal Tạo Dự án mới */}
      <ProjectForm
        isOpen={isCreateProjectOpen}
        mode="create"
        workspaceId={workspaceId}
        onClose={() => setIsCreateProjectOpen(false)}
        onSuccess={(newProjectId) => {
          setIsCreateProjectOpen(false);
          fetchData();
          onSelectProject?.(newProjectId);
        }}
      />
    </div>
  );
}
