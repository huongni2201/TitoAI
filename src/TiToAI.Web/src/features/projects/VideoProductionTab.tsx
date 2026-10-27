import { useState, useEffect, useCallback, useMemo } from "react";
import {
  videoWorkflowApi,
  WorkflowModes,
  AudioOptions,
  WorkflowRunState,
  SceneWorkflowStatus,
  type WorkflowMode,
  type AudioOption,
  type ProjectWorkflowConfigDto,
  type SceneWorkflowDetailDto,
  type PreviewAiPromptResponseData,
  DEFAULT_SUPPORTED_DURATIONS,
} from "./videoWorkflowApi";
import { AssetPicker } from "../library/AssetPicker";
import { uploadAsset, type AssetItem } from "../library/libraryApi";
import { Card, CardHeader, CardTitle, CardContent, CardFooter } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import { Progress } from "@/components/ui/progress";
import { Separator } from "@/components/ui/separator";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
} from "@/components/ui/dialog";
import {
  Sparkles,
  Play,
  RotateCcw,
  Pause,
  AlertCircle,
  CheckCircle2,
  Image as ImageIcon,
  Music,
  FolderOpen,
  Upload,
  X,
  Sliders,
  Layers,
  Film,
  Loader2,
  RefreshCw,
  Eye,
} from "lucide-react";

export interface VideoProductionTabProps {
  workspaceId: string;
  projectId: string;
  onRefreshProject?: () => void;
}

