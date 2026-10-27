import { bridgeClient } from "../../services/bridgeClient";

export interface JobHandoffDto {
  jobId: string;
  workspaceId: string;
  projectId: string;
  sceneId: string | null;
  taskType: string;
  providerKey: string;
  promptText: string;
  configSnapshotJson: string | null;
  workflowVersion: string | null;
  revision: number;
  reasonCode: string;
  instructions: string;
  expectedOutputSchema: string;
  hasUnresolvedSideEffect: boolean;
  resultReference: string | null;
}

export interface JobGetHandoffRequest {
  workspaceId: string;
  jobId: string;
}

export interface JobGetHandoffResponseData {
  handoff: JobHandoffDto;
}

export interface JobImportResultRequest {
  workspaceId: string;
  jobId: string;
  expectedRevision: number;
  clientRequestId: string;
  textResult?: string | null;
  assetId?: string | null;
  confirmedNotReceived?: boolean;
}

export interface JobImportResultResponseData {
  jobId: string;
  state: string;
  revision: number;
  resultId: string | null;
  assetId: string | null;
  isDuplicate: boolean;
  wasCanceled: boolean;
}

export const manualHandoffApi = {
  /**
   * Lấy thông tin handoff, prompt snapshot và hướng dẫn thao tác cho tác vụ (job.get_handoff)
   */
  async getHandoff(
    workspaceId: string,
    jobId: string,
  ): Promise<JobGetHandoffResponseData> {
    const payload: JobGetHandoffRequest = { workspaceId, jobId };
    return await bridgeClient.invoke<JobGetHandoffResponseData>(
      "job.get_handoff",
      payload,
      { workspaceId },
    );
  },

  /**
   * Nhập kết quả thủ công cho tác vụ (job.import_result)
   * Hỗ trợ Script (JSON), VideoPrompt (text), Image (asset ID), VideoRender (asset ID)
   */
  async importResult(
    request: JobImportResultRequest,
  ): Promise<JobImportResultResponseData> {
    return await bridgeClient.invoke<JobImportResultResponseData>(
      "job.import_result",
      request,
      { workspaceId: request.workspaceId },
    );
  },
};
