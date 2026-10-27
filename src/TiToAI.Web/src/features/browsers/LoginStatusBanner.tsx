import React, { useState } from "react";
import type { BrowserProfileDto } from "./browserManagerApi";
import { browserManagerApi } from "./browserManagerApi";
import { Alert, AlertTitle, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { ExternalLink, AlertTriangle, X } from "lucide-react";

interface LoginStatusBannerProps {
  profiles: BrowserProfileDto[];
  onActionComplete?: () => void;
}

export const LoginStatusBanner: React.FC<LoginStatusBannerProps> = ({
  profiles,
  onActionComplete,
}) => {
  const [processingProfileId, setProcessingProfileId] = useState<string | null>(null);

  // Danh sách các profile đang mở cửa sổ đăng nhập (VisibleActive)
  const activeLoginProfiles = profiles.filter(
    (p) => p.visibilityState === "VisibleActive"
  );

  // Kiểm tra Shared AI có bị hết hạn phiên không
  const sharedAiExpired = profiles.find(
    (p) => p.kind === "SharedAI" && p.sessionStatus === "SessionExpired"
  );

  const handleCancel = async (profileId: string) => {
    setProcessingProfileId(profileId);
    try {
      await browserManagerApi.cancelLogin(profileId);
      onActionComplete?.();
    } catch (err) {
      console.error(`[LoginStatusBanner] Lỗi khi đóng cửa sổ profile ${profileId}:`, err);
    } finally {
      setProcessingProfileId(null);
    }
  };

  const handleReLoginSharedAi = async () => {
    if (!sharedAiExpired) return;
    setProcessingProfileId(sharedAiExpired.profileId);
    try {
      await browserManagerApi.requestLogin(sharedAiExpired.profileId);
      onActionComplete?.();
    } catch (err) {
      console.error("[LoginStatusBanner] Lỗi yêu cầu đăng nhập Shared AI:", err);
    } finally {
      setProcessingProfileId(null);
    }
  };

  const getProfileDisplayName = (profile: BrowserProfileDto): string => {
    switch (profile.kind) {
      case "SharedAI":
        return profile.displayName || "Tài khoản AI (ChatGPT / Gemini)";
      case "MuseAI":
        return `MuseAI (${profile.accountIdentity ?? profile.displayName ?? "Kênh chưa đặt tên"})`;
      case "TikTok":
        return `TikTok (${profile.accountIdentity ?? profile.displayName ?? "Kênh chưa đặt tên"})`;
      default:
        return profile.displayName || profile.profileId;
    }
  };

  // Nếu không có trạng thái đặc biệt nào cần banner, không render
  if (activeLoginProfiles.length === 0 && !sharedAiExpired) {
    return null;
  }

  return (
    <div
      className="flex flex-col gap-3 my-2"
      role="region"
      aria-label="Thông báo trạng thái đăng nhập trình duyệt"
    >
      {/* 1. BANNER KHI CỬA SỔ ĐĂNG NHẬP ĐANG MỞ (VisibleActive) */}
      {activeLoginProfiles.length > 0 && (
        <Alert
          className="border-primary/50 bg-primary/5 p-4 relative flex flex-col justify-between gap-4 shadow-xs"
          role="status"
          aria-live="polite"
        >
          <div className="flex gap-3">
            <div className="h-8 w-8 rounded-full bg-primary/20 text-primary flex items-center justify-center shrink-0 mt-0.5">
              <span className="h-2.5 w-2.5 rounded-full bg-primary animate-ping" aria-hidden="true" />
            </div>
            <div className="space-y-1 flex-1">
              <AlertTitle className="text-sm font-bold text-foreground">
                {activeLoginProfiles.length === 1 ? (
                  <>Đang mở cửa sổ đăng nhập: <span>{getProfileDisplayName(activeLoginProfiles[0])}</span></>
                ) : (
                  <>Đang mở {activeLoginProfiles.length} cửa sổ đăng nhập đồng thời</>
                )}
              </AlertTitle>
              <AlertDescription className="text-xs text-muted-foreground leading-relaxed">
                Cửa sổ trình duyệt đang hiển thị để bạn thao tác. Vui lòng tự nhập tài khoản, mật khẩu và mã xác minh (OTP/MFA) trực tiếp trên trang web. TiToAI <strong className="text-foreground">tuyệt đối không can thiệp, không đọc hoặc lưu trữ mật khẩu</strong> của bạn.
              </AlertDescription>
              <p className="text-[11px] text-muted-foreground/80 mt-1">
                Sau khi bạn đăng nhập thành công và đóng cửa sổ, trình duyệt sẽ tự động chuyển về trạng thái <strong className="text-foreground">chạy ẩn</strong> để tiếp tục phục vụ các tác vụ AI.
              </p>
            </div>
            {activeLoginProfiles.length === 1 && (
              <div className="shrink-0">
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => handleCancel(activeLoginProfiles[0].profileId)}
                  disabled={processingProfileId === activeLoginProfiles[0].profileId}
                  className="gap-1.5"
                >
                  <X className="h-3.5 w-3.5" />
                  Hủy / Đóng cửa sổ
                </Button>
              </div>
            )}
          </div>

          {activeLoginProfiles.length > 1 && (
            <div className="pt-2 border-t border-border/40">
              <ul className="space-y-1.5">
                {activeLoginProfiles.map((p) => (
                  <li
                    key={p.profileId}
                    className="flex items-center justify-between gap-2 p-2 rounded-md bg-background/60 border border-border/40 text-xs"
                  >
                    <span className="font-medium text-foreground">{getProfileDisplayName(p)}</span>
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={() => handleCancel(p.profileId)}
                      disabled={processingProfileId === p.profileId}
                      className="h-7 text-xs gap-1"
                    >
                      <X className="h-3 w-3" />
                      Đóng cửa sổ
                    </Button>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </Alert>
      )}

      {/* 2. CẢNH BÁO KHI PHIÊN SHARED AI BỊ HẾT HẠN */}
      {sharedAiExpired && activeLoginProfiles.length === 0 && (
        <Alert
          variant="destructive"
          className="p-4 flex flex-col md:flex-row md:items-start justify-between gap-4"
          role="alert"
          aria-live="assertive"
        >
          <div className="flex gap-3">
            <AlertTriangle className="h-4 w-4 shrink-0 mt-0.5" />
            <div className="space-y-1">
              <AlertTitle className="text-sm font-bold">
                Phiên làm việc của AI ({getProfileDisplayName(sharedAiExpired)}) đã hết hạn
              </AlertTitle>
              <AlertDescription className="text-xs leading-relaxed">
                Vì đây là profile điều phối hệ thống, toàn bộ tác vụ tạo kịch bản, ảnh và video prompt sẽ tạm dừng cho đến khi phiên được kết nối lại. Các tác vụ đã gửi thành công trước đó vẫn được bảo toàn.
              </AlertDescription>
            </div>
          </div>
          <div className="shrink-0">
            <Button
              type="button"
              variant="default"
              size="sm"
              onClick={handleReLoginSharedAi}
              disabled={processingProfileId === sharedAiExpired.profileId}
              className="gap-1.5"
            >
              <ExternalLink className="h-3.5 w-3.5" />
              Đăng nhập lại ngay
            </Button>
          </div>
        </Alert>
      )}
    </div>
  );
};
