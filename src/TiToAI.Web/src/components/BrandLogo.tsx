import type { SVGProps } from "react";

export interface BrandLogoProps extends SVGProps<SVGSVGElement> {
  size?: number;
  className?: string;
  color?: string;
}

export function BrandLogo({
  size = 24,
  className = "",
  color = "var(--primary, #6d3af2)",
  ...props
}: BrandLogoProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      className={className}
      aria-hidden="true"
      {...props}
    >
      <path
        d="M12 2L2 7L12 12L22 7L12 2Z"
        fill={color}
      />
      <path
        d="M2 17L12 22L22 17"
        stroke={color}
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <path
        d="M2 12L12 17L22 12"
        stroke={color}
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}
