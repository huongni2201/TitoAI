import React from "react";
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
  const definitionsMap = React.useMemo(() => {
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
        className="prompt-alert prompt-alert-info"
        style={{ fontSize: "0.8rem" }}
      >
        <div>ℹ️</div>
        <div>
          Template hiện không chứa biến số nào. Thêm biến vào nội dung theo định dạng{" "}
          <code>&#123;tên_biến&#125;</code> (ví dụ: <code>&#123;chủ_đề&#125;</code>,{" "}
          <code>&#123;phong_cách&#125;</code>).
        </div>
      </div>
    );
  }

  return (
    <div className="prompt-form-group">
      <div className="prompt-form-label">
        <span>Danh sách biến số ({variables.length})</span>
        <span style={{ fontSize: "0.75rem", color: "var(--text-muted)" }}>
          Giá trị truyền vào khi render preview
        </span>
      </div>

      <div style={{ display: "flex", flexDirection: "column", gap: "0.65rem" }}>
        {variables.map((varName) => {
          const def = definitionsMap.get(varName);
          const val = values[varName] ?? "";
          const isRequired = def?.isRequired ?? true;
          const isMissing = isRequired && val.trim() === "";

          return (
            <div
              key={varName}
              style={{
                display: "flex",
                flexDirection: "column",
                gap: "0.25rem",
                padding: "0.5rem 0.65rem",
                backgroundColor: "var(--surface-card)",
                border: `1px solid ${isMissing ? "var(--status-warning)" : "var(--border-subtle)"}`,
                borderRadius: "var(--radius-md)",
              }}
            >
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                  fontSize: "0.8rem",
                }}
              >
                <label
                  htmlFor={`var-input-${varName}`}
                  style={{
                    fontFamily: "monospace",
                    fontWeight: 600,
                    color: "var(--text-primary)",
                  }}
                >
                  &#123;{varName}&#125;
                  {isRequired && (
                    <span
                      style={{ color: "var(--status-error)", marginLeft: "0.25rem" }}
                      title="Biến bắt buộc"
                    >
                      *
                    </span>
                  )}
                </label>

                {def?.description && (
                  <span
                    style={{
                      fontSize: "0.7rem",
                      color: "var(--text-muted)",
                      maxWidth: "200px",
                      overflow: "hidden",
                      textOverflow: "ellipsis",
                      whiteSpace: "nowrap",
                    }}
                    title={def.description}
                  >
                    {def.description}
                  </span>
                )}
              </div>

              <input
                id={`var-input-${varName}`}
                type="text"
                className="prompt-input"
                style={{ height: "32px", fontSize: "0.8rem" }}
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
