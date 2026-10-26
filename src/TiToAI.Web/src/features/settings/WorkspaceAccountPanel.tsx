import { useCallback, useEffect, useRef, useState } from "react";
import { workspaceApi, type AccountDto } from "../workspaces/workspaceApi";
import { browserManagerApi, type BrowserProfileDto } from "../browsers/browserManagerApi";
import type { WorkspaceSummaryDto } from "../../contracts/dtos";

export function WorkspaceAccountPanel({ workspaces }: { workspaces: WorkspaceSummaryDto[] }) {
  const [workspaceId, setWorkspaceId] = useState("");
  const [accounts, setAccounts] = useState<AccountDto[]>([]);
  const [profiles, setProfiles] = useState<BrowserProfileDto[]>([]);
  const [profileId, setProfileId] = useState("");
  const [isLoading, setIsLoading] = useState(false);
  const [isBusy, setIsBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const requestId = useRef(0);

  const load = useCallback(async () => {
    const generation = ++requestId.current;
    setError(null);
    setAccounts([]);
    setProfiles([]);
    setProfileId("");
    if (!workspaceId) return;
    setIsLoading(true);
    try {
      const [linked, allProfiles] = await Promise.all([
        workspaceApi.listAccounts(workspaceId),
        browserManagerApi.listProfiles(),
      ]);
      if (generation !== requestId.current) return;
      setAccounts(linked.filter((account) => account.provider === "TikTok" && account.status !== "Unlinked"));
      setProfiles(allProfiles.filter((profile) => profile.kind === "TikTok" &&
        (!profile.workspaceId || profile.workspaceId === workspaceId) &&
        (!profile.lifecycle || profile.lifecycle === "Active")));
    } catch (err) {
      if (generation !== requestId.current) return;
      setError((err as { message?: string })?.message || "Không thể tải tài khoản TikTok. Vui lòng thử lại.");
    } finally {
      if (generation === requestId.current) setIsLoading(false);
    }
  }, [workspaceId]);

  useEffect(() => {
    void load();
    return () => { requestId.current += 1; };
  }, [load]);

  const link = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!workspaceId || !profileId || isBusy) return;
    setIsBusy(true);
    setError(null);
    setNotice(null);
    try {
      await workspaceApi.linkAccount({ workspaceId, profileId, provider: "TikTok" });
      await load();
      setNotice("Đã liên kết TikTok với Workspace đã chọn.");
    } catch (err) {
      setError((err as { message?: string })?.message || "Không thể liên kết tài khoản TikTok.");
    } finally {
      setIsBusy(false);
    }
  };

  const unlink = async (account: AccountDto) => {
    if (isBusy || !window.confirm(`Hủy liên kết TikTok ${account.accountIdentity} khỏi Workspace này?`)) return;
    setIsBusy(true);
    setError(null);
    setNotice(null);
    try {
      await workspaceApi.unlinkAccount({ workspaceId, accountId: account.accountId });
      await load();
      setNotice("Đã hủy liên kết TikTok.");
    } catch (err) {
      setError((err as { message?: string })?.message || "Không thể hủy liên kết tài khoản.");
    } finally {
      setIsBusy(false);
    }
  };

  return (
    <section className="settings-section settings-account-links" aria-labelledby="tiktok-links-title">
      <div className="section-header">
        <h3 id="tiktok-links-title">Liên kết kênh TikTok</h3>
        <p>Chọn Workspace để quản lý liên kết TikTok. Tài khoản AI dùng chung toàn ứng dụng ở phía trên.</p>
      </div>
      <div className="form-group">
        <label htmlFor="account-workspace">Workspace</label>
        <select id="account-workspace" className="form-control" value={workspaceId} disabled={isBusy}
          onChange={(event) => { setWorkspaceId(event.target.value); setNotice(null); }}>
          <option value="">Chọn Workspace…</option>
          {workspaces.map((workspace) => <option key={workspace.workspaceId} value={workspace.workspaceId}>{workspace.name}</option>)}
        </select>
      </div>
      {workspaces.length === 0 && <p className="page-desc">Tạo Workspace trước khi liên kết kênh TikTok.</p>}
      {error && <div role="alert" className="notice-box"><span>{error}</span><button type="button" className="btn btn-secondary" onClick={() => void load()} disabled={isBusy || isLoading}>Thử lại</button></div>}
      {notice && <p role="status">{notice}</p>}
      {workspaceId && (isLoading ? <p role="status">Đang tải tài khoản TikTok…</p> : !error && <>
        {accounts.length === 0 ? <p className="page-desc">Workspace này chưa liên kết TikTok.</p> : accounts.map((account) => (
          <div key={account.accountId} className="settings-linked-account">
            <span><strong>{account.accountIdentity || "TikTok"}</strong> · {account.status === "Connected" ? "Đã kết nối" : "Cần kiểm tra phiên"}</span>
            <button type="button" className="btn btn-secondary" disabled={isBusy} onClick={() => void unlink(account)}>Hủy liên kết</button>
          </div>
        ))}
        <form onSubmit={link} className="settings-section">
          <div className="form-group">
            <label htmlFor="tiktok-profile">Hồ sơ TikTok đã đăng nhập</label>
            <select id="tiktok-profile" className="form-control" value={profileId} disabled={isBusy} onChange={(event) => setProfileId(event.target.value)}>
              <option value="">Chọn hồ sơ TikTok…</option>
              {profiles.map((profile) => <option key={profile.profileId} value={profile.profileId} disabled={profile.sessionStatus !== "Connected"}>{profile.accountIdentity || profile.profileId}{profile.sessionStatus !== "Connected" ? " — Cần đăng nhập" : ""}</option>)}
            </select>
            {profiles.length === 0 && <p className="field-hint">Chưa có hồ sơ TikTok phù hợp. Chức năng tạo hồ sơ mới chưa khả dụng trong phiên bản này.</p>}
          </div>
          <div><button type="submit" className="btn btn-primary" disabled={isBusy || !profileId}>{isBusy ? "Đang cập nhật…" : "Liên kết TikTok"}</button></div>
        </form>
      </>)}
    </section>
  );
}
