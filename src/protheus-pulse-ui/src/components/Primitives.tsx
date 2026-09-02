import {
  AlertTriangle, Check, ChevronDown, HeartPulse, RefreshCw, TerminalSquare,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import type {
  AlertSnapshot, ComponentSnapshot, DashboardSummary, HealthStatus,
} from '../types'
import { formatRelative, typeLabel, stateLabel } from '../lib/format'

export function NavItem({ label, icon: Icon, active, badge, onClick }: { label: string; icon: LucideIcon; active: boolean; badge?: number; onClick: () => void }) {
  return <button className={`nav-item ${active ? 'active' : ''}`} onClick={onClick}><Icon size={18} /><span>{label}</span>{badge != null && badge > 0 && <i>{badge}</i>}</button>
}

export function MetricCard({ icon: Icon, label, value, detail, tone }: { icon: LucideIcon; label: string; value: string | number; detail: string; tone: string }) {
  return <article className={`metric-card ${tone}`}><div className="metric-icon"><Icon size={20} /></div><div><span>{label}</span><strong>{value}</strong><small>{detail}</small></div></article>
}

export function PanelHeader({ title, subtitle, action, onAction }: { title: string; subtitle: string; action?: string; onAction?: () => void }) {
  return <header className="panel-header"><div><h3>{title}</h3><p>{subtitle}</p></div>{action && onAction && <button onClick={onAction}>{action} <ChevronDown size={14} /></button>}</header>
}

export function AvailabilityChart({ values }: { values: DashboardSummary['availability'] }) {
  const width = 640
  const height = 184
  const min = Math.min(...values.map(item => item.value), 90)
  const range = Math.max(100 - min, 1)
  const points = values.map((item, index) => `${(index / Math.max(values.length - 1, 1)) * width},${height - ((item.value - min) / range) * (height - 22) - 8}`).join(' ')
  const area = `0,${height} ${points} ${width},${height}`
  const axisValues = [100, min + (range * 2) / 3, min + range / 3, min]
  const axisLabel = (value: number) => `${Number.isInteger(value) ? value : value.toFixed(1)}%`
  return <div className="chart"><div className="chart-y">{axisValues.map(value => <span key={value}>{axisLabel(value)}</span>)}</div><svg viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="none" role="img" aria-label="Disponibilidade ao longo das últimas doze horas"><defs><linearGradient id="area" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stopOpacity=".28" /><stop offset="1" stopOpacity="0" /></linearGradient></defs><line x1="0" y1="42" x2={width} y2="42" /><line x1="0" y1="88" x2={width} y2="88" /><line x1="0" y1="134" x2={width} y2="134" /><polygon points={area} fill="url(#area)" /><polyline points={points} fill="none" strokeWidth="3" vectorEffect="non-scaling-stroke" /></svg><div className="chart-x">{values.filter((_, index) => index % 2 === 0).map(item => <span key={item.at}>{new Date(item.at).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}</span>)}</div></div>
}

export function StatusLegend({ label, value, status }: { label: string; value: number; status: HealthStatus }) {
  return <div><span><i className={`status-dot ${status.toLowerCase()}`} />{label}</span><strong>{value}</strong></div>
}

export function ComponentTable({ components }: { components: ComponentSnapshot[] }) {
  return <div className="table-wrap"><table><thead><tr><th>Componente</th><th>Instalação</th><th>Estado</th><th>Evidência atual</th><th>Métrica</th></tr></thead><tbody>{components.map(item => <tr key={item.id}><td><div className="component-name"><span><TerminalSquare size={17} /></span><div><strong>{item.name}</strong><small>{typeLabel(item.type)}</small></div></div></td><td><div className="installation-name">{item.installationName}<small>{item.isDemo ? 'Dado demonstrativo' : 'Monitoramento real'}</small></div></td><td><StatusBadge status={item.status} /></td><td><div className="evidence">{item.summary}<small>desde {formatRelative(item.lastStateChangeAt)}</small></div></td><td><div className="metric-value">{item.metricValue ?? '—'} <small>{item.metricUnit}</small><span>{item.metricLabel}</span></div></td></tr>)}</tbody></table>{components.length === 0 && <div className="empty-state"><Check size={22} /> Nenhum componente pede atenção agora.</div>}</div>
}

export function AlertList({ alerts, acknowledge, busyId }: { alerts: AlertSnapshot[]; acknowledge?: (id: string) => void; busyId?: string | null }) {
  return <div className="alert-list">{alerts.map(alert => <div className="alert-row" key={alert.id}><div className={`alert-symbol ${alert.severity.toLowerCase()}`}>{alert.state === 'Resolved' ? <Check size={17} /> : <AlertTriangle size={17} />}</div><div className="alert-main"><div><strong>{alert.ruleName}</strong><StatusBadge status={alert.state === 'Resolved' ? 'Healthy' : alert.severity === 'Critical' ? 'Critical' : 'Warning'} label={stateLabel(alert.state)} /></div><span>{alert.componentName} · {alert.installationName}</span><p>{alert.evidence}</p></div><div className="alert-time"><strong>{formatRelative(alert.startedAt)}</strong><span>#{alert.correlationId.slice(0, 8)}</span>{alert.state === 'Active' && acknowledge && <button className="secondary-button alert-action" disabled={busyId === alert.id} onClick={() => acknowledge(alert.id)}>{busyId === alert.id ? 'Salvando…' : 'Reconhecer'}</button>}</div></div>)}</div>
}

/// Ação de serviço leva segundos e a tela não dizia nada: o operador clicava de novo,
/// ou clicava em outra coisa no meio. A camada cobre a página enquanto a chamada corre.
export function BusyOverlay({ label }: { label: string }) {
  return <div className="busy-overlay" role="alert" aria-busy="true" aria-live="assertive">
    <div className="busy-card"><RefreshCw className="spin" size={22} /><strong>{label}</strong><span>Aguarde o servidor confirmar antes de continuar.</span></div>
  </div>
}

export function Diagnostic({ title, status, detail }: { title: string; status: HealthStatus; detail: string }) {
  return <article className="panel diagnostic-card"><div><span className={`status-dot ${status.toLowerCase()}`} /><strong>{title}</strong></div><StatusBadge status={status} /><p>{detail}</p></article>
}

export function StatusBadge({ status, label }: { status: HealthStatus; label?: string }) {
  const labels: Record<HealthStatus, string> = { Healthy: 'Saudável', Warning: 'Atenção', Critical: 'Crítico', Unknown: 'Desconhecido', Maintenance: 'Manutenção' }
  return <span className={`status-badge ${status.toLowerCase()}`}><i />{label ?? labels[status]}</span>
}

export function Splash() { return <div className="splash"><div className="brand-mark"><HeartPulse size={28} /></div><span>Iniciando o Pulse…</span></div> }
export function DashboardSkeleton() { return <div className="page-body skeleton"><div /><div /><div /><div className="wide" /></div> }
