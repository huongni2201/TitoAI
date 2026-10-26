export const bridgeContractVersion = "1.0.4" as const;
export const bridgeOrigin = "https://appassets.local" as const;
export const maxBridgeEnvelopeBytes = 4 * 1024 * 1024;

/**
 * Danh mục lệnh được phép trên Bridge (bao gồm Gate G1 và G2 Core Business)
 */
export const bridgeCommandAllowlist = [
  // Gate G1 — Foundation
  "workspace.list",
  "settings.get",
  "settings.save",
  "settings.browse_folder",
  "settings.validate_roots",
  "theme.get_preference",
  "theme.set_preference",

  // Gate G2 — Workspaces & Accounts
  "workspace.get",
  "workspace.create",
  "workspace.update",
  "workspace.pause",
  "workspace.resume",
  "workspace.delete",
  "account.list_by_workspace",
  "account.link",
  "account.unlink",

  // Gate G2 — Prompts
  "prompt.list_templates",
  "prompt.get_template",
  "prompt.list_versions",
  "prompt.create_version",
  "prompt.preview",
  "prompt.rollback_version",
  "prompt.delete_override",

  // Gate G2 — Projects & Scenes
  "project.list_by_workspace",
  "project.get",
  "project.create",
  "project.update",
  "project.delete",
  "scene.list_by_project",
  "scene.get",
  "scene.create",
  "scene.update",
  "scene.delete",
  "scene.reorder",

  // Gate G2 — Browser Management
  "browser.list_profiles",
  "browser.request_login",
  "browser.cancel_login",
  "browser.check_session",

  // Gate G2 — Asset Storage & Documents
  "asset.import_audio",
  "asset.select_reference_image",
  "asset.get_metadata",
  "asset.open_folder",
  "document.export",

  // Gate G2 — Job Queue & Scheduling
  "job.enqueue",
  "job.get_status",
  "job.list_active",
  "job.cancel",
  "job.reconcile",
] as const;

export const g1CommandAllowlist = bridgeCommandAllowlist;
export type BridgeCommand = (typeof bridgeCommandAllowlist)[number];
export type G1Command = BridgeCommand;

export type RequestEnvelope<TPayload = unknown> = {
  id: string;
  type: "request";
  command: BridgeCommand | string;
  payload: TPayload;
  clientTimestampUtc: string;
  contractVersion: typeof bridgeContractVersion;
};

export type ResponseEnvelope<TData = unknown> = {
  id: string;
  type: "response";
  success: boolean;
  data: TData | null;
  error: ErrorDto | null;
  serverTimestampUtc: string;
  contractVersion: typeof bridgeContractVersion;
};

export type EventEnvelope<TPayload = unknown> = {
  id: string;
  type: "event";
  eventName: string;
  workspaceId: string | null;
  seq: number;
  payload: TPayload;
  serverTimestampUtc: string;
  contractVersion: typeof bridgeContractVersion;
};

export type ErrorDto = {
  code: string;
  message: string;
  retryable: boolean;
  correlationId: string;
  details: Record<string, unknown> | null;
};

export const ErrorCodes = {
  UNKNOWN_COMMAND: "UNKNOWN_COMMAND",
  INVALID_ARGUMENT: "INVALID_ARGUMENT",
  UNAUTHORIZED_WORKSPACE: "UNAUTHORIZED_WORKSPACE",
  ENTITY_NOT_FOUND: "ENTITY_NOT_FOUND",
  VALIDATION_FAILED: "VALIDATION_FAILED",
  STORAGE_ROOT_UNAVAILABLE: "STORAGE_ROOT_UNAVAILABLE",
  PROVIDER_SESSION_REQUIRED: "PROVIDER_SESSION_REQUIRED",
  AUTOMATION_BLOCKED: "AUTOMATION_BLOCKED",
  JOB_ALREADY_SUBMITTED: "JOB_ALREADY_SUBMITTED",
  CONCURRENCY_CONFLICT: "CONCURRENCY_CONFLICT",
  INTERNAL_ERROR: "INTERNAL_ERROR",
  VERSION_MISMATCH: "VERSION_MISMATCH",
  PAYLOAD_TOO_LARGE: "PAYLOAD_TOO_LARGE",
  BRIDGE_UNAVAILABLE: "BRIDGE_UNAVAILABLE",
  REQUEST_TIMEOUT: "REQUEST_TIMEOUT",
} as const;
