import { bridgeClient } from "../../services/bridgeClient";

export const WorkflowModes = {
  ModelAndProduct: "ModelAndProduct",
  ReadyImage: "ReadyImage",
  ProductOnly: "ProductOnly",
} as const;

export type WorkflowMode = (typeof WorkflowModes)[keyof typeof WorkflowModes];

export const AudioOptions = {
  None: "None",
  CustomAudio: "CustomAudio",
} as const;

export type AudioOption = (typeof AudioOptions)[keyof typeof AudioOptions];

export const WorkflowRunState = {
  Idle: "Idle",
  Preparing: "Preparing",
  Running: "Running",
  Paused: "Paused",
  Completed: "Completed",
  Failed: "Failed",
} as const;

export type WorkflowRunState = (typeof WorkflowRunState)[keyof typeof WorkflowRunState];

export const SceneWorkflowStatus = {
  MissingInputs: "MissingInputs",
  PreparingImage: "PreparingImage",
  PreparingPrompt: "PreparingPrompt",
  ReadyForVideo: "ReadyForVideo",
  VideoCompleted: "VideoCompleted",
  NeedsReview: "NeedsReview",
} as const;

export type SceneWorkflowStatus = (typeof SceneWorkflowStatus)[keyof typeof SceneWorkflowStatus];

export interface ProjectWorkflowConfigDto {
  projectId: string;
  workspaceId: string;
  workflowMode: WorkflowMode;
  goalPrompt: string;
  modelAssetId: string | null;
  productAssetId: string | null;
  readyImageAssetId: string | null;
  foundationImageAssetId: string | null;
  audioOption: AudioOption;
  audioAssetId: string | null;
  targetSceneCount: number;
  targetDurationPerSceneSeconds: number;
  runState: WorkflowRunState;
  currentRunId: string | null;
  updatedAtUtc: string;
  scriptPromptText?: string | null;
  videoPromptText?: string | null;
}

export interface SaveProjectWorkflowConfigRequest {
  workspaceId: string;
  projectId: string;
  workflowMode: WorkflowMode;
  goalPrompt: string;
  modelAssetId?: string | null;
  productAssetId?: string | null;
  readyImageAssetId?: string | null;
  audioOption: AudioOption;
  audioAssetId?: string | null;
  targetSceneCount: number;
  targetDurationPerSceneSeconds: number;
  scriptPromptText?: string | null;
  videoPromptText?: string | null;
}

export interface SceneWorkflowDetailDto {
  sceneId: string;
  projectId: string;
  workspaceId: string;
  sequenceOrder: number;
  name: string;
  targetDurationSeconds: number;
  workflowStatus: SceneWorkflowStatus;
  imageStrategy: string;
  sourceImageAssetId: string | null;
  selectedImageAssetId: string | null;
  motionPromptText: string | null;
  audioOption: AudioOption;
  audioAssetId: string | null;
  videoAssetId: string | null;
  isStale: boolean;
  staleReason: string | null;
  activeJobId: string | null;
  activeJobType: string | null;
  activeJobState: string | null;
  activeJobProgressPercent: number | null;
  videoCandidateHistory?: string[] | null;
  revision: string;
}

export interface StartProjectWorkflowResponseData {
  runId: string;
  runState: string;
  createdSceneCount: number;
  message: string;
}

export interface UpdateSceneWorkflowInputRequest {
  workspaceId: string;
  projectId: string;
  sceneId: string;
  imageStrategy?: string;
  selectedImageAssetId?: string | null;
  motionPromptText?: string | null;
  audioOption?: AudioOption;
  audioAssetId?: string | null;
  targetDurationSeconds?: number;
  expectedRevision?: string;
}

export interface PreviewAiPromptRequest {
  workspaceId: string;
  projectId: string;
  sceneId?: string | null;
}

export interface PreviewAiPromptResponseData {
  templateId: string;
  templateName: string;
  versionId: string;
  isOverride: boolean;
  userBrief: string;
  composedPrompt: string;
  outputContractVersion: number;
}

