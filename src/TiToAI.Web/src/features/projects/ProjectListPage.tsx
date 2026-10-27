import { useState, useEffect, useMemo, useCallback, useRef } from "react";
import { projectApi, type ProjectSummaryDto } from "./projectApi";
import { ProjectForm } from "./ProjectForm";
import type { ErrorDto } from "../../contracts/bridge";
import {
  Card,
  CardHeader,
  CardTitle,
  CardDescription,
  CardContent,
  CardFooter,
} from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Alert, AlertDescription } from "@/components/ui/alert";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import {
  AlertDialog,
  AlertDialogContent,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogCancel,
} from "@/components/ui/alert-dialog";
import {
  ArrowLeft,
  Plus,
  Search,
  RefreshCw,
  Loader2,
  AlertCircle,
  MoreHorizontal,
  Pencil,
  Trash2,
  Info,
  Film,
} from "lucide-react";
import { cn } from "@/lib/utils";

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

  // Modals & Form
  const [isFormOpen, setIsFormOpen] = useState(false);
  const [formMode, setFormMode] = useState<"create" | "edit">("create");
  const [editingProject, setEditingProject] = useState<ProjectSummaryDto | null>(null);

  // Modal Xóa
  const [deletingProject, setDeletingProject] = useState<ProjectSummaryDto | null>(null);
  const [isDeleting, setIsDeleting] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);

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
    <div className="space-y-6 pb-10">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
        <div className="flex items-start sm:items-center gap-3">
          {onBackToWorkspaces && (
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={onBackToWorkspaces}
              className="gap-1.5"
            >
              <ArrowLeft className="h-4 w-4" /> Danh sách kênh
            </Button>
          )}
          <div>
            <h2 className="text-2xl font-bold tracking-tight text-foreground">
              Dự Án Video
            </h2>
            <p className="text-sm text-muted-foreground mt-0.5">
              Quản lý kịch bản phân cảnh, prompt chuyển động và video hoàn chỉnh cho kênh.
            </p>
          </div>
        </div>

        <Button
          type="button"
          onClick={() => {
            setEditingProject(null);
            setFormMode("create");
            setIsFormOpen(true);
          }}
          className="gap-1.5 self-start sm:self-auto"
        >
          <Plus className="h-4 w-4" /> Tạo Dự Án Mới
        </Button>
      </div>

      {/* Filter & Search Bar */}
      <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-3">
        <div
          className="inline-flex h-9 items-center rounded-lg bg-muted p-1 text-muted-foreground w-fit overflow-x-auto"
          role="tablist"
          aria-label="Lọc theo trạng thái dự án"
        >
          <button
            type="button"
            role="tab"
            aria-selected={statusFilter === "all"}
            className={cn(
              "inline-flex items-center justify-center whitespace-nowrap rounded-md px-3 py-1 text-xs font-medium ring-offset-background transition-all focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2",
              statusFilter === "all"
                ? "bg-background text-foreground shadow-xs"
                : "text-muted-foreground hover:text-foreground"
            )}
            onClick={() => setStatusFilter("all")}
          >
            Tất cả ({projects.length})
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={statusFilter === "InProgress"}
            className={cn(
              "inline-flex items-center justify-center whitespace-nowrap rounded-md px-3 py-1 text-xs font-medium ring-offset-background transition-all focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2",
              statusFilter === "InProgress"
                ? "bg-background text-foreground shadow-xs"
                : "text-muted-foreground hover:text-foreground"
            )}
            onClick={() => setStatusFilter("InProgress")}
          >
            Đang sản xuất ({projects.filter((p) => p.status === "InProgress").length})
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={statusFilter === "Completed"}
            className={cn(
              "inline-flex items-center justify-center whitespace-nowrap rounded-md px-3 py-1 text-xs font-medium ring-offset-background transition-all focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2",
              statusFilter === "Completed"
                ? "bg-background text-foreground shadow-xs"
                : "text-muted-foreground hover:text-foreground"
            )}
            onClick={() => setStatusFilter("Completed")}
          >
            Hoàn thành ({projects.filter((p) => p.status === "Completed").length})
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={statusFilter === "Draft"}
            className={cn(
              "inline-flex items-center justify-center whitespace-nowrap rounded-md px-3 py-1 text-xs font-medium ring-offset-background transition-all focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2",
              statusFilter === "Draft"
                ? "bg-background text-foreground shadow-xs"
                : "text-muted-foreground hover:text-foreground"
            )}
            onClick={() => setStatusFilter("Draft")}
          >
            Nháp ({projects.filter((p) => p.status === "Draft").length})
          </button>
        </div>

        <div className="flex items-center gap-2">
          <div className="relative w-full md:w-72">
            <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
            <Input
              type="text"
              placeholder="Tìm dự án theo tên hoặc mô tả…"
              className="pl-9 h-9"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
            />
          </div>

          <Button
            type="button"
            variant="outline"
            size="icon"
            className="h-9 w-9 shrink-0"
            onClick={fetchProjects}
            title="Tải lại danh sách"
            disabled={isLoading}
          >
            <RefreshCw className={cn("h-4 w-4", isLoading && "animate-spin")} />
          </Button>
        </div>
      </div>

      {/* Danh sách dự án */}
      {isLoading ? (
        <Card className="flex flex-col items-center justify-center p-12 text-center">
          <Loader2 className="h-8 w-8 animate-spin text-primary mb-3" />
          <p className="text-sm text-muted-foreground">Đang tải danh sách Dự án từ Host…</p>
        </Card>
      ) : errorMessage ? (
        <Card className="p-8 text-center border-destructive/50 bg-destructive/5">
          <AlertCircle className="h-10 w-10 text-destructive mx-auto mb-3" />
          <h3 className="text-lg font-semibold text-foreground mb-1">Không thể tải danh sách Dự án</h3>
          <p className="text-sm text-muted-foreground mb-4">{errorMessage}</p>
          <Button variant="outline" onClick={fetchProjects}>
            <RefreshCw className="mr-2 h-4 w-4" /> Thử lại
          </Button>
        </Card>
      ) : projects.length === 0 ? (
        <Card className="flex flex-col items-center justify-center p-12 text-center">
          <div className="rounded-full bg-primary/10 p-4 mb-4 text-primary">
            <Film className="h-10 w-10" />
          </div>
          <h3 className="text-lg font-semibold text-foreground mb-1">Chưa có dự án nào trong kênh này</h3>
          <p className="text-sm text-muted-foreground max-w-md mb-4">
            Hãy tạo dự án video đầu tiên để bắt đầu xây dựng kịch bản phân cảnh và tự động sinh prompt.
          </p>
          <Button
            onClick={() => {
              setEditingProject(null);
              setFormMode("create");
              setIsFormOpen(true);
            }}
          >
            <Plus className="mr-2 h-4 w-4" /> Tạo Dự Án Mới
          </Button>
        </Card>
      ) : filteredProjects.length === 0 ? (
        <Card className="flex flex-col items-center justify-center p-12 text-center">
          <h3 className="text-lg font-semibold text-foreground mb-1">Không có dự án phù hợp</h3>
          <p className="text-sm text-muted-foreground mb-4">Không tìm thấy dự án nào khớp với điều kiện lọc hiện tại.</p>
          <Button
            variant="outline"
            onClick={() => {
              setStatusFilter("all");
              setSearchQuery("");
            }}
          >
            Xóa bộ lọc tìm kiếm
          </Button>
        </Card>
      ) : (
        <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-5 gap-5">
          {filteredProjects.map((prj) => {
            return (
              <Card
                key={prj.projectId}
                className="flex flex-col justify-between hover:border-primary/50 transition-colors shadow-xs"
              >
                <CardHeader className="p-5 pb-3">
                  <div className="flex items-start justify-between gap-2">
                    <CardTitle className="text-base font-bold text-foreground line-clamp-1">
                      {prj.name}
                    </CardTitle>

                    <DropdownMenu>
                      <DropdownMenuTrigger asChild>
                        <Button
                          variant="ghost"
                          size="icon"
                          className="h-8 w-8 -mr-2 text-muted-foreground hover:text-foreground shrink-0"
                        >
                          <MoreHorizontal className="h-4 w-4" />
                          <span className="sr-only">Tùy chọn</span>
                        </Button>
                      </DropdownMenuTrigger>
                      <DropdownMenuContent align="end">
                        <DropdownMenuItem
                          onClick={() => {
                            setEditingProject(prj);
                            setFormMode("edit");
                            setIsFormOpen(true);
                          }}
                        >
                          <Pencil className="mr-2 h-4 w-4" /> Sửa thông tin
                        </DropdownMenuItem>
                        <DropdownMenuItem
                          className="text-destructive focus:text-destructive"
                          onClick={() => {
                            setDeletingProject(prj);
                            setDeleteError(null);
                          }}
                        >
                          <Trash2 className="mr-2 h-4 w-4" /> Xóa dự án
                        </DropdownMenuItem>
                      </DropdownMenuContent>
                    </DropdownMenu>
                  </div>

                  <CardDescription className="line-clamp-2 text-xs leading-relaxed mt-1">
                    {prj.description || "Chưa có mô tả chi tiết."}
                  </CardDescription>
                </CardHeader>

                <CardContent className="p-5 pt-0 pb-3">
                  <div className="flex items-center justify-between text-xs">
                    <Badge variant="secondary" className="gap-1 font-medium">
                      <Film className="h-3 w-3 text-muted-foreground" />
                      {prj.sceneCount} cảnh
                    </Badge>
                    <Badge
                      variant={
                        prj.status === "Completed"
                          ? "success"
                          : prj.status === "InProgress"
                          ? "info"
                          : "secondary"
                      }
                    >
                      ● {prj.status}
                    </Badge>
                  </div>
                </CardContent>

                <CardFooter className="p-5 pt-3 border-t border-border flex items-center justify-between">
                  <span className="text-xs text-muted-foreground">
                    Cập nhật: {new Date(prj.updatedAtUtc).toLocaleDateString("vi-VN")}
                  </span>

                  <Button
                    type="button"
                    size="sm"
                    onClick={() => onSelectProject(prj.projectId)}
                  >
                    Mở dự án →
                  </Button>
                </CardFooter>
              </Card>
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
      <AlertDialog
        open={!!deletingProject}
        onOpenChange={(open) => {
          if (!open && !isDeleting) {
            setDeletingProject(null);
          }
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle className="text-destructive">
              Xác nhận xóa Dự Án
            </AlertDialogTitle>
            <AlertDialogDescription className="space-y-3 text-sm">
              {deleteError && (
                <Alert variant="destructive">
                  <AlertCircle className="h-4 w-4" />
                  <AlertDescription>{deleteError}</AlertDescription>
                </Alert>
              )}
              <span className="block text-foreground">
                Bạn có chắc chắn muốn xóa dự án <strong>{deletingProject?.name}</strong>?
              </span>
              <Alert variant="default" className="bg-muted/40">
                <Info className="h-4 w-4 text-muted-foreground" />
                <AlertDescription className="text-xs text-muted-foreground">
                  Toàn bộ các cảnh và tài nguyên phân cảnh trong dự án này sẽ bị xóa.
                </AlertDescription>
              </Alert>
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel
              disabled={isDeleting}
              onClick={() => setDeletingProject(null)}
            >
              Hủy
            </AlertDialogCancel>
            <Button
              variant="destructive"
              disabled={isDeleting}
              onClick={handleDelete}
            >
              {isDeleting ? (
                <>
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" /> Đang xóa…
                </>
              ) : (
                "Xác nhận xóa"
              )}
            </Button>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
