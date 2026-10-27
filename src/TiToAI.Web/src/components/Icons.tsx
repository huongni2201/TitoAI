import type { LucideIcon, LucideProps } from "lucide-react";
import {
  AlertCircle,
  AlertTriangle,
  BarChart2,
  Check,
  CheckCircle2,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  ChevronUp,
  Clock,
  Copy,
  Edit,
  ExternalLink,
  Eye,
  Film,
  Folder,
  FolderKanban,
  Globe,
  HardDrive,
  Image,
  Info,
  Layers,
  LayoutDashboard,
  ListTodo,
  MoreVertical,
  Music,
  Pause,
  Play,
  Plus,
  RefreshCw,
  Search,
  Settings,
  Sliders,
  Sparkles,
  Sun,
  Trash2,
  Upload,
  User,
  Video,
  X,
} from "lucide-react";
import type { SVGProps } from "react";

export type IconProps = SVGProps<SVGSVGElement> &
  Omit<LucideProps, "ref"> & {
    size?: number | string;
    color?: string;
    className?: string;
  };

function createIcon(IconComponent: LucideIcon, defaultSize = 16) {
  return function IconWrapper({
    size = defaultSize,
    color = "currentColor",
    className = "",
    ...props
  }: IconProps) {
    return (
      <IconComponent
        size={size}
        color={color}
        className={className}
        aria-hidden="true"
        {...props}
      />
    );
  };
}

export const IconOverview = createIcon(LayoutDashboard, 18);
export const IconWorkspace = createIcon(FolderKanban, 18);
export const IconProject = createIcon(Video, 18);
export const IconBrowser = createIcon(Globe, 18);
export const IconLibrary = createIcon(Image, 18);
export const IconSettings = createIcon(Settings, 18);
export const IconQueue = createIcon(ListTodo, 18);
export const IconTheme = createIcon(Sun, 18);
export const IconSearch = createIcon(Search, 16);
export const IconPlus = createIcon(Plus, 16);
export const IconClose = createIcon(X, 16);
export const IconCheck = createIcon(Check, 16);
export const IconInfo = createIcon(Info, 16);
export const IconRefresh = createIcon(RefreshCw, 16);
export const IconHardDrive = createIcon(HardDrive, 16);
export const IconChevronDown = createIcon(ChevronDown, 16);
export const IconChevronUp = createIcon(ChevronUp, 16);
export const IconChevronRight = createIcon(ChevronRight, 16);
export const IconChevronLeft = createIcon(ChevronLeft, 16);
export const IconEdit = createIcon(Edit, 16);
export const IconTrash = createIcon(Trash2, 16);
export const IconPlay = createIcon(Play, 16);
export const IconPause = createIcon(Pause, 16);
export const IconWarning = createIcon(AlertTriangle, 16);
export const IconSparkles = createIcon(Sparkles, 16);
export const IconFilm = createIcon(Film, 16);
export const IconChart = createIcon(BarChart2, 16);
export const IconFolder = createIcon(Folder, 16);
export const IconImage = createIcon(Image, 16);
export const IconMusic = createIcon(Music, 16);
export const IconCopy = createIcon(Copy, 16);
export const IconExternalLink = createIcon(ExternalLink, 16);
export const IconSliders = createIcon(Sliders, 16);
export const IconCheckCircle = createIcon(CheckCircle2, 16);
export const IconAlertCircle = createIcon(AlertCircle, 16);
export const IconClock = createIcon(Clock, 16);
export const IconMoreVertical = createIcon(MoreVertical, 16);
export const IconLayers = createIcon(Layers, 16);
export const IconUser = createIcon(User, 16);
export const IconEye = createIcon(Eye, 16);
export const IconUpload = createIcon(Upload, 16);

export function IconTikTok({
  size = 16,
  color = "currentColor",
  className = "",
  ...props
}: IconProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill={color}
      className={className}
      aria-hidden="true"
      {...props}
    >
      <path d="M19.59 6.69a4.83 4.83 0 0 1-3.77-4.25V2h-3.45v13.67a2.89 2.89 0 0 1-5.2 1.74 2.89 2.89 0 0 1 2.31-4.64 2.93 2.93 0 0 1 .88.13V9.4a6.84 6.84 0 0 0-1-.05A6.33 6.33 0 0 0 3 15.68 6.34 6.34 0 0 0 9.35 22a6.34 6.34 0 0 0 6.34-6.32V8.92a8.28 8.28 0 0 0 4.84 1.55v-3.5a4.78 4.78 0 0 1-.94-.28Z" />
    </svg>
  );
}
