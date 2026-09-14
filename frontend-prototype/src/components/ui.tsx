import * as Dialog from "@radix-ui/react-dialog";
import { cva, type VariantProps } from "class-variance-authority";
import { AlertTriangle, CheckCircle2, X } from "lucide-react";
import type { ButtonHTMLAttributes, HTMLAttributes, ReactNode } from "react";
import { cn } from "../lib/utils";

const buttonVariants = cva("button", {
  variants: {
    variant: {
      primary: "button--primary",
      secondary: "button--secondary",
      ghost: "button--ghost",
      danger: "button--danger",
      warning: "button--warning",
    },
    size: {
      sm: "button--sm",
      md: "button--md",
      lg: "button--lg",
    },
  },
  defaultVariants: {
    variant: "secondary",
    size: "md",
  },
});

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> &
  VariantProps<typeof buttonVariants>;

export function Button({ className, variant, size, type = "button", ...props }: ButtonProps) {
  return (
    <button
      type={type}
      className={cn(buttonVariants({ variant, size }), className)}
      {...props}
    />
  );
}

export function Card({
  children,
  className,
  as: Component = "section",
  ...props
}: HTMLAttributes<HTMLElement> & {
  children: ReactNode;
  as?: "section" | "article" | "div";
}) {
  return <Component className={cn("card", className)} {...props}>{children}</Component>;
}

export function StatusPill({
  tone = "success",
  children,
  compact = false,
}: {
  tone?: "success" | "warning" | "danger" | "neutral" | "info";
  children: ReactNode;
  compact?: boolean;
}) {
  return (
    <span className={cn("status-pill", `status-pill--${tone}`, compact && "status-pill--compact")}>
      <span className="status-pill__dot" aria-hidden="true" />
      {children}
    </span>
  );
}

export function IconBox({ tone, children }: { tone: string; children: ReactNode }) {
  return <span className={cn("icon-box", `icon-box--${tone}`)}>{children}</span>;
}

export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  description,
  actionLabel,
  danger = false,
  onConfirm,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description: string;
  actionLabel: string;
  danger?: boolean;
  onConfirm: () => void;
}) {
  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay className="dialog-overlay dialog-overlay--confirm" />
        <Dialog.Content className="dialog-content dialog-content--confirm" aria-describedby="confirm-description">
          <div className={cn("dialog-icon", danger && "dialog-icon--danger")} aria-hidden="true">
            <AlertTriangle size={22} strokeWidth={2.2} />
          </div>
          <div className="dialog-copy">
            <Dialog.Title className="dialog-title">{title}</Dialog.Title>
            <Dialog.Description id="confirm-description" className="dialog-description">
              {description}
            </Dialog.Description>
          </div>
          <Dialog.Close className="dialog-close" aria-label="关闭确认窗口">
            <X size={18} />
          </Dialog.Close>
          <div className="dialog-actions">
            <Dialog.Close asChild>
              <Button variant="secondary">取消</Button>
            </Dialog.Close>
            <Button
              variant={danger ? "danger" : "primary"}
              onClick={() => {
                onConfirm();
                onOpenChange(false);
              }}
            >
              {actionLabel}
            </Button>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

export function Toast({ message }: { message: string | null }) {
  return (
    <div className={cn("toast", message && "toast--visible")} role="status" aria-live="polite">
      <CheckCircle2 size={19} aria-hidden="true" />
      <span>{message}</span>
    </div>
  );
}
