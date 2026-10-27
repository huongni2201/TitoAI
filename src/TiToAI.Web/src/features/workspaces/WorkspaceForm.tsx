import React, { useState, useEffect, useRef } from "react";
import { workspaceApi, type WorkspaceSummaryDto, type WorkspaceDetailDto } from "./workspaceApi";
import type { ErrorDto } from "../../contracts/bridge";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { AlertTriangle, Loader2 } from "lucide-react";

export interface WorkspaceFormProps {
  isOpen: boolean;
  mode: "create" | "edit";
  workspace?: WorkspaceDetailDto | WorkspaceSummaryDto | null;
  onClose: () => void;
  onSuccess: (savedWorkspaceId: string) => void;
}

export function WorkspaceForm({
  isOpen,
  mode,
  workspace,
  onClose,
  onSuccess,
}: WorkspaceFormProps) {
  const [name, setName] = useState("");
  const [channelHandle, setChannelHandle] = useState("");
  const [topic, setTopic] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});

  const nameInputRef = useRef<HTMLInputElement>(null);

  // Khởi tạo giá trị khi mở form
  useEffect(() => {
    if (isOpen) {
      if (mode === "edit" && workspace) {
        setName(workspace.name || "");
        setChannelHandle(workspace.channelHandle || "");
        setTopic(workspace.topic || "");
      } else {
        setName("");
        setChannelHandle("");
        setTopic("");
      }
      setErrorMessage(null);
      setFieldErrors({});
      setIsSubmitting(false);

      // Focus vào ô input đầu tiên
      setTimeout(() => {
        nameInputRef.current?.focus();
      }, 50);
    }
  }, [isOpen, mode, workspace]);

  // Xử lý tự động chuẩn hóa handle (@...)
  const handleHandleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    let val = e.target.value;
    if (val && !val.startsWith("@")) {
      val = "@" + val;
    }
    setChannelHandle(val);
    if (fieldErrors.channelHandle) {
      setFieldErrors((prev) => ({ ...prev, channelHandle: "" }));
    }
  };

  const handleNameChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setName(e.target.value);
    if (fieldErrors.name) {
      setFieldErrors((prev) => ({ ...prev, name: "" }));
    }
  };

  const validate = (): boolean => {
    const errors: Record<string, string> = {};
    const trimmedName = name.trim();
    const trimmedHandle = channelHandle.trim();

    if (!trimmedName) {
      errors.name = "Vui lòng nhập tên kênh.";
    } else if (trimmedName.length > 100) {
      errors.name = "Tên kênh không được vượt quá 100 ký tự.";
    }

    if (!trimmedHandle || trimmedHandle === "@") {
      errors.channelHandle = "Vui lòng nhập handle kênh (ví dụ: @thoitrangnuxinh).";
    } else if (trimmedHandle.length > 60) {
      errors.channelHandle = "Handle không được vượt quá 60 ký tự.";
    }

    setFieldErrors(errors);
    return Object.keys(errors).length === 0;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (isSubmitting) return;

    if (!validate()) {
      return;
    }

    setIsSubmitting(true);
    setErrorMessage(null);

    const trimmedName = name.trim();
    let normalizedHandle = channelHandle.trim();
    if (!normalizedHandle.startsWith("@")) {
      normalizedHandle = "@" + normalizedHandle;
    }
    const trimmedTopic = topic.trim();

    try {
      if (mode === "create") {
        const result = await workspaceApi.create({
          name: trimmedName,
          channelHandle: normalizedHandle,
          topic: trimmedTopic,
        });
        onSuccess(result.workspaceId);
      } else if (mode === "edit" && workspace) {
        const result = await workspaceApi.update({
          workspaceId: workspace.workspaceId,
          name: trimmedName,
          channelHandle: normalizedHandle,
          topic: trimmedTopic,
        });
        onSuccess(result.workspaceId);
      }
    } catch (err: unknown) {
      const errorDto = err as ErrorDto;
      console.error("[WorkspaceForm] Lỗi lưu Workspace:", errorDto);
      // Giữ nguyên form, không đóng form khi lỗi
      setErrorMessage(
        errorDto?.message || "Đã xảy ra lỗi khi lưu thông tin kênh. Vui lòng thử lại.",
      );
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <Dialog
      open={isOpen}
      onOpenChange={(open) => {
        if (!open && !isSubmitting) {
          onClose();
        }
      }}
    >
      <DialogContent className="sm:max-w-[520px]">
        <DialogHeader>
          <DialogTitle className="text-xl font-bold tracking-tight">
            {mode === "create" ? "Tạo Workspace Kênh Mới" : "Chỉnh Sửa Workspace"}
          </DialogTitle>
          <DialogDescription className="text-sm text-muted-foreground">
            {mode === "create"
              ? "Thiết lập kênh TikTok mới để quản lý dự án video và prompt."
              : `Cập nhật thông tin nhận diện cho kênh ${workspace?.name || ""}.`}
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4">
          {errorMessage && (
            <Alert variant="destructive">
              <AlertTriangle className="h-4 w-4" />
              <AlertDescription>{errorMessage}</AlertDescription>
            </Alert>
          )}

          <div className="space-y-2">
            <Label htmlFor="ws-name">
              Tên kênh hiển thị <span className="text-destructive">*</span>
            </Label>
            <Input
              id="ws-name"
              ref={nameInputRef}
              type="text"
              placeholder="Ví dụ: Thời Trang Nữ Xinh, Sức Khỏe & Đời Sống"
              value={name}
              maxLength={100}
              disabled={isSubmitting}
              onChange={handleNameChange}
              aria-invalid={!!fieldErrors.name}
            />
            {fieldErrors.name ? (
              <p className="text-xs text-destructive">{fieldErrors.name}</p>
            ) : (
              <p className="text-xs text-muted-foreground">Tối đa 100 ký tự.</p>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor="ws-handle">
              Handle kênh (@) <span className="text-destructive">*</span>
            </Label>
            <Input
              id="ws-handle"
              type="text"
              placeholder="@thoitrangnuxinh"
              value={channelHandle}
              maxLength={60}
              disabled={isSubmitting}
              onChange={handleHandleChange}
              aria-invalid={!!fieldErrors.channelHandle}
            />
            {fieldErrors.channelHandle ? (
              <p className="text-xs text-destructive">{fieldErrors.channelHandle}</p>
            ) : (
              <p className="text-xs text-muted-foreground">
                Định danh TikTok của kênh (tự động thêm tiền tố @ nếu thiếu).
              </p>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor="ws-topic">Chủ đề / Niche nội dung</Label>
            <Input
              id="ws-topic"
              type="text"
              placeholder="Ví dụ: Thời trang nữ, review đồ xinh, gia dụng tiện ích"
              value={topic}
              maxLength={150}
              disabled={isSubmitting}
              onChange={(e) => setTopic(e.target.value)}
            />
            <p className="text-xs text-muted-foreground">
              Chủ đề định hướng để AI hỗ trợ gợi ý kịch bản và prompt phù hợp.
            </p>
          </div>

          <DialogFooter className="flex flex-col-reverse sm:flex-row sm:items-center sm:justify-between gap-3 pt-2">
            <span className="text-xs text-muted-foreground">
              {isSubmitting ? "Đang xử lý…" : "Dữ liệu được lưu trữ cục bộ."}
            </span>
            <div className="flex items-center justify-end gap-2">
              <Button
                type="button"
                variant="outline"
                onClick={onClose}
                disabled={isSubmitting}
              >
                Hủy
              </Button>
              <Button type="submit" disabled={isSubmitting}>
                {isSubmitting ? (
                  <>
                    <Loader2 className="h-4 w-4 animate-spin" />
                    Đang lưu…
                  </>
                ) : mode === "create" ? (
                  "Tạo Workspace"
                ) : (
                  "Lưu thay đổi"
                )}
              </Button>
            </div>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
