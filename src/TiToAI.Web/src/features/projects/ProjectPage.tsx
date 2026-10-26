import { useState, useEffect, useCallback, useRef, useMemo } from "react";
import {
  projectApi,
  type ProjectDetailDto,
  type SceneDto,
} from "./projectApi";
import { SceneTable } from "./SceneTable";
import { SceneForm } from "./SceneForm";
import { ProjectForm } from "./ProjectForm";
import type { ErrorDto } from "../../contracts/bridge";
import {
  IconPlus,
  IconRefresh,
} from "../../components/Icons";

export interface ProjectPageProps {
  workspaceId: string;
  projectId: string;
  onBack: () => void;
}

type ProjectTab = "scenes" | "overview" | "assets";

export function ProjectPage({ workspaceId, projectId, onBack }: ProjectPageProps) {
  const [project, setProject] = useState<ProjectDetailDto | null>(null);
  const [scenes, setScenes] = useState<SceneDto[]>([]);
  const [activeTab, setActiveTab] = useState<ProjectTab>("scenes");

  const [isLoading, setIsLoading] = useState(true);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  // Modals
  const [isSceneFormOpen, setIsSceneFormOpen] = useState(false);
  const [sceneFormMode, setSceneFormMode] = useState<"create" | "edit">("create");
  const [editingScene, setEditingScene] = useState<SceneDto | null>(null);

  const [isEditProjectOpen, setIsEditProjectOpen] = useState(false);

  const abortControllerRef = useRef<AbortController | null>(null);

  const fetchData = useCallback(async () => {
    if (!workspaceId || !projectId) return;

    if (abortControllerRef.current) {
      abortControllerRef.current.abort();
    }
    const controller = new AbortController();
    abortControllerRef.current = controller;

    setIsLoading(true);
    setErrorMessage(null);

    try {
      const [prj, sceneList] = await Promise.all([
        projectApi.getProject(workspaceId, projectId),
        projectApi.listScenes(workspaceId, projectId),
      ]);

      if (controller.signal.aborted) return;

      setProject(prj);
      setScenes(sceneList);
    } catch (err: unknown) {
      if (controller.signal.aborted) return;
      const errorDto = err as ErrorDto;
      console.error("[ProjectPage] Lỗi tải dữ liệu dự án:", errorDto);
      setErrorMessage(errorDto?.message || "Không thể tải dữ liệu chi tiết của dự án.");
    } finally {
      if (!controller.signal.aborted) {
        setIsLoading(false);
      }
    }
  }, [workspaceId, projectId]);

  useEffect(() => {
    fetchData();
    return () => {
      if (abortControllerRef.current) {
        abortControllerRef.current.abort();
      }
    };
  }, [fetchData]);

  // Thống kê tổng hợp cho Tab Tổng quan
  const stats = useMemo(() => {
    const totalDuration = scenes.reduce((sum, s) => sum + (s.targetDurationSeconds || 0), 0);
    const withPrompt = scenes.filter((s) => s.promptText && s.promptText.trim().length > 0).length;
    const withImage = scenes.filter((s) => !!s.imageAssetId).length;
    const withAudio = scenes.filter((s) => !!s.audioAssetId).length;
    const withVideo = scenes.filter((s) => !!s.videoAssetId).length;
    const completed = scenes.filter((s) => s.status === "Completed").length;

    return {
      totalDuration,
      withPrompt,
      withImage,
      withAudio,
      withVideo,
      completed,
    };
  }, [scenes]);

  const handleOpenCreateScene = () => {
    setEditingScene(null);
    setSceneFormMode("create");
    setIsSceneFormOpen(true);
  };

  const handleEditScene = (scene: SceneDto) => {
    setEditingScene(scene);
    setSceneFormMode("edit");
    setIsSceneFormOpen(true);
  };

  if (isLoading && !project) {
    return (
      <div className="loading-card" style={{ marginTop: "2rem" }}>
        <IconRefresh size={32} className="spin" />
        <p>Đang tải thông tin chi tiết dự án…</p>
      </div>
    );
  }

  if (errorMessage || !project) {
    return (
      <div className="empty-card error-card" style={{ marginTop: "2rem" }}>
        <h3>Không thể tải thông tin dự án</h3>
        <p>{errorMessage || "Dự án không tồn tại hoặc đã bị xóa."}</p>
        <div style={{ display: "flex", gap: "0.5rem" }}>
          <button type="button" className="btn btn-secondary" onClick={onBack}>
            ← Quay lại danh sách dự án
          </button>
          <button type="button" className="btn btn-secondary" onClick={fetchData}>
            <IconRefresh size={16} /> Thử lại
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="view-project-detail" style={{ paddingBottom: "2.5rem" }}>
      {/* Header dự án theo Panel 03 */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          flexWrap: "wrap",
          gap: "1rem",
          backgroundColor: "var(--surface-card)",
          border: "1px solid var(--border-subtle)",
          borderRadius: "var(--radius-xl)",
          padding: "1.25rem 1.5rem",
          marginBottom: "1.5rem",
          boxShadow: "0 2px 10px rgba(0, 0, 0, 0.05)",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "1rem" }}>
          <button
            type="button"
            className="btn btn-secondary btn-sm"
            onClick={onBack}
            style={{ display: "flex", alignItems: "center", gap: "0.3rem" }}
          >
            ← Quay lại
          </button>

          <div>
            <div style={{ display: "flex", alignItems: "center", gap: "0.75rem", flexWrap: "wrap" }}>
              <h2 style={{ margin: 0, fontSize: "1.3rem", fontWeight: 700, color: "var(--text-primary)" }}>
                {project.name}
              </h2>
              <span
                className={`badge ${
                  project.status === "Completed"
                    ? "badge-success"
                    : project.status === "InProgress"
                    ? "badge-info"
                    : "badge-neutral"
                }`}
              >
                ● {project.status}
              </span>
            </div>
            <div style={{ fontSize: "0.82rem", color: "var(--text-muted)", marginTop: "0.2rem" }}>
              {project.description || "Chưa có mô tả chi tiết."}
            </div>
          </div>
        </div>

        <div style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
          <button
            type="button"
            className="btn btn-secondary"
            onClick={() => setIsEditProjectOpen(true)}
          >
            ✏️ Sửa dự án
          </button>
          <button
            type="button"
            className="btn btn-primary"
            onClick={handleOpenCreateScene}
            style={{ display: "flex", alignItems: "center", gap: "0.4rem" }}
          >
            <IconPlus size={16} /> Thêm Cảnh
          </button>
        </div>
      </div>

      {/* Tabs Điều Hướng */}
      <div
        className="settings-tabs"
        style={{
          backgroundColor: "var(--surface-card)",
          borderRadius: "var(--radius-md) var(--radius-md) 0 0",
          border: "1px solid var(--border-subtle)",
          borderBottom: "none",
          padding: "0 0.5rem",
        }}
      >
        <button
          type="button"
          className={`settings-tab-btn ${activeTab === "scenes" ? "active" : ""}`}
          onClick={() => setActiveTab("scenes")}
        >
          🎬 Danh sách Cảnh ({scenes.length})
        </button>
        <button
          type="button"
          className={`settings-tab-btn ${activeTab === "overview" ? "active" : ""}`}
          onClick={() => setActiveTab("overview")}
        >
          📊 Tổng quan tiến độ
        </button>
        <button
          type="button"
          className={`settings-tab-btn ${activeTab === "assets" ? "active" : ""}`}
          onClick={() => setActiveTab("assets")}
        >
          📁 Tài nguyên phân cảnh
        </button>
      </div>

      {/* Nội dung Tab */}
      <div
        style={{
          backgroundColor: "var(--surface-card)",
          border: "1px solid var(--border-subtle)",
          borderRadius: "0 0 var(--radius-lg) var(--radius-lg)",
          padding: "1.25rem",
        }}
      >
        {activeTab === "scenes" && (
          <SceneTable
            workspaceId={workspaceId}
            projectId={projectId}
            scenes={scenes}
            isLoading={isLoading}
            onRefresh={fetchData}
            onEditScene={handleEditScene}
          />
        )}

        {activeTab === "overview" && (
          <div style={{ display: "flex", flexDirection: "column", gap: "1.5rem" }}>
            <div className="kpi-grid">
              <div className="kpi-card">
                <span className="kpi-label">Tổng số cảnh</span>
                <span className="kpi-val">{scenes.length}</span>
                <span className="kpi-sub">Phân đoạn nội dung</span>
              </div>
              <div className="kpi-card">
                <span className="kpi-label">Thời lượng ước tính</span>
                <span className="kpi-val">{stats.totalDuration}s</span>
                <span className="kpi-sub">
                  Khoảng {Math.floor(stats.totalDuration / 60)}m {stats.totalDuration % 60}s
                </span>
              </div>
              <div className="kpi-card">
                <span className="kpi-label">Cảnh đã có Prompt</span>
                <span className="kpi-val">
                  {stats.withPrompt} / {scenes.length}
                </span>
                <span className="kpi-sub">
                  {scenes.length > 0 ? Math.round((stats.withPrompt / scenes.length) * 100) : 0}% hoàn thành
                </span>
              </div>
              <div className="kpi-card">
                <span className="kpi-label">Cảnh đã kết xuất Video</span>
                <span className="kpi-val">
                  {stats.withVideo} / {scenes.length}
                </span>
                <span className="kpi-sub">
                  {scenes.length > 0 ? Math.round((stats.withVideo / scenes.length) * 100) : 0}% hoàn thành
                </span>
              </div>
            </div>

            <div
              style={{
                backgroundColor: "var(--surface-card-subtle)",
                borderRadius: "var(--radius-md)",
                padding: "1.25rem",
                border: "1px solid var(--border-subtle)",
              }}
            >
              <h4 style={{ margin: "0 0 1rem 0", color: "var(--text-primary)" }}>
                Tiến độ chuẩn bị sản xuất
              </h4>
              <div style={{ display: "flex", flexDirection: "column", gap: "0.85rem" }}>
                <div>
                  <div style={{ display: "flex", justifyContent: "space-between", fontSize: "0.85rem", marginBottom: "0.3rem" }}>
                    <span>1. Lời dẫn & Kịch bản</span>
                    <span>{stats.withPrompt}/{scenes.length} cảnh</span>
                  </div>
                  <div style={{ height: 6, backgroundColor: "var(--border)", borderRadius: 3, overflow: "hidden" }}>
                    <div
                      style={{
                        height: "100%",
                        width: `${scenes.length ? (stats.withPrompt / scenes.length) * 100 : 0}%`,
                        backgroundColor: "var(--status-success)",
                      }}
                    />
                  </div>
                </div>

                <div>
                  <div style={{ display: "flex", justifyContent: "space-between", fontSize: "0.85rem", marginBottom: "0.3rem" }}>
                    <span>2. Hình ảnh minh họa AI</span>
                    <span>{stats.withImage}/{scenes.length} cảnh</span>
                  </div>
                  <div style={{ height: 6, backgroundColor: "var(--border)", borderRadius: 3, overflow: "hidden" }}>
                    <div
                      style={{
                        height: "100%",
                        width: `${scenes.length ? (stats.withImage / scenes.length) * 100 : 0}%`,
                        backgroundColor: "var(--accent)",
                      }}
                    />
                  </div>
                </div>

                <div>
                  <div style={{ display: "flex", justifyContent: "space-between", fontSize: "0.85rem", marginBottom: "0.3rem" }}>
                    <span>3. Âm thanh gốc</span>
                    <span>{stats.withAudio}/{scenes.length} cảnh</span>
                  </div>
                  <div style={{ height: 6, backgroundColor: "var(--border)", borderRadius: 3, overflow: "hidden" }}>
                    <div
                      style={{
                        height: "100%",
                        width: `${scenes.length ? (stats.withAudio / scenes.length) * 100 : 0}%`,
                        backgroundColor: "var(--status-info)",
                      }}
                    />
                  </div>
                </div>

                <div>
                  <div style={{ display: "flex", justifyContent: "space-between", fontSize: "0.85rem", marginBottom: "0.3rem" }}>
                    <span>4. Kết xuất Video MuseAI</span>
                    <span>{stats.withVideo}/{scenes.length} cảnh</span>
                  </div>
                  <div style={{ height: 6, backgroundColor: "var(--border)", borderRadius: 3, overflow: "hidden" }}>
                    <div
                      style={{
                        height: "100%",
                        width: `${scenes.length ? (stats.withVideo / scenes.length) * 100 : 0}%`,
                        backgroundColor: "var(--status-warning)",
                      }}
                    />
                  </div>
                </div>
              </div>
            </div>
          </div>
        )}

        {activeTab === "assets" && (
          <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
            <h4 style={{ margin: 0, color: "var(--text-primary)" }}>
              Danh mục tệp tài nguyên phân cảnh
            </h4>
            <div
              style={{
                backgroundColor: "var(--surface-card-subtle)",
                borderRadius: "var(--radius-md)",
                padding: "1rem",
                border: "1px solid var(--border-subtle)",
              }}
            >
              {scenes.every((s) => !s.imageAssetId && !s.audioAssetId && !s.videoAssetId) ? (
                <div style={{ textAlign: "center", padding: "1.5rem", color: "var(--text-muted)" }}>
                  Chưa có tài nguyên media nào được tạo hoặc nhập vào các phân cảnh.
                </div>
              ) : (
                <div style={{ display: "flex", flexDirection: "column", gap: "0.5rem" }}>
                  {scenes.map((s) => (
                    <div
                      key={s.sceneId}
                      style={{
                        display: "flex",
                        alignItems: "center",
                        justifyContent: "space-between",
                        padding: "0.5rem 0.75rem",
                        backgroundColor: "var(--surface-card)",
                        border: "1px solid var(--border-subtle)",
                        borderRadius: "var(--radius-sm)",
                        fontSize: "0.82rem",
                      }}
                    >
                      <span style={{ fontWeight: 600 }}>Cảnh #{s.sceneSequence}: {s.name}</span>
                      <div style={{ display: "flex", gap: "1rem", color: "var(--text-secondary)" }}>
                        <span>Ảnh: {s.imageAssetId || "—"}</span>
                        <span>Âm thanh: {s.audioAssetId || "—"}</span>
                        <span>Video: {s.videoAssetId || "—"}</span>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>
        )}
      </div>

      {/* Modal Form Cảnh */}
      <SceneForm
        isOpen={isSceneFormOpen}
        mode={sceneFormMode}
        workspaceId={workspaceId}
        projectId={projectId}
        scene={editingScene}
        onClose={() => setIsSceneFormOpen(false)}
        onSuccess={() => {
          setIsSceneFormOpen(false);
          fetchData();
        }}
      />

      {/* Modal Form Sửa Dự án */}
      <ProjectForm
        isOpen={isEditProjectOpen}
        mode="edit"
        workspaceId={workspaceId}
        project={project}
        onClose={() => setIsEditProjectOpen(false)}
        onSuccess={() => {
          setIsEditProjectOpen(false);
          fetchData();
        }}
      />
    </div>
  );
}
