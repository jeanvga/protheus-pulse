import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  AlertTriangle, Archive, LockKeyhole, RefreshCw, Search, ShieldCheck,
} from 'lucide-react'
import {
  getAuditEvents, login, session,
} from '../api'
import type {
  AuditEventPage,
} from '../types'
import { PanelHeader } from '../components/Primitives'

const auditPageSize = 50

const auditPeriodFilters = [
  { id: '24h', label: 'Últimas 24 horas', hours: 24 },
  { id: '7d', label: 'Últimos 7 dias', hours: 168 },
  { id: '30d', label: 'Últimos 30 dias', hours: 720 },
  { id: 'all', label: 'Todo o período', hours: 0 },
]

/// O nome técnico da ação é o que fica gravado; a tela mostra o que a pessoa fez.
const auditActionLabels: Record<string, string> = {
  LoginSucceeded: 'Entrou no painel',
  InitialAdministratorCreated: 'Criou a conta administrativa inicial',
  InstallationCreated: 'Cadastrou uma instalação',
  InstallationUpdated: 'Alterou uma instalação',
  InstallationDeleted: 'Removeu uma instalação',
  ServiceActionExecuted: 'Executou ação em serviço Windows',
  MaintenanceModeEntered: 'Entrou no modo manutenção',
  MaintenanceModeExited: 'Encerrou o modo manutenção',
  MaintenanceWindowCreated: 'Abriu um silenciamento',
  MaintenanceWindowDeleted: 'Encerrou um silenciamento',
  AlertRuleCreated: 'Criou uma regra de alerta',
  AlertRuleUpdated: 'Editou uma regra de alerta',
  AlertRuleDeleted: 'Removeu uma regra de alerta',
  AlertRuleStateChanged: 'Ativou ou desativou uma regra',
  AlertAcknowledged: 'Reconheceu um alerta',
  NotificationChannelCreated: 'Cadastrou um ponto de contato',
  NotificationChannelDeleted: 'Removeu um ponto de contato',
  NotificationChannelStateChanged: 'Ativou ou desativou um ponto de contato',
  EmailSettingsUpdated: 'Alterou o envio de e-mail',
  EmailSettingsTested: 'Enviou e-mail de teste',
  ExclusiveInstallationChanged: 'Alterou a instalação exclusiva',
  AutoStartSettingChanged: 'Alterou o auto-start',
  HeartbeatDefinitionCreated: 'Cadastrou um heartbeat',
  HeartbeatDefinitionDeleted: 'Removeu um heartbeat',
  HeartbeatTokenRotated: 'Rotacionou o token de um heartbeat',
  UserCreated: 'Criou uma conta',
  UserUpdated: 'Alterou uma conta',
  UserDeleted: 'Removeu uma conta',
  UserPasswordReset: 'Redefiniu uma senha',
  NetworkSettingsUpdated: 'Alterou o acesso pela rede',
  RetentionSettingsUpdated: 'Alterou a retenção',
}

function auditActionLabel(action: string) {
  return auditActionLabels[action] ?? action
}

/// Ações que mexem no ambiente monitorado merecem destaque na lista.
const auditSensitiveActions = new Set([
  'ServiceActionExecuted', 'MaintenanceModeEntered', 'MaintenanceModeExited',
  'InstallationDeleted', 'UserDeleted', 'UserPasswordReset', 'NetworkSettingsUpdated',
])

function auditDetails(details?: string | null) {
  if (!details || details === '{}') return null
  try {
    const parsed = JSON.parse(details) as Record<string, unknown>
    const entries = Object.entries(parsed).filter(([, value]) => value !== null && value !== '')
    if (entries.length === 0) return null
    return entries.map(([key, value]) => `${key}: ${typeof value === 'object' ? JSON.stringify(value) : String(value)}`).join(' · ')
  } catch {
    return null
  }
}

