import * as Dialog from "@radix-ui/react-dialog";
import {
  AlertCircle,
  ArrowLeft,
  Folder,
  HardDrive,
  LoaderCircle,
  RefreshCw,
  Server,
  X,
} from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { Button } from "./ui";

type DirectoryEntry = {
  name: string;
  path: string;
  isDrive: boolean;
  availableBytes: number | null;
  totalBytes: number | null;
};

type BrowseResult = {
  currentPath: string | null;
  parentPath: string | null;
  isRootView: boolean;
  items: DirectoryEntry[];
};

type ApiErrorEnvelope = {
  error?: {
    message?: string;
  };
};

function formatBytes(value: number | null) {
  if (value === null) return null;
  const gibibytes = value / 1024 / 1024 / 1024;
  return `${gibibytes >= 100 ? gibibytes.toFixed(0) : gibibytes.toFixed(1)} GB`;
}

function joinServerPath(parent: string, child: string) {
  return `${parent.replace(/[\\/]+$/, "")}\\${child}`;
}

export function ServerDirectoryPicker({
  open,
  onOpenChange,
  title,
  description,
  confirmLabel = "选择当前目录",
  proposedFolderName,
  onSelect,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description: string;
  confirmLabel?: string;
  proposedFolderName?: string;
  onSelect: (path: string) => void;
}) {
  const [result, setResult] = useState<BrowseResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [folderName, setFolderName] = useState(proposedFolderName ?? "");

  const browse = useCallback(async (path: string | null, signal?: AbortSignal) => {
    setLoading(true);
    setError(null);

    try {
      const query = path ? `?path=${encodeURIComponent(path)}` : "";
      const response = await fetch(`/api/v1/servers/local/filesystem/directories${query}`, {
        headers: { Accept: "application/json" },
        signal,
      });

      if (!response.ok) {
        const body = await response.json().catch(() => null) as ApiErrorEnvelope | null;
        throw new Error(body?.error?.message || `目录读取失败（HTTP ${response.status}）`);
      }

      setResult(await response.json() as BrowseResult);
    } catch (caught) {
      if (caught instanceof DOMException && caught.name === "AbortError") return;
      setError(caught instanceof Error ? caught.message : "无法读取服务器目录");
    } finally {
      if (!signal?.aborted) setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (!open) return;
    setFolderName(proposedFolderName ?? "");
    const controller = new AbortController();
    void browse(null, controller.signal);
    return () => controller.abort();
  }, [browse, open, proposedFolderName]);

  const currentPath = result?.currentPath ?? null;
  const folderNameValid = !proposedFolderName || (folderName.trim().length > 0 && !/[<>:"/\\|?*]/.test(folderName));
  const targetPath = currentPath && proposedFolderName ? joinServerPath(currentPath, folderName.trim()) : currentPath;

  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay className="dialog-overlay" />
        <Dialog.Content className="directory-picker" aria-describedby="directory-picker-description">
          <header className="directory-picker__header">
            <span className="directory-picker__icon" aria-hidden="true"><Server size={22} /></span>
            <div>
              <Dialog.Title>{title}</Dialog.Title>
              <Dialog.Description id="directory-picker-description">{description}</Dialog.Description>
            </div>
            <Dialog.Close className="directory-picker__close" aria-label="关闭目录选择器"><X size={19} /></Dialog.Close>
          </header>

          <div className="directory-picker__toolbar">
            <Button
              variant="ghost"
              size="sm"
              disabled={loading || !result || result.isRootView}
              onClick={() => void browse(result?.parentPath ?? null)}
            >
              <ArrowLeft size={16} />上一级
            </Button>
            <div className="directory-picker__path" title={currentPath ?? "服务器磁盘"}>
              <HardDrive size={16} aria-hidden="true" />
              <span>{currentPath ?? "服务器磁盘"}</span>
            </div>
            <button
              type="button"
              className="directory-picker__refresh"
              aria-label="刷新当前目录"
              disabled={loading}
              onClick={() => void browse(currentPath)}
            >
              <RefreshCw className={loading ? "spin" : undefined} size={17} />
            </button>
          </div>

          <div className="directory-picker__body" aria-busy={loading} aria-live="polite">
            {loading && !result && (
              <div className="directory-picker__state"><LoaderCircle className="spin" size={26} /><strong>正在读取服务器目录</strong><span>数据来自本机 Server Agent</span></div>
            )}

            {error && (
              <div className="directory-picker__state directory-picker__state--error" role="alert">
                <AlertCircle size={27} />
                <strong>无法打开服务器目录</strong>
                <span>{error}</span>
                <Button variant="secondary" size="sm" onClick={() => void browse(currentPath)}>重新连接</Button>
              </div>
            )}

            {!error && result && result.items.length === 0 && !loading && (
              <div className="directory-picker__state"><Folder size={28} /><strong>此目录中没有子目录</strong><span>可以直接选择当前目录。</span></div>
            )}

            {!error && result && result.items.length > 0 && (
              <div className="directory-picker__list" role="list">
                {result.items.map((item) => (
                  <button key={item.path} type="button" role="listitem" onClick={() => void browse(item.path)}>
                    <span className={item.isDrive ? "directory-picker__item-icon directory-picker__item-icon--drive" : "directory-picker__item-icon"}>
                      {item.isDrive ? <HardDrive size={20} /> : <Folder size={20} />}
                    </span>
                    <span className="directory-picker__item-copy">
                      <strong>{item.name}</strong>
                      <small>{item.isDrive && item.availableBytes !== null ? `可用 ${formatBytes(item.availableBytes)} / ${formatBytes(item.totalBytes)}` : item.path}</small>
                    </span>
                    <span aria-hidden="true">›</span>
                  </button>
                ))}
              </div>
            )}
          </div>

          {proposedFolderName && (
            <div className="directory-picker__destination">
              <label htmlFor="directory-picker-folder-name">目标文件夹名称</label>
              <input
                id="directory-picker-folder-name"
                value={folderName}
                aria-invalid={!folderNameValid}
                onChange={(event) => setFolderName(event.target.value)}
              />
              <small>{targetPath ? `计划安装到：${targetPath}` : "选择父目录后将生成完整安装路径"}</small>
              {!folderNameValid && <em>文件夹名称不能为空，也不能包含 Windows 禁用字符。</em>}
            </div>
          )}

          <footer className="directory-picker__footer">
            <span>{currentPath ? proposedFolderName ? "安装时才会创建目标文件夹" : "将使用服务器上的当前目录" : "请先进入一个磁盘并选择目录"}</span>
            <div>
              <Dialog.Close asChild><Button variant="secondary">取消</Button></Dialog.Close>
              <Button
                variant="primary"
                disabled={!targetPath || loading || Boolean(error) || !folderNameValid}
                onClick={() => {
                  if (!targetPath) return;
                  onSelect(targetPath);
                  onOpenChange(false);
                }}
              >
                <Folder size={17} />{confirmLabel}
              </Button>
            </div>
          </footer>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
