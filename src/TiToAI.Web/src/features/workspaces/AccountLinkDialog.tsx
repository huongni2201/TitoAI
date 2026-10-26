import { useState, useEffect, useCallback, useRef } from "react";
import { workspaceApi, type AccountDto } from "./workspaceApi";
import { browserManagerApi, type BrowserProfileDto } from "../browsers/browserManagerApi";
import type { ErrorDto } from "../../contracts/bridge";
import { IconClose, IconRefresh, IconCheck, IconInfo } from "../../components/Icons";

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

  if (!isOpen) return null;

  // Lọc profile hợp lệ cho provider đang chọn
  const candidateProfiles = profiles.filter(
    (p) => p.kind === selectedProvider || p.kind === "SharedAI",
  );

  return (
    <div
      className="modal-backdrop"
      role="presentation"
      onClick={(e) => {
        if (e.target === e.currentTarget && !isLinking && !unlinkingAccountId) {
          onClose();
        }
      }}
    >
      <div
        className="modal-container"
        role="dialog"
        aria-modal="true"
        aria-labelledby="account-link-title"
        style={{ maxWidth: 560 }}
      >
        <div className="modal-header">
          <div className="modal-title-group">
            <h2 id="account-link-title">Liên Kết Tài Khoản Mạng Xã Hội & AI</h2>
            <p className="modal-subtitle">
              Không gian làm việc: <strong>{workspaceName || workspaceId}</strong>
            </p>
          </div>
          <button
            type="button"
            className="btn-icon"
            onClick={onClose}
            disabled={isLinking || !!unlinkingAccountId}
            aria-label="Đóng"
          >
            <IconClose size={18} />
          </button>
        </div>

        <div className="modal-body" style={{ display: "flex", flexDirection: "column", gap: "1.25rem" }}>
          {errorMessage && (
            <div
              className="notice-box"
              style={{
                backgroundColor: "rgba(239, 68, 68, 0.12)",
                border: "1px solid var(--status-error)",
                color: "var(--status-error)",
              }}
              role="alert"
            >
              <span>⚠️</span>
              <div>{errorMessage}</div>
            </div>
          )}

          {successMessage && (
            <div
              className="notice-box"
              style={{
                backgroundColor: "rgba(34, 197, 94, 0.12)",
                border: "1px solid var(--status-success)",
                color: "var(--status-success)",
              }}
              role="status"
            >
              <IconCheck size={16} />
              <div>{successMessage}</div>
            </div>
          )}

          {/* Danh sách tài khoản đã liên kết */}
          <div>
            <h4 style={{ margin: "0 0 0.5rem 0", fontSize: "0.9rem", color: "var(--text-primary)" }}>
              Tài khoản đang liên kết ({accounts.length})
            </h4>

            {isLoading ? (
              <div className="loading-card" style={{ padding: "1rem" }}>
                <IconRefresh size={18} className="spin" />
                <span style={{ fontSize: "0.85rem" }}>Đang tải danh sách tài khoản…</span>
              </div>
            ) : accounts.length === 0 ? (
              <div
                style={{
                  padding: "1rem",
                  backgroundColor: "var(--surface-card-subtle)",
                  borderRadius: "var(--radius-md)",
                  border: "1px dashed var(--border)",
                  textAlign: "center",
                  fontSize: "0.85rem",
                  color: "var(--text-muted)",
                }}
              >
                Chưa có tài khoản nào được liên kết với Workspace này.
              </div>
            ) : (
              <div style={{ display: "flex", flexDirection: "column", gap: "0.5rem" }}>
                {accounts.map((acc) => {
                  const isConnected = acc.status === "Connected";
                  return (
                    <div
                      key={acc.accountId}
                      style={{
                        display: "flex",
                        alignItems: "center",
                        justifyContent: "space-between",
                        padding: "0.75rem 1rem",
                        backgroundColor: "var(--surface-card-subtle)",
                        border: "1px solid var(--border-subtle)",
                        borderRadius: "var(--radius-md)",
                      }}
                    >
                      <div style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}>
                        <div
                          style={{
                            width: 32,
                            height: 32,
                            borderRadius: "50%",
                            backgroundColor:
                              acc.provider === "TikTok" ? "#000000" : "var(--accent-subtle)",
                            color: acc.provider === "TikTok" ? "#ffffff" : "var(--accent)",
                            display: "flex",
                            alignItems: "center",
                            justifyContent: "center",
                            fontWeight: 700,
                            fontSize: "0.8rem",
                          }}
                        >
                          {acc.provider === "TikTok" ? "TT" : "AI"}
                        </div>
                        <div>
                          <div style={{ fontWeight: 600, color: "var(--text-primary)", fontSize: "0.85rem" }}>
                            {acc.provider} — {acc.accountIdentity || acc.profileId}
                          </div>
                          <div style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
                            Profile: {acc.profileId}
                          </div>
                        </div>
                      </div>

                      <div style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}>
                        <span
                          className={`badge ${isConnected ? "badge-success" : "badge-neutral"}`}
                          style={{ textTransform: "capitalize" }}
                        >
                          {isConnected ? "● Đã kết nối" : `● ${acc.status}`}
                        </span>

                        <button
                          type="button"
                          className="btn btn-secondary btn-sm"
                          style={{ color: "var(--status-error)" }}
                          onClick={() => handleUnlink(acc.accountId, acc.provider)}
                          disabled={unlinkingAccountId === acc.accountId}
                        >
                          {unlinkingAccountId === acc.accountId ? "Đang hủy…" : "Hủy liên kết"}
                        </button>
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </div>

          <hr style={{ border: "none", borderTop: "1px solid var(--border-subtle)", margin: 0 }} />

          {/* Form thêm liên kết mới */}
          <form onSubmit={handleLink} style={{ display: "flex", flexDirection: "column", gap: "1rem" }}>
            <h4 style={{ margin: 0, fontSize: "0.9rem", color: "var(--text-primary)" }}>
              + Thêm liên kết tài khoản mới
            </h4>

            {/* Chọn Provider */}
            <div className="form-group">
              <label>Nhà cung cấp / Nền tảng</label>
              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "0.75rem" }}>
                <button
                  type="button"
                  className={`theme-option-card ${selectedProvider === "MuseAI" ? "selected" : ""}`}
                  style={{ padding: "0.75rem", cursor: "pointer" }}
                  onClick={() => handleProviderChange("MuseAI")}
                >
                  <div style={{ fontWeight: 600, fontSize: "0.85rem" }}>MuseAI Video</div>
                  <p style={{ fontSize: "0.75rem", margin: "0.25rem 0 0 0" }}>Tự động render video từ prompt</p>
                </button>
                <button
                  type="button"
                  className={`theme-option-card ${selectedProvider === "TikTok" ? "selected" : ""}`}
                  style={{ padding: "0.75rem", cursor: "pointer" }}
                  onClick={() => handleProviderChange("TikTok")}
                >
                  <div style={{ fontWeight: 600, fontSize: "0.85rem" }}>TikTok Channel</div>
                  <p style={{ fontSize: "0.75rem", margin: "0.25rem 0 0 0" }}>Đăng tải video & phân tích kênh</p>
                </button>
              </div>
            </div>

            {/* Chọn Profile */}
            <div className="form-group">
              <label htmlFor="profile-select">Hồ sơ trình duyệt (Browser Profile)</label>
              {candidateProfiles.length === 0 ? (
                <div
                  className="notice-box info"
                  style={{ fontSize: "0.8rem", padding: "0.5rem 0.75rem" }}
                >
                  <IconInfo size={16} />
                  <div>
                    Chưa có hồ sơ trình duyệt nào cho nền tảng <strong>{selectedProvider}</strong>.
                    Vui lòng vào tab <strong>Quản lý trình duyệt</strong> để khởi tạo hồ sơ và đăng nhập tài khoản.
                  </div>
                </div>
              ) : (
                <select
                  id="profile-select"
                  className="form-control"
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
              <span className="field-hint">
                Chọn hồ sơ đã đăng nhập để TiToAI có thể tự động đồng bộ mà không cần lưu mật khẩu.
              </span>
            </div>

            <div style={{ display: "flex", justifyContent: "flex-end", marginTop: "0.5rem" }}>
              <button
                type="submit"
                className="btn btn-primary"
                disabled={isLinking || !selectedProfileId || candidateProfiles.length === 0}
              >
                {isLinking ? (
                  <>
                    <IconRefresh size={16} className="spin" /> Đang liên kết…
                  </>
                ) : (
                  `Xác nhận liên kết ${selectedProvider}`
                )}
              </button>
            </div>
          </form>
        </div>

        <div className="modal-footer">
          <span className="footer-status">Hệ thống bảo vệ phiên đăng nhập an toàn qua Playwright</span>
          <button type="button" className="btn btn-secondary" onClick={onClose}>
            Đóng
          </button>
        </div>
      </div>
    </div>
  );
}
