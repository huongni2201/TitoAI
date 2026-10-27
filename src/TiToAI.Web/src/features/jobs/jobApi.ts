import { bridgeClient } from "../../services/bridgeClient";
import type { EventEnvelope } from "@/contracts/bridge";
import type {
  JobSummaryDto,
  JobDetailDto,
  JobListActiveRequest,
  JobListActiveResponseData,
  JobGetStatusRequest,
  JobGetStatusResponseData,
  JobCancelRequest,
  JobCancelResponseData,
  JobResumeRequest,
  JobResumeResponseData,
  JobReconcileRequest,
  JobReconcileResponseData,
  JobStateChangedPayload,
  JobProgressUpdatedPayload,
  JobRetryRequest,
  JobRetryResponseData,
  JobListHistoryRequest,
  JobListHistoryResponseData,
} from "./jobTypes";

export const jobApi = {
  /**
   * Lấy danh sách các tác vụ đang hoạt động toàn app (hoặc theo workspaceId nếu cung cấp)
   * Bao gồm cả tác vụ Canceled/Failed chưa giải quyết (unresolved).
   */
  async listActive(workspaceId?: string): Promise<JobSummaryDto[]> {
    const payload: JobListActiveRequest = workspaceId ? { workspaceId } : {};
    const res = await bridgeClient.invoke<JobListActiveResponseData>(
      "job.list_active",
      payload,
    );
    return res?.jobs ?? [];
  },

  /**
   * Lấy thông tin trạng thái chi tiết của một tác vụ cụ thể
   */
  async getStatus(workspaceId: string, jobId: string): Promise<JobDetailDto> {
    const payload: JobGetStatusRequest = { workspaceId, jobId };
    const res = await bridgeClient.invoke<JobGetStatusResponseData>(
      "job.get_status",
      payload,
      { workspaceId },
    );
    return res.job;
  },

  /**
   * Hủy bỏ một tác vụ đang chạy hoặc đang chờ
   */
  async cancel(workspaceId: string, jobId: string): Promise<JobCancelResponseData> {
    const payload: JobCancelRequest = { workspaceId, jobId };
    return await bridgeClient.invoke<JobCancelResponseData>(
      "job.cancel",
      payload,
      { workspaceId },
    );
  },

  /**
   * Tiếp tục một tác vụ ở trạng thái AwaitingUser sau khi đã đáp ứng điều kiện
   */
  async resume(workspaceId: string, jobId: string): Promise<JobResumeResponseData> {
    const payload: JobResumeRequest = { workspaceId, jobId };
    return await bridgeClient.invoke<JobResumeResponseData>(
      "job.resume",
      payload,
      { workspaceId },
    );
  },

  /**
   * Đối soát kết quả tác vụ không chắc chắn (reconcile_required)
   */
  async reconcile(payload: JobReconcileRequest): Promise<JobReconcileResponseData> {
    return await bridgeClient.invoke<JobReconcileResponseData>(
      "job.reconcile",
      payload,
      { workspaceId: payload.workspaceId },
    );
  },

  /**
   * Thử lại an toàn một tác vụ thất bại (C2 - job.retry) với ExpectedRevision (CAS)
   */
  async retry(workspaceId: string, jobId: string, expectedRevision: number): Promise<JobRetryResponseData> {
    const payload: JobRetryRequest = { workspaceId, jobId, expectedRevision };
    return await bridgeClient.invoke<JobRetryResponseData>(
      "job.retry",
      payload,
      { workspaceId },
    );
  },

  /**
   * Lấy lịch sử các tác vụ đã hoàn tất / kết thúc (C3 - job.list_history)
   */
  async listHistory(req: JobListHistoryRequest = {}): Promise<JobListHistoryResponseData> {
    return await bridgeClient.invoke<JobListHistoryResponseData>(
      "job.list_history",
      req,
      req.workspaceId ? { workspaceId: req.workspaceId } : undefined,
    );
  },

  /**
   * Yêu cầu mở trình duyệt đăng nhập thủ công cho profile AI khi gặp login_required
   */
  async requestLogin(profileId: string): Promise<unknown> {
    return await bridgeClient.invoke(
      "browser.request_login",
      { profileId },
    );
  },

  /**
   * Lắng nghe sự kiện chuyển đổi trạng thái tác vụ theo thời gian thực
   */
  onStateChanged(
    handler: (event: EventEnvelope<JobStateChangedPayload>) => void,
  ): () => void {
    return bridgeClient.subscribeEvent<JobStateChangedPayload>(
      "job.state_changed",
      handler,
    );
  },

  /**
   * Lắng nghe sự kiện tiến độ thực tế theo thời gian thực (tỷ lệ % và bước hiện tại)
   */
  onProgressUpdated(
    handler: (event: EventEnvelope<JobProgressUpdatedPayload>) => void,
  ): () => void {
    return bridgeClient.subscribeEvent<JobProgressUpdatedPayload>(
      "job.progress_updated",
      handler,
    );
  },
};
