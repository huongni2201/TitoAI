import { useState, useEffect, useCallback, useMemo, useRef } from "react";
import type { ErrorDto } from "@/contracts/bridge";
import {
  type JobSummaryDto,
  type JobDetailDto,
  type JobState,
  type JobReconcileRequest,
  type QueueTabFilter,
  isTerminalState,
} from "./jobTypes";
import { jobApi } from "./jobApi";

export type JobItem = JobSummaryDto;

export interface UseQueueStateOptions {
  workspaceId?: string;
  autoFetch?: boolean;
  isOpen?: boolean;
}

export interface UseQueueStateReturn {
  jobs: JobItem[];
  filteredJobs: JobItem[];
  isLoading: boolean;
  error: ErrorDto | null;
  activeTab: QueueTabFilter;
  searchQuery: string;
  selectedJobId: string | null;
  selectedJobDetail: JobDetailDto | null;
  reconcileJob: JobItem | null;
  isActionLoading: Record<string, boolean>;
  tabCounts: Record<QueueTabFilter, number>;

  // State setters
  setActiveTab: (tab: QueueTabFilter) => void;
  setSearchQuery: (query: string) => void;
  setSelectedJobId: (jobId: string | null) => void;
  setReconcileJob: (job: JobItem | null) => void;
  clearError: () => void;

  // Actions
  refresh: () => Promise<void>;
  handleCancel: (job: JobSummaryDto | JobDetailDto) => Promise<boolean>;
  handleResume: (job: JobSummaryDto | JobDetailDto) => Promise<boolean>;
  handleReconcile: (request: JobReconcileRequest) => Promise<boolean>;
  handleRequestLogin: (profileId: string) => Promise<boolean>;
  handleRetry: (job: JobSummaryDto | JobDetailDto) => Promise<boolean>;
  fetchJobDetail: (workspaceId: string, jobId: string) => Promise<JobDetailDto | null>;
}

export function matchesTab(state: JobState, tab: QueueTabFilter): boolean {
  switch (tab) {
    case "all":
      return true;
    case "running":
      return state === "Running" || state === "Reserved";
    case "awaiting":
      return state === "AwaitingUser" || state === "Queued";
    case "failed":
      return state === "Failed";
    case "completed":
      return state === "Succeeded" || state === "Canceled";
    default:
      return true;
  }
}

const ACTIVE_STATES: readonly JobState[] = ["Queued", "Reserved", "Running", "AwaitingUser"];

function createErrorDto(code: string, message: string): ErrorDto {
  return {
    code,
    message,
    retryable: false,
    correlationId: `err_${Date.now()}_${Math.random().toString(36).slice(2, 8)}`,
    details: null,
  };
}

