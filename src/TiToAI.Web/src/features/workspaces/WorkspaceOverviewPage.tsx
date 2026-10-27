import { useState, useEffect, useCallback, useRef } from "react";
import { workspaceApi, type WorkspaceDetailDto } from "./workspaceApi";
import { projectApi, type ProjectSummaryDto } from "../projects/projectApi";
import { WorkspaceForm } from "./WorkspaceForm";
import { ProjectForm } from "../projects/ProjectForm";
import type { ErrorDto } from "../../contracts/bridge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Alert, AlertDescription } from "@/components/ui/alert";
import {
  Film,
  Layers,
  Sparkles,
  Clock,
  Edit,
  Plus,
  RefreshCw,
  AlertTriangle,
  Loader2,
  ChevronRight,
  Video,
} from "lucide-react";

export interface WorkspaceOverviewPageProps {
  workspaceId: string;
  onNavigateToProjects?: () => void;
  onSelectProject?: (projectId: string) => void;
}

// Tạo màu avatar ổn định theo tên
function getAvatarBgColor(text: string): string {
  const colors = [
    "#6d3af2",
    "#0284c7",
    "#059669",
    "#d97706",
    "#e11d48",
    "#7c3aed",
    "#2563eb",
    "#db2777",
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
      <Card className="flex flex-col items-center justify-center p-16 text-center mt-6">
        <Loader2 className="h-8 w-8 animate-spin text-primary mb-3" />
        <p className="text-sm text-muted-foreground">Đang tải dữ liệu tổng quan kênh…</p>
      </Card>
    );
  }

  if (errorMessage || !workspace) {
    return (
      <Alert variant="destructive" className="flex flex-col items-center p-6 text-center mt-6">
        <AlertTriangle className="h-6 w-6 mb-2" />
        <h3 className="text-base font-semibold">Không thể tải thông tin kênh</h3>
        <AlertDescription className="mt-1 text-sm">
          {errorMessage || "Kênh không tồn tại hoặc đã bị xóa."}
        </AlertDescription>
        <Button variant="outline" size="sm" className="mt-4 gap-2" onClick={fetchData}>
          <RefreshCw className="h-4 w-4" /> Thử lại
        </Button>
      </Alert>
    );
  }

  const totalScenes = projects.reduce((acc, p) => acc + (p.sceneCount || 0), 0);
  const recentProjects = projects.slice(0, 3);
  const avatarColor = getAvatarBgColor(workspace.name);
  const initials = getInitials(workspace.name);
  const isActive = workspace.status === "Active";

  return (
    <div className="space-y-6 pb-10">
      {/* Header kênh */}
      <Card className="p-6 shadow-sm">
        <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
          <div className="flex items-center gap-4">
            {/* Avatar to */}
            <div
              className="flex h-16 w-16 shrink-0 items-center justify-center rounded-full text-2xl font-bold text-white shadow-md"
              style={{ backgroundColor: avatarColor }}
            >
              {initials}
            </div>

            <div>
              <div className="flex items-center gap-2.5 flex-wrap">
                <h2 className="text-2xl font-bold tracking-tight text-foreground">
                  {workspace.name}
                </h2>
                <Badge variant={isActive ? "success" : "secondary"}>
                  <span className="mr-1 text-[8px]">●</span>
                  {isActive ? "Đang hoạt động" : "Tạm dừng"}
                </Badge>
              </div>

              <div className="flex items-center gap-2 mt-1 text-sm text-muted-foreground flex-wrap">
                <span className="font-medium text-foreground/80">{workspace.channelHandle}</span>
                <span>•</span>
                <span>{workspace.topic || "Chưa thiết lập chủ đề"}</span>
              </div>
            </div>
          </div>

          {/* Nút hành động */}
          <div className="flex items-center gap-2 self-start sm:self-center">
            <Button
              variant="outline"
              size="sm"
              onClick={() => setIsEditFormOpen(true)}
              className="gap-2"
            >
              <Edit className="h-4 w-4" /> Chỉnh sửa kênh
            </Button>
          </div>
        </div>
      </Card>

      {/* Hàng 4 thẻ chỉ số thống kê (KPI Cards) */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <Card className="p-5 shadow-xs">
          <div className="flex items-center justify-between pb-2">
            <span className="text-xs font-medium text-muted-foreground uppercase tracking-wider">
              Tổng số dự án
            </span>
            <div className="flex h-8 w-8 items-center justify-center rounded-md bg-primary/10 text-primary">
              <Film className="h-4 w-4" />
            </div>
          </div>
          <div className="text-2xl font-bold text-foreground">{projects.length}</div>
          <p className="text-xs text-muted-foreground mt-1">Dự án video đang quản lý</p>
        </Card>

        <Card className="p-5 shadow-xs">
          <div className="flex items-center justify-between pb-2">
            <span className="text-xs font-medium text-muted-foreground uppercase tracking-wider">
              Tổng số cảnh (Scenes)
            </span>
            <div className="flex h-8 w-8 items-center justify-center rounded-md bg-primary/10 text-primary">
              <Layers className="h-4 w-4" />
            </div>
          </div>
          <div className="text-2xl font-bold text-foreground">{totalScenes}</div>
          <p className="text-xs text-muted-foreground mt-1">Cảnh được tạo trong kênh</p>
        </Card>

        <Card className="p-5 shadow-xs">
          <div className="flex items-center justify-between pb-2">
            <span className="text-xs font-medium text-muted-foreground uppercase tracking-wider">
              Tác vụ đang chạy (Jobs)
            </span>
            <div className="flex h-8 w-8 items-center justify-center rounded-md bg-primary/10 text-primary">
              <Sparkles className="h-4 w-4" />
            </div>
          </div>
          <div className="text-lg font-semibold text-muted-foreground">Chưa có dữ liệu</div>
          <p className="text-xs text-muted-foreground mt-1">Chờ kết nối hàng đợi xử lý</p>
        </Card>

        <Card className="p-5 shadow-xs">
          <div className="flex items-center justify-between pb-2">
            <span className="text-xs font-medium text-muted-foreground uppercase tracking-wider">
              Tác vụ chờ xử lý
            </span>
            <div className="flex h-8 w-8 items-center justify-center rounded-md bg-primary/10 text-primary">
              <Clock className="h-4 w-4" />
            </div>
          </div>
          <div className="text-lg font-semibold text-muted-foreground">Chưa có dữ liệu</div>
          <p className="text-xs text-muted-foreground mt-1">Cần người dùng can thiệp</p>
        </Card>
      </div>

      {/* 2 cột nội dung: Cột trái "Dự án hiện tại", Cột phải "Quy trình sản xuất" */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
        {/* CỘT TRÁI: DỰ ÁN GẦN ĐÂY */}
        <Card className="lg:col-span-7 flex flex-col justify-between shadow-xs">
          <CardHeader className="p-5 pb-3">
            <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
              <div>
                <CardTitle className="text-base font-bold text-foreground">
                  Dự án hiện tại
                </CardTitle>
                <p className="text-xs text-muted-foreground mt-0.5">
                  Các dự án video đang tiến hành trong Workspace này
                </p>
              </div>

              <div className="flex items-center gap-2">
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setIsCreateProjectOpen(true)}
                  className="gap-1.5"
                >
                  <Plus className="h-3.5 w-3.5" /> Dự án mới
                </Button>
                {projects.length > 0 && onNavigateToProjects && (
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={onNavigateToProjects}
                    className="text-primary hover:text-primary gap-1"
                  >
                    Xem tất cả ({projects.length}) →
                  </Button>
                )}
              </div>
            </div>
          </CardHeader>

          <CardContent className="p-5 pt-2 space-y-3">
            {projects.length === 0 ? (
              <div className="flex flex-col items-center justify-center rounded-lg border border-dashed border-border bg-muted/20 p-8 text-center">
                <Video className="h-9 w-9 text-muted-foreground mb-2" />
                <p className="text-sm text-muted-foreground">
                  Chưa có dự án nào trong kênh này.
                </p>
                <Button
                  size="sm"
                  className="mt-3 gap-1.5"
                  onClick={() => setIsCreateProjectOpen(true)}
                >
                  <Plus className="h-3.5 w-3.5" /> Bắt đầu dự án đầu tiên
                </Button>
              </div>
            ) : (
              <div className="space-y-3">
                {recentProjects.map((prj) => (
                  <div
                    key={prj.projectId}
                    className="flex flex-col gap-2.5 rounded-lg border border-border/70 bg-card p-4 transition-all hover:border-border hover:shadow-xs"
                  >
                    <div className="flex items-start justify-between gap-2">
                      <div>
                        <h4 className="text-sm font-semibold text-foreground">
                          {prj.name}
                        </h4>
                        <p className="mt-0.5 text-xs text-muted-foreground line-clamp-2">
                          {prj.description || "Không có mô tả chi tiết."}
                        </p>
                      </div>
                      <Badge variant="outline" className="shrink-0 text-xs">
                        {prj.sceneCount} cảnh
                      </Badge>
                    </div>

                    <div className="flex items-center justify-between pt-1">
                      <span className="text-xs text-muted-foreground">
                        Trạng thái: <strong className="text-foreground">{prj.status}</strong>
                      </span>

                      <Button
                        size="sm"
                        className="h-8 gap-1 text-xs"
                        onClick={() => {
                          if (onSelectProject) {
                            onSelectProject(prj.projectId);
                          } else if (onNavigateToProjects) {
                            onNavigateToProjects();
                          }
                        }}
                      >
                        Tiếp tục làm việc <ChevronRight className="h-3.5 w-3.5" />
                      </Button>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </CardContent>
        </Card>

        {/* CỘT PHẢI: QUY TRÌNH SẢN XUẤT NỘI DUNG (PIPELINE MAP) */}
        <Card className="lg:col-span-5 flex flex-col justify-between shadow-xs">
          <CardHeader className="p-5 pb-3">
            <CardTitle className="text-base font-bold text-foreground">
              Quy trình sản xuất nội dung
            </CardTitle>
            <p className="text-xs text-muted-foreground mt-0.5">
              Chu trình chuyển đổi từ ý tưởng kịch bản đến video TikTok
            </p>
          </CardHeader>

          <CardContent className="p-5 pt-2 space-y-2.5">
            {/* Bước 1: Kịch bản */}
            <div className="flex items-center justify-between rounded-lg border border-border/70 bg-card p-3 shadow-2xs">
              <div className="flex items-center gap-3">
                <div className="flex h-7 w-7 items-center justify-center rounded-full bg-primary/10 text-xs font-bold text-primary">
                  1
                </div>
                <div>
                  <div className="text-xs font-semibold text-foreground">
                    Kịch bản & Lời dẫn
                  </div>
                  <div className="text-[11px] text-muted-foreground">
                    Phân đoạn cảnh và soạn thảo nội dung
                  </div>
                </div>
              </div>
              <Badge variant="success">Sẵn sàng</Badge>
            </div>

            {/* Bước 2: Tạo ảnh AI */}
            <div className="flex items-center justify-between rounded-lg border border-border/70 bg-card p-3 shadow-2xs">
              <div className="flex items-center gap-3">
                <div className="flex h-7 w-7 items-center justify-center rounded-full bg-primary/10 text-xs font-bold text-primary">
                  2
                </div>
                <div>
                  <div className="text-xs font-semibold text-foreground">
                    Tạo ảnh AI minh họa
                  </div>
                  <div className="text-[11px] text-muted-foreground">
                    Sinh hình ảnh nhân vật & bối cảnh
                  </div>
                </div>
              </div>
              <Badge variant="secondary">Tự động</Badge>
            </div>

            {/* Bước 3: Prompt chuyển động */}
            <div className="flex items-center justify-between rounded-lg border border-border/70 bg-card p-3 shadow-2xs">
              <div className="flex items-center gap-3">
                <div className="flex h-7 w-7 items-center justify-center rounded-full bg-primary/10 text-xs font-bold text-primary">
                  3
                </div>
                <div>
                  <div className="text-xs font-semibold text-foreground">
                    Prompt chuyển động video
                  </div>
                  <div className="text-[11px] text-muted-foreground">
                    Đặc tả camera và hành động nhân vật
                  </div>
                </div>
              </div>
              <Badge variant="secondary">Tự động</Badge>
            </div>

            {/* Bước 4: Âm thanh gốc & Video AI */}
            <div className="flex items-center justify-between rounded-lg border border-border/70 bg-card p-3 shadow-2xs">
              <div className="flex items-center gap-3">
                <div className="flex h-7 w-7 items-center justify-center rounded-full bg-primary/10 text-xs font-bold text-primary">
                  4
                </div>
                <div>
                  <div className="text-xs font-semibold text-foreground">
                    Âm thanh gốc & Render Video
                  </div>
                  <div className="text-[11px] text-muted-foreground">
                    Sinh video AI qua MuseAI
                  </div>
                </div>
              </div>
              <Badge variant="outline">MuseAI</Badge>
            </div>

            {/* Bước 5: Đăng tải TikTok */}
            <div className="flex items-center justify-between rounded-lg border border-border/70 bg-card p-3 shadow-2xs">
              <div className="flex items-center gap-3">
                <div className="flex h-7 w-7 items-center justify-center rounded-full bg-primary/10 text-xs font-bold text-primary">
                  5
                </div>
                <div>
                  <div className="text-xs font-semibold text-foreground">
                    Thư viện & Đăng TikTok
                  </div>
                  <div className="text-[11px] text-muted-foreground">
                    Lưu trữ asset và lên lịch phát hành
                  </div>
                </div>
              </div>
              <Badge variant="outline">TikTok</Badge>
            </div>
          </CardContent>
        </Card>
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