export interface VideoContentItemDto {
  id: string;
  projectId: string;
  workspaceId: string;
  sceneId?: string | null;
  sequenceOrder: number;
  idea: string;
  hook: string;
  videoScript: string;
  videoPrompt: string;
  tiktokContent: string;
  imageAssetId?: string | null;
  videoAssetId?: string | null;
  audioAssetId?: string | null;
  status: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface UpdateVideoContentItemRequest {
  workspaceId: string;
  projectId: string;
  itemId: string;
  idea?: string;
  hook?: string;
  videoScript?: string;
  videoPrompt?: string;
  tiktokContent?: string;
}

export interface CreateSceneVideoRequest {
  workspaceId: string;
  projectId: string;
  itemId: string;
}

export const DEFAULT_SUPPORTED_DURATIONS = [10, 15] as const;

export const videoWorkflowApi = {
  async getConfig(workspaceId: string, projectId: string): Promise<ProjectWorkflowConfigDto> {
    const data = await bridgeClient.invoke<ProjectWorkflowConfigDto>(
      "workflow.get_config",
      { workspaceId, projectId },
      { workspaceId }
    );
    return data!;
  },

  async saveConfig(req: SaveProjectWorkflowConfigRequest): Promise<ProjectWorkflowConfigDto> {
    const data = await bridgeClient.invoke<ProjectWorkflowConfigDto>(
      "workflow.save_config",
      req,
      { workspaceId: req.workspaceId }
    );
    return data!;
  },

  async startWorkflow(workspaceId: string, projectId: string): Promise<StartProjectWorkflowResponseData> {
    const data = await bridgeClient.invoke<StartProjectWorkflowResponseData>(
      "workflow.start",
      { workspaceId, projectId },
      { workspaceId }
    );
    return data!;
  },

  async listScenes(workspaceId: string, projectId: string): Promise<SceneWorkflowDetailDto[]> {
    const res = await bridgeClient.invoke<{ scenes: SceneWorkflowDetailDto[] }>(
      "workflow.list_scenes",
      { workspaceId, projectId },
      { workspaceId }
    );
    return res?.scenes || [];
  },

  async updateSceneInput(req: UpdateSceneWorkflowInputRequest): Promise<SceneWorkflowDetailDto> {
    const data = await bridgeClient.invoke<SceneWorkflowDetailDto>(
      "workflow.update_scene_input",
      req,
      { workspaceId: req.workspaceId }
    );
    return data!;
  },

  async pauseWorkflow(workspaceId: string, projectId: string): Promise<ProjectWorkflowConfigDto> {
    const data = await bridgeClient.invoke<ProjectWorkflowConfigDto>(
      "workflow.pause",
      { workspaceId, projectId },
      { workspaceId }
    );
    return data!;
  },

  async resumeWorkflow(workspaceId: string, projectId: string): Promise<ProjectWorkflowConfigDto> {
    const data = await bridgeClient.invoke<ProjectWorkflowConfigDto>(
      "workflow.resume",
      { workspaceId, projectId },
      { workspaceId }
    );
    return data!;
  },

  async previewAiPrompt(req: PreviewAiPromptRequest): Promise<PreviewAiPromptResponseData> {
    const data = await bridgeClient.invoke<PreviewAiPromptResponseData>(
      "workflow.preview_ai_prompt",
      req,
      { workspaceId: req.workspaceId }
    );
    return data!;
  },

  async listContentItems(workspaceId: string, projectId: string): Promise<VideoContentItemDto[]> {
    const res = await bridgeClient.invoke<{ items: VideoContentItemDto[] }>(
      "workflow.list_content_items",
      { workspaceId, projectId },
      { workspaceId }
    );
    return res?.items || [];
  },

  async updateContentItem(req: UpdateVideoContentItemRequest): Promise<VideoContentItemDto> {
    const data = await bridgeClient.invoke<{ item: VideoContentItemDto }>(
      "workflow.update_content_item",
      req,
      { workspaceId: req.workspaceId }
    );
    return data?.item!;
  },

  async createSceneVideo(req: CreateSceneVideoRequest): Promise<unknown> {
    const data = await bridgeClient.invoke<unknown>(
      "workflow.create_scene_video",
      req,
      { workspaceId: req.workspaceId }
    );
    return data;
  },
};
