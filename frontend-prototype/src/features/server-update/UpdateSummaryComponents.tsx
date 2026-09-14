import { CheckCircle2, Circle, Info, SquareTerminal } from "lucide-react";
import { Card } from "../../components/ui";

export function ManagementSummary({
  icon: Icon,
  label,
  value,
  tone,
  ok = false,
  running = false,
}: {
  icon: typeof SquareTerminal;
  label: string;
  value: string;
  tone: "blue" | "purple" | "green";
  ok?: boolean;
  running?: boolean;
}) {
  return (
    <div className="update-summary__item">
      <span className={`update-summary__icon update-summary__icon--${tone}`}><Icon size={25} /></span>
      <div>
        <span className="update-summary__label">{label}{ok && <CheckCircle2 size={14} />}</span>
        <strong className={running ? "update-summary__running" : undefined}>{running && <span />}{value}</strong>
      </div>
    </div>
  );
}

export function UpdateHeading({ detail }: { detail: string }) {
  return (
    <header className="setup-update-heading">
      <h2>服务器更新</h2>
      <p>{detail}</p>
    </header>
  );
}

export function SummaryItem({
  icon: Icon,
  label,
  value,
  tone,
}: {
  icon: typeof SquareTerminal;
  label: string;
  value: string;
  tone: "success" | "warning" | "danger" | "info";
}) {
  return (
    <div className="setup-summary-item">
      <span className={`setup-summary-item__icon setup-summary-item__icon--${tone}`}><Icon size={26} /></span>
      <div>
        <span>{label}</span>
        <strong className={`setup-summary-item__value--${tone}`}>{value}</strong>
      </div>
    </div>
  );
}

export function VerificationCard({
  title,
  helper,
  items,
  tone,
  showNote = false,
}: {
  title: string;
  helper: string;
  items: ReadonlyArray<{ label: string; result: string; icon: typeof SquareTerminal }>;
  tone: "success" | "warning";
  showNote?: boolean;
}) {
  return (
    <Card className="verification-card">
      <h3>{title}</h3>
      <p>{helper}</p>
      <div className="verification-list">
        {items.map((item) => {
          const Icon = item.icon;
          return (
            <div className="verification-row" key={item.label}>
              <span className={`verification-row__icon verification-row__icon--${tone}`}><Icon size={19} /></span>
              <strong>{item.label}</strong>
              <span className={`verification-row__result verification-row__result--${tone}`}>
                {tone === "success" ? <CheckCircle2 size={16} /> : <Circle size={15} />}
                {item.result}
              </span>
            </div>
          );
        })}
      </div>
      {showNote && (
        <div className="verification-note">
          <Info size={19} />
          <span>继续配置后才能使用安全保存、重启、备份和游戏内管理能力。</span>
        </div>
      )}
    </Card>
  );
}



