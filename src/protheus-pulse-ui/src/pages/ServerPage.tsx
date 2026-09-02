import { useCallback, useEffect, useState } from 'react'
import {
  AlertTriangle, CircleHelp, Clock3, Cpu, HardDrive, MemoryStick, RefreshCw,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import {
  getServerResources,
} from '../api'
import type {
  HealthStatus, ServerDiskUsage, ServerResources,
} from '../types'
import { formatBytes, formatUptime, formatPercent } from '../lib/format'
import { PanelHeader, StatusBadge } from '../components/Primitives'

const serverRefreshMilliseconds = 5_000

export function ServerPage() {
  const [resources, setResources] = useState<ServerResources | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  const load = useCallback(async () => {
    try {
      setResources(await getServerResources())
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível ler os recursos do servidor.')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
    const timer = window.setInterval(() => void load(), serverRefreshMilliseconds)
    return () => window.clearInterval(timer)
  }, [load])

  if (loading && !resources) {
    return <div className="page-body"><div className="modal-loading"><RefreshCw className="spin" size={20} /> Lendo os recursos do servidor…</div></div>
  }

  if (!resources) {
    return <div className="page-body"><div className="form-error"><AlertTriangle size={16} /> {error ?? 'Recursos indisponíveis.'}</div></div>
  }

  const { server, thresholds } = resources
  const worstDisk = server.disks.reduce<ServerDiskUsage | null>(
    (worst, disk) => (worst === null || disk.freePercent < worst.freePercent ? disk : worst), null)

  return <div className="page-body">
    <section className="intro-row">
      <div>
        <h2>{server.hostName}</h2>
        <p>{server.operatingSystem} · {server.processorCount} processadores lógicos · no ar há {formatUptime(server.uptimeSeconds)}</p>
      </div>
      <div className="intro-actions">
        <span className="refresh-hint"><span className="live-dot" /> leitura a cada {serverRefreshMilliseconds / 1_000} s</span>
        <button className="secondary-button" onClick={() => void load()}><RefreshCw size={16} /> Atualizar</button>
      </div>
    </section>

    {server.notice && <div className="maintenance-banner"><CircleHelp size={16} /> {server.notice}</div>}
    {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}

    <section className="resource-grid">
      <ResourceCard
        icon={Cpu}
        label="Processador"
        value={formatPercent(server.cpuUsagePercent)}
        detail={`atenção em ${thresholds.cpuWarningPercent}% · crítico em ${thresholds.cpuCriticalPercent}%`}
        status={server.cpuStatus}
        percent={server.cpuUsagePercent ?? 0}
      />
      <ResourceCard
        icon={MemoryStick}
        label="Memória"
        value={formatPercent(server.memory?.usedPercent)}
        detail={server.memory
          ? `${formatBytes(server.memory.usedBytes)} de ${formatBytes(server.memory.totalBytes)} · ${formatBytes(server.memory.availableBytes)} livres`
          : 'leitura indisponível nesta plataforma'}
        status={server.memoryStatus}
        percent={server.memory?.usedPercent ?? 0}
      />
      <ResourceCard
        icon={HardDrive}
        label="Disco mais cheio"
        value={worstDisk ? `${worstDisk.usedPercent.toFixed(1)}%` : '—'}
        detail={worstDisk
          ? `${worstDisk.name} · ${formatBytes(worstDisk.freeBytes)} livres de ${formatBytes(worstDisk.totalBytes)}`
          : 'nenhum disco fixo encontrado'}
        status={worstDisk?.status ?? 'Unknown'}
        percent={worstDisk?.usedPercent ?? 0}
      />
    </section>

    <article className="panel resource-history">
      <PanelHeader title="Uso nos últimos minutos" subtitle="Processador e memória, amostrados pelo próprio serviço" />
      <ResourceHistoryChart history={server.history} />
      <div className="chart-legend">
        <span><i className="legend-cpu" /> Processador</span>
        <span><i className="legend-memory" /> Memória</span>
        <strong>{new Date(server.observedAt).toLocaleTimeString('pt-BR')} <small>última leitura</small></strong>
      </div>
    </article>

    <article className="panel disk-panel">
      <PanelHeader title="Discos fixos" subtitle={`Atenção abaixo de ${thresholds.diskFreeWarningPercent}% livre · crítico abaixo de ${thresholds.diskFreeCriticalPercent}%`} />
      {server.disks.length === 0
        ? <div className="empty-state"><CircleHelp size={22} /> Nenhum disco fixo pôde ser lido.</div>
        : server.disks.map(disk => <div className="disk-row" key={disk.name}>
          <div className="disk-name"><span><HardDrive size={17} /></span><div><strong>{disk.name}{disk.label ? ` · ${disk.label}` : ''}</strong><small>{disk.format} · {formatBytes(disk.totalBytes)} no total</small></div></div>
          <ResourceBar percent={disk.usedPercent} status={disk.status} />
          <div className="disk-figures"><strong>{formatBytes(disk.freeBytes)} livres</strong><small>{disk.usedPercent.toFixed(1)}% usado</small></div>
          <StatusBadge status={disk.status} />
        </div>)}
    </article>
  </div>
}

function ResourceCard({ icon: Icon, label, value, detail, status, percent }: {
  icon: LucideIcon
  label: string
  value: string
  detail: string
  status: HealthStatus
  percent: number
}) {
  return <article className="panel resource-card">
    <header><span className={`resource-icon ${status.toLowerCase()}`}><Icon size={20} /></span><div><h3>{label}</h3><p>{detail}</p></div><StatusBadge status={status} /></header>
    <strong className="resource-value">{value}</strong>
    <ResourceBar percent={percent} status={status} />
  </article>
}

function ResourceBar({ percent, status }: { percent: number; status: HealthStatus }) {
  const bounded = Math.min(100, Math.max(0, percent))
  return <div className="resource-bar" role="img" aria-label={`${bounded.toFixed(0)}% em uso`}>
    <i className={status.toLowerCase()} style={{ width: `${bounded}%` }} />
  </div>
}

function ResourceHistoryChart({ history }: { history: ServerResources['server']['history'] }) {
  const width = 640
  const height = 150
  if (history.length < 2) {
    return <div className="empty-state"><Clock3 size={22} /> Coletando as primeiras amostras…</div>
  }

  const line = (pick: (sample: ServerResources['server']['history'][number]) => number | null | undefined) => history
    .map((sample, index) => {
      const value = Math.min(100, Math.max(0, pick(sample) ?? 0))
      return `${(index / (history.length - 1)) * width},${height - (value / 100) * (height - 16) - 8}`
    })
    .join(' ')

  return <div className="chart resource-chart">
    <div className="chart-y"><span>100%</span><span>66%</span><span>33%</span><span>0%</span></div>
    <svg viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="none" role="img" aria-label="Uso de processador e memória ao longo do tempo">
      <line x1="0" y1="38" x2={width} y2="38" />
      <line x1="0" y1="75" x2={width} y2="75" />
      <line x1="0" y1="112" x2={width} y2="112" />
      <polyline className="cpu-line" points={line(sample => sample.cpuPercent)} fill="none" strokeWidth="2.5" vectorEffect="non-scaling-stroke" />
      <polyline className="memory-line" points={line(sample => sample.memoryPercent)} fill="none" strokeWidth="2.5" vectorEffect="non-scaling-stroke" />
    </svg>
  </div>
}
