import { useState, useEffect, useCallback, useRef } from "react";
import { workspaceApi, type AccountDto } from "./workspaceApi";
import { browserManagerApi, type BrowserProfileDto } from "../browsers/browserManagerApi";
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
import { Badge } from "@/components/ui/badge";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Label } from "@/components/ui/label";
import { IconTikTok } from "../../components/Icons";
import { AlertTriangle, CheckCircle2, Info, Loader2, Sparkles } from "lucide-react";

export interface AccountLinkDialogProps {
  isOpen: boolean;
  workspaceId: string;
  workspaceName?: string;
  onClose: () => void;
  onSuccess?: () => void;
}

export function AccountLinkDialog({
  isOpen,
  workspaceId,
  workspaceName,
  onClose,
  onSuccess,
}: AccountLinkDialogProps) {
  const [accounts, setAccounts] = useState<AccountDto[]>([]);
  const [profiles, setProfiles] = useState<BrowserProfileDto[]>([]);
  const [selectedProvider, setSelectedProvider] = useState<"MuseAI" | "TikTok">("MuseAI");
  const [selectedProfileId, setSelectedProfileId] = useState<string>("");

  const [isLoading, setIsLoading] = useState(true);
  const [isLinking, setIsLinking] = useState(false);
  const [unlinkingAccountId, setUnlinkingAccountId] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

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
      const [accList, profList] = await Promise.all([
        workspaceApi.listAccounts(workspaceId),
        browserManagerApi.listProfiles().catch(() => [] as BrowserProfileDto[]),
      ]);

      if (controller.signal.aborted) return;

      setAccounts(accList);
      setProfiles(profList);

      // Tự động chọn profile đầu tiên phù hợp với provider nếu có
      const matched = profList.filter((p) => p.kind === selectedProvider);
      if (matched.length > 0) {
        setSelectedProfileId(matched[0].profileId);
      } else if (profList.length > 0) {
        setSelectedProfileId(profList[0].profileId);
      }
    } catch (err: unknown) {
      if (controller.signal.aborted) return;
      const errorDto = err as ErrorDto;
      console.warn("[AccountLinkDialog] Lỗi tải dữ liệu tài khoản:", errorDto);
      setErrorMessage(errorDto?.message || "Không thể tải danh sách tài khoản liên kết.");
    } finally {
      if (!controller.signal.aborted) {
        setIsLoading(false);
      }
    }
  }, [workspaceId, selectedProvider]);

  useEffect(() => {
    if (isOpen) {
      fetchData();
      setSuccessMessage(null);
    }
    return () => {
      if (abortControllerRef.current) {
        abortControllerRef.current.abort();
      }
    };
  }, [isOpen, fetchData]);

  // Cập nhật selectedProfile khi provider thay đổi
  const handleProviderChange = (provider: "MuseAI" | "TikTok") => {
    setSelectedProvider(provider);
    const matched = profiles.filter((p) => p.kind === provider);
    if (matched.length > 0) {
      setSelectedProfileId(matched[0].profileId);
    } else {
      setSelectedProfileId("");
    }
  };

  const handleLink = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedProfileId || isLinking) return;

    setIsLinking(true);
    setErrorMessage(null);
    setSuccessMessage(null);

    try {
      await workspaceApi.linkAccount({
        workspaceId,
        provider: selectedProvider,
        profileId: selectedProfileId,
      });

      setSuccessMessage(`Đã liên kết tài khoản ${selectedProvider} thành công!`);
      await fetchData();
      onSuccess?.();
    } catch (err: unknown) {
      const errorDto = err as ErrorDto;
      console.error("[AccountLinkDialog] Lỗi liên kết tài khoản:", errorDto);
      setErrorMessage(
        errorDto?.message || "Không thể liên kết tài khoản. Vui lòng thử lại.",
      );
    } finally {
      setIsLinking(false);
    }
  };

  const handleUnlink = async (accountId: string, providerName: string) => {
    const confirmUnlink = window.confirm(
      `Bạn có chắc chắn muốn hủy liên kết tài khoản ${providerName} khỏi Workspace này?`,
    );
    if (!confirmUnlink) return;

    setUnlinkingAccountId(accountId);
    setErrorMessage(null);
    setSuccessMessage(null);

    try {
      await workspaceApi.unlinkAccount({
        workspaceId,
        accountId,
      });

      setSuccessMessage(`Đã hủy liên kết tài khoản ${providerName}.`);
      await fetchData();
      onSuccess?.();
    } catch (err: unknown) {
      const errorDto = err as ErrorDto;
      console.error("[AccountLinkDialog] Lỗi hủy liên kết:", errorDto);
      setErrorMessage(errorDto?.message || "Không thể hủy liên kết tài khoản.");
    } finally {
      setUnlinkingAccountId(null);
    }
  };

  // Lọc profile hợp lệ cho provider đang chọn
  const candidateProfiles = profiles.filter(
    (p) => p.kind === selectedProvider || p.kind === "SharedAI",
  );

  return (
    <Dialog
      open={isOpen}
      onOpenChange={(open) => {
        if (!open && !isLinking && !unlinkingAccountId) {
          onClose();
        }
      }}
    >
      <DialogContent className="sm:max-w-[560px]">
        <DialogHeader>
          <DialogTitle className="text-xl font-bold tracking-tight">
            Liên Kết Tài Khoản Mạng Xã Hội & AI
          </DialogTitle>
          <DialogDescription className="text-sm text-muted-foreground">
            Không gian làm việc: <strong className="text-foreground">{workspaceName || workspaceId}</strong>
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4 py-1">
          {errorMessage && (
            <Alert variant="destructive">
              <AlertTriangle className="h-4 w-4" />
              <AlertDescription>{errorMessage}</AlertDescription>
            </Alert>
          )}

          {successMessage && (
            <Alert variant="success">
              <CheckCircle2 className="h-4 w-4" />
              <AlertDescription>{successMessage}</AlertDescription>
            </Alert>
          )}

          {/* Danh sách tài khoản đã liên kết */}
          <div className="space-y-2">
            <h4 className="text-sm font-semibold text-foreground">
              Tài khoản đang liên kết ({accounts.length})
            </h4>

            {isLoading ? (
              <div className="flex items-center justify-center gap-2 rounded-lg border border-border bg-muted/20 p-4 text-sm text-muted-foreground">
                <Loader2 className="h-4 w-4 animate-spin" />
                <span>Đang tải danh sách tài khoản…</span>
              </div>
            ) : accounts.length === 0 ? (
              <div className="rounded-lg border border-dashed border-border bg-muted/20 p-4 text-center text-sm text-muted-foreground">
                Chưa có tài khoản nào được liên kết với Workspace này.
              </div>
            ) : (
              <div className="space-y-2">
                {accounts.map((acc) => {
                  const isConnected = acc.status === "Connected";
                  return (
                    <div
                      key={acc.accountId}
                      className="flex items-center justify-between rounded-lg border border-border bg-card p-3 shadow-xs"
                    >
                      <div className="flex items-center gap-3">
                        <div
                          className={`flex h-8 w-8 items-center justify-center rounded-full text-xs font-bold ${
                            acc.provider === "TikTok"
                              ? "bg-black text-white dark:bg-white dark:text-black"
                              : "bg-primary/15 text-primary"
                          }`}
                        >
                          {acc.provider === "TikTok" ? (
                            <IconTikTok size={14} />
                          ) : (
                            <Sparkles className="h-4 w-4" />
                          )}
                        </div>
                        <div>
                          <div className="text-sm font-semibold text-foreground">
                            {acc.provider} — {acc.accountIdentity || acc.profileId}
                          </div>
                          <div className="text-xs text-muted-foreground">
                            Profile: {acc.profileId}
                          </div>
                        </div>
                      </div>

                      <div className="flex items-center gap-2">
                        <Badge variant={isConnected ? "success" : "secondary"}>
                          {isConnected ? "● Đã kết nối" : `● ${acc.status}`}
                        </Badge>

                        <Button
                          type="button"
                          variant="ghost"
                          size="sm"
                          className="h-8 text-xs text-destructive hover:bg-destructive/10 hover:text-destructive"
                          onClick={() => handleUnlink(acc.accountId, acc.provider)}
                          disabled={unlinkingAccountId === acc.accountId}
                        >
                          {unlinkingAccountId === acc.accountId ? "Đang hủy…" : "Hủy liên kết"}
                        </Button>
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </div>

          <div className="h-px bg-border" />

          {/* Form thêm liên kết mới */}
          <form onSubmit={handleLink} className="space-y-4">
            <h4 className="text-sm font-semibold text-foreground">
              + Thêm liên kết tài khoản mới
            </h4>

            {/* Chọn Provider */}
            <div className="space-y-2">
              <Label className="text-xs text-muted-foreground">Nhà cung cấp / Nền tảng</Label>
              <div className="grid grid-cols-2 gap-3">
                <button
                  type="button"
                  className={`flex flex-col items-start rounded-lg border p-3 text-left transition-all ${
                    selectedProvider === "MuseAI"
                      ? "border-primary bg-primary/10 text-foreground ring-1 ring-primary"
                      : "border-border bg-card text-muted-foreground hover:bg-accent hover:text-foreground"
                  }`}
                  onClick={() => handleProviderChange("MuseAI")}
                >
                  <div className="flex items-center gap-1.5 text-sm font-semibold">
                    <Sparkles className="h-4 w-4 text-primary" />
                    MuseAI Video
                  </div>
                  <p className="mt-1 text-xs text-muted-foreground">
                    Tự động render video từ prompt
                  </p>
                </button>

                <button
                  type="button"
                  className={`flex flex-col items-start rounded-lg border p-3 text-left transition-all ${
                    selectedProvider === "TikTok"
                      ? "border-primary bg-primary/10 text-foreground ring-1 ring-primary"
                      : "border-border bg-card text-muted-foreground hover:bg-accent hover:text-foreground"
                  }`}
                  onClick={() => handleProviderChange("TikTok")}
                >
                  <div className="flex items-center gap-1.5 text-sm font-semibold">
                    <IconTikTok size={14} className="text-foreground" />
                    TikTok Channel
                  </div>
                  <p className="mt-1 text-xs text-muted-foreground">
                    Đăng tải video & phân tích kênh
                  </p>
                </button>
              </div>
            </div>

            {/* Chọn Profile */}
            <div className="space-y-2">
              <Label htmlFor="profile-select" className="text-xs text-muted-foreground">
                Hồ sơ trình duyệt (Browser Profile)
              </Label>
              {candidateProfiles.length === 0 ? (
                <Alert variant="info">
                  <Info className="h-4 w-4" />
                  <AlertDescription className="text-xs">
                    Chưa có hồ sơ trình duyệt nào cho nền tảng <strong>{selectedProvider}</strong>.
                    Vui lòng vào <strong>Cài đặt → Tài khoản</strong> để khởi tạo hồ sơ và đăng nhập tài khoản.
                  </AlertDescription>
                </Alert>
              ) : (
                <select
                  id="profile-select"
                  className="flex h-9 w-full rounded-md border border-input bg-background px-3 py-1 text-sm shadow-xs transition-colors focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50"
                  value={selectedProfileId}
                  onChange={(e) => setSelectedProfileId(e.target.value)}
                  disabled={isLinking}
                >
                  {candidateProfiles.map((p) => (
                    <option key={p.profileId} value={p.profileId}>
                      {p.profileId} — {p.accountIdentity || "Chưa gắn tên"} ({p.sessionStatus})
                    </option>
                  ))}
                </select>
              )}
              <p className="text-xs text-muted-foreground">
                Chọn hồ sơ đã đăng nhập để TiToAI có thể tự động đồng bộ mà không cần lưu mật khẩu.
              </p>
            </div>

            <div className="flex justify-end pt-1">
              <Button
                type="submit"
                disabled={isLinking || !selectedProfileId || candidateProfiles.length === 0}
              >
                {isLinking ? (
                  <>
                    <Loader2 className="h-4 w-4 animate-spin" />
                    Đang liên kết…
                  </>
                ) : (
                  `Xác nhận liên kết ${selectedProvider}`
                )}
              </Button>
            </div>
          </form>
        </div>

        <DialogFooter className="flex flex-col-reverse sm:flex-row sm:items-center sm:justify-between gap-3 pt-2">
          <span className="text-xs text-muted-foreground">
            Hệ thống bảo vệ phiên đăng nhập an toàn qua Playwright
          </span>
          <Button type="button" variant="outline" onClick={onClose}>
            Đóng
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
