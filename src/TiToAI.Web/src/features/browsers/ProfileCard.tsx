import React from "react";
import type {
  BrowserProfileDto,
  OperationalState,
  SessionStatus,
  VisibilityState,
} from "./browserManagerApi";
import { browserManagerApi } from "./browserManagerApi";

interface ProfileCardProps {
  profile: BrowserProfileDto;
  workspaceName?: string | null;
  onActionComplete?: () => void;
}

export const ProfileCard: React.FC<ProfileCardProps> = ({
  profile,
  workspaceName,
  onActionComplete,
}) => {
  const [isRequestingLogin, setIsRequestingLogin] = React.useState(false);
  const [isCheckingSession, setIsCheckingSession] = React.useState(false);
  const [isCanceling, setIsCanceling] = React.useState(false);
  const [actionMessage, setActionMessage] = React.useState<string | null>(null);

  // 1. Phân loại cấu hình tiêu đề và icon theo kind
  const renderProviderHeader = () => {
    switch (profile.kind) {
      case "SharedAI":
        return {
          title: profile.displayName || (profile.providerKey === "chatgpt-web" ? "ChatGPT Web" : profile.providerKey === "gemini-web" ? "Gemini Web" : "Tài khoản AI"),
          subtitle: profile.accountIdentity || "Chưa xác minh danh tính",
          badgeScope: "Toàn ứng dụng (Tất cả Workspace)",
          icon: (
            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <path d="M12 2a10 10 0 0 1 10 10c0 5.523-4.477 10-10 10S2 17.523 2 12 6.477 2 12 2Z" />
              <path d="m9 12 2 2 4-4" />
            </svg>
          ),
        };
      case "MuseAI":
        return {
          title: profile.displayName || "MuseAI",
          subtitle: profile.accountIdentity ? `Tài khoản: ${profile.accountIdentity}` : "Chưa liên kết tài khoản",
          badgeScope: "Dùng chung toàn ứng dụng",
          icon: (
            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <rect x="2" y="2" width="20" height="20" rx="5" ry="5" />
              <path d="M16 11.37A4 4 0 1 1 12.63 8 4 4 0 0 1 16 11.37z" />
              <line x1="17.5" y1="6.5" x2="17.51" y2="6.5" />
            </svg>
          ),
        };
      case "TikTok":
        return {
          title: "TikTok Creator",
          subtitle: profile.accountIdentity ? `Kênh: ${profile.accountIdentity}` : "Chưa liên kết kênh",
          badgeScope: workspaceName ? `Workspace: ${workspaceName}` : "Kênh TikTok",
          icon: (
            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <path d="M9 12a4 4 0 1 0 4 4V4a5 5 0 0 0 5 5" />
            </svg>
          ),
        };
    }
  };

  const headerInfo = renderProviderHeader();

  // 2. Chiều 1: Tiến trình (Operational State)
  const getOperationalBadge = (state: OperationalState) => {
    switch (state) {
      case "Running":
        return (
          <span className="badge badge-op badge-running" title="Tiến trình browser đang chạy nền">
            <span className="dot dot-pulse" aria-hidden="true" />
            Đang chạy ẩn
          </span>
        );
      case "Idle":
        return (
          <span className="badge badge-op badge-idle" title="Tiến trình sẵn sàng nhận tác vụ">
            <span className="dot" aria-hidden="true" />
            Chờ việc
          </span>
        );
      case "Stopped":
        return (
          <span className="badge badge-op badge-stopped" title="Trình duyệt chưa khởi động hoặc đã đóng">
            <span className="dot" aria-hidden="true" />
            Đã dừng
          </span>
        );
      case "Quiescing":
        return (
          <span className="badge badge-op badge-quiescing" title="Đang thu hồi tài nguyên an toàn">
            <span className="dot" aria-hidden="true" />
            Đang thu hồi
          </span>
        );
      case "Error":
        return (
          <span className="badge badge-op badge-error" title="Gặp sự cố tiến trình">
            <span className="dot" aria-hidden="true" />
            Lỗi tiến trình
          </span>
        );
      default:
        return <span className="badge badge-op badge-stopped">Đã dừng</span>;
    }
  };

  // 3. Chiều 2: Hiển thị (Visibility State)
  const getVisibilityBadge = (state: VisibilityState) => {
    switch (state) {
      case "Hidden":
        return (
          <span className="badge badge-vis badge-hidden" title="Chạy hoàn toàn ẩn trên desktop">
            Chạy ẩn
          </span>
        );
      case "VisibleActive":
        return (
          <span className="badge badge-vis badge-active" title="Cửa sổ trình duyệt đang mở để đăng nhập">
            Đang mở cửa sổ
          </span>
        );
      case "VisibleWaiting":
        return (
          <span className="badge badge-vis badge-waiting" title="Đang xếp hàng chờ đến lượt mở cửa sổ">
            Đang xếp hàng chờ
          </span>
        );
      default:
        return <span className="badge badge-vis badge-hidden">Chạy ẩn</span>;
    }
  };

  // 4. Chiều 3: Phiên (Session Status)
  const getSessionBadge = (status: SessionStatus) => {
    switch (status) {
      case "Connected":
        return (
          <span className="badge badge-sess badge-connected" title="Phiên đăng nhập hợp lệ và sẵn sàng">
            Đã kết nối
          </span>
        );
      case "SessionExpired":
        return (
          <span className="badge badge-sess badge-expired" title="Phiên đăng nhập đã hết hạn hoặc bị hủy">
            Hết phiên
          </span>
        );
      case "Unverified":
        return (
          <span className="badge badge-sess badge-unverified" title="Chưa kiểm tra hoặc chưa xác minh phiên">
            Chưa xác minh
          </span>
        );
      default:
        return <span className="badge badge-sess badge-unverified">Chưa xác minh</span>;
    }
  };

  // Các actions
  const handleRequestLogin = async () => {
    setIsRequestingLogin(true);
    setActionMessage(null);
    try {
      const res = await browserManagerApi.requestLogin(profile.profileId);
      if (res.queued) {
        setActionMessage("Đang xếp hàng chờ lượt đăng nhập...");
      } else {
        setActionMessage("Cửa sổ đăng nhập đã được mở.");
      }
      onActionComplete?.();
    } catch (err) {
      setActionMessage(`Lỗi đăng nhập: ${err instanceof Error ? err.message : String(err)}`);
    } finally {
      setIsRequestingLogin(false);
    }
  };

  const handleCancelLogin = async () => {
    setIsCanceling(true);
    setActionMessage(null);
    try {
      await browserManagerApi.cancelLogin(profile.profileId);
      setActionMessage("Đã hủy chờ đăng nhập.");
      onActionComplete?.();
    } catch (err) {
      setActionMessage(`Lỗi hủy chờ: ${err instanceof Error ? err.message : String(err)}`);
    } finally {
      setIsCanceling(false);
    }
  };

  const handleCheckSession = async () => {
    setIsCheckingSession(true);
    setActionMessage(null);
    try {
      const res = await browserManagerApi.checkSession(profile.profileId);
      setActionMessage(`Kiểm tra phiên: ${res.sessionStatus === "Connected" ? "Hợp lệ" : "Hết hạn"}`);
      onActionComplete?.();
    } catch (err) {
      setActionMessage(`Lỗi kiểm tra phiên: ${err instanceof Error ? err.message : String(err)}`);
    } finally {
      setIsCheckingSession(false);
    }
  };

  const isBusy = isRequestingLogin || isCheckingSession || isCanceling;
  const isWaiting = profile.visibilityState === "VisibleWaiting";
  const isActive = profile.visibilityState === "VisibleActive";

  return (
    <article
      className={`browser-profile-card ${profile.kind.toLowerCase()}-card ${
        isActive ? "is-active-window" : ""
      } ${isWaiting ? "is-waiting-queue" : ""}`}
      aria-label={`Trình duyệt ${headerInfo.title}`}
    >
      {/* HEADER CARD */}
      <div className="card-top">
        <div className="provider-icon-wrapper" aria-hidden="true">
          {headerInfo.icon}
        </div>
        <div className="provider-info">
          <div className="card-title-row">
            <h3 className="provider-name">{headerInfo.title}</h3>
            <span className="scope-tag">{headerInfo.badgeScope}</span>
          </div>
          <div className="provider-subtitle">{headerInfo.subtitle}</div>
        </div>
      </div>

      {/* 3 CHIỀU TRẠNG THÁI MINH BẠCH */}
      <div className="card-state-section">
        <div className="state-dimension">
          <span className="dim-label">Tiến trình:</span>
          {getOperationalBadge(profile.operationalState)}
        </div>
        <div className="state-dimension">
          <span className="dim-label">Hiển thị:</span>
          {getVisibilityBadge(profile.visibilityState)}
        </div>
        <div className="state-dimension">
          <span className="dim-label">Phiên đăng nhập:</span>
          {getSessionBadge(profile.sessionStatus)}
        </div>
      </div>

      {/* THÔNG TIN CHI TIẾT (SHARED AI ACTIVE TABS & JOBS) */}
      {profile.kind === "SharedAI" && (
        <div className="shared-ai-details">
          <div className="tabs-header">
            <span className="detail-label">Vai trò tabs đang mở:</span>
            <span className="active-jobs-badge">
              {profile.activeJobCount > 0
                ? `${profile.activeJobCount} tác vụ đang chạy`
                : "0 tác vụ đang chạy"}
            </span>
          </div>
          <div className="tabs-list">
            {profile.activeTabs && profile.activeTabs.length > 0 ? (
              profile.activeTabs.map((tab) => (
                <span key={tab} className="tab-pill">
                  {tab === "Script" ? "Kịch bản" : tab === "Image" ? "Sinh ảnh" : tab === "VideoPrompt" ? "Prompt Video" : tab}
                </span>
              ))
            ) : (
              <span className="text-muted-sm">Chưa có tab hoạt động</span>
            )}
          </div>
          <div className="security-notice">
            Khép kín: Quản lý ẩn danh, không lộ thông tin phiên hoặc đường dẫn thư mục lưu trữ.
          </div>
        </div>
      )}

      {/* BANNER HÀNG ĐỢI NỘI BỘ CARD KHI ĐANG VISIBLE WAITING */}
      {isWaiting && (
        <div className="card-queue-alert" role="status" aria-live="polite">
          <div className="alert-text">
            <strong>Đang xếp hàng chờ:</strong> Đang có cửa sổ khác mở. Cửa sổ này sẽ tự động xuất hiện khi lượt trước kết thúc.
          </div>
          <button
            type="button"
            className="btn btn-secondary btn-sm"
            onClick={handleCancelLogin}
            disabled={isBusy}
          >
            Hủy chờ
          </button>
        </div>
      )}

      {/* TIN NHẮN PHẢN HỒI THAO TÁC */}
      {actionMessage && (
        <div className="card-action-msg" role="status">
          {actionMessage}
        </div>
      )}

      {/* NÚT THAO TÁC CTA NGỮ CẢNH */}
      <div className="card-actions">
        {isActive ? (
          <div className="active-window-indicator">
            <span className="pulse-indicator" aria-hidden="true" />
            <span>Cửa sổ đăng nhập đang mở...</span>
          </div>
        ) : isWaiting ? (
          <button
            type="button"
            className="btn btn-secondary btn-sm"
            onClick={handleCancelLogin}
            disabled={isBusy}
          >
            {isCanceling ? "Đang hủy..." : "Hủy chờ đăng nhập"}
          </button>
        ) : profile.sessionStatus === "Connected" ? (
          <>
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={handleCheckSession}
              disabled={isBusy}
              title="Kiểm tra lại tính hợp lệ của phiên làm việc"
            >
              {isCheckingSession ? "Đang kiểm tra..." : "Kiểm tra phiên"}
            </button>
            <button
              type="button"
              className="btn btn-secondary btn-sm btn-relogin"
              onClick={handleRequestLogin}
              disabled={isBusy}
              title="Mở lại cửa sổ để đăng nhập lại hoặc đổi tài khoản"
            >
              {isRequestingLogin ? "Đang gửi..." : "Đăng nhập lại"}
            </button>
          </>
        ) : profile.sessionStatus === "SessionExpired" ? (
          <>
            <button
              type="button"
              className="btn btn-primary btn-sm"
              onClick={handleRequestLogin}
              disabled={isBusy}
              title="Phiên đã hết hạn, nhấn để mở cửa sổ đăng nhập"
            >
              {isRequestingLogin ? "Đang gửi..." : "Đăng nhập lại"}
            </button>
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={handleCheckSession}
              disabled={isBusy}
            >
              {isCheckingSession ? "Đang kiểm tra..." : "Kiểm tra phiên"}
            </button>
          </>
        ) : (
          /* Unverified */
          <>
            <button
              type="button"
              className="btn btn-primary btn-sm"
              onClick={handleRequestLogin}
              disabled={isBusy}
              title="Mở cửa sổ trình duyệt để đăng nhập tài khoản lần đầu"
            >
              {isRequestingLogin ? "Đang gửi..." : "Đăng nhập"}
            </button>
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={handleCheckSession}
              disabled={isBusy}
            >
              {isCheckingSession ? "Đang kiểm tra..." : "Kiểm tra phiên"}
            </button>
          </>
        )}
      </div>
    </article>
  );
};