export function AuditPage() {
  const [page, setPage] = useState<AuditEventPage>({ total: 0, byAction: {}, items: [] })
  const [search, setSearch] = useState('')
  const [appliedSearch, setAppliedSearch] = useState('')
  const [action, setAction] = useState('all')
  const [period, setPeriod] = useState('7d')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const isAdministrator = session.role === 'Administrator'

  useEffect(() => {
    const timer = setTimeout(() => setAppliedSearch(search), 350)
    return () => clearTimeout(timer)
  }, [search])

  const from = useMemo(() => {
    const hours = auditPeriodFilters.find(item => item.id === period)?.hours ?? 0
    return hours === 0 ? undefined : new Date(Date.now() - hours * 3_600_000).toISOString()
  }, [period])

  const query = useMemo(() => ({ search: appliedSearch, action, from }), [appliedSearch, action, from])

  const load = useCallback(async (skip: number) => {
    if (!isAdministrator) { setLoading(false); return }
    setLoading(true)
    try {
      const result = await getAuditEvents({ ...query, take: auditPageSize, skip })
      setPage(previous => skip === 0 ? result : { ...result, items: [...previous.items, ...result.items] })
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível carregar a auditoria.')
    } finally { setLoading(false) }
  }, [query, isAdministrator])

  useEffect(() => { void load(0) }, [load])

  if (!isAdministrator) {
    return <div className="page-body"><div className="read-only-notice"><LockKeyhole size={22} /><div><strong>Somente administradores</strong><p>A auditoria registra quem fez cada alteração, com endereço de origem. Só o perfil Administrator pode consultá-la.</p></div></div></div>
  }

  const shown = page.items.length
  const hasMore = shown < page.total
  const knownActions = Object.keys(page.byAction).sort((left, right) => auditActionLabel(left).localeCompare(auditActionLabel(right), 'pt-BR'))

  return <div className="page-body">
    <section className="toolbar">
      <div className="search-box"><Search size={17} /><input aria-label="Pesquisar auditoria" placeholder="Pesquisar ação, tipo ou usuário…" value={search} onChange={event => setSearch(event.target.value)} /></div>
      <label className="toolbar-field">Ação
        <select aria-label="Filtrar por ação" value={action} onChange={event => setAction(event.target.value)}>
          <option value="all">Todas</option>
          {knownActions.map(item => <option key={item} value={item}>{auditActionLabel(item)}</option>)}
        </select>
      </label>
      <label className="toolbar-field">Período
        <select aria-label="Filtrar período da auditoria" value={period} onChange={event => setPeriod(event.target.value)}>
          {auditPeriodFilters.map(item => <option key={item.id} value={item.id}>{item.label}</option>)}
        </select>
      </label>
      <button className="secondary-button" disabled={loading} onClick={() => void load(0)}><RefreshCw size={16} /> Atualizar</button>
    </section>

    {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}

    <article className="panel">
      <PanelHeader title="Eventos administrativos" subtitle={`${page.total.toLocaleString('pt-BR')} evento(s) no período · horários no fuso do servidor`} />
      {loading && shown === 0 && <div className="modal-loading"><RefreshCw className="spin" size={20} /> Carregando auditoria…</div>}
      {!loading && shown === 0 && <div className="tab-empty">
        <Archive size={22} />
        <div><strong>Nenhum evento no período</strong><p>A auditoria guarda login, ação em serviço, mudança de regra, manutenção, conta e configuração. Ela não é apagada pela retenção — amplie o período se procura algo antigo.</p></div>
      </div>}
      {page.items.map(item => {
        const detail = auditDetails(item.details)
        return <div className="audit-line" key={item.id}>
          <span className={auditSensitiveActions.has(item.action) ? 'sensitive' : ''}>
            {auditSensitiveActions.has(item.action) ? <ShieldCheck size={16} /> : <LockKeyhole size={16} />}
          </span>
          <div>
            <strong>{auditActionLabel(item.action)}</strong>
            <p>{item.userDisplayName ?? 'Sistema'}{item.username ? ` (${item.username})` : ''} · {item.entityType}{item.entityId ? ` ${item.entityId.slice(0, 8)}` : ''}</p>
            {detail && <p className="audit-detail">{detail}</p>}
            <small>{new Date(item.occurredAt).toLocaleString('pt-BR')}{item.remoteAddress ? ` · ${item.remoteAddress}` : ''}</small>
          </div>
        </div>
      })}
      {hasMore && <button className="secondary-button load-more" disabled={loading} onClick={() => void load(shown)}>
        {loading ? 'Carregando…' : `Carregar mais (${(page.total - shown).toLocaleString('pt-BR')} restantes)`}
      </button>}
    </article>
  </div>
}
