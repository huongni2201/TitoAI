import { useState } from "react";
import { projectApi, type SceneDto } from "./projectApi";
import { bridgeClient } from "../../services/bridgeClient";
import type { ErrorDto } from "../../contracts/bridge";
import {
  IconCheck,
  IconRefresh,
} from "../../components/Icons";

export interface SceneTableProps {
  workspaceId: string;
  projectId: string;
  scenes: SceneDto[];
  isLoading: boolean;
  onRefresh: () => void;
  onEditScene: (scene: SceneDto) => void;
}

// Định dạng giây thành mm:ss (ví dụ: 15 -> 00:15)
function formatDuration(seconds: number): string {
  const mins = Math.floor(seconds / 60);
  const secs = seconds % 60;
  return `${mins.toString().padStart(2, "0")}:${secs.toString().padStart(2, "0")}`;
}

export function SceneTable({
  workspaceId,
  projectId,
  scenes,
  isLoading,
  onRefresh,
  onEditScene,
}: SceneTableProps) {
  const [reorderingSceneId, setReorderingSceneId] = useState<string | null>(null);
  const [deletingScene, setDeletingScene] = useState<SceneDto | null>(null);
  const [isDeleting, setIsDeleting] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [activeAssetOperation, setActiveAssetOperation] = useState<{
    sceneId: string;
    type: "audio" | "image";
  } | null>(null);

  // Thao tác nhập âm thanh gốc (MP3/WAV)
  const handleImportAudio = async (sceneId: string) => {
    setActiveAssetOperation({ sceneId, type: "audio" });
    setErrorMessage(null);
    try {
      const response = await bridgeClient.invoke<{ asset?: { id: string } | null; canceled?: boolean }>(
        "asset.import_audio",
        {
          workspaceId,
          projectId,
          sceneId,
        }
      );
      if (response && !response.canceled) {
        onRefresh();
      }
    } catch (err: unknown) {
      const errorDto = err as ErrorDto;
      console.error("[SceneTable] Lỗi nhập âm thanh:", errorDto);
      setErrorMessage(errorDto?.message || "Không thể nhập tệp âm thanh. Vui lòng kiểm tra lại định dạng MP3/WAV.");
    } finally {
      setActiveAssetOperation(null);
    }
  };

  // Thao tác chọn ảnh tham chiếu (PNG/JPG/WEBP)
  const handleSelectImage = async (sceneId: string) => {
    setActiveAssetOperation({ sceneId, type: "image" });
    setErrorMessage(null);
    try {
      const response = await bridgeClient.invoke<{ asset?: { id: string } | null; canceled?: boolean }>(
        "asset.select_reference_image",
        {
          workspaceId,
          projectId,
          sceneId,
        }
      );
      if (response && !response.canceled) {
        onRefresh();
      }
    } catch (err: unknown) {
      const errorDto = err as ErrorDto;
      console.error("[SceneTable] Lỗi chọn ảnh tham chiếu:", errorDto);
      setErrorMessage(errorDto?.message || "Không thể chọn tệp hình ảnh tham chiếu.");
    } finally {
      setActiveAssetOperation(null);
    }
  };

  // Thao tác đổi thứ tự (Reorder Lên / Xuống)
  const handleMove = async (index: number, direction: "up" | "down") => {
    const targetIndex = direction === "up" ? index - 1 : index + 1;
    if (targetIndex < 0 || targetIndex >= scenes.length) return;

    const movingScene = scenes[index];
    setReorderingSceneId(movingScene.sceneId);
    setErrorMessage(null);

    // Tạo mảng thứ tự mới
    const newScenes = [...scenes];
    const temp = newScenes[index];
    newScenes[index] = newScenes[targetIndex];
    newScenes[targetIndex] = temp;

    const newSceneIds = newScenes.map((s) => s.sceneId);

    // Chuẩn bị dictionary expectedSceneRevisions
    const expectedSceneRevisions: Record<string, string> = {};
    scenes.forEach((s) => {
      expectedSceneRevisions[s.sceneId] = s.revision;
    });

    try {
      await projectApi.reorderScenes({
        workspaceId,
        projectId,
        sceneIds: newSceneIds,
        expectedSceneRevisions,
      });
      onRefresh();
    } catch (err: unknown) {
      const errorDto = err as ErrorDto;
      console.error("[SceneTable] Lỗi sắp xếp cảnh:", errorDto);
      if (errorDto?.code === "CONCURRENCY_CONFLICT") {
        setErrorMessage(
          "Không thể thay đổi thứ tự do có phiên khác vừa cập nhật cảnh. Bảng đã được tải lại dữ liệu mới nhất.",
        );
      } else {
        setErrorMessage(errorDto?.message || "Đã xảy ra lỗi khi sắp xếp lại phân cảnh.");
      }
      onRefresh();
    } finally {
      setReorderingSceneId(null);
    }
  };

  // Xác nhận xóa cảnh
  const handleConfirmDelete = async () => {
    if (!deletingScene || isDeleting) return;

    setIsDeleting(true);
    setErrorMessage(null);

    try {
      await projectApi.deleteScene(workspaceId, projectId, deletingScene.sceneId);
      setDeletingScene(null);
      onRefresh();
    } catch (err: unknown) {
      const errorDto = err as ErrorDto;
      console.error("[SceneTable] Lỗi xóa phân cảnh:", errorDto);
      setErrorMessage(errorDto?.message || "Đã xảy ra lỗi khi xóa phân cảnh.");
    } finally {
      setIsDeleting(false);
    }
  };

  if (isLoading && scenes.length === 0) {
    return (
      <div className="loading-card" style={{ padding: "3rem" }}>
        <IconRefresh size={24} className="spin" />
        <p>Đang tải danh sách phân cảnh…</p>
      </div>
    );
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
      {errorMessage && (
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
          <div>{errorMessage}</div>
        </div>
      )}

      <div
        style={{
          overflowX: "auto",
          backgroundColor: "var(--surface-card)",
          border: "1px solid var(--border-subtle)",
          borderRadius: "var(--radius-lg)",
          boxShadow: "0 2px 8px rgba(0, 0, 0, 0.05)",
        }}
      >
        <table
          style={{
            width: "100%",
            borderCollapse: "collapse",
            textAlign: "left",
            fontSize: "0.85rem",
          }}
        >
          <thead>
            <tr
              style={{
                borderBottom: "1px solid var(--border-subtle)",
                backgroundColor: "var(--surface-card-subtle)",
                color: "var(--text-secondary)",
                fontSize: "0.8rem",
                textTransform: "uppercase",
                letterSpacing: "0.5px",
              }}
            >
              <th style={{ padding: "0.85rem 1rem", width: 44, textAlign: "center" }}>#</th>
              <th style={{ padding: "0.85rem 1rem", width: 70 }}>Xem trước</th>
              <th style={{ padding: "0.85rem 1rem", minWidth: 220 }}>Tên cảnh & Lời dẫn</th>
              <th style={{ padding: "0.85rem 1rem", width: 110 }}>Thời lượng</th>
              <th style={{ padding: "0.85rem 0.5rem", width: 60, textAlign: "center" }}>Ảnh</th>
              <th style={{ padding: "0.85rem 0.5rem", width: 60, textAlign: "center" }}>Prompt</th>
              <th style={{ padding: "0.85rem 0.5rem", width: 60, textAlign: "center" }}>Video</th>
              <th style={{ padding: "0.85rem 1rem", width: 130 }}>Âm thanh</th>
              <th style={{ padding: "0.85rem 1rem", width: 120 }}>Trạng thái</th>
              <th style={{ padding: "0.85rem 1rem", width: 140, textAlign: "right" }}>Thao tác</th>
            </tr>
          </thead>
          <tbody>
            {scenes.length === 0 ? (
              <tr>
                <td colSpan={10} style={{ padding: "3rem 1rem", textAlign: "center", color: "var(--text-muted)" }}>
                  Dự án này chưa có phân cảnh nào. Hãy bấm "+ Thêm Cảnh" để bắt đầu!
                </td>
              </tr>
            ) : (
              scenes.map((scene, idx) => {
                const hasImage = !!scene.imageAssetId;
                const hasPrompt = !!scene.promptText && scene.promptText.trim().length > 0;
                const hasVideo = !!scene.videoAssetId;
                const hasAudio = !!scene.audioAssetId;
                const isReorderingThis = reorderingSceneId === scene.sceneId;

                return (
                  <tr
                    key={scene.sceneId}
                    style={{
                      borderBottom: "1px solid var(--border-subtle)",
                      backgroundColor: isReorderingThis ? "var(--surface-hover)" : "transparent",
                      transition: "background-color 150ms ease",
                    }}
                  >
                    {/* # Thứ tự */}
                    <td
                      style={{
                        padding: "0.85rem 1rem",
                        textAlign: "center",
                        fontWeight: 700,
                        color: "var(--text-muted)",
                      }}
                    >
                      {scene.sceneSequence}
                    </td>

                    {/* Thumbnail Xem trước & Thao tác chọn ảnh */}
                    <td style={{ padding: "0.85rem 1rem" }}>
                      <button
                        type="button"
                        onClick={() => handleSelectImage(scene.sceneId)}
                        disabled={!!activeAssetOperation}
                        style={{
                          width: 52,
                          height: 38,
                          borderRadius: "var(--radius-sm)",
                          backgroundColor: "var(--surface-card-subtle)",
                          border: hasImage ? "1px solid var(--status-success)" : "1px dashed var(--border)",
                          display: "flex",
                          alignItems: "center",
                          justifyContent: "center",
                          color: hasImage ? "var(--status-success)" : "var(--text-muted)",
                          fontSize: "0.72rem",
                          fontWeight: 600,
                          cursor: "pointer",
                          padding: 0,
                          transition: "all 0.15s ease",
                        }}
                        title={hasImage ? "Bấm để thay đổi ảnh tham chiếu" : "Bấm để chọn ảnh tham chiếu"}
                      >
                        {activeAssetOperation?.sceneId === scene.sceneId && activeAssetOperation.type === "image"
                          ? "..."
                          : hasImage
                          ? "🖼️ Đổi"
                          : "+ Ảnh"}
                      </button>
                    </td>

                    {/* Tên cảnh & Lời dẫn */}
                    <td style={{ padding: "0.85rem 1rem" }}>
                      <div
                        style={{
                          fontWeight: 600,
                          color: "var(--text-primary)",
                          marginBottom: "0.2rem",
                        }}
                      >
                        {scene.name}
                      </div>
                      <div
                        style={{
                          fontSize: "0.78rem",
                          color: "var(--text-muted)",
                          display: "-webkit-box",
                          WebkitLineClamp: 2,
                          WebkitBoxOrient: "vertical",
                          overflow: "hidden",
                          lineHeight: 1.35,
                        }}
                      >
                        {scene.promptText || "(Chưa có lời dẫn / prompt)"}
                      </div>
                    </td>

                    {/* Thời lượng */}
                    <td style={{ padding: "0.85rem 1rem", color: "var(--text-secondary)", fontVariantNumeric: "tabular-nums" }}>
                      {formatDuration(scene.targetDurationSeconds)} ({scene.targetDurationSeconds}s)
                    </td>

                    {/* Trạng thái Ảnh */}
                    <td style={{ padding: "0.85rem 0.5rem", textAlign: "center" }}>
                      {hasImage ? (
                        <span
                          style={{
                            display: "inline-flex",
                            alignItems: "center",
                            justifyContent: "center",
                            width: 22,
                            height: 22,
                            borderRadius: "50%",
                            backgroundColor: "rgba(34, 197, 94, 0.2)",
                            color: "var(--status-success)",
                          }}
                          title="Đã có ảnh minh họa"
                        >
                          <IconCheck size={14} />
                        </span>
                      ) : (
                        <span
                          style={{
                            display: "inline-block",
                            width: 8,
                            height: 8,
                            borderRadius: "50%",
                            backgroundColor: "var(--border)",
                          }}
                          title="Chưa có ảnh minh họa"
                        />
                      )}
                    </td>

                    {/* Trạng thái Prompt */}
                    <td style={{ padding: "0.85rem 0.5rem", textAlign: "center" }}>
                      {hasPrompt ? (
                        <span
                          style={{
                            display: "inline-flex",
                            alignItems: "center",
                            justifyContent: "center",
                            width: 22,
                            height: 22,
                            borderRadius: "50%",
                            backgroundColor: "rgba(34, 197, 94, 0.2)",
                            color: "var(--status-success)",
                          }}
                          title="Đã có prompt chuyển động"
                        >
                          <IconCheck size={14} />
                        </span>
                      ) : (
                        <span
                          style={{
                            display: "inline-block",
                            width: 8,
                            height: 8,
                            borderRadius: "50%",
                            backgroundColor: "var(--border)",
                          }}
                          title="Chưa có prompt"
                        />
                      )}
                    </td>

                    {/* Trạng thái Video */}
                    <td style={{ padding: "0.85rem 0.5rem", textAlign: "center" }}>
                      {hasVideo ? (
                        <span
                          style={{
                            display: "inline-flex",
                            alignItems: "center",
                            justifyContent: "center",
                            width: 22,
                            height: 22,
                            borderRadius: "50%",
                            backgroundColor: "rgba(34, 197, 94, 0.2)",
                            color: "var(--status-success)",
                          }}
                          title="Đã kết xuất video"
                        >
                          <IconCheck size={14} />
                        </span>
                      ) : (
                        <span
                          style={{
                            display: "inline-block",
                            width: 8,
                            height: 8,
                            borderRadius: "50%",
                            backgroundColor: "var(--border)",
                          }}
                          title="Chưa có video"
                        />
                      )}
                    </td>

                    {/* Cột Âm thanh độc lập & Thao tác nhập âm thanh */}
                    <td style={{ padding: "0.85rem 1rem" }}>
                      <button
                        type="button"
                        className={`badge ${hasAudio ? "badge-success" : "badge-neutral"}`}
                        style={{
                          fontSize: "0.75rem",
                          cursor: "pointer",
                          border: hasAudio ? "none" : "1px dashed var(--border)",
                          background: hasAudio ? undefined : "transparent",
                          padding: "0.25rem 0.6rem",
                        }}
                        onClick={() => handleImportAudio(scene.sceneId)}
                        disabled={!!activeAssetOperation}
                        title={hasAudio ? "Bấm để thay đổi tệp âm thanh (MP3/WAV)" : "Bấm để nhập tệp âm thanh gốc (MP3/WAV)"}
                      >
                        {activeAssetOperation?.sceneId === scene.sceneId && activeAssetOperation.type === "audio"
                          ? "Đang nạp..."
                          : hasAudio
                          ? "🎵 Đã có âm thanh"
                          : "+ Nhập âm thanh"}
                      </button>
                    </td>

                    {/* Trạng thái cảnh */}
                    <td style={{ padding: "0.85rem 1rem" }}>
                      <span
                        className={`badge ${
                          scene.status === "Completed"
                            ? "badge-success"
                            : scene.status === "Generating"
                            ? "badge-info"
                            : "badge-neutral"
                        }`}
                        style={{ fontSize: "0.75rem" }}
                      >
                        ● {scene.status}
                      </span>
                    </td>

                    {/* Thao tác: Lên / Xuống / Sửa / Xóa */}
                    <td style={{ padding: "0.85rem 1rem", textAlign: "right" }}>
                      <div style={{ display: "inline-flex", alignItems: "center", gap: "0.25rem" }}>
                        <button
                          type="button"
                          className="btn btn-secondary btn-sm"
                          style={{ padding: "0.2rem 0.45rem", minWidth: 26 }}
                          disabled={idx === 0 || !!reorderingSceneId}
                          onClick={() => handleMove(idx, "up")}
                          title="Di chuyển lên trên"
                          aria-label="Di chuyển lên"
                        >
                          ▲
                        </button>
                        <button
                          type="button"
                          className="btn btn-secondary btn-sm"
                          style={{ padding: "0.2rem 0.45rem", minWidth: 26 }}
                          disabled={idx === scenes.length - 1 || !!reorderingSceneId}
                          onClick={() => handleMove(idx, "down")}
                          title="Di chuyển xuống dưới"
                          aria-label="Di chuyển xuống"
                        >
                          ▼
                        </button>
                        <button
                          type="button"
                          className="btn btn-secondary btn-sm"
                          onClick={() => onEditScene(scene)}
                          title="Chỉnh sửa cảnh & prompt"
                        >
                          Sửa
                        </button>
                        <button
                          type="button"
                          className="btn btn-secondary btn-sm"
                          style={{ color: "var(--status-error)" }}
                          onClick={() => setDeletingScene(scene)}
                          title="Xóa cảnh"
                        >
                          Xóa
                        </button>
                      </div>
                    </td>
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>

      {/* Modal xác nhận xóa cảnh */}
      {deletingScene && (
        <div
          className="modal-backdrop"
          role="presentation"
          onClick={(e) => {
            if (e.target === e.currentTarget && !isDeleting) {
              setDeletingScene(null);
            }
          }}
        >
          <div
            className="modal-container"
            role="dialog"
            aria-modal="true"
            aria-labelledby="delete-scene-title"
            style={{ maxWidth: 440 }}
          >
            <div className="modal-header">
              <h3 id="delete-scene-title" style={{ margin: 0, color: "var(--status-error)" }}>
                Xác nhận xóa phân cảnh #{deletingScene.sceneSequence}
              </h3>
            </div>
            <div className="modal-body">
              <p style={{ margin: 0 }}>
                Bạn có chắc chắn muốn xóa phân cảnh <strong>{deletingScene.name}</strong>?
              </p>
              <p style={{ fontSize: "0.8rem", color: "var(--text-muted)", marginTop: "0.5rem" }}>
                Thứ tự các phân cảnh sau sẽ được tự động cập nhật liên tục.
              </p>
            </div>
            <div className="modal-footer" style={{ justifyContent: "flex-end", gap: "0.5rem" }}>
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => setDeletingScene(null)}
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
                {isDeleting ? "Đang xóa…" : "Xác nhận xóa"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
