import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  AlertTriangle, Check, RefreshCw, Search,
} from 'lucide-react'
import {
  getLogEvents,
} from '../api'
import type {
  ComponentSnapshot, HealthStatus, LogEventItem, LogEventPage,
} from '../types'
import { formatRelative } from '../lib/format'
import { PanelHeader, StatusBadge } from '../components/Primitives'

const logLevelFilters = [
  { id: 'all', label: 'Todos' },
  { id: 'Critical', label: 'Críticos' },
  { id: 'Error', label: 'Erros' },
  { id: 'Warning', label: 'Avisos' },
  { id: 'Information', label: 'Informativos' },
]

function logLevelStatus(level: string): HealthStatus {
  if (level === 'Critical' || level === 'Error') return 'Critical'
  if (level === 'Warning') return 'Warning'
  return 'Maintenance'
}

function logLevelLabel(level: string) {
  return ({ Critical: 'Crítico', Error: 'Erro', Warning: 'Aviso', Information: 'Info' } as Record<string, string>)[level] ?? level
}

const logPeriodFilters = [
  { id: '24h', label: '24 horas', hours: 24 },
  { id: '7d', label: '7 dias', hours: 24 * 7 },
  { id: '30d', label: '30 dias', hours: 24 * 30 },
  { id: 'all', label: 'Todo o histórico', hours: 0 }
]

const logPageSize = 100

function LogEventFacts({ item }: { item: LogEventItem }) {
  const facts: Array<[string, string]> = []
  if (item.sourceFile) facts.push(['Fonte', item.sourceLine ? `${item.sourceFile}:${item.sourceLine}` : item.sourceFile])
  if (item.routine) facts.push(['Rotina', item.routine])
  if (item.module) facts.push(['Módulo', item.module])
  if (item.company) facts.push(['Empresa/filial', item.company])
  if (item.user) facts.push(['Usuário', item.user])
  if (item.computer) facts.push(['Máquina', item.computer])
  if (item.environment) facts.push(['Ambiente', item.environment])
  if (item.threadId) facts.push(['Thread', item.threadId])
  if (facts.length === 0 && !item.detail) return null
  return <>
    {facts.length > 0 && <ul className="log-facts">{facts.map(([label, value]) => <li key={label}><span>{label}</span><strong>{value}</strong></li>)}</ul>}
    {item.detail && <details className="log-detail">
      <summary>Pilha e detalhes do erro</summary>
      <pre>{item.detail}</pre>
    </details>}
  </>
}

export function LogsPage({ components }: { components: ComponentSnapshot[] }) {
  const [page, setPage] = useState<LogEventPage>({ total: 0, byLevel: {}, items: [] })
  const [search, setSearch] = useState('')
  const [appliedSearch, setAppliedSearch] = useState('')
  const [level, setLevel] = useState('all')
  const [componentId, setComponentId] = useState('all')
  const [period, setPeriod] = useState('7d')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // A busca vai ao servidor; sem a espera, cada tecla viraria uma consulta.
  useEffect(() => {
    const timer = setTimeout(() => setAppliedSearch(search), 350)
    return () => clearTimeout(timer)
  }, [search])

  const from = useMemo(() => {
    const hours = logPeriodFilters.find(item => item.id === period)?.hours ?? 0
    return hours === 0 ? undefined : new Date(Date.now() - hours * 3_600_000).toISOString()
  }, [period])

  const query = useMemo(() => ({
    search: appliedSearch,
    level,
    componentId: componentId === 'all' ? undefined : componentId,
    from
  }), [appliedSearch, level, componentId, from])

  const load = useCallback(async (skip: number) => {
    setLoading(true)
    try {
      const result = await getLogEvents({ ...query, take: logPageSize, skip })
      setPage(previous => skip === 0 ? result : { ...result, items: [...previous.items, ...result.items] })
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível carregar os logs.')
    } finally { setLoading(false) }
  }, [query])

  useEffect(() => { void load(0) }, [load])

  const shown = page.items.length
  const hasMore = shown < page.total
  const filtersActive = appliedSearch !== '' || level !== 'all' || componentId !== 'all' || period !== 'all'

  return <div className="page-body">
    <section className="toolbar">
      <div className="search-box"><Search size={17} /><input aria-label="Pesquisar logs" placeholder="Pesquisar mensagem, componente ou instalação…" value={search} onChange={event => setSearch(event.target.value)} /></div>
      <label className="toolbar-field">Componente
        <select aria-label="Filtrar por componente" value={componentId} onChange={event => setComponentId(event.target.value)}>
          <option value="all">Todos</option>
          {components.map(item => <option key={item.id} value={item.id}>{item.name} · {item.installationName}</option>)}
        </select>
      </label>
      <label className="toolbar-field">Período
        <select aria-label="Filtrar por período" value={period} onChange={event => setPeriod(event.target.value)}>
          {logPeriodFilters.map(item => <option key={item.id} value={item.id}>{item.label}</option>)}
        </select>
      </label>
      <button className="secondary-button" disabled={loading} onClick={() => void load(0)}><RefreshCw size={16} /> Atualizar</button>
    </section>
    <section className="summary-chips">{logLevelFilters.map(item => <button key={item.id} className={level === item.id ? 'active' : ''} onClick={() => setLevel(item.id)}>{item.label} <strong>{item.id === 'all' ? page.total : page.byLevel[item.id] ?? 0}</strong></button>)}</section>
    {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
    <article className="panel"><PanelHeader title="Eventos coletados dos logs" subtitle={`Mensagens sanitizadas e agrupadas por assinatura · ${shown} de ${page.total} no período`} />
      {loading && shown === 0
        ? <div className="modal-loading"><RefreshCw className="spin" size={20} /> Carregando eventos…</div>
        : shown === 0
          ? <div className="empty-state"><Check size={22} /> {filtersActive ? 'Nenhum evento de log para os filtros atuais.' : 'Nenhum evento de log coletado ainda.'}</div>
          : <>
            {page.items.map(item => <div className="log-group" key={item.id}><span className={`log-count ${item.level === 'Information' ? 'muted' : ''}`}>{item.occurrenceCount}×</span><div><strong>{item.message}</strong><p>{item.componentName} · {item.installationName}</p><LogEventFacts item={item} /><small>{new Date(item.observedAt).toLocaleString('pt-BR')} · {formatRelative(item.observedAt)}</small></div><StatusBadge status={logLevelStatus(item.level)} label={logLevelLabel(item.level)} /></div>)}
            {hasMore && <button className="secondary-button load-more" disabled={loading} onClick={() => void load(shown)}>{loading ? 'Carregando…' : `Carregar mais ${Math.min(logPageSize, page.total - shown)}`}</button>}
          </>}
    </article>
  </div>
}
