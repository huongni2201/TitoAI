import { listTikTokAnalyticsAccounts, type TikTokAnalyticsAccount } from "../analytics/analyticsApi";

export type PublishPrivacy = "private" | "friends" | "public";
export type PublishState = "Preparing" | "Submitted" | "Published" | "Uncertain" | "Failed" | "UserConfirmedPublished" | "UserConfirmedNotPublished";

export interface PublishVideo {
  projectId: string;
  projectName: string;
  assetId: string;
  fileName: string;
  byteSize: number;
  caption: string;
}

export interface PublishRequest {
  clientRequestId: string;
  projectId: string;
  assetId: string;
  accountId: string;
  caption: string;
  privacy: PublishPrivacy;
}

export interface PublishAttempt {
  attemptId: string;
  projectId: string;
  assetId: string;
  accountId: string;
  accountIdentity: string;
  caption: string;
  privacy: PublishPrivacy;
  state: PublishState;
  message: string;
  externalPostUrl: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface PublishingList { videos: PublishVideo[]; attempts: PublishAttempt[] }
export interface ReconcileRequest { attemptId: string; wasPublished: boolean; externalPostUrl?: string }
export type PublishingCommand = "publishing.list" | "publishing.publish" | "publishing.reconcile";

// Host truyền bridge đã có allow-list sau khi coordinator hoàn tất tích hợp.
export type PublishingInvoke = <T>(command: PublishingCommand, payload: unknown, options: { timeoutMs: number }) => Promise<T>;
export interface PublishingApi {
  list(): Promise<PublishingList>;
  listAccounts(): Promise<TikTokAnalyticsAccount[]>;
  publish(request: PublishRequest): Promise<PublishAttempt>;
  reconcile(request: ReconcileRequest): Promise<PublishAttempt>;
}

export function createPublishingApi(invoke: PublishingInvoke): PublishingApi {
  return {
    list: () => invoke("publishing.list", {}, { timeoutMs: 30000 }),
    listAccounts: listTikTokAnalyticsAccounts,
    publish: (request) => invoke("publishing.publish", request, { timeoutMs: 300000 }),
    reconcile: (request) => invoke("publishing.reconcile", request, { timeoutMs: 30000 }),
  };
}

export function blocksAnotherPublish(state: PublishState): boolean {
  return state !== "Failed" && state !== "UserConfirmedNotPublished";
}

export function publishingError(error: unknown): string {
  return typeof error === "object" && error !== null && "message" in error
    ? String(error.message) : "Không thể xử lý đăng video. Hãy tải lại lịch sử trước khi thử lại.";
}
