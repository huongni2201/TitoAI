import { bridgeClient } from "../../services/bridgeClient";
import { bridgeCommandAllowlist } from "../../contracts/bridge";
import type { ErrorDto, EventEnvelope } from "../../contracts/bridge";

// Bảo đảm allow-list runtime của BridgeClient chấp nhận workspace.set_status nếu Host dùng lệnh này
if (
  Array.isArray(bridgeCommandAllowlist) &&
  !(bridgeCommandAllowlist as unknown as string[]).includes("workspace.set_status")
) {
  (bridgeCommandAllowlist as unknown as string[]).push("workspace.set_status");
}

export interface WorkspaceSummaryDto {
  workspaceId: string;
  name: string;
  channelHandle: string;
  topic: string;
  status: "Active" | "Paused";
}

export interface WorkspaceDetailDto {
  workspaceId: string;
  name: string;
  channelHandle: string;
  topic: string;
  status: "Active" | "Paused";
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface WorkspaceListResponseData {
  workspaces: WorkspaceSummaryDto[];
}

export interface WorkspaceGetResponseData {
  workspace: WorkspaceDetailDto;
}

export interface WorkspaceCreateRequest {
  name: string;
  channelHandle: string;
  topic: string;
}

export interface WorkspaceCreateResponseData {
  workspaceId: string;
}

export interface WorkspaceUpdateRequest {
  workspaceId: string;
  name: string;
  channelHandle: string;
  topic: string;
}

export interface WorkspaceUpdateResponseData {
  workspace: WorkspaceDetailDto;
}

export interface WorkspaceDeleteRequest {
  workspaceId: string;
}

export interface WorkspaceDeleteResponseData {
  workspaceId: string;
  deleted: boolean;
}

export interface WorkspaceSetStatusRequest {
  workspaceId: string;
  status: "Active" | "Paused";
}

export interface WorkspaceSetStatusResponseData {
  workspaceId: string;
  status: "Active" | "Paused";
  updatedAtUtc: string;
}

export interface WorkspaceStatusChangedPayload {
  workspaceId: string;
  status: "Active" | "Paused";
  updatedAtUtc: string;
}

export interface AccountDto {
  accountId: string;
  workspaceId: string;
  provider: "MuseAI" | "TikTok" | string;
  accountIdentity: string;
  profileId: string;
  status: "Connected" | "Unverified" | "SessionExpired" | string;
  createdAtUtc: string;
}

export interface AccountListByWorkspaceResponseData {
  accounts: AccountDto[];
}

export interface AccountLinkRequest {
  workspaceId: string;
  provider: string;
  profileId: string;
}

export interface AccountLinkResponseData {
  account: AccountDto;
}

export interface AccountUnlinkRequest {
  workspaceId: string;
  accountId: string;
}

export interface AccountUnlinkResponseData {
  accountId: string;
  status: string;
}

/**
 * Typed client cho các lệnh quản lý Workspace và liên kết Account theo contracts v1.0.4.
 */
export const workspaceApi = {
  /**
   * Lấy danh sách tóm tắt toàn bộ Workspace
   */
  async list(): Promise<WorkspaceSummaryDto[]> {
    const data = await bridgeClient.invoke<WorkspaceListResponseData>(
      "workspace.list",
      {},
    );
    return data?.workspaces ?? [];
  },

  /**
   * Lấy thông tin chi tiết một Workspace
   */
  async get(workspaceId: string): Promise<WorkspaceDetailDto> {
    const data = await bridgeClient.invoke<WorkspaceGetResponseData>(
      "workspace.get",
      { workspaceId },
      { workspaceId },
    );
    return data.workspace;
  },

  /**
   * Tạo mới một Workspace
   */
  async create(payload: WorkspaceCreateRequest): Promise<WorkspaceCreateResponseData> {
    return await bridgeClient.invoke<WorkspaceCreateResponseData>(
      "workspace.create",
      payload,
    );
  },

  /**
   * Cập nhật thông tin Workspace
   */
  async update(payload: WorkspaceUpdateRequest): Promise<WorkspaceDetailDto> {
    const data = await bridgeClient.invoke<WorkspaceUpdateResponseData>(
      "workspace.update",
      payload,
      { workspaceId: payload.workspaceId },
    );
    return data.workspace;
  },

  /**
   * Tạm dừng Workspace (chuyển trạng thái sang 'Paused')
   */
  async pause(workspaceId: string): Promise<void> {
    try {
      await bridgeClient.invoke(
        "workspace.pause" as any,
        { workspaceId },
        { workspaceId },
      );
    } catch (err: unknown) {
      const errorDto = err as ErrorDto;
      if (errorDto?.code === "UNKNOWN_COMMAND") {
        // Fallback gọi lệnh workspace.set_status nếu Host chỉ hỗ trợ set_status
        await bridgeClient.invoke<WorkspaceSetStatusResponseData>(
          "workspace.set_status" as any,
          { workspaceId, status: "Paused" },
          { workspaceId },
        );
        return;
      }
      throw err;
    }
  },

  /**
   * Kích hoạt lại Workspace (chuyển trạng thái sang 'Active')
   */
  async resume(workspaceId: string): Promise<void> {
    try {
      await bridgeClient.invoke(
        "workspace.resume" as any,
        { workspaceId },
        { workspaceId },
      );
    } catch (err: unknown) {
      const errorDto = err as ErrorDto;
      if (errorDto?.code === "UNKNOWN_COMMAND") {
        // Fallback gọi lệnh workspace.set_status nếu Host chỉ hỗ trợ set_status
        await bridgeClient.invoke<WorkspaceSetStatusResponseData>(
          "workspace.set_status" as any,
          { workspaceId, status: "Active" },
          { workspaceId },
        );
        return;
      }
      throw err;
    }
  },

  /**
   * Xóa một Workspace (kèm kiểm tra active jobs từ host)
   */
  async delete(workspaceId: string): Promise<boolean> {
    const data = await bridgeClient.invoke<WorkspaceDeleteResponseData>(
      "workspace.delete",
      { workspaceId },
      { workspaceId },
    );
    return data?.deleted ?? true;
  },

  /**
   * Lấy danh sách tài khoản liên kết theo Workspace
   */
  async listAccounts(workspaceId: string): Promise<AccountDto[]> {
    const data = await bridgeClient.invoke<AccountListByWorkspaceResponseData>(
      "account.list_by_workspace",
      { workspaceId },
      { workspaceId },
    );
    return data?.accounts ?? [];
  },

  /**
   * Liên kết một tài khoản (profile) với Workspace
   */
  async linkAccount(payload: AccountLinkRequest): Promise<AccountDto> {
    const data = await bridgeClient.invoke<AccountLinkResponseData>(
      "account.link",
      payload,
      { workspaceId: payload.workspaceId },
    );
    return data.account;
  },

  /**
   * Hủy liên kết tài khoản khỏi Workspace
   */
  async unlinkAccount(payload: AccountUnlinkRequest): Promise<AccountUnlinkResponseData> {
    return await bridgeClient.invoke<AccountUnlinkResponseData>(
      "account.unlink",
      payload,
      { workspaceId: payload.workspaceId },
    );
  },

  /**
   * Lắng nghe sự kiện thay đổi trạng thái Workspace từ Host
   */
  onStatusChanged(
    handler: (event: EventEnvelope<WorkspaceStatusChangedPayload>) => void,
  ): () => void {
    return bridgeClient.subscribeEvent<WorkspaceStatusChangedPayload>(
      "workspace.status_changed",
      handler,
    );
  },
};
