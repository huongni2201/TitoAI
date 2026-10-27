import { bridgeClient } from "../../services/bridgeClient";

export interface AssetItem {
  assetId: string;
  workspaceId: string;
  projectId?: string | null;
  assetType: string;
  storageRootId: string;
  relativePath: string;
  byteSize: number;
  sha256Hash: string;
  status: string;
  createdAtUtc: string;
  mimeType?: string | null;
}

export interface ListAssetsRequest {
  workspaceId: string;
  projectId?: string;
  assetType?: string;
  page?: number;
  pageSize?: number;
  search?: string;
}

export interface ListAssetsResponse {
  assets: AssetItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface GetAssetPreviewRequest {
  assetId: string;
  workspaceId: string;
}

export interface GetAssetPreviewResponse {
  assetId: string;
  workspaceId: string;
  assetType: string;
  relativePath: string;
  mimeType?: string | null;
  byteSize: number;
  sha256Hash: string;
  fileExists: boolean;
  isRootOffline: boolean;
  status: string;
}

export interface OpenFolderRequest {
  assetId: string;
  workspaceId: string;
}

export interface OpenFolderResponse {
  success: boolean;
  folderPath?: string | null;
}

export async function listAssets(request: ListAssetsRequest): Promise<ListAssetsResponse> {
  return await bridgeClient.invoke<ListAssetsResponse>("asset.list", {
    workspaceId: request.workspaceId,
    projectId: request.projectId || undefined,
    assetType: request.assetType || undefined,
    page: request.page ?? 1,
    pageSize: request.pageSize ?? 20,
    search: request.search || undefined,
  }, { workspaceId: request.workspaceId });
}

export async function getAssetPreview(request: GetAssetPreviewRequest): Promise<GetAssetPreviewResponse> {
  return await bridgeClient.invoke<GetAssetPreviewResponse>("asset.get_preview", {
    assetId: request.assetId,
    workspaceId: request.workspaceId,
  }, { workspaceId: request.workspaceId });
}

export async function openAssetFolder(request: OpenFolderRequest): Promise<OpenFolderResponse> {
  return await bridgeClient.invoke<OpenFolderResponse>("asset.open_folder", {
    assetId: request.assetId,
    workspaceId: request.workspaceId,
  }, { workspaceId: request.workspaceId });
}

export interface UploadAssetRequest {
  workspaceId: string;
  projectId?: string;
  assetType: "Image" | "Audio";
  fileName?: string;
  base64Data?: string;
  sourceFilePath?: string;
}

export interface UploadAssetResponse {
  asset?: AssetItem | null;
  canceled?: boolean;
}

export async function uploadAsset(request: UploadAssetRequest): Promise<UploadAssetResponse> {
  return await bridgeClient.invoke<UploadAssetResponse>("asset.upload", {
    workspaceId: request.workspaceId,
    projectId: request.projectId || undefined,
    assetType: request.assetType,
    fileName: request.fileName || undefined,
    base64Data: request.base64Data || undefined,
    sourceFilePath: request.sourceFilePath || undefined,
  }, { workspaceId: request.workspaceId });
}
