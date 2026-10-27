import { useState, useEffect, useMemo, useCallback, useRef } from "react";
import { workspaceApi, type WorkspaceSummaryDto } from "./workspaceApi";
import { WorkspaceForm } from "./WorkspaceForm";
import { bridgeClient } from "../../services/bridgeClient";
import type { ErrorDto } from "../../contracts/bridge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardFooter, CardHeader } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Alert, AlertDescription } from "@/components/ui/alert";
import {
  DropdownMenu,
  DropdownMenuTrigger,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
} from "@/components/ui/dropdown-menu";
import {
  AlertDialog,
  AlertDialogContent,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogCancel,
  AlertDialogAction,
} from "@/components/ui/alert-dialog";
import {
  FolderKanban,
  Search,
  Plus,
  RefreshCw,
  MoreVertical,
  Edit,
  Trash2,
  Play,
  Pause,
  AlertTriangle,
  Info,
  ChevronRight,
  Loader2,
} from "lucide-react";
import { cn } from "@/lib/utils";

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
    <div className="space-y-6 pb-8">
      {/* Tiêu đề trang */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
        <div>
          <h2 className="text-2xl font-bold tracking-tight text-foreground">
            Danh sách Workspace
          </h2>
          <p className="text-sm text-muted-foreground mt-1">
            Quản lý các kênh TikTok của bạn. Mỗi workspace là một tài khoản TikTok độc lập.
          </p>
        </div>
        <Button onClick={handleOpenCreateForm} className="shrink-0 gap-2">
          <Plus className="h-4 w-4" /> Tạo workspace
        </Button>
      </div>

      {/* Thanh công cụ: Bộ lọc, Tìm kiếm, Sắp xếp */}
      <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-4">
        {/* Filter Tabs */}
        <div className="inline-flex items-center rounded-lg bg-muted p-1 text-muted-foreground" role="tablist" aria-label="Bộ lọc trạng thái">
          <button
            type="button"
            role="tab"
            aria-selected={statusFilter === "all"}
            className={`inline-flex items-center justify-center whitespace-nowrap rounded-md px-3 py-1.5 text-xs font-medium transition-all cursor-pointer ${
              statusFilter === "all"
                ? "bg-background text-foreground shadow-xs font-semibold"
                : "hover:text-foreground"
            }`}
            onClick={() => setStatusFilter("all")}
          >
            Tất cả ({workspaces.length})
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={statusFilter === "Active"}
            className={`inline-flex items-center justify-center whitespace-nowrap rounded-md px-3 py-1.5 text-xs font-medium transition-all cursor-pointer ${
              statusFilter === "Active"
                ? "bg-background text-foreground shadow-xs font-semibold"
                : "hover:text-foreground"
            }`}
            onClick={() => setStatusFilter("Active")}
          >
            Đang hoạt động ({activeCount})
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={statusFilter === "Paused"}
            className={`inline-flex items-center justify-center whitespace-nowrap rounded-md px-3 py-1.5 text-xs font-medium transition-all cursor-pointer ${
              statusFilter === "Paused"
                ? "bg-background text-foreground shadow-xs font-semibold"
                : "hover:text-foreground"
            }`}
            onClick={() => setStatusFilter("Paused")}
          >
            Tạm dừng ({pausedCount})
          </button>
        </div>

        {/* Search & Sort Controls */}
        <div className="flex items-center gap-2.5 flex-wrap">
          <div className="relative min-w-[240px] flex-1 sm:flex-initial">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
            <Input
              type="text"
              placeholder="Tìm workspace theo tên, handle…"
              className="pl-9"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
            />
          </div>

          <select
            className="flex h-9 rounded-md border border-input bg-background px-3 py-1 text-sm shadow-xs transition-colors focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring cursor-pointer"
            value={sortOption}
            onChange={(e) => setSortOption(e.target.value as SortOption)}
          >
            <option value="latest">Mới nhất</option>
            <option value="oldest">Cũ nhất</option>
            <option value="name_asc">Tên (A-Z)</option>
          </select>

          <Button
            type="button"
            variant="outline"
            size="icon"
            onClick={fetchWorkspaces}
            title="Làm mới danh sách"
            disabled={isLoading}
          >
            <RefreshCw className={`h-4 w-4 ${isLoading ? "animate-spin" : ""}`} />
          </Button>
        </div>
      </div>

      {/* Nội dung danh sách */}
      {isLoading ? (
        <Card className="flex flex-col items-center justify-center p-12 text-center">
          <Loader2 className="h-8 w-8 animate-spin text-primary mb-3" />
          <p className="text-sm text-muted-foreground">Đang tải danh sách Workspace từ Host…</p>
        </Card>
      ) : errorMessage ? (
        <Alert variant="destructive" className="flex flex-col items-center p-6 text-center">
          <AlertTriangle className="h-6 w-6 mb-2" />
          <h3 className="text-base font-semibold">Không thể tải danh sách Workspace</h3>
          <AlertDescription className="mt-1 text-sm">{errorMessage}</AlertDescription>
          <Button variant="outline" size="sm" className="mt-4 gap-2" onClick={fetchWorkspaces}>
            <RefreshCw className="h-4 w-4" /> Thử lại
          </Button>
        </Alert>
      ) : workspaces.length === 0 ? (
        <Card className="flex flex-col items-center justify-center p-12 text-center border-dashed">
          <div className="flex h-14 w-14 items-center justify-center rounded-full bg-primary/10 text-primary mb-4">
            <FolderKanban className="h-7 w-7" />
          </div>
          <h3 className="text-lg font-semibold text-foreground">Chưa có Workspace nào</h3>
          <p className="text-sm text-muted-foreground mt-1 max-w-md">
            Bạn chưa tạo kênh TikTok nào. Hãy bắt đầu bằng cách tạo Workspace đầu tiên để
            quản lý kịch bản, prompt và render video.
          </p>
          <Button onClick={handleOpenCreateForm} className="mt-5 gap-2">
            <Plus className="h-4 w-4" /> Tạo Workspace Đầu Tiên
          </Button>
        </Card>
      ) : filteredWorkspaces.length === 0 ? (
        <Card className="flex flex-col items-center justify-center p-12 text-center border-dashed">
          <h3 className="text-lg font-semibold text-foreground">Không tìm thấy kết quả phù hợp</h3>
          <p className="text-sm text-muted-foreground mt-1">
            Không có workspace nào khớp với bộ lọc hoặc từ khóa tìm kiếm của bạn.
          </p>
          <Button
            variant="outline"
            size="sm"
            className="mt-4"
            onClick={() => {
              setStatusFilter("all");
              setSearchQuery("");
            }}
          >
            Xóa bộ lọc tìm kiếm
          </Button>
        </Card>
      ) : (
        /* Grid các card Workspace (co giãn linh hoạt 1-5 cột theo màn hình) */
        <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-5 gap-5">
          {filteredWorkspaces.map((ws) => {
            const isSelected = selectedWorkspaceId === ws.workspaceId;
            const isActive = ws.status === "Active";
            const avatarColor = getAvatarBgColor(ws.name);
            const initials = getInitials(ws.name);

            return (
              <Card
                key={ws.workspaceId}
                className={cn(
                  "flex flex-col justify-between transition-colors shadow-xs hover:border-primary/50",
                  isSelected && "border-primary ring-2 ring-primary/20"
                )}
              >
                {/* Header Card: Avatar, Thông tin, Menu 3 chấm */}
                <CardHeader className="p-5 pb-3">
                  <div className="flex items-start justify-between gap-3">
                    <div className="flex items-center gap-3 min-w-0">
                      <div
                        className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full text-base font-bold text-white shadow-xs"
                        style={{ backgroundColor: avatarColor }}
                      >
                        {initials}
                      </div>
                      <div className="min-w-0">
                        <h4
                          className="text-base font-bold text-foreground truncate"
                          title={ws.name}
                        >
                          {ws.name}
                        </h4>
                        <p className="text-xs text-muted-foreground truncate">
                          {ws.channelHandle}
                        </p>
                      </div>
                    </div>

                    {/* Nút hành động 3 chấm và dropdown */}
                    <DropdownMenu>
                      <DropdownMenuTrigger asChild>
                        <Button
                          variant="ghost"
                          size="icon"
                          className="h-8 w-8 text-muted-foreground hover:text-foreground"
                          aria-label="Tùy chọn thao tác"
                        >
                          <MoreVertical className="h-4 w-4" />
                        </Button>
                      </DropdownMenuTrigger>
                      <DropdownMenuContent align="end" className="w-48">
                        <DropdownMenuItem onClick={() => handleOpenEditForm(ws)}>
                          <Edit className="h-4 w-4 mr-2" /> Sửa thông tin
                        </DropdownMenuItem>
                        <DropdownMenuItem onClick={() => handleToggleStatus(ws)}>
                          {isActive ? (
                            <>
                              <Pause className="h-4 w-4 mr-2" /> Tạm dừng kênh
                            </>
                          ) : (
                            <>
                              <Play className="h-4 w-4 mr-2" /> Kích hoạt lại
                            </>
                          )}
                        </DropdownMenuItem>
                        <DropdownMenuSeparator />
                        <DropdownMenuItem
                          className="text-destructive focus:bg-destructive/10 focus:text-destructive"
                          onClick={() => {
                            setDeletingWorkspace(ws);
                            setDeleteError(null);
                          }}
                        >
                          <Trash2 className="h-4 w-4 mr-2" /> Xóa kênh
                        </DropdownMenuItem>
                      </DropdownMenuContent>
                    </DropdownMenu>
                  </div>
                </CardHeader>

                {/* Card Content: Topic & Status */}
                <CardContent className="p-5 pt-0 pb-4">
                  <div className="flex items-center justify-between gap-2 pt-2">
                    <span className="inline-flex items-center rounded-full bg-muted/60 px-2.5 py-0.5 text-xs text-muted-foreground border border-border/50 truncate max-w-[180px]">
                      {ws.topic || "Chưa đặt chủ đề"}
                    </span>

                    <Badge variant={isActive ? "success" : "secondary"}>
                      <span className="mr-1 text-[8px]">●</span>
                      {isActive ? "Đang hoạt động" : "Tạm dừng"}
                    </Badge>
                  </div>
                </CardContent>

                {/* Card Footer: Nút mở Workspace */}
                <CardFooter className="p-5 pt-3 border-t border-border">
                  <Button
                    className="w-full justify-center gap-1.5"
                    onClick={() => handleSelectWorkspace(ws)}
                  >
                    Mở Workspace <ChevronRight className="h-4 w-4" />
                  </Button>
                </CardFooter>
              </Card>
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
      <AlertDialog
        open={!!deletingWorkspace}
        onOpenChange={(open) => {
          if (!open && !isDeleting) {
            setDeletingWorkspace(null);
            setDeleteError(null);
          }
        }}
      >
        <AlertDialogContent className="sm:max-w-[460px]">
          <AlertDialogHeader>
            <AlertDialogTitle className="text-lg font-bold text-destructive">
              Xác nhận xóa Workspace
            </AlertDialogTitle>
            <AlertDialogDescription asChild>
              <div className="space-y-3 pt-2 text-sm text-foreground">
                {deleteError && (
                  <Alert variant="destructive">
                    <AlertTriangle className="h-4 w-4" />
                    <AlertDescription>{deleteError}</AlertDescription>
                  </Alert>
                )}
                <p>
                  Bạn có chắc chắn muốn xóa kênh <strong>{deletingWorkspace?.name}</strong> (
                  {deletingWorkspace?.channelHandle})?
                </p>
                <Alert variant="warning">
                  <Info className="h-4 w-4" />
                  <AlertDescription className="text-xs">
                    Hành động này sẽ xóa các dự án, cảnh và tệp cục bộ liên quan của Workspace. Không thể hoàn tác.
                  </AlertDescription>
                </Alert>
              </div>
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={isDeleting}>Hủy</AlertDialogCancel>
            <AlertDialogAction
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
              onClick={(e) => {
                e.preventDefault();
                handleConfirmDelete();
              }}
              disabled={isDeleting}
            >
              {isDeleting ? (
                <>
                  <Loader2 className="h-4 w-4 animate-spin mr-1.5" /> Đang xóa…
                </>
              ) : (
                "Xác nhận xóa"
              )}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
