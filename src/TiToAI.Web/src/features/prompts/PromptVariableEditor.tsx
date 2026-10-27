import { useMemo } from "react";
import { Info } from "lucide-react";
import { cn } from "../../lib/utils.ts";
import type { PromptVariableDefinition } from "./promptApi.ts";

export interface PromptVariableEditorProps {
  variables: string[];
  values: Record<string, string>;
  definitions?: PromptVariableDefinition[] | null;
  onChange: (name: string, value: string) => void;
  disabled?: boolean;
}

export function PromptVariableEditor({
  variables,
  values,
  definitions = [],
  onChange,
  disabled = false,
}: PromptVariableEditorProps) {
  const definitionsMap = useMemo(() => {
    const map = new Map<string, PromptVariableDefinition>();
    if (definitions) {
      for (const def of definitions) {
        map.set(def.name, def);
      }
    }
    return map;
  }, [definitions]);

  if (variables.length === 0) {
    return (
      <div
        className="relative w-full rounded-lg border border-border/80 bg-muted/40 p-3.5 text-xs text-muted-foreground shadow-xs flex items-start gap-2.5"
        role="status"
      >
        <Info className="h-4 w-4 shrink-0 text-primary mt-0.5" />
        <div className="leading-relaxed">
          Template hiện không chứa biến số nào. Thêm biến vào nội dung theo định dạng{" "}
          <code className="inline-flex items-center rounded-md border border-border bg-background px-1.5 py-0.5 font-mono text-[11px] font-medium text-foreground shadow-2xs">
            &#123;tên_biến&#125;
          </code>{" "}
          (ví dụ:{" "}
          <code className="inline-flex items-center rounded-md border border-border bg-background px-1.5 py-0.5 font-mono text-[11px] font-medium text-foreground shadow-2xs">
            &#123;chủ_đề&#125;
          </code>
          ,{" "}
          <code className="inline-flex items-center rounded-md border border-border bg-background px-1.5 py-0.5 font-mono text-[11px] font-medium text-foreground shadow-2xs">
            &#123;phong_cách&#125;
          </code>
          ).
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-2.5">
      <div className="flex items-center justify-between pb-0.5">
        <span className="text-xs font-semibold text-foreground">
          Danh sách biến số ({variables.length})
        </span>
        <span className="text-[11px] text-muted-foreground">
          Giá trị truyền vào khi render preview
        </span>
      </div>

      <div className="flex flex-col gap-2">
        {variables.map((varName) => {
          const def = definitionsMap.get(varName);
          const val = values[varName] ?? "";
          const isRequired = def?.isRequired ?? true;
          const isMissing = isRequired && val.trim() === "";

          return (
            <div
              key={varName}
              className={cn(
                "flex flex-col gap-1.5 rounded-lg border p-3 transition-colors bg-card shadow-xs",
                isMissing
                  ? "border-amber-500/60 bg-amber-500/5 dark:bg-amber-500/10"
                  : "border-border/80 hover:border-border"
              )}
            >
              <div className="flex items-center justify-between text-xs">
                <label
                  htmlFor={`var-input-${varName}`}
                  className="font-mono font-semibold text-foreground flex items-center gap-1 cursor-pointer"
                >
                  &#123;{varName}&#125;
                  {isRequired && (
                    <span className="text-destructive font-bold text-xs" title="Biến bắt buộc">
                      *
                    </span>
                  )}
                </label>

                {def?.description && (
                  <span
                    className="text-[11px] text-muted-foreground max-w-[200px] truncate"
                    title={def.description}
                  >
                    {def.description}
                  </span>
                )}
              </div>

              <input
                id={`var-input-${varName}`}
                type="text"
                className="flex h-8 w-full rounded-md border border-input bg-background px-3 py-1 text-xs font-mono text-foreground shadow-xs transition-colors placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50"
                value={val}
                placeholder={
                  def?.example
                    ? `Ví dụ: ${def.example}`
                    : def?.defaultValue
                      ? `Mặc định: ${def.defaultValue}`
                      : `Nhập giá trị cho {${varName}}...`
                }
                onChange={(e) => onChange(varName, e.target.value)}
                disabled={disabled}
                aria-required={isRequired}
              />
            </div>
          );
        })}
      </div>
    </div>
  );
}
