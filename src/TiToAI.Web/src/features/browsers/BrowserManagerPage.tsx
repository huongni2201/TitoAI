import React, { useCallback, useEffect, useState } from "react";
import type { WorkspaceSummaryDto } from "../../contracts/dtos";
import type { BrowserProfileDto } from "./browserManagerApi";
import { browserManagerApi } from "./browserManagerApi";
import { LoginStatusBanner } from "./LoginStatusBanner";
import { ProfileCard } from "./ProfileCard";

interface BrowserManagerPageProps {
  currentWorkspaceId?: string | null;
  workspaces?: WorkspaceSummaryDto[];
}

export const BrowserManagerPage: React.FC<BrowserManagerPageProps> = ({
  currentWorkspaceId = null,
  workspaces = [],
}) => {
  const [profiles, setProfiles] = useState<BrowserProfileDto[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [filterScope, setFilterScope] = useState<"all" | "workspace">("all");

  const currentWorkspace = workspaces.find(
    (w) => w.workspaceId === currentWorkspaceId,
  );

  const fetchProfiles = useCallback(async () => {
    setIsLoading(true);
    setErrorMessage(null);
    try {
      const scopeId = filterScope === "workspace" ? currentWorkspaceId : null;
      const data = await browserManagerApi.listProfiles(scopeId);
      setProfiles(data);
    } catch (err) {
      console.error("[BrowserManagerPage] Lỗi tải profiles:", err);
      setErrorMessage(
        err instanceof Error
          ? err.message
          : "Không thể kết nối đến dịch vụ quản lý trình duyệt của Host.",
      );
    } finally {
      setIsLoading(false);
    }
  }, [filterScope, currentWorkspaceId]);

  // Tải danh sách khi mount hoặc khi scope / currentWorkspaceId thay đổi
  useEffect(() => {
    fetchProfiles();
  }, [fetchProfiles]);

  // Lắng nghe sự kiện browser.profile_state_changed từ WebView2 Host / Mock
  useEffect(() => {
    const unsubscribe = browserManagerApi.onProfileStateChanged((event) => {
      const updated = event.payload;
      if (!updated || !updated.profileId) return;

      setProfiles((prev) =>
        prev.map((item) => {
          if (item.profileId === updated.profileId) {
            return {
              ...item,
              visibilityState: updated.visibilityState,
              sessionStatus: updated.sessionStatus,
              operationalState: updated.operationalState,
            };
          }
          return item;
        }),
      );
    });

    return () => {
      unsubscribe();
    };
  }, []);

  // Phân chia profiles theo 3 nhóm chuẩn
  const sharedAiProfiles = profiles.filter((p) => p.kind === "SharedAI");
  const museAiProfiles = profiles.filter((p) => p.kind === "MuseAI");
  const tikTokProfiles = profiles.filter((p) => p.kind === "TikTok");

  const getWorkspaceName = (wsId: string | null): string | null => {
    if (!wsId) return null;
    const found = workspaces.find((w) => w.workspaceId === wsId);
    return found ? found.name : wsId;
  };

  return (
    <div className="browser-manager-view" role="main" aria-label="Màn hình Quản lý Trình duyệt AI">
      {/* SCOPED CSS STYLES TUÂN THỦ UI_THEME_PLAN & UI_BROWSER_PLAN */}
      <style>{`
        .browser-manager-view {
          display: flex;
          flex-direction: column;
          gap: 1.25rem;
          padding-bottom: 2.5rem;
        }

        .browser-groups-container {
          display: flex;
          flex-direction: column;
          gap: 1.75rem;
        }

        .browser-group-section {
          background-color: var(--surface-card);
          border: 1px solid var(--border-subtle);
          border-radius: var(--radius-lg);
          padding: 1.25rem 1.5rem;
        }

        .group-header {
          display: flex;
          justify-content: space-between;
          align-items: flex-start;
          border-bottom: 1px solid var(--border-subtle);
          padding-bottom: 0.85rem;
          margin-bottom: 1.15rem;
        }

        .group-title-col {
          display: flex;
          flex-direction: column;
          gap: 0.25rem;
        }

        .group-title {
          margin: 0;
          font-size: 1.05rem;
          font-weight: 700;
          color: var(--text-primary);
        }

        .group-desc {
          margin: 0;
          font-size: 0.825rem;
          color: var(--text-secondary);
          line-height: 1.45;
        }

        .group-count {
          font-size: 0.75rem;
          font-weight: 600;
          padding: 0.2rem 0.6rem;
          border-radius: 999px;
          background-color: var(--surface-hover);
          color: var(--text-secondary);
          white-space: nowrap;
        }

        .profile-grid {
          display: grid;
          grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
          gap: 1.15rem;
        }

        .empty-subgroup {
          padding: 1.5rem;
          text-align: center;
          color: var(--text-muted);
          font-size: 0.85rem;
          border: 1px dashed var(--border-subtle);
          border-radius: var(--radius-md);
        }

        /* CARD STYLING */
        .browser-profile-card {
          background-color: var(--surface-card-subtle);
          border: 1px solid var(--border);
          border-radius: var(--radius-lg);
          padding: 1.15rem;
          display: flex;
          flex-direction: column;
          transition: border-color var(--transition-fast), box-shadow var(--transition-fast);
        }

        .browser-profile-card:hover {
          border-color: var(--border);
        }

        .browser-profile-card.is-active-window {
          border-color: var(--accent);
          box-shadow: 0 0 0 1px var(--accent), 0 4px 14px var(--accent-subtle);
        }

        .browser-profile-card.is-waiting-queue {
          border-color: var(--status-warning);
        }

        .card-top {
          display: flex;
          align-items: center;
          gap: 0.85rem;
          margin-bottom: 0.85rem;
        }

        .provider-icon-wrapper {
          width: 42px;
          height: 42px;
          border-radius: var(--radius-md);
          background-color: var(--surface-hover);
          color: var(--accent);
          display: flex;
          align-items: center;
          justify-content: center;
          flex-shrink: 0;
        }

        .provider-info {
          display: flex;
          flex-direction: column;
          min-width: 0;
          flex: 1;
        }

        .card-title-row {
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: 0.5rem;
        }

        .provider-name {
          margin: 0;
          font-size: 0.95rem;
          font-weight: 700;
          color: var(--text-primary);
          white-space: nowrap;
          overflow: hidden;
          text-overflow: ellipsis;
        }

        .scope-tag {
          font-size: 0.68rem;
          padding: 0.1rem 0.45rem;
          border-radius: 4px;
          background-color: var(--surface-hover);
          color: var(--text-muted);
          white-space: nowrap;
        }

        .provider-subtitle {
          font-size: 0.775rem;
          color: var(--text-secondary);
          margin-top: 0.15rem;
          white-space: nowrap;
          overflow: hidden;
          text-overflow: ellipsis;
        }

        /* 3 CHIỀU TRẠNG THÁI */
        .card-state-section {
          display: flex;
          flex-direction: column;
          gap: 0.45rem;
          background-color: var(--surface-card);
          border: 1px solid var(--border-subtle);
          border-radius: var(--radius-md);
          padding: 0.65rem 0.85rem;
          margin-bottom: 0.85rem;
        }

        .state-dimension {
          display: flex;
          align-items: center;
          justify-content: space-between;
          font-size: 0.775rem;
        }

        .dim-label {
          color: var(--text-muted);
          font-weight: 500;
        }

        /* BADGES */
        .badge-op,
        .badge-vis,
        .badge-sess {
          font-size: 0.725rem;
          font-weight: 600;
          padding: 0.15rem 0.5rem;
          border-radius: 4px;
          display: inline-flex;
          align-items: center;
          gap: 0.35rem;
          line-height: 1.2;
        }

        .badge-running {
          background-color: rgba(34, 197, 94, 0.15);
          color: var(--status-success);
        }

        .badge-idle {
          background-color: rgba(56, 189, 248, 0.15);
          color: var(--status-info);
        }

        .badge-stopped {
          background-color: var(--surface-hover);
          color: var(--text-muted);
        }

        .badge-quiescing {
          background-color: rgba(245, 158, 11, 0.15);
          color: var(--status-warning);
        }

        .badge-error {
          background-color: rgba(239, 68, 68, 0.15);
          color: var(--status-error);
        }

        .badge-hidden {
          background-color: var(--surface-hover);
          color: var(--text-secondary);
        }

        .badge-active {
          background-color: var(--accent-subtle);
          color: var(--accent);
          border: 1px solid var(--accent);
        }

        .badge-waiting {
          background-color: rgba(245, 158, 11, 0.15);
          color: var(--status-warning);
        }

        .badge-connected {
          background-color: rgba(34, 197, 94, 0.15);
          color: var(--status-success);
        }

        .badge-expired {
          background-color: rgba(245, 158, 11, 0.15);
          color: var(--status-warning);
        }

        .badge-unverified {
          background-color: var(--surface-hover);
          color: var(--text-muted);
        }

        .dot {
          width: 6px;
          height: 6px;
          border-radius: 50%;
          background-color: currentColor;
        }

        .dot-pulse {
          animation: dotPulsing 1.4s infinite ease-in-out;
        }

        @keyframes dotPulsing {
          0% { opacity: 0.4; transform: scale(0.85); }
          50% { opacity: 1; transform: scale(1.15); }
          100% { opacity: 0.4; transform: scale(0.85); }
        }

        /* SHARED AI ACTIVE TABS */
        .shared-ai-details {
          padding: 0.65rem 0.85rem;
          background-color: var(--surface-card);
          border: 1px solid var(--border-subtle);
          border-radius: var(--radius-md);
          margin-bottom: 0.85rem;
        }

        .tabs-header {
          display: flex;
          align-items: center;
          justify-content: space-between;
          font-size: 0.75rem;
          margin-bottom: 0.45rem;
        }

        .detail-label {
          color: var(--text-muted);
          font-weight: 500;
        }

        .active-jobs-badge {
          color: var(--accent);
          font-weight: 600;
        }

        .tabs-list {
          display: flex;
          gap: 0.4rem;
          flex-wrap: wrap;
        }

        .tab-pill {
          font-size: 0.7rem;
          padding: 0.125rem 0.5rem;
          border-radius: 4px;
          background-color: var(--surface-hover);
          color: var(--text-primary);
          border: 1px solid var(--border-subtle);
        }

        .security-notice {
          font-size: 0.68rem;
          color: var(--text-muted);
          font-style: italic;
          margin-top: 0.5rem;
          line-height: 1.35;
        }

        .text-muted-sm {
          font-size: 0.725rem;
          color: var(--text-muted);
        }

        /* BANNER CẢNH BÁO / HÀNG ĐỢI */
        .card-queue-alert {
          background-color: rgba(245, 158, 11, 0.12);
          border: 1px solid var(--status-warning);
          border-radius: var(--radius-md);
          padding: 0.65rem 0.85rem;
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: 0.65rem;
          margin-bottom: 0.85rem;
          font-size: 0.75rem;
          color: var(--text-primary);
        }

        .card-action-msg {
          font-size: 0.725rem;
          color: var(--text-secondary);
          margin-bottom: 0.65rem;
          font-style: italic;
        }

        .card-actions {
          display: flex;
          gap: 0.5rem;
          margin-top: auto;
          justify-content: flex-end;
          align-items: center;
        }

        .btn-relogin {
          font-weight: 500;
        }

        .active-window-indicator {
          display: flex;
          align-items: center;
          gap: 0.5rem;
          font-size: 0.775rem;
          color: var(--accent);
          font-weight: 600;
        }

        /* LOGIN STATUS BANNER CONTAINER */
        .login-status-banner-container {
          display: flex;
          flex-direction: column;
          gap: 0.85rem;
          margin: 0.5rem 0 1rem 0;
        }

        .login-banner {
          display: flex;
          gap: 1rem;
          padding: 1.15rem 1.35rem;
          border-radius: var(--radius-lg);
          align-items: flex-start;
        }

        .banner-active {
          background-color: var(--accent-subtle);
          border: 1px solid var(--accent);
        }

        .banner-waiting {
          background-color: rgba(245, 158, 11, 0.12);
          border: 1px solid var(--status-warning);
        }

        .banner-warning {
          background-color: rgba(239, 68, 68, 0.12);
          border: 1px solid var(--status-error);
        }

        .banner-icon-col {
          display: flex;
          align-items: center;
          justify-content: center;
          padding-top: 0.2rem;
          font-size: 1.25rem;
        }

        .pulse-indicator {
          display: inline-block;
          width: 12px;
          height: 12px;
          border-radius: 50%;
          background-color: var(--accent);
          box-shadow: 0 0 0 0 rgba(109, 58, 242, 0.7);
          animation: pulseRing 1.6s infinite cubic-bezier(0.66, 0, 0, 1);
        }

        @keyframes pulseRing {
          to {
            box-shadow: 0 0 0 10px rgba(109, 58, 242, 0);
          }
        }

        .banner-content-col {
          flex: 1;
          display: flex;
          flex-direction: column;
          gap: 0.35rem;
        }

        .banner-title {
          font-size: 0.95rem;
          font-weight: 700;
          color: var(--text-primary);
        }

        .banner-desc {
          margin: 0;
          font-size: 0.825rem;
          color: var(--text-secondary);
          line-height: 1.45;
        }

        .banner-notes {
          font-size: 0.775rem;
          color: var(--text-muted);
          margin-top: 0.25rem;
        }

        .banner-actions-col {
          display: flex;
          flex-direction: column;
          gap: 0.5rem;
          align-items: flex-end;
          flex-shrink: 0;
        }

        .waiting-list {
          margin: 0.45rem 0 0 0;
          padding-left: 1.2rem;
          font-size: 0.8rem;
          display: flex;
          flex-direction: column;
          gap: 0.3rem;
        }

        .waiting-item {
          display: flex;
          align-items: center;
          gap: 0.75rem;
        }

        .waiting-name {
          color: var(--text-primary);
          font-weight: 500;
        }

        .btn-link-cancel {
          background: none;
          border: none;
          color: var(--status-warning);
          text-decoration: underline;
          cursor: pointer;
          font-size: 0.75rem;
          padding: 0;
        }

        .btn-link-cancel:hover {
          color: var(--text-primary);
        }

        /* DEV MOCK HELPERS */
        .dev-mock-helpers {
          display: flex;
          align-items: center;
          gap: 0.35rem;
          margin-top: 0.5rem;
          padding-top: 0.35rem;
          border-top: 1px dashed var(--border-subtle);
        }

        .dev-mock-label {
          font-size: 0.65rem;
          color: var(--text-muted);
          font-weight: 600;
        }

        .btn-mock-action {
          font-size: 0.68rem;
          padding: 0.15rem 0.45rem;
          border-radius: 4px;
          border: 1px solid var(--border-subtle);
          background-color: var(--surface-hover);
          color: var(--text-secondary);
          cursor: pointer;
          transition: background-color var(--transition-fast);
        }

        .btn-mock-action:hover {
          background-color: var(--surface-active);
          color: var(--text-primary);
        }

        .btn-mock-action.success {
          border-color: rgba(34, 197, 94, 0.4);
          color: var(--status-success);
        }

        .btn-mock-action.dismiss {
          border-color: rgba(239, 68, 68, 0.4);
          color: var(--status-error);
        }
      `}</style>

      {/* 1. HEADER TRANG */}
      <div className="page-header">
        <div>
          <h2 className="page-title">Quản lý Trình duyệt AI &amp; Phiên làm việc</h2>
          <p className="page-desc">
            TiToAI tự động hóa các tác vụ tạo kịch bản, ảnh và video ở chế độ <strong>chạy ẩn</strong> trên máy tính. Cửa sổ trình duyệt chỉ hiển thị khi cần bạn <strong>đăng nhập thủ công</strong> hoặc xác thực tài khoản.
          </p>
        </div>
        <div className="page-header-actions">
          <button
            type="button"
            className="btn btn-secondary btn-sm"
            onClick={fetchProfiles}
            disabled={isLoading}
            title="Tải lại danh sách trình duyệt và trạng thái phiên"
          >
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className={isLoading ? "spin" : ""} aria-hidden="true">
              <path d="M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8" />
              <path d="M21 3v5h-5" />
              <path d="M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16" />
              <path d="M3 21v-5h5" />
            </svg>
            {isLoading ? "Đang tải…" : "Làm mới"}
          </button>
        </div>
      </div>

      {/* 2. BỘ LỌC PHẠM VI (TẤT CẢ vs WORKSPACE HIỆN TẠI) */}
      <div className="filter-bar">
        <div
          className="tab-group"
          role="tablist"
          aria-label="Bộ lọc phạm vi trình duyệt"
        >
          <button
            type="button"
            role="tab"
            aria-selected={filterScope === "all"}
            className={`filter-tab ${filterScope === "all" ? "active" : ""}`}
            onClick={() => setFilterScope("all")}
          >
            Tất cả trình duyệt {!isLoading && `(${profiles.length})`}
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={filterScope === "workspace"}
            className={`filter-tab ${filterScope === "workspace" ? "active" : ""}`}
            onClick={() => setFilterScope("workspace")}
            disabled={!currentWorkspaceId}
            title={
              currentWorkspaceId
                ? `Chỉ hiển thị cho kênh: ${currentWorkspace?.name ?? currentWorkspaceId}`
                : "Chưa chọn Workspace"
            }
          >
            Kênh hiện tại {currentWorkspace ? `(${currentWorkspace.name})` : ""}
          </button>
        </div>
      </div>

      {/* 3. BANNER TRẠNG THÁI ĐĂNG NHẬP (ACTIVE / WAITING / EXPIRED) */}
      <LoginStatusBanner profiles={profiles} onActionComplete={fetchProfiles} />

      {/* 4. VÙNG NỘI DUNG CHÍNH */}
      {isLoading ? (
        <div className="loading-card" role="status">
          <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className="spin" aria-hidden="true">
            <path d="M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8" />
            <path d="M21 3v5h-5" />
            <path d="M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16" />
            <path d="M3 21v-5h5" />
          </svg>
          <p>Đang kiểm tra trạng thái trình duyệt từ host…</p>
        </div>
      ) : errorMessage ? (
        <div className="empty-card error-card" role="alert">
          <h3>Không thể tải danh sách trình duyệt</h3>
          <p>{errorMessage}</p>
          <button
            type="button"
            className="btn btn-secondary btn-sm"
            onClick={fetchProfiles}
          >
            Thử lại
          </button>
        </div>
      ) : profiles.length === 0 ? (
        <div className="empty-card">
          <div className="empty-icon" aria-hidden="true">
            <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <circle cx="12" cy="12" r="10" />
              <line x1="2" y1="12" x2="22" y2="12" />
              <path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z" />
            </svg>
          </div>
          <h3>Chưa có hồ sơ trình duyệt nào</h3>
          <p>
            Hệ thống chưa ghi nhận cấu hình trình duyệt nào. Các hồ sơ Shared AI và tài khoản kênh sẽ tự động được khởi tạo khi liên kết tài khoản.
          </p>
        </div>
      ) : (
        <div className="browser-groups-container">
          {/* NHÓM 1: SHARED AI */}
          <section className="browser-group-section" aria-labelledby="group-shared-ai">
            <div className="group-header">
              <div className="group-title-col">
                <h3 id="group-shared-ai" className="group-title">
                  1. AI dùng chung (Shared AI Profiles)
                </h3>
                <p className="group-desc">
                  Mô hình ngôn ngữ và sinh ảnh dùng chung cho toàn bộ dự án (ChatGPT Web, Gemini Web). Nhiều tác vụ kịch bản, ảnh và prompt video chạy theo các tab độc lập trong cùng một phiên.
                </p>
              </div>
              <span className="group-count">{sharedAiProfiles.length} hồ sơ</span>
            </div>
            <div className="profile-grid">
              {sharedAiProfiles.map((p) => (
                <ProfileCard
                  key={p.profileId}
                  profile={p}
                  workspaceName={getWorkspaceName(p.workspaceId)}
                  onActionComplete={fetchProfiles}
                />
              ))}
            </div>
          </section>

          {/* NHÓM 2: MUSEAI PROFILES */}
          <section className="browser-group-section" aria-labelledby="group-museai">
            <div className="group-header">
              <div className="group-title-col">
                <h3 id="group-museai" className="group-title">
                  2. Trình duyệt MuseAI (Theo từng kênh)
                </h3>
                <p className="group-desc">
                  Trình duyệt tự động hóa dịch vụ dựng kịch bản video và tạo chuyển động hình ảnh của từng kênh TikTok riêng biệt.
                </p>
              </div>
              <span className="group-count">{museAiProfiles.length} hồ sơ</span>
            </div>
            {museAiProfiles.length === 0 ? (
              <div className="empty-subgroup">
                <p>Chưa có tài khoản MuseAI nào được liên kết cho phạm vi này.</p>
              </div>
            ) : (
              <div className="profile-grid">
                {museAiProfiles.map((p) => (
                  <ProfileCard
                    key={p.profileId}
                    profile={p}
                    workspaceName={getWorkspaceName(p.workspaceId)}
                    onActionComplete={fetchProfiles}
                  />
                ))}
              </div>
            )}
          </section>

          {/* NHÓM 3: TIKTOK PROFILES */}
          <section className="browser-group-section" aria-labelledby="group-tiktok">
            <div className="group-header">
              <div className="group-title-col">
                <h3 id="group-tiktok" className="group-title">
                  3. Trình duyệt TikTok (Theo từng kênh)
                </h3>
                <p className="group-desc">
                  Trình duyệt quản lý phiên đăng nhập kênh TikTok để theo dõi trạng thái, đồng bộ dữ liệu và chuẩn bị video đăng tải.
                </p>
              </div>
              <span className="group-count">{tikTokProfiles.length} hồ sơ</span>
            </div>
            {tikTokProfiles.length === 0 ? (
              <div className="empty-subgroup">
                <p>Chưa có tài khoản TikTok nào được liên kết cho phạm vi này.</p>
              </div>
            ) : (
              <div className="profile-grid">
                {tikTokProfiles.map((p) => (
                  <ProfileCard
                    key={p.profileId}
                    profile={p}
                    workspaceName={getWorkspaceName(p.workspaceId)}
                    onActionComplete={fetchProfiles}
                  />
                ))}
              </div>
            )}
          </section>
        </div>
      )}
    </div>
  );
};
