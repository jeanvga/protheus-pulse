import type { CSSProperties } from 'react'
import {
  Activity, AlertTriangle, Boxes, Plus, RefreshCw, Server,
} from 'lucide-react'
import type {
  DashboardSummary,
} from '../types'
import { formatRelative } from '../lib/format'
import type { Page } from '../lib/navigation'
import { MetricCard, PanelHeader, AvailabilityChart, StatusLegend, ComponentTable, AlertList } from '../components/Primitives'

export function Overview({ summary, refresh, goTo, addInstallation }: { summary: DashboardSummary; refresh: () => Promise<void>; goTo: (page: Page) => void; addInstallation: () => void }) {
  const updated = formatRelative(summary.generatedAt)
  return <div className="page-body">
    <section className="intro-row"><div><h2>Panorama dos ambientes</h2><p>Última consolidação {updated}. Os dados críticos aparecem primeiro.</p></div><div className="intro-actions"><button className="secondary-button" onClick={() => void refresh()}><RefreshCw size={16} /> Atualizar</button><button className="primary-button" onClick={addInstallation}><Plus size={16} /> Adicionar instalação</button></div></section>
    <section className="metric-grid">
      <MetricCard icon={Server} label="Instalações" value={summary.totals.installations} detail="ambientes acompanhados" tone="blue" />
      <MetricCard icon={Boxes} label="Componentes" value={summary.totals.components} detail={`${summary.totals.healthy} operando normalmente`} tone="teal" />
      <MetricCard icon={AlertTriangle} label="Alertas ativos" value={summary.totals.activeAlerts} detail={`${summary.totals.critical} componente crítico`} tone="red" />
      <MetricCard icon={Activity} label="Disponibilidade" value={`${summary.totals.availabilityPercent}%`} detail="janela consolidada" tone="green" />
    </section>
    <section className="overview-grid">
      <article className="panel availability-panel"><PanelHeader title="Disponibilidade consolidada" subtitle="Últimas 12 horas" /><AvailabilityChart values={summary.availability} /><div className="chart-legend"><span><i className="legend-green" /> Disponibilidade</span><strong>{summary.totals.availabilityPercent}% <small>média</small></strong></div></article>
      <article className="panel status-panel"><PanelHeader title="Estado dos componentes" subtitle="Distribuição atual" /><div className="donut-wrap"><div className="donut" style={{ '--donut-healthy': summary.totals.healthy, '--donut-warning': summary.totals.warning, '--donut-critical': summary.totals.critical, '--donut-total': Math.max(summary.totals.components, 1) } as CSSProperties}><div><strong>{summary.totals.components}</strong><span>total</span></div></div><div className="status-legend"><StatusLegend label="Saudável" value={summary.totals.healthy} status="Healthy" /><StatusLegend label="Atenção" value={summary.totals.warning} status="Warning" /><StatusLegend label="Crítico" value={summary.totals.critical} status="Critical" /><StatusLegend label="Desconhecido" value={summary.totals.unknown} status="Unknown" /></div></div></article>
    </section>
    <section className="panel component-panel"><PanelHeader title="Componentes que pedem atenção" subtitle="Ordenados por impacto operacional" action="Ver instalações" onAction={() => goTo('installations')} /><ComponentTable components={summary.components.filter(item => item.status !== 'Healthy')} /></section>
    <section className="panel alert-panel"><PanelHeader title="Alertas recentes" subtitle="Evidência sanitizada e resolução automática" action="Ver todos" onAction={() => goTo('alerts')} /><AlertList alerts={summary.alerts.slice(0, 4)} /></section>
  </div>
}