export function VideoProductionTab({
  workspaceId,
  projectId,
  onRefreshProject,
}: VideoProductionTabProps) {
  const [config, setConfig] = useState<ProjectWorkflowConfigDto | null>(null);
  const [scenes, setScenes] = useState<SceneWorkflowDetailDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isStarting, setIsStarting] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  // Asset Pickers
  const [pickerTarget, setPickerTarget] = useState<"model" | "product" | "ready" | "audio" | null>(null);
  const [uploadingTarget, setUploadingTarget] = useState<"model" | "product" | "ready" | "audio" | null>(null);

  // Scene Edit Dialog
  const [editingScene, setEditingScene] = useState<SceneWorkflowDetailDto | null>(null);
  const [editPromptText, setEditPromptText] = useState("");
  const [isUpdatingScene, setIsUpdatingScene] = useState(false);

  // Video Preview Dialog
  const [previewVideoScene, setPreviewVideoScene] = useState<SceneWorkflowDetailDto | null>(null);

  // Workflow 4-Step Technical Details Dialog
  const [isDetailsOpen, setIsDetailsOpen] = useState(false);
  const [detailedScene, setDetailedScene] = useState<SceneWorkflowDetailDto | null>(null);

  // AI Prompt Preview Dialog (§3.1-3.3)
  const [isAiPreviewOpen, setIsAiPreviewOpen] = useState(false);
  const [aiPreviewData, setAiPreviewData] = useState<PreviewAiPromptResponseData | null>(null);
  const [isLoadingAiPreview, setIsLoadingAiPreview] = useState(false);

  // Dirty state tracking
  const [isDirty, setIsDirty] = useState(false);

  // Fetch initial config & scene list
  const loadData = useCallback(async () => {
    if (!workspaceId || !projectId) return;
    try {
      setIsLoading(true);
      setErrorMessage(null);
      const [cfg, scns] = await Promise.all([
        videoWorkflowApi.getConfig(workspaceId, projectId),
        videoWorkflowApi.listScenes(workspaceId, projectId),
      ]);
      setConfig(cfg);
      setScenes(scns);
    } catch (err: unknown) {
      console.error("[VideoProductionTab] Lỗi tải dữ liệu quy trình:", err);
      setErrorMessage((err as Error)?.message || "Không thể tải cấu hình quy trình sản xuất video.");
    } finally {
      setIsLoading(false);
    }
  }, [workspaceId, projectId]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  // Handle Mode Change
  const handleModeChange = (mode: WorkflowMode) => {
    if (!config) return;
    setConfig({
      ...config,
      workflowMode: mode,
    });
    setIsDirty(true);
  };

  // Handle Input Changes
  const handleGoalPromptChange = (val: string) => {
    if (!config) return;
    setConfig({ ...config, goalPrompt: val });
    setIsDirty(true);
  };

  const handleAudioOptionChange = (option: AudioOption) => {
    if (!config) return;
    setConfig({
      ...config,
      audioOption: option,
      audioAssetId: option === AudioOptions.None ? null : config.audioAssetId,
    });
    setIsDirty(true);
  };

  const handleTargetSceneCountChange = (val: number) => {
    if (!config) return;
    setConfig({ ...config, targetSceneCount: Math.max(1, Math.min(10, val)) });
    setIsDirty(true);
  };

  const handleTargetDurationChange = (val: number) => {
    if (!config) return;
    setConfig({ ...config, targetDurationPerSceneSeconds: Math.max(3, Math.min(60, val)) });
    setIsDirty(true);
  };

  // Asset picker selections
  const handleSelectAsset = (asset: AssetItem) => {
    if (!config) return;

    if (pickerTarget === "model") {
      setConfig({ ...config, modelAssetId: asset.assetId });
    } else if (pickerTarget === "product") {
      setConfig({ ...config, productAssetId: asset.assetId });
    } else if (pickerTarget === "ready") {
      setConfig({ ...config, readyImageAssetId: asset.assetId });
    } else if (pickerTarget === "audio") {
      setConfig({ ...config, audioAssetId: asset.assetId, audioOption: AudioOptions.CustomAudio });
    }
    setIsDirty(true);
    setPickerTarget(null);
  };

  const handleClearAsset = (target: "model" | "product" | "ready" | "audio") => {
    if (!config) return;
    if (target === "model") setConfig({ ...config, modelAssetId: null });
    if (target === "product") setConfig({ ...config, productAssetId: null });
    if (target === "ready") setConfig({ ...config, readyImageAssetId: null });
    if (target === "audio") setConfig({ ...config, audioAssetId: null, audioOption: AudioOptions.None });
    setIsDirty(true);
  };

  const handleTriggerUpload = async (target: "model" | "product" | "ready" | "audio") => {
    if (!workspaceId) return;
    setUploadingTarget(target);
    setErrorMessage(null);
    try {
      const isAudio = target === "audio";
      const res = await uploadAsset({
        workspaceId,
        projectId,
        assetType: isAudio ? "Audio" : "Image",
      });
      if (res.asset) {
        if (target === "model") {
          setConfig((prev) => (prev ? { ...prev, modelAssetId: res.asset!.assetId } : prev));
        } else if (target === "product") {
          setConfig((prev) => (prev ? { ...prev, productAssetId: res.asset!.assetId } : prev));
        } else if (target === "ready") {
          setConfig((prev) => (prev ? { ...prev, readyImageAssetId: res.asset!.assetId } : prev));
        } else if (target === "audio") {
          setConfig((prev) =>
            prev ? { ...prev, audioAssetId: res.asset!.assetId, audioOption: AudioOptions.CustomAudio } : prev
          );
        }
        setIsDirty(true);
      }
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : "Tải lên tệp thất bại.";
      setErrorMessage(msg);
    } finally {
      setUploadingTarget(null);
    }
  };

  // Save Config
  const handleSaveConfig = async () => {
    if (!config) return;
    try {
      setErrorMessage(null);
      const saved = await videoWorkflowApi.saveConfig({
        workspaceId,
        projectId,
        workflowMode: config.workflowMode,
        goalPrompt: config.goalPrompt,
        modelAssetId: config.modelAssetId,
        productAssetId: config.productAssetId,
        readyImageAssetId: config.readyImageAssetId,
        audioOption: config.audioOption,
        audioAssetId: config.audioAssetId,
        targetSceneCount: config.targetSceneCount,
        targetDurationPerSceneSeconds: config.targetDurationPerSceneSeconds,
      });
      setConfig(saved);
      setIsDirty(false);
      setSuccessMessage("Đã lưu cấu hình dữ liệu ban đầu.");
      setTimeout(() => setSuccessMessage(null), 3000);
    } catch (err: unknown) {
      setErrorMessage((err as Error)?.message || "Không thể lưu cấu hình.");
    }
  };

  // Preview AI Request (§3.1-3.3)
  const handleOpenAiPreview = async () => {
    if (!config) return;
    try {
      setIsLoadingAiPreview(true);
      setErrorMessage(null);

      // Lưu config nếu có thay đổi để preview khớp dữ liệu hiện hành
      if (isDirty) {
        await videoWorkflowApi.saveConfig({
          workspaceId,
          projectId,
          workflowMode: config.workflowMode,
          goalPrompt: config.goalPrompt,
          modelAssetId: config.modelAssetId,
          productAssetId: config.productAssetId,
          readyImageAssetId: config.readyImageAssetId,
          audioOption: config.audioOption,
          audioAssetId: config.audioAssetId,
          targetSceneCount: config.targetSceneCount,
          targetDurationPerSceneSeconds: config.targetDurationPerSceneSeconds,
        });
        setIsDirty(false);
      }

      const res = await videoWorkflowApi.previewAiPrompt({
        workspaceId,
        projectId,
      });
      setAiPreviewData(res);
      setIsAiPreviewOpen(true);
    } catch (err: unknown) {
      setErrorMessage((err as Error)?.message || "Không thể tải bản xem trước yêu cầu gửi AI.");
    } finally {
      setIsLoadingAiPreview(false);
    }
  };

  // Start Workflow (Bắt đầu tạo video 1 lần duy nhất)
  const handleStartWorkflow = async () => {
    if (!config) return;
    try {
      setIsStarting(true);
      setErrorMessage(null);

      const trimmedGoal = (config.goalPrompt || "").trim();
      if (!trimmedGoal) {
        setErrorMessage("Vui lòng nhập yêu cầu nội dung / phong cách chuyển động trước khi bắt đầu tạo video.");
        setIsStarting(false);
        return;
      }

      const isSupportedDuration = DEFAULT_SUPPORTED_DURATIONS.includes(config.targetDurationPerSceneSeconds as 10 | 15);
      if (!isSupportedDuration) {
        setErrorMessage(`Thời lượng ${config.targetDurationPerSceneSeconds}s không được nhà cung cấp video hỗ trợ. Vui lòng chọn lại thời lượng hợp lệ (${DEFAULT_SUPPORTED_DURATIONS.join("s hoặc ")}s) trước khi bắt đầu.`);
        setIsStarting(false);
        return;
      }

      // Lưu config trước khi start
      await videoWorkflowApi.saveConfig({
        workspaceId,
        projectId,
        workflowMode: config.workflowMode,
        goalPrompt: config.goalPrompt,
        modelAssetId: config.modelAssetId,
        productAssetId: config.productAssetId,
        readyImageAssetId: config.readyImageAssetId,
        audioOption: config.audioOption,
        audioAssetId: config.audioAssetId,
        targetSceneCount: config.targetSceneCount,
        targetDurationPerSceneSeconds: config.targetDurationPerSceneSeconds,
      });

      const res = await videoWorkflowApi.startWorkflow(workspaceId, projectId);
      setSuccessMessage(res.message);
      setIsDirty(false);
      await loadData();
      onRefreshProject?.();
    } catch (err: unknown) {
      setErrorMessage((err as Error)?.message || "Không thể bắt đầu quy trình tạo video.");
    } finally {
      setIsStarting(false);
    }
  };

  // Pause / Resume
  const handlePause = async () => {
    try {
      const updated = await videoWorkflowApi.pauseWorkflow(workspaceId, projectId);
      setConfig(updated);
    } catch (err: unknown) {
      setErrorMessage((err as Error)?.message || "Không thể tạm dừng.");
    }
  };

  const handleResume = async () => {
    try {
      const updated = await videoWorkflowApi.resumeWorkflow(workspaceId, projectId);
      setConfig(updated);
    } catch (err: unknown) {
      setErrorMessage((err as Error)?.message || "Không thể tiếp tục.");
    }
  };

  // Open Edit Scene Prompt
  const handleOpenEditScene = (scene: SceneWorkflowDetailDto) => {
    setEditingScene(scene);
    setEditPromptText(scene.motionPromptText || "");
  };

  // Save Scene Prompt Edit (chỉ làm lại video cảnh đó)
  const handleSaveSceneEdit = async () => {
    if (!editingScene) return;
    try {
      setIsUpdatingScene(true);
      await videoWorkflowApi.updateSceneInput({
        workspaceId,
        projectId,
        sceneId: editingScene.sceneId,
        motionPromptText: editPromptText,
        expectedRevision: editingScene.revision,
      });
      setEditingScene(null);
      await loadData();
    } catch (err: unknown) {
      setErrorMessage((err as Error)?.message || "Không thể cập nhật cảnh.");
    } finally {
      setIsUpdatingScene(false);
    }
  };

  // Determine current active main screen state:
  // - Preparing: Khi chưa có run hoặc đang ở trạng thái Idle/Preparing
  // - Running: Khi runState === "Running" hoặc "Paused"
  // - Completed: Khi toàn bộ cảnh đã có video hoặc runState === "Completed"
  const completedVideoCount = useMemo(() => {
    return scenes.filter((s) => !!s.videoAssetId).length;
  }, [scenes]);

  const mainView = useMemo<"preparing" | "running" | "completed">(() => {
    if (!config) return "preparing";
    if (config.runState === WorkflowRunState.Completed || (scenes.length > 0 && completedVideoCount === scenes.length)) {
      return "completed";
    }
    if (config.runState === WorkflowRunState.Running || config.runState === WorkflowRunState.Paused) {
      return "running";
    }
    return "preparing";
  }, [config, scenes.length, completedVideoCount]);

  if (isLoading && !config) {
    return (
      <Card className="flex flex-col items-center justify-center p-12 text-center mt-4">
        <Loader2 className="h-8 w-8 animate-spin text-primary mb-3" />
        <p className="text-sm text-muted-foreground">Đang tải cấu hình quy trình sản xuất video…</p>
      </Card>
    );
  }

  if (!config) return null;

  return (
    <div className="space-y-6">
      {/* Thông báo lỗi / thành công */}
      {errorMessage && (
        <div className="flex items-center gap-2 p-3 text-sm rounded-md bg-destructive/10 text-destructive border border-destructive/20">
          <AlertCircle className="h-4 w-4 shrink-0" />
          <span className="flex-1">{errorMessage}</span>
          <Button variant="ghost" size="icon" className="h-6 w-6" onClick={() => setErrorMessage(null)}>
            <X className="h-3.5 w-3.5" />
          </Button>
        </div>
      )}

      {successMessage && (
        <div className="flex items-center gap-2 p-3 text-sm rounded-md bg-success/10 text-success border border-success/20">
          <CheckCircle2 className="h-4 w-4 shrink-0" />
          <span className="flex-1">{successMessage}</span>
          <Button variant="ghost" size="icon" className="h-6 w-6" onClick={() => setSuccessMessage(null)}>
            <X className="h-3.5 w-3.5" />
          </Button>
        </div>
      )}

      {/* Header điều hướng trạng thái quy trình */}
      <div className="flex items-center justify-between flex-wrap gap-3">
        <div className="flex items-center gap-2">
          <Badge
            variant={
              mainView === "completed"
                ? "success"
                : mainView === "running"
                ? "info"
                : "secondary"
            }
            className="px-2.5 py-1 text-xs uppercase font-semibold tracking-wider"
          >
            {mainView === "completed"
              ? "Hoàn tất kết quả"
              : mainView === "running"
              ? "Đang sản xuất video"
              : "Chuẩn bị dữ liệu ban đầu"}
          </Badge>
          {scenes.length > 0 && (
            <span className="text-xs text-muted-foreground">
              {completedVideoCount}/{scenes.length} clip sẵn sàng
            </span>
          )}
        </div>

        <div className="flex items-center gap-2">
          {mainView === "running" && (
            <>
              {config.runState === WorkflowRunState.Running ? (
                <Button variant="outline" size="sm" onClick={handlePause} className="gap-1.5">
                  <Pause className="h-3.5 w-3.5" /> Tạm dừng
                </Button>
              ) : (
                <Button variant="outline" size="sm" onClick={handleResume} className="gap-1.5">
                  <Play className="h-3.5 w-3.5" /> Tiếp tục
                </Button>
              )}
            </>
          )}

          {mainView === "completed" && (
            <Button
              variant="outline"
              size="sm"
              onClick={() => {
                setConfig({ ...config, runState: WorkflowRunState.Idle });
              }}
              className="gap-1.5"
            >
              <RotateCcw className="h-3.5 w-3.5" /> Sửa cấu hình & Chạy lại
            </Button>
          )}

          <Button
            variant="ghost"
            size="sm"
            onClick={loadData}
            title="Làm mới trạng thái"
          >
            <RefreshCw className="h-3.5 w-3.5" />
          </Button>
        </div>
      </div>

      {/* ============================================================== */}
      {/* 1. MÀN HÌNH CHÍNH: CHUẨN BỊ (PREPARING)                         */}
      {/* ============================================================== */}
      {mainView === "preparing" && (
        <div className="space-y-6">
          <Card>
            <CardHeader className="pb-4">
              <CardTitle className="text-lg flex items-center gap-2">
                <Sparkles className="h-5 w-5 text-primary" />
                1. Chọn cách bắt đầu & Dữ liệu hình ảnh ban đầu
              </CardTitle>
              <p className="text-xs text-muted-foreground">
                Cung cấp hình ảnh và yêu cầu một lần ở cấp dự án. Hệ thống sẽ tự động lập các cảnh, chuẩn bị ảnh nền tảng, viết prompt chuyển động và tạo clip video.
              </p>
            </CardHeader>

            <CardContent className="space-y-6">
              {/* Ba chế độ bắt đầu */}
              <div>
                <Label className="text-sm font-medium mb-3 block">Chế độ đầu vào (§2):</Label>
                <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
                  {/* Mode 1: Mẫu + sản phẩm */}
                  <div
                    onClick={() => handleModeChange(WorkflowModes.ModelAndProduct)}
                    className={`p-4 rounded-lg border cursor-pointer transition-all ${
                      config.workflowMode === WorkflowModes.ModelAndProduct
                        ? "border-primary bg-primary/5 ring-2 ring-primary/20"
                        : "border-border hover:border-primary/40 bg-card"
                    }`}
                  >
                    <div className="flex items-center gap-2.5 font-semibold text-sm mb-1.5 text-foreground">
                      <Layers className="h-4 w-4 text-primary" />
                      <span>Mẫu + sản phẩm</span>
                    </div>
                    <p className="text-xs text-muted-foreground leading-relaxed">
                      Đưa một ảnh mẫu người và một ảnh sản phẩm. AI tự tạo ảnh mẫu mặc/dùng sản phẩm theo từng cảnh.
                    </p>
                  </div>

                  {/* Mode 2: Ảnh đã sẵn sàng */}
                  <div
                    onClick={() => handleModeChange(WorkflowModes.ReadyImage)}
                    className={`p-4 rounded-lg border cursor-pointer transition-all ${
                      config.workflowMode === WorkflowModes.ReadyImage
                        ? "border-primary bg-primary/5 ring-2 ring-primary/20"
                        : "border-border hover:border-primary/40 bg-card"
                    }`}
                  >
                    <div className="flex items-center gap-2.5 font-semibold text-sm mb-1.5 text-foreground">
                      <ImageIcon className="h-4 w-4 text-primary" />
                      <span>Ảnh đã hoàn thiện</span>
                    </div>
                    <p className="text-xs text-muted-foreground leading-relaxed">
                      Ảnh mẫu đã mặc hoặc cầm sẵn sản phẩm. Đi thẳng sang viết prompt video, không cần sinh ảnh.
                    </p>
                  </div>

                  {/* Mode 3: Chỉ sản phẩm */}
                  <div
                    onClick={() => handleModeChange(WorkflowModes.ProductOnly)}
                    className={`p-4 rounded-lg border cursor-pointer transition-all ${
                      config.workflowMode === WorkflowModes.ProductOnly
                        ? "border-primary bg-primary/5 ring-2 ring-primary/20"
                        : "border-border hover:border-primary/40 bg-card"
                    }`}
                  >
                    <div className="flex items-center gap-2.5 font-semibold text-sm mb-1.5 text-foreground">
                      <Film className="h-4 w-4 text-primary" />
                      <span>Chỉ sản phẩm</span>
                    </div>
                    <p className="text-xs text-muted-foreground leading-relaxed">
                      Chỉ đưa ảnh sản phẩm, không bắt ảnh mẫu. Dùng trực tiếp hoặc AI tự đổi nền/bố cục theo cảnh.
                    </p>
                  </div>
                </div>
              </div>

              <Separator />

              {/* Ô gán vai trò hình ảnh theo chế độ */}
              <div>
                <Label className="text-sm font-medium mb-3 block">Hình ảnh đầu vào:</Label>

                <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 gap-4">
                  {/* Ô Ảnh Mẫu (nếu mode là ModelAndProduct) */}
                  {config.workflowMode === WorkflowModes.ModelAndProduct && (
                    <div className="p-4 rounded-lg border border-border bg-muted/20 flex flex-col items-center justify-center text-center min-h-[180px]">
                      <span className="text-xs font-semibold text-foreground mb-2 flex items-center gap-1.5">
                        <ImageIcon className="h-3.5 w-3.5 text-primary" />
                        Ảnh Mẫu người (Bắt buộc)
                      </span>
                      {config.modelAssetId ? (
                        <div className="space-y-2 w-full">
                          <div className="h-24 w-full bg-muted rounded flex items-center justify-center overflow-hidden border">
                            <span className="text-xs text-muted-foreground truncate px-2">
                              ID: {config.modelAssetId.slice(0, 12)}…
                            </span>
                          </div>
                          <div className="flex items-center justify-center gap-1.5">
                            <Button
                              type="button"
                              variant="outline"
                              size="sm"
                              className="h-7 text-xs px-2 gap-1"
                              disabled={uploadingTarget === "model"}
                              onClick={() => handleTriggerUpload("model")}
                              title="Tải ảnh mới từ máy tính (tự động lưu vào Media)"
                            >
                              <Upload className="h-3 w-3" />
                              <span>{uploadingTarget === "model" ? "..." : "Tải lên"}</span>
                            </Button>
                            <Button
                              type="button"
                              variant="outline"
                              size="sm"
                              className="h-7 text-xs px-2 gap-1"
                              onClick={() => setPickerTarget("model")}
                              title="Chọn từ Thư viện media"
                            >
                              <FolderOpen className="h-3 w-3" />
                              <span>Thư viện</span>
                            </Button>
                            <Button
                              type="button"
                              variant="ghost"
                              size="sm"
                              className="h-7 text-xs px-2 text-destructive"
                              onClick={() => handleClearAsset("model")}
                            >
                              Gỡ
                            </Button>
                          </div>
                        </div>
                      ) : (
                        <div className="space-y-2 w-full">
                          <p className="text-xs text-muted-foreground mb-2">Chưa chọn ảnh mẫu</p>
                          <div className="flex flex-col gap-1.5">
                            <Button
                              type="button"
                              variant="default"
                              size="sm"
                              className="w-full gap-1.5 text-xs h-8"
                              disabled={uploadingTarget === "model"}
                              onClick={() => handleTriggerUpload("model")}
                              title="Tải tệp từ máy tính, tự động lưu vào Media"
                            >
                              <Upload className="h-3.5 w-3.5" />
                              <span>{uploadingTarget === "model" ? "Đang tải lên…" : "Tải lên ảnh"}</span>
                            </Button>
                            <Button
                              type="button"
                              variant="outline"
                              size="sm"
                              className="w-full gap-1.5 text-xs h-8"
                              onClick={() => setPickerTarget("model")}
                            >
                              <FolderOpen className="h-3.5 w-3.5" /> Chọn từ Thư viện
                            </Button>
                          </div>
                        </div>
                      )}
                    </div>
                  )}

                  {/* Ô Ảnh Sản Phẩm (cho ModelAndProduct và ProductOnly) */}
                  {(config.workflowMode === WorkflowModes.ModelAndProduct ||
                    config.workflowMode === WorkflowModes.ProductOnly) && (
                    <div className="p-4 rounded-lg border border-border bg-muted/20 flex flex-col items-center justify-center text-center min-h-[180px]">
                      <span className="text-xs font-semibold text-foreground mb-2 flex items-center gap-1.5">
                        <ImageIcon className="h-3.5 w-3.5 text-primary" />
                        Ảnh Sản Phẩm (Bắt buộc)
                      </span>
                      {config.productAssetId ? (
                        <div className="space-y-2 w-full">
                          <div className="h-24 w-full bg-muted rounded flex items-center justify-center overflow-hidden border">
                            <span className="text-xs text-muted-foreground truncate px-2">
                              ID: {config.productAssetId.slice(0, 12)}…
                            </span>
                          </div>
                          <div className="flex items-center justify-center gap-1.5">
                            <Button
                              type="button"
                              variant="outline"
                              size="sm"
                              className="h-7 text-xs px-2 gap-1"
                              disabled={uploadingTarget === "product"}
                              onClick={() => handleTriggerUpload("product")}
                              title="Tải ảnh mới từ máy tính (tự động lưu vào Media)"
                            >
                              <Upload className="h-3 w-3" />
                              <span>{uploadingTarget === "product" ? "..." : "Tải lên"}</span>
                            </Button>
                            <Button
                              type="button"
                              variant="outline"
                              size="sm"
                              className="h-7 text-xs px-2 gap-1"
                              onClick={() => setPickerTarget("product")}
                              title="Chọn từ Thư viện media"
                            >
                              <FolderOpen className="h-3 w-3" />
                              <span>Thư viện</span>
                            </Button>
                            <Button
                              type="button"
                              variant="ghost"
                              size="sm"
                              className="h-7 text-xs px-2 text-destructive"
                              onClick={() => handleClearAsset("product")}
                            >
                              Gỡ
                            </Button>
                          </div>
                        </div>
                      ) : (
                        <div className="space-y-2 w-full">
                          <p className="text-xs text-muted-foreground mb-2">Chưa chọn ảnh sản phẩm</p>
                          <div className="flex flex-col gap-1.5">
                            <Button
                              type="button"
                              variant="default"
                              size="sm"
                              className="w-full gap-1.5 text-xs h-8"
                              disabled={uploadingTarget === "product"}
                              onClick={() => handleTriggerUpload("product")}
                              title="Tải tệp từ máy tính, tự động lưu vào Media"
                            >
                              <Upload className="h-3.5 w-3.5" />
                              <span>{uploadingTarget === "product" ? "Đang tải lên…" : "Tải lên ảnh"}</span>
                            </Button>
                            <Button
                              type="button"
                              variant="outline"
                              size="sm"
                              className="w-full gap-1.5 text-xs h-8"
                              onClick={() => setPickerTarget("product")}
                            >
                              <FolderOpen className="h-3.5 w-3.5" /> Chọn từ Thư viện
                            </Button>
                          </div>
                        </div>
                      )}
                    </div>
                  )}

                  {/* Ô Ảnh Đã Có Sẵn (cho ReadyImage) */}
                  {config.workflowMode === WorkflowModes.ReadyImage && (
                    <div className="p-4 rounded-lg border border-border bg-muted/20 flex flex-col items-center justify-center text-center min-h-[180px]">
                      <span className="text-xs font-semibold text-foreground mb-2 flex items-center gap-1.5">
                        <ImageIcon className="h-3.5 w-3.5 text-primary" />
                        Ảnh Đã Hoàn Thiện (Bắt buộc)
                      </span>
                      {config.readyImageAssetId ? (
                        <div className="space-y-2 w-full">
                          <div className="h-24 w-full bg-muted rounded flex items-center justify-center overflow-hidden border">
                            <span className="text-xs text-muted-foreground truncate px-2">
                              ID: {config.readyImageAssetId.slice(0, 12)}…
                            </span>
                          </div>
                          <div className="flex items-center justify-center gap-1.5">
                            <Button
                              type="button"
                              variant="outline"
                              size="sm"
                              className="h-7 text-xs px-2 gap-1"
                              disabled={uploadingTarget === "ready"}
                              onClick={() => handleTriggerUpload("ready")}
                              title="Tải ảnh mới từ máy tính (tự động lưu vào Media)"
                            >
                              <Upload className="h-3 w-3" />
                              <span>{uploadingTarget === "ready" ? "..." : "Tải lên"}</span>
                            </Button>
                            <Button
                              type="button"
                              variant="outline"
                              size="sm"
                              className="h-7 text-xs px-2 gap-1"
                              onClick={() => setPickerTarget("ready")}
                              title="Chọn từ Thư viện media"
                            >
                              <FolderOpen className="h-3 w-3" />
                              <span>Thư viện</span>
                            </Button>
                            <Button
                              type="button"
                              variant="ghost"
                              size="sm"
                              className="h-7 text-xs px-2 text-destructive"
                              onClick={() => handleClearAsset("ready")}
                            >
                              Gỡ
                            </Button>
                          </div>
                        </div>
                      ) : (
                        <div className="space-y-2 w-full">
                          <p className="text-xs text-muted-foreground mb-2">Chưa chọn ảnh hoàn thiện</p>
                          <div className="flex flex-col gap-1.5">
                            <Button
                              type="button"
                              variant="default"
                              size="sm"
                              className="w-full gap-1.5 text-xs h-8"
                              disabled={uploadingTarget === "ready"}
                              onClick={() => handleTriggerUpload("ready")}
                              title="Tải tệp từ máy tính, tự động lưu vào Media"
                            >
                              <Upload className="h-3.5 w-3.5" />
                              <span>{uploadingTarget === "ready" ? "Đang tải lên…" : "Tải lên ảnh"}</span>
                            </Button>
                            <Button
                              type="button"
                              variant="outline"
                              size="sm"
                              className="w-full gap-1.5 text-xs h-8"
                              onClick={() => setPickerTarget("ready")}
                            >
                              <FolderOpen className="h-3.5 w-3.5" /> Chọn từ Thư viện
                            </Button>
                          </div>
                        </div>
                      )}
                    </div>
                  )}
                </div>
              </div>

              <Separator />

              {/* Ô Prompt / Brief mục tiêu */}
              <div className="space-y-2">
                <div className="flex items-center justify-between">
                  <Label htmlFor="goal-prompt" className="text-sm font-medium">
                    Yêu cầu nội dung / Phong cách chuyển động:
                  </Label>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    className="h-6 text-xs px-2 gap-1 text-primary hover:text-primary"
                    disabled={isLoadingAiPreview}
                    onClick={handleOpenAiPreview}
                    title="Xem chi tiết cách ứng dụng ghép yêu cầu này cùng mẫu Prompt video"
                  >
                    <Eye className="h-3 w-3" />
                    <span>{isLoadingAiPreview ? "Đang tải…" : "Xem yêu cầu gửi AI"}</span>
                  </Button>
                </div>
                <Textarea
                  id="goal-prompt"
                  value={config.goalPrompt}
                  onChange={(e) => handleGoalPromptChange(e.target.value)}
                  placeholder="Ví dụ: Trang phục dạo phố năng động thanh lịch. Thể hiện rõ chất vải lụa mềm rủ và form dáng khi người mẫu bước đi tự nhiên..."
                  rows={3}
                  className="resize-none"
                />
                <p className="text-xs text-muted-foreground">
                  Yêu cầu này được gửi cùng mẫu Prompt video để AI viết nội dung cho từng clip.
                </p>
              </div>

              {/* Tùy chọn Audio (§3.3 - Audio không bắt buộc) */}
              <div className="space-y-3">
                <Label className="text-sm font-medium">Âm thanh đi kèm (Không bắt buộc):</Label>
                <div className="flex items-center gap-4 flex-wrap">
                  <label className="flex items-center gap-2 cursor-pointer text-xs">
                    <input
                      type="radio"
                      name="audioOption"
                      checked={config.audioOption === AudioOptions.None}
                      onChange={() => handleAudioOptionChange(AudioOptions.None)}
                      className="accent-primary"
                    />
                    <span>Không gửi audio (Mặc định)</span>
                  </label>

                  <label className="flex items-center gap-2 cursor-pointer text-xs">
                    <input
                      type="radio"
                      name="audioOption"
                      checked={config.audioOption === AudioOptions.CustomAudio}
                      onChange={() => handleAudioOptionChange(AudioOptions.CustomAudio)}
                      className="accent-primary"
                    />
                    <span>Chọn tệp MP3/WAV có sẵn</span>
                  </label>
                </div>

                {config.audioOption === AudioOptions.CustomAudio && (
                  <div className="flex items-center gap-3 p-3 rounded-md border border-border bg-muted/10 w-full sm:w-auto flex-wrap">
                    <Music className="h-4 w-4 text-primary shrink-0" />
                    <span className="text-xs text-muted-foreground truncate max-w-[200px]">
                      {config.audioAssetId
                        ? `Tệp âm thanh: ${config.audioAssetId.slice(0, 16)}…`
                        : "Chưa chọn tệp âm thanh"}
                    </span>
                    <div className="flex items-center gap-1.5">
                      <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        className="h-7 text-xs px-2.5 gap-1"
                        disabled={uploadingTarget === "audio"}
                        onClick={() => handleTriggerUpload("audio")}
                        title="Tải âm thanh từ máy tính (tự động lưu vào Media)"
                      >
                        <Upload className="h-3 w-3" />
                        <span>{uploadingTarget === "audio" ? "Đang tải…" : "Tải lên audio"}</span>
                      </Button>
                      <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        className="h-7 text-xs px-2.5 gap-1"
                        onClick={() => setPickerTarget("audio")}
                        title="Chọn từ Thư viện media"
                      >
                        <FolderOpen className="h-3 w-3" />
                        <span>Thư viện</span>
                      </Button>
                      {config.audioAssetId && (
                        <Button
                          type="button"
                          variant="ghost"
                          size="sm"
                          className="h-7 text-xs px-2 text-destructive"
                          onClick={() => handleClearAsset("audio")}
                        >
                          Gỡ
                        </Button>
                      )}
                    </div>
                  </div>
                )}
              </div>

              {/* Giới hạn Run: Số clip và thời lượng mục tiêu theo capability (§2, CD01–CD05) */}
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 max-w-lg pt-2">
                <div className="space-y-1.5">
                  <Label htmlFor="target-scenes" className="text-xs font-medium">
                    Số clip cần tạo:
                  </Label>
                  <Input
                    id="target-scenes"
                    type="number"
                    min={1}
                    max={10}
                    value={config.targetSceneCount}
                    onChange={(e) => handleTargetSceneCountChange(parseInt(e.target.value) || 1)}
                  />
                  <p className="text-[11px] text-muted-foreground">
                    Số clip độc lập muốn tạo (mặc định 1 clip).
                  </p>
                </div>

                <div className="space-y-1.5">
                  <Label htmlFor="target-duration" className="text-xs font-medium">
                    Thời lượng mỗi clip:
                  </Label>
                  <select
                    id="target-duration"
                    className="w-full h-9 rounded-md border border-input bg-background px-3 py-1 text-xs shadow-xs focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
                    value={config.targetDurationPerSceneSeconds}
                    onChange={(e) => handleTargetDurationChange(parseInt(e.target.value) || 15)}
                  >
                    {DEFAULT_SUPPORTED_DURATIONS.map((dur) => (
                      <option key={dur} value={dur}>
                        {dur} giây{dur === 15 ? " (Ưu tiên)" : ""}
                      </option>
                    ))}
                    {!DEFAULT_SUPPORTED_DURATIONS.includes(config.targetDurationPerSceneSeconds as 10 | 15) && (
                      <option value={config.targetDurationPerSceneSeconds}>
                        {config.targetDurationPerSceneSeconds} giây (Cần chọn lại thời lượng được hỗ trợ)
                      </option>
                    )}
                  </select>
                  <p className="text-[11px] text-muted-foreground">
                    AI tự chia các phân cảnh trong từng clip theo nội dung. Các clip được xuất riêng.
                  </p>
                </div>

                {/* Cảnh báo nếu dữ liệu cũ chưa hợp lệ theo capability */}
                {!DEFAULT_SUPPORTED_DURATIONS.includes(config.targetDurationPerSceneSeconds as 10 | 15) && (
                  <div className="flex items-center gap-2 p-2.5 rounded text-xs bg-warning/10 text-warning border border-warning/20 col-span-1 sm:col-span-2">
                    <AlertCircle className="h-4 w-4 shrink-0" />
                    <span>
                      Thời lượng {config.targetDurationPerSceneSeconds}s hiện tại chưa được nhà cung cấp hỗ trợ. Vui lòng chọn lại 10s hoặc 15s trước khi bắt đầu.
                    </span>
                  </div>
                )}

                {/* Tóm tắt trước khi Bắt đầu */}
                <div className="col-span-1 sm:col-span-2 p-3 rounded-lg bg-muted/40 border border-border text-xs text-foreground space-y-1">
                  <div className="font-semibold flex items-center gap-1.5 text-primary">
                    <Sparkles className="h-3.5 w-3.5" />
                    <span>Tóm tắt: Tạo {config.targetSceneCount} clip riêng × {config.targetDurationPerSceneSeconds} giây</span>
                  </div>
                  <p className="text-muted-foreground text-[11px]">
                    Hệ thống sẽ sản xuất {config.targetSceneCount} clip độc lập. Phân cảnh bên trong do AI tự điều phối theo nội dung; không ghép thành một video dài.
                  </p>
                </div>
              </div>
            </CardContent>

            <CardFooter className="flex items-center justify-between border-t pt-4">
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={handleSaveConfig}
                disabled={!isDirty}
              >
                Lưu cấu hình
              </Button>

              <Button
                type="button"
                size="lg"
                onClick={handleStartWorkflow}
                disabled={isStarting}
                className="gap-2 bg-primary text-primary-foreground font-semibold px-6 shadow-md hover:shadow-lg transition-all"
              >
                {isStarting ? (
                  <>
                    <Loader2 className="h-4 w-4 animate-spin" />
                    Đang khởi tạo quy trình…
                  </>
                ) : (
                  <>
                    <Sparkles className="h-4 w-4" />
                    Bắt đầu tạo video
                  </>
                )}
              </Button>
            </CardFooter>
          </Card>
        </div>
      )}

      {/* ============================================================== */}
      {/* 2. MÀN HÌNH CHÍNH: ĐANG CHẠY (RUNNING)                          */}
      {/* ============================================================== */}
      {mainView === "running" && (
        <div className="space-y-6">
          <Card>
            <CardHeader className="pb-3">
              <div className="flex items-center justify-between">
                <div>
                  <CardTitle className="text-lg flex items-center gap-2">
                    <Loader2 className="h-5 w-5 text-primary animate-spin" />
                    Tiến độ sản xuất video tự động
                  </CardTitle>
                  <p className="text-xs text-muted-foreground mt-1">
                    Cảnh nào đủ ảnh sẽ tự động viết prompt chuyển động và đưa vào hàng đợi tạo video theo slot tài khoản có sẵn.
                  </p>
                </div>
                <Badge variant={config.runState === WorkflowRunState.Paused ? "warning" : "info"}>
                  {config.runState === WorkflowRunState.Paused ? "Đang tạm dừng" : "Đang xử lý"}
                </Badge>
              </div>
            </CardHeader>

            <CardContent className="space-y-4">
              <div className="space-y-1.5">
                <div className="flex justify-between text-xs text-muted-foreground">
                  <span>Tiến độ tổng thể:</span>
                  <span>{completedVideoCount}/{scenes.length} clip hoàn tất</span>
                </div>
                <Progress
                  value={scenes.length > 0 ? (completedVideoCount / scenes.length) * 100 : 10}
                  className="h-2"
                />
              </div>

              {/* Danh sách các cảnh đang chạy */}
              <div className="space-y-3 pt-2">
                {scenes.map((scene) => (
                  <div
                    key={scene.sceneId}
                    className="p-3.5 rounded-lg border border-border bg-card flex items-center justify-between gap-3"
                  >
                    <div className="space-y-1 min-w-0">
                      <div className="flex items-center gap-2">
                        <span className="text-xs font-semibold text-foreground truncate">
                          {scene.name}
                        </span>
                        <Badge
                          variant={
                            scene.workflowStatus === SceneWorkflowStatus.VideoCompleted
                              ? "success"
                              : scene.workflowStatus === SceneWorkflowStatus.ReadyForVideo
                              ? "info"
                              : "secondary"
                          }
                          className="text-[10px] px-1.5 py-0"
                        >
                          {scene.workflowStatus === SceneWorkflowStatus.VideoCompleted
                            ? "Đã có video"
                            : scene.workflowStatus === SceneWorkflowStatus.ReadyForVideo
                            ? "Đang tạo video"
                            : scene.workflowStatus === SceneWorkflowStatus.PreparingPrompt
                            ? "Đang viết prompt"
                            : "Đang chuẩn bị ảnh"}
                        </Badge>
                      </div>
                      <p className="text-[11px] text-muted-foreground line-clamp-1">
                        {scene.motionPromptText || "Đang lập prompt chuyển động…"}
                      </p>
                    </div>

                    <div className="flex items-center gap-2 shrink-0">
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        className="h-7 text-xs px-2"
                        onClick={() => {
                          setDetailedScene(scene);
                          setIsDetailsOpen(true);
                        }}
                      >
                        Chi tiết
                      </Button>
                    </div>
                  </div>
                ))}
              </div>
            </CardContent>
          </Card>
        </div>
      )}

      {/* ============================================================== */}
      {/* 3. MÀN HÌNH CHÍNH: KẾT QUẢ (COMPLETED)                          */}
      {/* ============================================================== */}
      {mainView === "completed" && (
        <div className="space-y-6">
          <Card>
            <CardHeader className="pb-3">
              <div className="flex items-center justify-between">
                <div>
                  <CardTitle className="text-lg flex items-center gap-2">
                    <CheckCircle2 className="h-5 w-5 text-success" />
                    Các clip theo cảnh ({completedVideoCount} clip)
                  </CardTitle>
                  <p className="text-xs text-muted-foreground mt-1">
                    Đầu ra là các clip video độc lập theo từng cảnh. Bạn có thể xem thử, tải về hoặc yêu cầu tạo lại từng cảnh riêng lẻ.
                  </p>
                </div>
              </div>
            </CardHeader>

            <CardContent>
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
                {scenes.map((scene) => (
                  <Card key={scene.sceneId} className="overflow-hidden border border-border shadow-xs">
                    {/* Video Player / Thumbnail Preview */}
                    <div className="h-44 bg-muted/60 relative flex items-center justify-center border-b">
                      {scene.videoAssetId ? (
                        <div className="flex flex-col items-center justify-center gap-2">
                          <Film className="h-10 w-10 text-primary/70" />
                          <Button
                            type="button"
                            variant="secondary"
                            size="sm"
                            className="gap-1.5 shadow-xs"
                            onClick={() => setPreviewVideoScene(scene)}
                          >
                            <Play className="h-3.5 w-3.5 fill-current" /> Xem clip
                          </Button>
                        </div>
                      ) : (
                        <div className="text-center p-4">
                          <AlertCircle className="h-6 w-6 text-warning mx-auto mb-1" />
                          <p className="text-xs text-muted-foreground">Clip chưa hoàn thành</p>
                        </div>
                      )}

                      <div className="absolute top-2 left-2">
                        <Badge variant="secondary" className="text-[10px] bg-background/80 backdrop-blur-xs">
                          {scene.targetDurationSeconds}s
                        </Badge>
                      </div>

                      {scene.isStale && (
                        <div className="absolute top-2 right-2">
                          <Badge variant="warning" className="text-[10px]">
                            Cần xem lại
                          </Badge>
                        </div>
                      )}
                    </div>

                    <CardContent className="p-3.5 space-y-2">
                      <h4 className="text-sm font-semibold text-foreground truncate">
                        {scene.name}
                      </h4>
                      <p className="text-xs text-muted-foreground line-clamp-2 leading-relaxed">
                        {scene.motionPromptText || "Không có mô tả prompt"}
                      </p>
                    </CardContent>

                    <CardFooter className="p-3 pt-0 flex items-center justify-between gap-2 border-t mt-2">
                      <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        className="h-7 text-xs px-2 gap-1"
                        onClick={() => handleOpenEditScene(scene)}
                      >
                        <Sliders className="h-3 w-3" /> Sửa yêu cầu
                      </Button>

                      <div className="flex items-center gap-1.5">
                        <Button
                          type="button"
                          variant="ghost"
                          size="sm"
                          className="h-7 text-xs px-2 gap-1"
                          onClick={() => {
                            setDetailedScene(scene);
                            setIsDetailsOpen(true);
                          }}
                        >
                          <Eye className="h-3 w-3" /> Chi tiết
                        </Button>
                      </div>
                    </CardFooter>
                  </Card>
                ))}
              </div>
            </CardContent>
          </Card>
        </div>
      )}

      {/* ============================================================== */}
      {/* DIALOG CHỈNH SỬA YÊU CẦU CẢNH (CHỈ TẠO LẠI PHẦN PHỤ THUỘC §3.4) */}
      {/* ============================================================== */}
      <Dialog open={!!editingScene} onOpenChange={(open) => !open && setEditingScene(null)}>
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle>Sửa yêu cầu cảnh: {editingScene?.name}</DialogTitle>
            <DialogDescription className="text-xs">
              Theo ma trận phụ thuộc §3.4: Sửa prompt chuyển động chỉ làm lại video của cảnh này; ảnh gốc được giữ nguyên.
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-4 py-2">
            <div className="space-y-1.5">
              <Label className="text-xs">Prompt chuyển động tiếng Anh (Motion Prompt):</Label>
              <Textarea
                value={editPromptText}
                onChange={(e) => setEditPromptText(e.target.value)}
                rows={4}
                className="resize-none text-xs"
              />
            </div>
          </div>

          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => setEditingScene(null)}
            >
              Hủy
            </Button>
            <Button
              type="button"
              size="sm"
              onClick={handleSaveSceneEdit}
              disabled={isUpdatingScene}
              className="gap-1.5"
            >
              {isUpdatingScene ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <CheckCircle2 className="h-3.5 w-3.5" />}
              Lưu & Đánh dấu tạo lại
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* ============================================================== */}
      {/* DIALOG XEM THỬ VIDEO CLIP                                      */}
      {/* ============================================================== */}
      <Dialog open={!!previewVideoScene} onOpenChange={(open) => !open && setPreviewVideoScene(null)}>
        <DialogContent className="max-w-lg">
          <DialogHeader>
            <DialogTitle>{previewVideoScene?.name}</DialogTitle>
            <DialogDescription className="text-xs">
              Thời lượng: {previewVideoScene?.targetDurationSeconds} giây • Tệp Asset ID: {previewVideoScene?.videoAssetId}
            </DialogDescription>
          </DialogHeader>

          <div className="h-64 bg-black/90 rounded-md flex items-center justify-center my-2 text-white">
            <div className="text-center space-y-2">
              <Film className="h-12 w-12 text-primary mx-auto animate-pulse" />
              <p className="text-xs text-muted-foreground">
                [Player] Tệp video: {previewVideoScene?.videoAssetId}
              </p>
            </div>
          </div>

          <DialogFooter className="flex items-center justify-between sm:justify-between">
            <span className="text-xs text-muted-foreground truncate max-w-[260px]">
              Prompt: {previewVideoScene?.motionPromptText}
            </span>
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => setPreviewVideoScene(null)}
            >
              Đóng
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* ============================================================== */}
      {/* DIALOG CHI TIẾT KỸ THUẬT 4 BƯỚC (§4)                           */}
      {/* ============================================================== */}
      <Dialog open={isDetailsOpen} onOpenChange={setIsDetailsOpen}>
        <DialogContent className="max-w-lg">
          <DialogHeader>
            <DialogTitle>Chi tiết kỹ thuật 4 bước: {detailedScene?.name}</DialogTitle>
            <DialogDescription className="text-xs">
              Đầu vào → Ảnh dùng cho video → Prompt chuyển động → Video
            </DialogDescription>
          </DialogHeader>

          {detailedScene && (
            <div className="space-y-3 py-2 text-xs">
              <div className="p-2.5 rounded border bg-muted/20 space-y-1">
                <span className="font-semibold text-foreground">Bước 1: Đầu vào</span>
                <p className="text-muted-foreground">Chiến lược: {detailedScene.imageStrategy}</p>
                <p className="text-muted-foreground">Audio: {detailedScene.audioOption}</p>
              </div>

              <div className="p-2.5 rounded border bg-muted/20 space-y-1">
                <span className="font-semibold text-foreground">Bước 2: Ảnh dùng cho video</span>
                <p className="text-muted-foreground truncate">
                  Selected Asset ID: {detailedScene.selectedImageAssetId || "Chưa có"}
                </p>
              </div>

              <div className="p-2.5 rounded border bg-muted/20 space-y-1">
                <span className="font-semibold text-foreground">Bước 3: Prompt chuyển động</span>
                <p className="text-muted-foreground leading-relaxed">
                  {detailedScene.motionPromptText || "Chưa có"}
                </p>
              </div>

              <div className="p-2.5 rounded border bg-muted/20 space-y-1">
                <span className="font-semibold text-foreground">Bước 4: Clip Video</span>
                <p className="text-muted-foreground truncate">
                  Video Asset ID: {detailedScene.videoAssetId || "Chưa hoàn tất"}
                </p>
                <p className="text-muted-foreground">Trạng thái: {detailedScene.workflowStatus}</p>
              </div>
            </div>
          )}

          <DialogFooter>
            <Button variant="outline" size="sm" onClick={() => setIsDetailsOpen(false)}>
              Đóng
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* ============================================================== */}
      {/* DIALOG XEM TRƯỚC YÊU CẦU GỬI AI WRITER (§3.1-3.3)             */}
      {/* ============================================================== */}
      <Dialog open={isAiPreviewOpen} onOpenChange={setIsAiPreviewOpen}>
        <DialogContent className="max-w-2xl max-h-[85vh] flex flex-col">
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2 text-base">
              <Eye className="h-4 w-4 text-primary" />
              Xem yêu cầu gửi AI viết prompt chuyển động
            </DialogTitle>
            <DialogDescription className="text-xs">
              Ứng dụng tự động tổng hợp Mẫu Prompt Video (Template) và Yêu cầu nội dung của bạn thành văn bản gửi AI writer theo hợp đồng chuẩn.
            </DialogDescription>
          </DialogHeader>

          {aiPreviewData && (
            <div className="space-y-4 py-2 overflow-y-auto flex-1 text-xs pr-1">
              {/* Thông tin mẫu template */}
              <div className="p-3 rounded-lg border bg-muted/20 space-y-1.5">
                <div className="flex items-center justify-between">
                  <span className="font-semibold text-foreground">Mẫu Prompt áp dụng:</span>
                  <Badge variant={aiPreviewData.isOverride ? "info" : "secondary"} className="text-[10px]">
                    {aiPreviewData.isOverride ? "Kênh tùy chỉnh" : "Toàn cục mặc định"}
                  </Badge>
                </div>
                <div className="text-muted-foreground flex gap-4">
                  <span>Tên: <strong className="text-foreground">{aiPreviewData.templateName}</strong></span>
                  <span>Phiên bản: <strong className="text-foreground">{aiPreviewData.versionId}</strong></span>
                </div>
              </div>

              {/* Yêu cầu của bạn */}
              <div className="p-3 rounded-lg border bg-muted/20 space-y-1">
                <span className="font-semibold text-foreground">Yêu cầu nội dung (User Brief):</span>
                <p className="text-muted-foreground whitespace-pre-wrap leading-relaxed">
                  {aiPreviewData.userBrief || "(Chưa nhập yêu cầu nội dung)"}
                </p>
              </div>

              {/* Toàn bộ prompt đã compose gửi AI writer */}
              <div className="space-y-1.5">
                <span className="font-semibold text-foreground">Toàn bộ văn bản gửi AI writer (Đã compose):</span>
                <pre className="p-3 rounded-lg border bg-card text-[11px] font-mono whitespace-pre-wrap max-h-64 overflow-y-auto leading-relaxed border-border">
                  {aiPreviewData.composedPrompt}
                </pre>
              </div>
            </div>
          )}

          <DialogFooter className="border-t pt-3 flex items-center justify-between sm:justify-between">
            <span className="text-[11px] text-muted-foreground">
              MuseAI chỉ nhận prompt chuyển động kết quả, không nhận toàn bộ template.
            </span>
            <Button variant="outline" size="sm" onClick={() => setIsAiPreviewOpen(false)}>
              Đóng
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Asset Picker Modal */}
      {pickerTarget && (
        <AssetPicker
          workspaceId={workspaceId}
          projectId={projectId}
          assetType={pickerTarget === "audio" ? "Audio" : "Image"}
          onClose={() => setPickerTarget(null)}
          onSelect={(asset) => handleSelectAsset(asset)}
        />
      )}
    </div>
  );
}