export function useQueueState(options: UseQueueStateOptions = {}): UseQueueStateReturn {
  const { workspaceId, autoFetch = true, isOpen } = options;

  const [jobs, setJobs] = useState<JobItem[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(false);
  const [error, setError] = useState<ErrorDto | null>(null);
  const [activeTab, setActiveTab] = useState<QueueTabFilter>("all");
  const [searchQuery, setSearchQuery] = useState<string>("");
  const [selectedJobId, setSelectedJobId] = useState<string | null>(null);
  const [selectedJobDetail, setSelectedJobDetail] = useState<JobDetailDto | null>(null);
  const [reconcileJob, setReconcileJob] = useState<JobItem | null>(null);
  const [isActionLoading, setIsActionLoading] = useState<Record<string, boolean>>({});

  // Ref đồng bộ chặn race condition / double-click trên cùng jobId
  const actionLoadingRef = useRef<Set<string>>(new Set());

  // Lưu timestamp của event cuối cùng theo jobId để tránh stale event ghi đè dữ liệu mới
  const eventTimestampsRef = useRef<Map<string, number>>(new Map());

  // T17-R1: Generation counter để discard response cũ khi có nhiều refresh đồng thời
  const fetchGenerationRef = useRef<number>(0);

  // T17-R6: Lưu trữ lịch sử các job đã hoàn tất (Succeeded / Canceled resolved) trong phiên
  const completedHistoryRef = useRef<Map<string, JobItem>>(new Map());

  const clearError = useCallback(() => {
    setError(null);
  }, []);

  // T17-R1 & T17-R6: Refresh có kiểm soát generation và merge snapshot an toàn
  const refresh = useCallback(async () => {
    const currentGen = ++fetchGenerationRef.current;
    const fetchStartTime = Date.now();

    setIsLoading(true);
    setError(null);

    try {
      const activeJobs = await jobApi.listActive(workspaceId);

      // Nếu đã có một request refresh mới hơn được phát đi: discard response cũ này
      if (currentGen !== fetchGenerationRef.current) {
        return;
      }

      setJobs((prevJobs) => {
        // Tạo map từ previous jobs để đối chiếu trạng thái hiện thời
        const prevMap = new Map<string, JobItem>(prevJobs.map((j) => [j.id, j]));

        // Danh sách kết quả hợp nhất
        const mergedJobs: JobItem[] = [];
        const seenIds = new Set<string>();

        // 1. Duyệt qua các active jobs từ backend response
        for (const rawJob of activeJobs) {
          seenIds.add(rawJob.id);
          const localJob = prevMap.get(rawJob.id);
          const lastEventTime = eventTimestampsRef.current.get(rawJob.id) ?? 0;

          // T17-R1: Nếu localJob có event mới hơn thời điểm gửi request, HOẶC localJob đã là terminal
          // mà response từ backend vẫn là non-terminal -> GIỮ NGUYÊN local state!
          if (localJob) {
            const hasNewerEvent = lastEventTime > fetchStartTime;
            const isLocalTerminal = isTerminalState(localJob.state);
            const isRemoteActive = !isTerminalState(rawJob.state);

            if (hasNewerEvent || (isLocalTerminal && isRemoteActive)) {
              mergedJobs.push(localJob);
              continue;
            }
          } else {
            // Nếu localJob chưa có nhưng đã có trong completedHistoryRef hoặc event mới hơn
            const historyJob = completedHistoryRef.current.get(rawJob.id);
            if (historyJob && isTerminalState(historyJob.state) && !isTerminalState(rawJob.state)) {
              mergedJobs.push(historyJob);
              continue;
            }
          }

          // Cập nhật job từ snapshot backend
          const jobToAdd: JobItem = {
            ...rawJob,
            // Giữ lại các metadata hữu ích nếu local đã có (như profileId, cancelRequested)
            cancelRequested: localJob?.cancelRequested ?? rawJob.cancelRequested,
            profileId: localJob?.profileId ?? rawJob.profileId,
            sideEffectSubmitted: localJob?.sideEffectSubmitted ?? rawJob.sideEffectSubmitted,
            hasUnresolvedSideEffect: localJob?.hasUnresolvedSideEffect ?? rawJob.hasUnresolvedSideEffect,
            maxAttempts: localJob?.maxAttempts ?? rawJob.maxAttempts,
          };

          // Nếu job này ở trạng thái terminal, cập nhật vào history cache
          if (isTerminalState(jobToAdd.state)) {
            completedHistoryRef.current.set(jobToAdd.id, jobToAdd);
          }

          mergedJobs.push(jobToAdd);
        }

        // 2. T17-R6: Bổ sung các job hoàn tất từ lịch sử phiên nếu chưa có trong active jobs
        for (const [historyId, historyJob] of completedHistoryRef.current.entries()) {
          if (!seenIds.has(historyId)) {
            // Kiểm tra workspace nếu có lọc theo workspaceId
            if (!workspaceId || historyJob.workspaceId === workspaceId) {
              mergedJobs.push(historyJob);
              seenIds.add(historyId);
            }
          }
        }

        // Sắp xếp thứ tự createdAtUtc giảm dần (mới nhất lên đầu)
        mergedJobs.sort(
          (a, b) => new Date(b.createdAtUtc).getTime() - new Date(a.createdAtUtc).getTime()
        );

        return mergedJobs;
      });
    } catch (err) {
      if (currentGen === fetchGenerationRef.current) {
        setError(err as ErrorDto);
      }
    } finally {
      if (currentGen === fetchGenerationRef.current) {
        setIsLoading(false);
      }
    }
  }, [workspaceId]);

  // Tự động tải dữ liệu ban đầu hoặc khi mở drawer
  useEffect(() => {
    if (autoFetch) {
      void refresh();
    }
  }, [refresh, autoFetch]);

  // Nếu options truyền isOpen, nạp lại khi mở drawer
  useEffect(() => {
    if (isOpen) {
      void refresh();
    }
  }, [isOpen, refresh]);

  // T17-R5: Hàm hydrate chi tiết tác vụ bất đồng bộ từ backend
  const hydrateJob = useCallback(async (wsId: string, jobId: string) => {
    try {
      const detail = await jobApi.getStatus(wsId, jobId);
      setJobs((prevJobs) =>
        prevJobs.map((j) => {
          if (j.id !== jobId) return j;
          return {
            ...j,
            taskType: detail.taskType,
            projectId: detail.projectId,
            sceneId: detail.sceneId,
            providerKey: detail.providerKey,
            profileId: detail.profileId ?? null,
            attempts: detail.attempts,
            maxAttempts: detail.maxAttempts,
            sideEffectSubmitted: detail.sideEffectSubmitted,
            cancelRequested: detail.cancelRequested,
            hasUnresolvedSideEffect: detail.hasUnresolvedSideEffect,
            resultReference: detail.resultReference ?? j.resultReference,
            isHydrating: false,
          };
        })
      );
    } catch (err) {
      console.warn(`[TiToAI Queue] Hydrate job ${jobId} failed:`, err);
      // Tắt trạng thái hydrating dù lỗi để không treo UI
      setJobs((prevJobs) =>
        prevJobs.map((j) => (j.id === jobId ? { ...j, isHydrating: false } : j))
      );
    }
  }, []);

  // Lắng nghe realtime events từ Bridge
  useEffect(() => {
    const unsubState = jobApi.onStateChanged((envelope) => {
      const payload = envelope.payload;
      if (!payload?.jobId) return;

      const eventTime = payload.timestampUtc
        ? new Date(payload.timestampUtc).getTime()
        : Date.now();
      const lastTime = eventTimestampsRef.current.get(payload.jobId) ?? 0;

      // Tránh event cũ hoặc đến trễ ghi đè trạng thái mới hơn
      if (eventTime < lastTime) {
        return;
      }
      eventTimestampsRef.current.set(payload.jobId, eventTime);

      setJobs((prevJobs) => {
        const existingIdx = prevJobs.findIndex((j) => j.id === payload.jobId);
        if (existingIdx >= 0) {
          const updated = [...prevJobs];
          const existing = updated[existingIdx];
          const updatedJob: JobItem = {
            ...existing,
            state: payload.newState,
            reasonCode: payload.reasonCode ?? null,
            resultReference: payload.resultReference ?? existing.resultReference,
            cancelRequested:
              payload.newState === "Canceled" ? false : existing.cancelRequested,
          };
          updated[existingIdx] = updatedJob;

          // T17-R6: Nếu chuyển sang terminal state, lưu vào history cache
          if (isTerminalState(payload.newState)) {
            completedHistoryRef.current.set(payload.jobId, updatedJob);
          }

          return updated;
        }

        // T17-R5 & T17-R6: Nếu job mới chưa có trong danh sách
        const isTerminal = isTerminalState(payload.newState);
        const newJob: JobItem = {
          id: payload.jobId,
          workspaceId: payload.workspaceId,
          projectId: payload.projectId ?? "",
          sceneId: null,
          // Đặt isHydrating: true cho non-terminal, taskType tạm thời không gán Script giả mạo
          taskType: isTerminal ? "Tác vụ hoàn tất" : "Đang tải...",
          state: payload.newState,
          reasonCode: payload.reasonCode ?? null,
          attempts: 1,
          progressPercent: null,
          currentStep: null,
          createdAtUtc: payload.timestampUtc || new Date().toISOString(),
          resultReference: payload.resultReference ?? null,
          isHydrating: !isTerminal,
        };

        if (isTerminal) {
          completedHistoryRef.current.set(payload.jobId, newJob);
        } else if (ACTIVE_STATES.includes(payload.newState)) {
          // Kích hoạt hydrate bất đồng bộ để lấy taskType, sceneId, attempts thật từ backend
          void hydrateJob(payload.workspaceId, payload.jobId);
        }

        const newList = [newJob, ...prevJobs];
        newList.sort(
          (a, b) => new Date(b.createdAtUtc).getTime() - new Date(a.createdAtUtc).getTime()
        );
        return newList;
      });
    });

    // T17-R1: onProgressUpdated bảo vệ tuyệt đối terminal state
    const unsubProgress = jobApi.onProgressUpdated((envelope) => {
      const payload = envelope.payload;
      if (!payload?.jobId) return;

      setJobs((prevJobs) => {
        const idx = prevJobs.findIndex((j) => j.id === payload.jobId);
        if (idx === -1) return prevJobs;

        const existing = prevJobs[idx];

        // T17-R1: NẾU JOB ĐÃ Ở TRẠNG THÁI TERMINAL (Succeeded, Failed, Canceled):
        // CẤM TUYỆT ĐỐI không cho progress event trễ đổi state về Running hay Queued!
        if (isTerminalState(existing.state)) {
          return prevJobs;
        }

        const updated = [...prevJobs];
        updated[idx] = {
          ...existing,
          progressPercent: payload.progressPercent,
          currentStep: payload.currentStep,
          // Chỉ cập nhật state nếu payload có state và không làm lùi terminal state
          state: payload.state || existing.state,
        };
        return updated;
      });
    });

    return () => {
      unsubState();
      unsubProgress();
    };
  }, [hydrateJob]);

  // Chi tiết job khi người dùng chọn
  const fetchJobDetail = useCallback(
    async (wsId: string, jobId: string): Promise<JobDetailDto | null> => {
      try {
        const detail = await jobApi.getStatus(wsId, jobId);
        setSelectedJobDetail(detail);

        // Đồng thời cập nhật metadata vào job trong danh sách nếu có
        setJobs((prevJobs) =>
          prevJobs.map((j) =>
            j.id === jobId
              ? {
                  ...j,
                  profileId: detail.profileId ?? j.profileId,
                  sideEffectSubmitted: detail.sideEffectSubmitted,
                  maxAttempts: detail.maxAttempts,
                  cancelRequested: detail.cancelRequested,
                  hasUnresolvedSideEffect: detail.hasUnresolvedSideEffect,
                }
              : j
          )
        );

        return detail;
      } catch (err) {
        setError(err as ErrorDto);
        return null;
      }
    },
    []
  );

  useEffect(() => {
    if (!selectedJobId) {
      setSelectedJobDetail(null);
      return;
    }
    const currentJob = jobs.find((j) => j.id === selectedJobId);
    if (currentJob) {
      void fetchJobDetail(currentJob.workspaceId, currentJob.id);
    }
  }, [selectedJobId, jobs, fetchJobDetail]);

  // Hành động Hủy tác vụ an toàn
  const handleCancel = useCallback(
    async (job: JobSummaryDto | JobDetailDto): Promise<boolean> => {
      if (actionLoadingRef.current.has(job.id)) {
        return false;
      }
      actionLoadingRef.current.add(job.id);
      setIsActionLoading((prev) => ({ ...prev, [job.id]: true }));
      try {
        const res = await jobApi.cancel(job.workspaceId, job.id);
        setJobs((prevJobs) =>
          prevJobs.map((j) => {
            if (j.id !== job.id) return j;
            const updated: JobItem = {
              ...j,
              state: res.state,
              cancelRequested: res.cancelRequested ?? true,
            };
            if (isTerminalState(res.state)) {
              completedHistoryRef.current.set(job.id, updated);
            }
            return updated;
          })
        );
        return true;
      } catch (err) {
        setError(err as ErrorDto);
        return false;
      } finally {
        actionLoadingRef.current.delete(job.id);
        setIsActionLoading((prev) => ({ ...prev, [job.id]: false }));
      }
    },
    []
  );

  // Hành động Tiếp tục tác vụ (chỉ áp dụng cho AwaitingUser)
  const handleResume = useCallback(
    async (job: JobSummaryDto | JobDetailDto): Promise<boolean> => {
      if (job.state !== "AwaitingUser") {
        setError(
          createErrorDto(
            "INVALID_STATE",
            `Chỉ có thể tiếp tục (resume) tác vụ ở trạng thái 'AwaitingUser' (trạng thái hiện tại: ${job.state}).`
          )
        );
        return false;
      }

      if (actionLoadingRef.current.has(job.id)) {
        return false;
      }
      actionLoadingRef.current.add(job.id);
      setIsActionLoading((prev) => ({ ...prev, [job.id]: true }));
      try {
        const res = await jobApi.resume(job.workspaceId, job.id);
        setJobs((prevJobs) =>
          prevJobs.map((j) =>
            j.id === job.id ? { ...j, state: res.state, reasonCode: null } : j
          )
        );
        return true;
      } catch (err) {
        setError(err as ErrorDto);
        return false;
      } finally {
        actionLoadingRef.current.delete(job.id);
        setIsActionLoading((prev) => ({ ...prev, [job.id]: false }));
      }
    },
    []
  );

  // Hành động Đối soát an toàn (reconcile)
  const handleReconcile = useCallback(
    async (request: JobReconcileRequest): Promise<boolean> => {
      if (actionLoadingRef.current.has(request.jobId)) {
        return false;
      }
      actionLoadingRef.current.add(request.jobId);
      setIsActionLoading((prev) => ({ ...prev, [request.jobId]: true }));
      try {
        const res = await jobApi.reconcile(request);
        setJobs((prevJobs) =>
          prevJobs.map((j) => {
            if (j.id !== request.jobId) return j;
            const updated: JobItem = {
              ...j,
              state: res.currentState,
              resultReference: res.resultReference ?? j.resultReference,
              reasonCode: null,
              hasUnresolvedSideEffect: false,
            };
            if (isTerminalState(res.currentState)) {
              completedHistoryRef.current.set(request.jobId, updated);
            }
            return updated;
          })
        );
        setReconcileJob(null);
        return true;
      } catch (err) {
        setError(err as ErrorDto);
        return false;
      } finally {
        actionLoadingRef.current.delete(request.jobId);
        setIsActionLoading((prev) => ({ ...prev, [request.jobId]: false }));
      }
    },
    []
  );

  // T17-R4: Yêu cầu mở trình duyệt đăng nhập thủ công cho profileId thực sự
  const handleRequestLogin = useCallback(
    async (profileId: string): Promise<boolean> => {
      if (!profileId || profileId.trim() === "") {
        setError(
          createErrorDto(
            "MISSING_PROFILE",
            "Tác vụ chưa được liên kết với hồ sơ trình duyệt (profileId). Vui lòng cấu hình tài khoản trong Cài đặt."
          )
        );
        return false;
      }

      try {
        await jobApi.requestLogin(profileId);
        return true;
      } catch (err) {
        setError(err as ErrorDto);
        return false;
      }
    },
    []
  );

  // T17-R2: Thử lại an toàn tuân thủ backend semantics và eligibility
  const handleRetry = useCallback(
    async (job: JobSummaryDto | JobDetailDto): Promise<boolean> => {
      // 1. Nếu tác vụ ở AwaitingUser: dùng resume
      if (job.state === "AwaitingUser") {
        return await handleResume(job);
      }

      // 2. Nếu tác vụ không phải Failed: không hỗ trợ retry trực tiếp
      if (job.state !== "Failed") {
        setError(
          createErrorDto(
            "INVALID_STATE",
            `Không thể thử lại tác vụ ở trạng thái '${job.state}'.`
          )
        );
        return false;
      }

      // 3. Hydrate metadata nếu summary hiện tại chưa có đầy đủ thông tin an toàn
      let sideEffect = job.sideEffectSubmitted;
      let attempts = job.attempts;
      let maxAttempts = job.maxAttempts;
      let cancelRequested = job.cancelRequested;
      let hasUnresolved = job.hasUnresolvedSideEffect;

      if (sideEffect === undefined || maxAttempts === undefined) {
        try {
          const detail = await jobApi.getStatus(job.workspaceId, job.id);
          sideEffect = detail.sideEffectSubmitted;
          attempts = detail.attempts;
          maxAttempts = detail.maxAttempts;
          cancelRequested = detail.cancelRequested;
          hasUnresolved = detail.hasUnresolvedSideEffect;
        } catch (fetchErr) {
          setError(fetchErr as ErrorDto);
          return false;
        }
      }

      // T17-R2: Kiểm tra các điều kiện an toàn trước khi gửi retry
      if (cancelRequested) {
        setError(
          createErrorDto(
            "CANNOT_RETRY_CANCELED",
            "Không thể thử lại tác vụ đã bị hủy bỏ bởi người dùng."
          )
        );
        return false;
      }

      if (sideEffect === 1 || hasUnresolved) {
        setError(
          createErrorDto(
            "UNCERTAIN_RETRY_BLOCKED",
            "Tác vụ có tác dụng phụ chưa xác định (side-effect). Vui lòng sử dụng chức năng Đối soát để xử lý an toàn."
          )
        );
        return false;
      }

      if (maxAttempts !== undefined && attempts >= maxAttempts) {
        setError(
          createErrorDto(
            "MAX_ATTEMPTS_EXCEEDED",
            `Tác vụ đã vượt quá số lần thử tối đa (${attempts}/${maxAttempts}). Vui lòng tạo tác vụ mới.`
          )
        );
        return false;
      }

      if (actionLoadingRef.current.has(job.id)) {
        return false;
      }
      actionLoadingRef.current.add(job.id);
      setIsActionLoading((prev) => ({ ...prev, [job.id]: true }));

      try {
        // Backend chấp nhận retry qua job.reconcile action="retry" với confirmedNotReceived=true
        const res = await jobApi.reconcile({
          workspaceId: job.workspaceId,
          jobId: job.id,
          action: "retry",
          confirmedNotReceived: true,
        });

        setJobs((prevJobs) =>
          prevJobs.map((j) =>
            j.id === job.id
              ? {
                  ...j,
                  state: res.currentState,
                  reasonCode: null,
                  attempts: (j.attempts || 1) + 1,
                }
              : j
          )
        );
        return true;
      } catch (err) {
        setError(err as ErrorDto);
        return false;
      } finally {
        actionLoadingRef.current.delete(job.id);
        setIsActionLoading((prev) => ({ ...prev, [job.id]: false }));
      }
    },
    [handleResume]
  );

  // Thống kê số lượng theo tab (tính trên toàn bộ jobs trước khi lọc search)
  const tabCounts = useMemo<Record<QueueTabFilter, number>>(() => {
    const counts: Record<QueueTabFilter, number> = {
      all: jobs.length,
      running: 0,
      awaiting: 0,
      failed: 0,
      completed: 0,
    };
    for (const job of jobs) {
      if (job.state === "Running" || job.state === "Reserved") {
        counts.running++;
      } else if (job.state === "AwaitingUser" || job.state === "Queued") {
        counts.awaiting++;
      } else if (job.state === "Failed") {
        counts.failed++;
      } else if (job.state === "Succeeded" || job.state === "Canceled") {
        counts.completed++;
      }
    }
    return counts;
  }, [jobs]);

  // Danh sách đã lọc theo activeTab và searchQuery
  const filteredJobs = useMemo(() => {
    const query = searchQuery.trim().toLowerCase();
    return jobs.filter((job) => {
      if (!matchesTab(job.state, activeTab)) {
        return false;
      }
      if (!query) {
        return true;
      }
      const matchId = job.id.toLowerCase().includes(query);
      const matchType = job.taskType.toLowerCase().includes(query);
      const matchStep = job.currentStep?.toLowerCase().includes(query) ?? false;
      const matchReason = job.reasonCode?.toLowerCase().includes(query) ?? false;
      const matchState = job.state.toLowerCase().includes(query);
      return matchId || matchType || matchStep || matchReason || matchState;
    });
  }, [jobs, activeTab, searchQuery]);

  return {
    jobs,
    filteredJobs,
    isLoading,
    error,
    activeTab,
    searchQuery,
    selectedJobId,
    selectedJobDetail,
    reconcileJob,
    isActionLoading,
    tabCounts,
    setActiveTab,
    setSearchQuery,
    setSelectedJobId,
    setReconcileJob,
    clearError,
    refresh,
    handleCancel,
    handleResume,
    handleReconcile,
    handleRequestLogin,
    handleRetry,
    fetchJobDetail,
  };
}
