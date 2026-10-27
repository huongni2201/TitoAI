import { useState, useEffect, type FormEvent } from "react";
import {
  Copy,
  Check,
  FileCode2,
  FileText,
  Image as ImageIcon,
  Video,
  AlertTriangle,
  Send,
  Loader2,
  CheckCircle2,
  RefreshCw,
  X,
  Sparkles,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardFooter, CardHeader, CardTitle, CardDescription } from "@/components/ui/card";
import { cn } from "@/lib/utils";
import {
  manualHandoffApi,
  type JobHandoffDto,
  type JobImportResultResponseData,
  type JobImportResultRequest,
} from "./manualHandoffApi";

export interface ManualHandoffPanelProps {
  workspaceId: string;
  jobId: string;
  initialHandoff?: JobHandoffDto;
  onSuccess?: (result: JobImportResultResponseData) => void;
  onCancel?: () => void;
  className?: string;
}

const SAMPLE_SCRIPT_JSON = JSON.stringify(
  {
    title: "Kịch bản mẫu giới thiệu sản phẩm",
    scenes: [
      {
        key: "scene_1",
        order: 1,
        name: "Mở đầu ấn tượng",
        description: "Góc quay cận cảnh sản phẩm với ánh sáng tự nhiên tinh tế.",
        targetDurationSeconds: 5,
        imagePrompt: "Cinematic shot of elegant product on minimal wooden desk, soft morning sunlight, 8k resolution",
      },
      {
        key: "scene_2",
        order: 2,
        name: "Tính năng nổi bật",
        description: "Góc máy lia ngang thể hiện các chi tiết gia công tinh xảo.",
        targetDurationSeconds: 8,
        imagePrompt: "Close-up macro pan of modern metallic device textures, dynamic reflections, photorealistic",
      },
    ],
  },
  null,
  2,
);

