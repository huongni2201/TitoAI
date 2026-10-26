import React from "react";
import type { BrowserProfileDto } from "./browserManagerApi";
import { browserManagerApi } from "./browserManagerApi";

interface LoginStatusBannerProps {
  profiles: BrowserProfileDto[];
  onActionComplete?: () => void;
}

export const LoginStatusBanner: React.FC<LoginStatusBannerProps> = ({
  profiles,
  onActionComplete,
}) => {
  const [isProcessing, setIsProcessing] = React.useState(false);

  // Tìm profile đang mở cửa sổ đăng nhập (VisibleActive)
  const activeLoginProfile = profiles.find(
    (p) => p.visibilityState === "VisibleActive",
  );

  // Danh sách các profile đang xếp hàng chờ (VisibleWaiting)
  const waitingProfiles = profiles.filter(
    (p) => p.visibilityState === "VisibleWaiting",
  );

  // Kiểm tra Shared AI có bị hết hạn phiên không
  const sharedAiExpired = profiles.find(
    (p) => p.kind === "SharedAI" && p.sessionStatus === "SessionExpired",
  );

  const handleCancelActive = async () => {
    if (!activeLoginProfile) return;
    setIsProcessing(true);
    try {
      await browserManagerApi.cancelLogin(activeLoginProfile.profileId);
      onActionComplete?.();
    } catch (err) {
      console.error("[LoginStatusBanner] Lỗi khi hủy đăng nhập:", err);
    } finally {
      setIsProcessing(false);
    }
  };

  const handleCancelWaiting = async (profileId: string) => {
    setIsProcessing(true);
    try {
      await browserManagerApi.cancelLogin(profileId);
      onActionComplete?.();
    } catch (err) {
      console.error("[LoginStatusBanner] Lỗi khi hủy hàng chờ:", err);
    } finally {
      setIsProcessing(false);
    }
  };

  const handleReLoginSharedAi = async () => {
    if (!sharedAiExpired) return;
    setIsProcessing(true);
    try {
      await browserManagerApi.requestLogin(sharedAiExpired.profileId);
      onActionComplete?.();
    } catch (err) {
      console.error("[LoginStatusBanner] Lỗi yêu cầu đăng nhập Shared AI:", err);
    } finally {
      setIsProcessing(false);
    }
  };

  const getProfileDisplayName = (profile: BrowserProfileDto): string => {
    switch (profile.kind) {
      case "SharedAI":
        return "AI dùng chung (ChatGPT / Gemini)";
      case "MuseAI":
        return `MuseAI (${profile.accountIdentity ?? "Kênh chưa đặt tên"})`;
      case "TikTok":
        return `TikTok (${profile.accountIdentity ?? "Kênh chưa đặt tên"})`;
      default:
        return profile.profileId;
    }
  };

  // Nếu không có trạng thái đặc biệt nào cần banner, không render
  if (!activeLoginProfile && waitingProfiles.length === 0 && !sharedAiExpired) {
    return null;
  }

  return (
    <div className="login-status-banner-container" role="region" aria-label="Thông báo trạng thái đăng nhập trình duyệt">
      {/* 1. BANNER KHI CỬA SỔ ĐĂNG NHẬP ĐANG MỞ (VisibleActive) */}
      {activeLoginProfile && (
        <div
          className="login-banner banner-active"
          role="status"
          aria-live="polite"
        >
          <div className="banner-icon-col">
            <span className="pulse-indicator" aria-hidden="true" />
          </div>
          <div className="banner-content-col">
            <div className="banner-title">
              Đang mở cửa sổ đăng nhập: <strong>{getProfileDisplayName(activeLoginProfile)}</strong>
            </div>
            <p className="banner-desc">
              Cửa sổ trình duyệt đang hiển thị để bạn thao tác. Vui lòng tự nhập tài khoản, mật khẩu và mã xác minh (OTP/MFA) trực tiếp trên trang web. TiToAI <strong>tuyệt đối không can thiệp, không đọc hoặc lưu trữ mật khẩu</strong> của bạn.
            </p>
            <div className="banner-notes">
              Sau khi bạn đăng nhập thành công và đóng cửa sổ, trình duyệt sẽ tự động chuyển về trạng thái <strong>chạy ẩn</strong> để tiếp tục phục vụ các tác vụ AI.
            </div>
          </div>
          <div className="banner-actions-col">
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={handleCancelActive}
              disabled={isProcessing}
              title="Đóng cửa sổ hoặc hủy phiên đăng nhập này"
            >
              Hủy / Đóng cửa sổ
            </button>
          </div>
        </div>
      )}

      {/* 2. BANNER KHI CÓ PROFILE ĐANG XẾP HÀNG CHỜ (VisibleWaiting) */}
      {waitingProfiles.length > 0 && (
        <div
          className="login-banner banner-waiting"
          role="status"
          aria-live="polite"
        >
          <div className="banner-icon-col">
            <span className="queue-clock-icon" aria-hidden="true">⏳</span>
          </div>
          <div className="banner-content-col">
            <div className="banner-title">
              Đang xếp hàng chờ lượt đăng nhập ({waitingProfiles.length} trình duyệt)
            </div>
            <p className="banner-desc">
              Ứng dụng tuân thủ quy tắc <strong>chỉ mở 1 cửa sổ đăng nhập tại một thời điểm</strong> để tránh xung đột cửa sổ và bảo vệ luồng xác thực tài khoản. Các trình duyệt dưới đây sẽ lần lượt được mở khi lượt trước kết thúc:
            </p>
            <ul className="waiting-list">
              {waitingProfiles.map((p) => (
                <li key={p.profileId} className="waiting-item">
                  <span className="waiting-name">{getProfileDisplayName(p)}</span>
                  <button
                    type="button"
                    className="btn-link-cancel"
                    onClick={() => handleCancelWaiting(p.profileId)}
                    disabled={isProcessing}
                  >
                    Hủy chờ
                  </button>
                </li>
              ))}
            </ul>
          </div>
        </div>
      )}

      {/* 3. CẢNH BÁO KHI PHIÊN SHARED AI BỊ HẾT HẠN */}
      {sharedAiExpired && !activeLoginProfile && (
        <div
          className="login-banner banner-warning"
          role="alert"
          aria-live="assertive"
        >
          <div className="banner-icon-col">
            <span className="warning-icon" aria-hidden="true">⚠️</span>
          </div>
          <div className="banner-content-col">
            <div className="banner-title">
              Phiên làm việc của AI dùng chung (ChatGPT / Gemini) đã hết hạn
            </div>
            <p className="banner-desc">
              Vì đây là profile dùng chung toàn hệ thống, toàn bộ tác vụ tạo kịch bản, ảnh và video prompt sẽ tạm dừng cho đến khi phiên được kết nối lại. Các tác vụ đã gửi thành công trước đó vẫn được bảo toàn.
            </p>
          </div>
          <div className="banner-actions-col">
            <button
              type="button"
              className="btn btn-primary btn-sm"
              onClick={handleReLoginSharedAi}
              disabled={isProcessing}
            >
              Đăng nhập lại ngay
            </button>
          </div>
        </div>
      )}
    </div>
  );
};
