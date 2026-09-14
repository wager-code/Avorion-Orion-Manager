import { Boxes } from "lucide-react";
import { useEffect, useState } from "react";

const safeIconPattern = /^data\/textures\/icons\/[a-z0-9][a-z0-9._/-]*\.(png|jpe?g|webp)$/i;

export function inventoryIconUrl(iconPath: string | null | undefined) {
  if (!iconPath || iconPath.length > 256 || !safeIconPattern.test(iconPath) ||
    iconPath.includes("..") || iconPath.includes("//")) return null;
  return `/api/v1/servers/local/inventory-icons?path=${encodeURIComponent(iconPath)}`;
}

export function GameItemIcon({ iconPath, label, size = 24 }: {
  iconPath: string | null | undefined;
  label: string;
  size?: number;
}) {
  const source = inventoryIconUrl(iconPath);
  const [failed, setFailed] = useState(false);
  useEffect(() => setFailed(false), [source]);
  if (!source || failed) return <Boxes size={Math.min(size, 20)} aria-hidden="true" />;
  return <img src={source} alt={label} width={size} height={size} onError={() => setFailed(true)} />;
}