export function ManualHandoffPanel({
  workspaceId,
  jobId,
  initialHandoff,
  onSuccess,
  onCancel,
  className,
}: ManualHandoffPanelProps) {
  const [handoff, setHandoff] = useState<JobHandoffDto | null>(initialHandoff || null);
  const [isLoading, setIsLoading] = useState<boolean>(!initialHandoff);
  const [loadError, setLoadError] = useState<string | null>(null);

  // Form draft inputs — bảo toàn nội dung người dùng khi có lỗi
  const [scriptJsonDraft, setScriptJsonDraft] = useState<string>("");
  const [videoPromptDraft, setVideoPromptDraft] = useState<string>("");
  const [assetIdDraft, setAssetIdDraft] = useState<string>("");

  // Trạng thái kiểm tra cú pháp JSON client-side
  const [syntaxCheckResult, setSyntaxCheckResult] = useState<{
    valid: boolean;
    message: string;
  } | null>(null);

  // Trạng thái copy clipboard
  const [hasCopiedPrompt, setHasCopiedPrompt] = useState<boolean>(false);

  // Trạng thái submit
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [submitSuccess, setSubmitSuccess] = useState<JobImportResultResponseData | null>(null);

  // Tải dữ liệu handoff nếu chưa có
  const fetchHandoff = async () => {
    setIsLoading(true);
    setLoadError(null);
    try {
      const res = await manualHandoffApi.getHandoff(workspaceId, jobId);
      setHandoff(res.handoff);
    } catch (err: unknown) {
      const errorMsg =
        err && typeof err === "object" && "message" in err
          ? String((err as { message: unknown }).message)
          : "Không thể nạp thông tin tác vụ thủ công.";
      setLoadError(errorMsg);
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    if (initialHandoff) {
      setHandoff(initialHandoff);
      setIsLoading(false);
    } else {
      fetchHandoff();
    }
  }, [workspaceId, jobId, initialHandoff]);

  // Xử lý sao chép prompt
  const handleCopyPrompt = async () => {
    if (!handoff?.promptText) return;

    try {
      if (typeof navigator !== "undefined" && navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(handoff.promptText);
      }
      setHasCopiedPrompt(true);
      setTimeout(() => setHasCopiedPrompt(false), 2500);
    } catch {
      // Fallback sao chép thủ công nếu clipboard API bị chặn
      setHasCopiedPrompt(true);
      setTimeout(() => setHasCopiedPrompt(false), 2500);
    }
  };

  // Nạp mẫu JSON kịch bản
  const handleFillSample = () => {
    setScriptJsonDraft(SAMPLE_SCRIPT_JSON);
    setSyntaxCheckResult(null);
    setSubmitError(null);
  };

  // Kiểm tra cú pháp JSON kịch bản client-side
  const handleValidateScriptSyntax = () => {
    if (!scriptJsonDraft.trim()) {
      setSyntaxCheckResult({
        valid: false,
        message: "Nội dung kịch bản đang để trống. Vui lòng nhập chuỗi JSON.",
      });
      return;
    }

    try {
      const parsed = JSON.parse(scriptJsonDraft);
      if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) {
        setSyntaxCheckResult({
          valid: false,
          message: "Kịch bản phải là một JSON object với các trường 'title' và 'scenes'.",
        });
        return;
      }

      if (!parsed.title || typeof parsed.title !== "string" || !parsed.title.trim()) {
        setSyntaxCheckResult({
          valid: false,
          message: "Kịch bản thiếu trường 'title' (tiêu đề kịch bản không được để trống).",
        });
        return;
      }

      if (!Array.isArray(parsed.scenes) || parsed.scenes.length === 0) {
        setSyntaxCheckResult({
          valid: false,
          message: "Kịch bản phải có mảng 'scenes' chứa ít nhất 1 phân cảnh.",
        });
        return;
      }

      // Kiểm tra order và key
      const keys = new Set<string>();
      for (let i = 0; i < parsed.scenes.length; i++) {
        const sc = parsed.scenes[i];
        if (!sc.key || typeof sc.key !== "string") {
          setSyntaxCheckResult({
            valid: false,
            message: `Phân cảnh thứ ${i + 1} thiếu trường 'key' định danh.`,
          });
          return;
        }
        if (keys.has(sc.key)) {
          setSyntaxCheckResult({
            valid: false,
            message: `Mã phân cảnh 'key' '${sc.key}' bị trùng lặp.`,
          });
          return;
        }
        keys.add(sc.key);

        if (sc.order !== i + 1) {
          setSyntaxCheckResult({
            valid: false,
            message: `Phân cảnh thứ ${i + 1} có 'order' = ${sc.order}, yêu cầu phải là ${i + 1}.`,
          });
          return;
        }
      }

      setSyntaxCheckResult({
        valid: true,
        message: `Cú pháp JSON hoàn toàn hợp lệ (${parsed.scenes.length} phân cảnh).`,
      });
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : "Chuỗi JSON sai định dạng cú pháp.";
      setSyntaxCheckResult({
        valid: false,
        message: `Lỗi phân tích JSON: ${msg}`,
      });
    }
  };

  // Gửi kết quả thủ công lên backend
  const handleSubmit = async (e?: FormEvent | React.MouseEvent) => {
    if (e) e.preventDefault();
    if (!handoff) return;

    setSubmitError(null);
    setSubmitSuccess(null);

    // Chuẩn bị payload theo loại tác vụ
    const isScript = handoff.taskType.toLowerCase() === "script";
    const isVideoPrompt = handoff.taskType.toLowerCase() === "videoprompt";
    const isImage = handoff.taskType.toLowerCase() === "image";
    const isVideoRender = handoff.taskType.toLowerCase() === "videorender";

    let textResult: string | null = null;
    let assetId: string | null = null;

    if (isScript) {
      if (!scriptJsonDraft.trim()) {
        setSubmitError("Vui lòng nhập nội dung JSON kịch bản trước khi gửi.");
        return;
      }
      textResult = scriptJsonDraft.trim();
    } else if (isVideoPrompt) {
      if (!videoPromptDraft.trim()) {
        setSubmitError("Vui lòng nhập nội dung prompt chuyển động video trước khi gửi.");
        return;
      }
      textResult = videoPromptDraft.trim();
    } else if (isImage || isVideoRender) {
      if (!assetIdDraft.trim()) {
        setSubmitError(`Vui lòng nhập Asset ID cho tác vụ ${handoff.taskType}.`);
        return;
      }
      assetId = assetIdDraft.trim();
    } else {
      textResult = scriptJsonDraft || videoPromptDraft || null;
      assetId = assetIdDraft || null;
    }

    const clientRequestId =
      typeof crypto !== "undefined" && crypto.randomUUID
        ? crypto.randomUUID()
        : `req_${Date.now()}_${Math.random().toString(36).substring(2, 9)}`;

    const requestPayload: JobImportResultRequest = {
      workspaceId,
      jobId,
      expectedRevision: handoff.revision,
      clientRequestId,
      textResult,
      assetId,
    };

    setIsSubmitting(true);
    try {
      const res = await manualHandoffApi.importResult(requestPayload);
      setSubmitSuccess(res);
      if (onSuccess) {
        onSuccess(res);
      }
    } catch (err: unknown) {
      // Bảo toàn nội dung đang nhập (scriptJsonDraft, videoPromptDraft, assetIdDraft)
      const errorMsg =
        err && typeof err === "object" && "message" in err
          ? String((err as { message: unknown }).message)
          : "Gửi kết quả thủ công thất bại. Dữ liệu đang nhập đã được giữ nguyên.";
      setSubmitError(errorMsg);
    } finally {
      setIsSubmitting(false);
    }
  };

  const getTaskIcon = (type: string) => {
    switch (type.toLowerCase()) {
      case "script":
        return <FileCode2 className="h-4 w-4 text-primary" />;
      case "videoprompt":
        return <FileText className="h-4 w-4 text-emerald-500" />;
      case "image":
        return <ImageIcon className="h-4 w-4 text-blue-500" />;
      case "videorender":
        return <Video className="h-4 w-4 text-purple-500" />;
      default:
        return <Sparkles className="h-4 w-4 text-muted-foreground" />;
    }
  };

  if (isLoading) {
    return (
      <Card
        className={cn("w-full border-border bg-card", className)}
        role="region"
        aria-label="Đang tải bảng nhập kết quả thủ công"
        aria-busy="true"
      >
        <CardContent className="flex flex-col items-center justify-center p-8 space-y-3">
          <Loader2 className="h-8 w-8 animate-spin text-primary" />
          <p className="text-sm text-muted-foreground">Đang nạp thông tin handoff tác vụ...</p>
        </CardContent>
      </Card>
    );
  }

  if (loadError || !handoff) {
    return (
      <Card
        className={cn("w-full border-destructive/40 bg-card", className)}
        role="region"
        aria-label="Lỗi nạp bảng nhập kết quả thủ công"
      >
        <CardHeader>
          <CardTitle className="text-destructive flex items-center gap-2 text-base">
            <AlertTriangle className="h-5 w-5" />
            Không thể mở Handoff thủ công
          </CardTitle>
          <CardDescription>
            {loadError || "Không tìm thấy dữ liệu handoff cho tác vụ yêu cầu."}
          </CardDescription>
        </CardHeader>
        <CardFooter className="flex justify-between">
          {onCancel && (
            <Button variant="ghost" onClick={onCancel} type="button">
              Đóng
            </Button>
          )}
          <Button variant="outline" onClick={fetchHandoff} type="button">
            <RefreshCw className="h-4 w-4 mr-2" />
            Thử lại
          </Button>
        </CardFooter>
      </Card>
    );
  }

  const isScript = handoff.taskType.toLowerCase() === "script";
  const isVideoPrompt = handoff.taskType.toLowerCase() === "videoprompt";
  const isImage = handoff.taskType.toLowerCase() === "image";
  const isVideoRender = handoff.taskType.toLowerCase() === "videorender";

  return (
    <Card
      className={cn("w-full border-border bg-card text-card-foreground shadow-sm", className)}
      role="region"
      aria-label="Bảng nhập kết quả thủ công"
      aria-busy={isSubmitting}
    >
      <form onSubmit={handleSubmit}>
        {/* Header với thông tin Job */}
        <CardHeader className="pb-3 border-b border-border">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2">
              <span className="p-1.5 rounded-md bg-muted border border-border">
                {getTaskIcon(handoff.taskType)}
              </span>
              <div>
                <CardTitle className="text-base font-semibold flex items-center gap-2">
                  Nhập kết quả thủ công (Manual Handoff)
                  <Badge variant="outline" className="text-xs font-normal">
                    {handoff.taskType}
                  </Badge>
                </CardTitle>
                <CardDescription className="text-xs font-mono mt-0.5 text-muted-foreground">
                  Job ID: {handoff.jobId} • Rev: {handoff.revision} • Provider: {handoff.providerKey}
                </CardDescription>
              </div>
            </div>
            {onCancel && (
              <Button
                variant="ghost"
                size="icon"
                onClick={onCancel}
                className="h-8 w-8 text-muted-foreground hover:text-foreground"
                type="button"
                aria-label="Đóng bảng handoff"
              >
                <X className="h-4 w-4" />
              </Button>
            )}
          </div>
        </CardHeader>

        <CardContent className="space-y-4 pt-4">
          {/* Cảnh báo unresolved side effect nếu có */}
          {handoff.hasUnresolvedSideEffect && (
            <Alert
              variant="default"
              className="border-amber-500/50 bg-amber-500/10 text-amber-900 dark:text-amber-200"
              role="alert"
            >
              <AlertTriangle className="h-4 w-4 text-amber-600 dark:text-amber-400" />
              <AlertTitle className="font-medium text-sm">Cảnh báo tác dụng phụ chưa đối soát</AlertTitle>
              <AlertDescription className="text-xs mt-1 leading-relaxed">
                Tác vụ này đã phát sinh yêu cầu tới AI provider ({handoff.providerKey}) trước đó nhưng bị gián đoạn.
                Vui lòng kiểm tra trên website dịch vụ AI để chắc chắn trạng thái sinh trước khi dán kết quả, tránh trùng lặp.
              </AlertDescription>
            </Alert>
          )}

          {/* Hướng dẫn thao tác */}
          <div className="rounded-lg border border-border bg-muted/40 p-3 space-y-1.5">
            <h4 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
              Hướng dẫn thực hiện
            </h4>
            <div className="text-xs text-foreground/90 whitespace-pre-line leading-relaxed">
              {handoff.instructions}
            </div>
          </div>

          {/* Prompt cần copy */}
          <div className="space-y-1.5">
            <div className="flex items-center justify-between">
              <Label htmlFor="prompt-display" className="text-xs font-medium text-foreground">
                Prompt đã snapshot (Đóng băng bất biến)
              </Label>
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={handleCopyPrompt}
                className="h-7 text-xs px-2 gap-1.5 border-border"
                aria-label="Sao chép nội dung prompt vào clipboard"
              >
                {hasCopiedPrompt ? (
                  <>
                    <Check className="h-3.5 w-3.5 text-emerald-500" />
                    <span className="text-emerald-600 dark:text-emerald-400 font-medium">Đã sao chép!</span>
                  </>
                ) : (
                  <>
                    <Copy className="h-3.5 w-3.5 text-muted-foreground" />
                    <span>Sao chép Prompt</span>
                  </>
                )}
              </Button>
            </div>
            <div
              id="prompt-display"
              tabIndex={0}
              role="textbox"
              aria-label="Nội dung prompt đã snapshot"
              aria-readonly="true"
              className="relative max-h-36 overflow-y-auto rounded-md border border-border bg-muted/20 p-2.5 font-mono text-xs leading-relaxed text-foreground select-text focus:outline-none focus:ring-1 focus:ring-ring"
            >
              {handoff.promptText || "(Không có prompt text trong snapshot)"}
            </div>
          </div>

          {/* Vùng nhập kết quả theo loại tác vụ */}
          {isScript && (
            <div className="space-y-2">
              <div className="flex items-center justify-between">
                <Label htmlFor="script-json-input" className="text-xs font-medium text-foreground">
                  Kịch bản JSON (<code className="text-[11px] font-mono">script_json_v1</code>)
                </Label>
                <div className="flex items-center gap-1.5">
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onClick={handleFillSample}
                    className="h-6 text-[11px] px-2 text-muted-foreground hover:text-foreground"
                  >
                    Dán mẫu JSON
                  </Button>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={handleValidateScriptSyntax}
                    className="h-6 text-[11px] px-2"
                  >
                    Kiểm tra cú pháp
                  </Button>
                </div>
              </div>
              <Textarea
                id="script-json-input"
                value={scriptJsonDraft}
                onChange={(e) => {
                  setScriptJsonDraft(e.target.value);
                  setSyntaxCheckResult(null);
                  setSubmitError(null);
                }}
                placeholder='{\n  "title": "Tên kịch bản...",\n  "scenes": [\n    {\n      "key": "scene_1",\n      "order": 1,\n      "name": "Cảnh 1",\n      "description": "Mô tả...",\n      "targetDurationSeconds": 15,\n      "imagePrompt": "Prompt..."\n    }\n  ]\n}'
                rows={8}
                className="font-mono text-xs resize-y border-border bg-background focus:ring-ring"
                aria-invalid={syntaxCheckResult?.valid === false}
                aria-describedby={syntaxCheckResult ? "syntax-check-msg" : undefined}
                required
              />
              {syntaxCheckResult && (
                <p
                  id="syntax-check-msg"
                  className={cn(
                    "text-xs font-medium flex items-center gap-1.5 mt-1",
                    syntaxCheckResult.valid
                      ? "text-emerald-600 dark:text-emerald-400"
                      : "text-destructive",
                  )}
                  role="status"
                >
                  {syntaxCheckResult.valid ? (
                    <CheckCircle2 className="h-3.5 w-3.5" />
                  ) : (
                    <AlertTriangle className="h-3.5 w-3.5" />
                  )}
                  {syntaxCheckResult.message}
                </p>
              )}
            </div>
          )}

          {isVideoPrompt && (
            <div className="space-y-1.5">
              <div className="flex items-center justify-between">
                <Label htmlFor="videoprompt-input" className="text-xs font-medium text-foreground">
                  Nội dung Prompt chuyển động video (<code className="text-[11px] font-mono">text_v1</code>)
                </Label>
                <span className="text-[11px] text-muted-foreground">
                  {videoPromptDraft.length} / 20.000 ký tự
                </span>
              </div>
              <Textarea
                id="videoprompt-input"
                value={videoPromptDraft}
                onChange={(e) => {
                  setVideoPromptDraft(e.target.value);
                  setSubmitError(null);
                }}
                placeholder="Nhập mô tả góc máy, chuyển động camera hoặc hiệu ứng video..."
                rows={5}
                maxLength={20000}
                className="font-mono text-xs resize-y border-border bg-background focus:ring-ring"
                required
              />
            </div>
          )}

          {(isImage || isVideoRender) && (
            <div className="space-y-1.5">
              <Label htmlFor="asset-id-input" className="text-xs font-medium text-foreground">
                Mã tài sản ({handoff.expectedOutputSchema})
              </Label>
              <Input
                id="asset-id-input"
                type="text"
                value={assetIdDraft}
                onChange={(e) => {
                  setAssetIdDraft(e.target.value);
                  setSubmitError(null);
                }}
                placeholder={isImage ? "asset_img_xxxxxxxx" : "asset_vid_xxxxxxxx"}
                className="font-mono text-xs border-border bg-background focus:ring-ring"
                required
              />
              <p className="text-[11px] text-muted-foreground">
                Nhập Asset ID của file {isImage ? "ảnh" : "video"} đã nạp vào thư viện Workspace của dự án.
              </p>
            </div>
          )}

          {/* Thông báo lỗi khi submit (bảo toàn form draft) */}
          {submitError && (
            <Alert
              variant="destructive"
              className="py-2.5 text-xs border-destructive/40 bg-destructive/10 text-destructive dark:text-red-300"
              role="alert"
            >
              <AlertTriangle className="h-4 w-4" />
              <AlertTitle className="font-semibold text-xs">Không thể nhập kết quả</AlertTitle>
              <AlertDescription className="mt-0.5 leading-relaxed">{submitError}</AlertDescription>
            </Alert>
          )}

          {/* Thông báo thành công */}
          {submitSuccess && (
            <Alert
              variant="default"
              className="py-2.5 text-xs border-emerald-500/50 bg-emerald-500/10 text-emerald-900 dark:text-emerald-200"
              role="status"
            >
              <CheckCircle2 className="h-4 w-4 text-emerald-600 dark:text-emerald-400" />
              <AlertTitle className="font-semibold text-xs">Nhập kết quả thành công</AlertTitle>
              <AlertDescription className="mt-0.5 leading-relaxed">
                Tác vụ đã được cập nhật thành công sang trạng thái{" "}
                <span className="font-semibold">{submitSuccess.state}</span> (Rev: {submitSuccess.revision}).
                {submitSuccess.wasCanceled && " (Tác vụ đã hủy: kết quả được bảo toàn dạng Unlinked)."}
              </AlertDescription>
            </Alert>
          )}
        </CardContent>

        <CardFooter className="flex items-center justify-between pt-3 border-t border-border">
          <div>
            {onCancel && (
              <Button
                variant="ghost"
                size="sm"
                onClick={onCancel}
                disabled={isSubmitting}
                type="button"
                className="text-xs text-muted-foreground hover:text-foreground"
              >
                Hủy bỏ
              </Button>
            )}
          </div>
          <Button
            type="submit"
            size="sm"
            onClick={handleSubmit}
            disabled={isSubmitting || !!submitSuccess}
            className="text-xs gap-1.5 min-w-[140px]"
          >
            {isSubmitting ? (
              <>
                <Loader2 className="h-3.5 w-3.5 animate-spin" />
                <span>Đang xử lý...</span>
              </>
            ) : (
              <>
                <Send className="h-3.5 w-3.5" />
                <span>Gửi kết quả thủ công</span>
              </>
            )}
          </Button>
        </CardFooter>
      </form>
    </Card>
  );
}
