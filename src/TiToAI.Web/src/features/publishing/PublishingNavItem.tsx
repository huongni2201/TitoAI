import { Send } from "lucide-react";
import { cn } from "@/lib/utils";

export function PublishingNavItem({ active, onClick }: { active: boolean; onClick: () => void }) {
  return (
    <button type="button" onClick={onClick} aria-current={active ? "page" : undefined}
      className={cn("nav-item flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm transition-colors text-left cursor-pointer",
        active ? "bg-primary/10 text-primary font-medium border-l-2 border-primary" : "text-sidebar-foreground hover:bg-muted/60 hover:text-foreground")}>
      <Send className="size-4 shrink-0" aria-hidden="true" />
      <span>Đăng video</span>
    </button>
  );
}
