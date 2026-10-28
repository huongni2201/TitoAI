import React, { useCallback, useRef, useEffect, useState } from "react";
import type { BrowserProfileDto } from "./browserManagerApi";
import { browserManagerApi } from "./browserManagerApi";
import { LoginStatusBanner } from "./LoginStatusBanner";
import { ProfileCard } from "./ProfileCard";
import { Button } from "@/components/ui/button";
import { Card, CardTitle, CardDescription } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Alert, AlertTitle, AlertDescription } from "@/components/ui/alert";
import { Globe, RefreshCw, AlertCircle, Bot, Video, Smartphone } from "lucide-react";
import { cn } from "@/lib/utils";

export const BrowserManagerPage: React.FC = () => {
  const [profiles, setProfiles] = useState<BrowserProfileDto[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const requestId = useRef(0);

  const fetchProfiles = useCallback(async () => {
    const generation = ++requestId.current;
    setIsLoading(true);
    setErrorMessage(null);
    try {
      const data = await browserManagerApi.listProfiles();
      if (generation !== requestId.current) return;
      setProfiles(data.filter((profile) => !profile.lifecycle || profile.lifecycle === "Active"));
    } catch (err) {
      if (generation !== requestId.current) return;
      console.error("[BrowserManagerPage] Lỗi tải profiles:", err);
      setErrorMessage(
        err && typeof err === "object" && "message" in err
          ? String((err as { message: string }).message)
          : "Không thể kết nối đến dịch vụ quản lý trình duyệt của Host."
      );
    } finally {
      if (generation === requestId.current) setIsLoading(false);
    }
  }, []);

  // Tải profiles cấp ứng dụng khi mở trang.
  useEffect(() => {
    void fetchProfiles();
    return () => {
      requestId.current += 1;
    };
  }, [fetchProfiles]);

  // Lắng nghe sự kiện browser.profile_state_changed từ WebView2 Host / Mock
  useEffect(() => {
    const unsubscribe = browserManagerApi.onProfileStateChanged((event) => {
      const updated = event.payload;
      if (!updated || !updated.profileId) return;

      setProfiles((prev) =>
        prev.map((item) => {
          if (item.profileId === updated.profileId) {
            return {
              ...item,
              visibilityState: updated.visibilityState,
              sessionStatus: updated.sessionStatus,
              operationalState: updated.operationalState,
            };
          }
          return item;
        })
      );
    });

    return () => {
      unsubscribe();
    };
  }, []);

  // Phân chia profiles theo nhóm
  const sharedAiProfiles = profiles.filter((p) => p.kind === "SharedAI");
  const museAiProfiles = profiles.filter((p) => p.kind === "MuseAI");
  const tikTokProfiles = profiles.filter((p) => p.kind === "TikTok");

  return (
    <div className="flex flex-col gap-6 p-6 max-w-6xl mx-auto w-full" aria-label="Tài khoản và phiên đăng nhập">
      {/* 1. HEADER TRANG */}
      <div className="flex flex-wrap items-center justify-between gap-4 pb-2 border-b border-border/50">
        <div>
          <div className="flex items-center gap-2.5">
            <Globe className="h-6 w-6 text-primary" />
            <h2 className="text-2xl font-bold tracking-tight text-foreground">
              Quản lý Trình duyệt &amp; Phiên đăng nhập
            </h2>
          </div>
          <p className="text-sm text-muted-foreground mt-1 max-w-3xl">
            TiToAI tự động hóa các tác vụ tạo kịch bản, ảnh và video ở chế độ{" "}
            <strong className="text-foreground">chạy ẩn</strong> trên máy tính. Cửa sổ trình duyệt chỉ hiển thị khi cần bạn{" "}
            <strong className="text-foreground">đăng nhập thủ công</strong> hoặc xác thực tài khoản.
          </p>
        </div>
        <div>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={fetchProfiles}
            disabled={isLoading}
            aria-label="Tải lại danh sách trình duyệt và trạng thái phiên"
            className="gap-2 shadow-xs"
          >
            <RefreshCw className={cn("h-4 w-4", isLoading && "animate-spin")} />
            {isLoading ? "Đang tải…" : "Làm mới"}
          </Button>
        </div>
      </div>

      {/* 2. BANNER TRẠNG THÁI ĐĂNG NHẬP (ACTIVE / WAITING / EXPIRED) */}
      <LoginStatusBanner profiles={profiles} onActionComplete={fetchProfiles} />

      {/* 3. VÙNG NỘI DUNG CHÍNH */}
      {isLoading ? (
        <div
          className="flex flex-col items-center justify-center p-12 text-muted-foreground gap-3 rounded-xl border border-dashed border-border/60 bg-card/40"
          role="status"
        >
          <RefreshCw className="h-8 w-8 animate-spin text-primary" />
          <p className="text-sm font-medium">Đang kiểm tra trạng thái trình duyệt từ host…</p>
        </div>
      ) : errorMessage ? (
        <Alert variant="destructive" role="alert">
          <AlertCircle className="h-4 w-4" />
          <AlertTitle>Không thể tải danh sách trình duyệt</AlertTitle>
          <AlertDescription className="flex flex-wrap items-center justify-between gap-4 mt-2">
            <span>{errorMessage}</span>
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={fetchProfiles}
              className="gap-1.5"
            >
              <RefreshCw className="h-3.5 w-3.5" /> Thử lại
            </Button>
          </AlertDescription>
        </Alert>
      ) : profiles.length === 0 ? (
        <Card className="p-8 text-center border-dashed flex flex-col items-center justify-center gap-3 bg-card/40">
          <div className="h-12 w-12 rounded-full bg-muted flex items-center justify-center text-muted-foreground">
            <Globe className="h-6 w-6" />
          </div>
          <CardTitle className="text-base font-semibold">Chưa có hồ sơ trình duyệt nào</CardTitle>
          <CardDescription className="max-w-md text-sm">
            Hệ thống chưa ghi nhận cấu hình trình duyệt nào. Bạn có thể thêm tài khoản AI mới tại Cài đặt → Tài khoản.
          </CardDescription>
        </Card>
      ) : (
        <div className="flex flex-col gap-6">
          {/* NHÓM 1: SHARED AI PROFILES */}
          <section className="rounded-xl border border-border bg-card p-5 space-y-4 shadow-2xs" aria-labelledby="group-shared-ai">
            <div className="flex items-start justify-between gap-4 pb-3 border-b border-border/50">
              <div className="flex items-start gap-2.5">
                <Bot className="h-5 w-5 text-primary shrink-0 mt-0.5" />
                <div>
                  <h3 id="group-shared-ai" className="font-bold text-base text-foreground">
                    Hồ sơ Trình duyệt AI (ChatGPT / Gemini)
                  </h3>
                  <p className="text-xs text-muted-foreground mt-0.5 leading-relaxed">
                    Điều phối các tác vụ tạo kịch bản, ảnh và prompt video ở chế độ chạy ẩn. Các tác vụ chạy theo các tab độc lập.
                  </p>
                </div>
              </div>
              <Badge variant="secondary" className="text-xs shrink-0 font-semibold">
                {sharedAiProfiles.length} hồ sơ
              </Badge>
            </div>
            {sharedAiProfiles.length === 0 ? (
              <div className="rounded-lg border border-dashed border-border/80 p-6 text-center text-xs text-muted-foreground bg-muted/20">
                Chưa có tài khoản AI nào được cấu hình trong hệ thống.
              </div>
            ) : (
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
                {sharedAiProfiles.map((p) => (
                  <ProfileCard
                    key={p.profileId}
                    profile={p}
                    onActionComplete={fetchProfiles}
                  />
                ))}
              </div>
            )}
          </section>

          {/* NHÓM 2: MUSEAI PROFILES */}
          <section className="rounded-xl border border-border bg-card p-5 space-y-4 shadow-2xs" aria-labelledby="group-museai">
            <div className="flex items-start justify-between gap-4 pb-3 border-b border-border/50">
              <div className="flex items-start gap-2.5">
                <Video className="h-5 w-5 text-primary shrink-0 mt-0.5" />
                <div>
                  <h3 id="group-museai" className="font-bold text-base text-foreground">
                    Hồ sơ Trình duyệt MuseAI Video
                  </h3>
                  <p className="text-xs text-muted-foreground mt-0.5 leading-relaxed">
                    Tài khoản tạo video thành phẩm. Mỗi tài khoản giữ hồ sơ và phiên đăng nhập riêng biệt.
                  </p>
                </div>
              </div>
              <Badge variant="secondary" className="text-xs shrink-0 font-semibold">
                {museAiProfiles.length} hồ sơ
              </Badge>
            </div>
            {museAiProfiles.length === 0 ? (
              <div className="rounded-lg border border-dashed border-border/80 p-6 text-center text-xs text-muted-foreground bg-muted/20">
                Chưa có tài khoản MuseAI nào trong pool toàn hệ thống.
              </div>
            ) : (
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
                {museAiProfiles.map((p) => (
                  <ProfileCard
                    key={p.profileId}
                    profile={p}
                    onActionComplete={fetchProfiles}
                  />
                ))}
              </div>
            )}
          </section>

          {/* NHÓM 3: TIKTOK PROFILES */}
          <section className="rounded-xl border border-border bg-card p-5 space-y-4 shadow-2xs" aria-labelledby="group-tiktok">
            <div className="flex items-start justify-between gap-4 pb-3 border-b border-border/50">
              <div className="flex items-start gap-2.5">
                <Smartphone className="h-5 w-5 text-primary shrink-0 mt-0.5" />
                <div>
                  <h3 id="group-tiktok" className="font-bold text-base text-foreground">
                    Hồ sơ Trình duyệt TikTok
                  </h3>
                  <p className="text-xs text-muted-foreground mt-0.5 leading-relaxed">
                    Quản lý phiên đăng nhập TikTok trong Cài đặt → Tài khoản.
                  </p>
                </div>
              </div>
              <Badge variant="secondary" className="text-xs shrink-0 font-semibold">
                {tikTokProfiles.length} hồ sơ
              </Badge>
            </div>
            {tikTokProfiles.length === 0 ? (
              <div className="rounded-lg border border-dashed border-border/80 p-6 text-center text-xs text-muted-foreground bg-muted/20">
                Chưa có tài khoản TikTok nào được liên kết cho phạm vi này.
              </div>
            ) : (
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
                {tikTokProfiles.map((p) => (
                  <ProfileCard
                    key={p.profileId}
                    profile={p}
                    onActionComplete={fetchProfiles}
                  />
                ))}
              </div>
            )}
          </section>
        </div>
      )}
    </div>
  );
};
