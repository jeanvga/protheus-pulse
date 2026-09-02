import { useCallback, useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import {
  AlertTriangle, Bell, BellOff, Check, LockKeyhole, Mail, Pencil, Plus, RefreshCw, Search, Send, Server, Siren, TerminalSquare, Trash2, X,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import {
  acknowledgeAlert, createAlertRule, createMaintenanceWindow, createNotificationChannel, deleteAlertRule, deleteMaintenanceWindow, deleteNotificationChannel, getAlertRules, getAlerts, getMaintenanceWindows, getNotificationChannels, session, setAlertRuleEnabled, setNotificationChannelEnabled, updateAlertRule,
} from '../api'
import type {
  AlertOccurrencePage, AlertRule, AlertSeverity, AlertState, ComponentSnapshot, DashboardSummary, HealthStatus, MaintenanceWindow, NotificationChannel, NotificationChannelType, ProbeType,
} from '../types'
import { toLocalInputValue, formatDateTime } from '../lib/format'
import { groupComponentsByInstallation } from '../lib/installations'
import type { Page } from '../lib/navigation'
import { AlertList, StatusBadge } from '../components/Primitives'

/// A aba segue a divisão do Grafana: a regra diz o que abre o incidente, o ponto de contato diz
/// para onde ele vai e o silenciamento diz quando ninguém deve ser avisado.
type AlertsTab = 'occurrences' | 'rules' | 'contacts' | 'silences'

const alertsTabs: Array<{ id: AlertsTab; label: string; icon: LucideIcon; hint: string }> = [
  { id: 'occurrences', label: 'Ocorrências', icon: Bell, hint: 'Incidentes abertos pelo coletor, com reconhecimento e resolução.' },
  { id: 'rules', label: 'Regras de alerta', icon: Siren, hint: 'O que abre um incidente, depois de quantas falhas seguidas e com que severidade.' },
  { id: 'contacts', label: 'Pontos de contato', icon: Send, hint: 'Para onde o incidente é enviado quando abre, reativa ou resolve.' },
  { id: 'silences', label: 'Silenciamentos', icon: BellOff, hint: 'Janelas que suspendem o disparo sem interromper a coleta.' },
]

const occurrenceFilters: Array<{ value: AlertState | 'all'; label: string }> = [
  { value: 'Active', label: 'Ativos' },
  { value: 'Acknowledged', label: 'Reconhecidos' },
  { value: 'Resolved', label: 'Resolvidos' },
  { value: 'Silenced', label: 'Silenciados' },
  { value: 'all', label: 'Todos' },
]

const probeTypeOptions: Array<{ value: ProbeType; label: string }> = [
  { value: 'WindowsService', label: 'Serviço Windows' },
  { value: 'Process', label: 'Processo' },
  { value: 'Tcp', label: 'Porta TCP' },
  { value: 'Http', label: 'HTTP/HTTPS' },
  { value: 'TlsCertificate', label: 'Certificado TLS' },
  { value: 'File', label: 'Arquivo obrigatório' },
  { value: 'Disk', label: 'Espaço em disco' },
  { value: 'Log', label: 'Erros no log' },
  { value: 'Heartbeat', label: 'Heartbeat de job' },
  { value: 'Internal', label: 'Coleta interna' },
  { value: 'ServerCpu', label: 'Processador do servidor' },
  { value: 'ServerMemory', label: 'Memória do servidor' },
  { value: 'ServerDisk', label: 'Discos do servidor' },
]

/// Verificações que medem uso da máquina em percentual e por isso aceitam limite próprio na regra.
const serverProbeTypes: ProbeType[] = ['ServerCpu', 'ServerMemory', 'ServerDisk']
const isServerProbe = (probeType: ProbeType) => serverProbeTypes.includes(probeType)

const triggerStatusOptions: Array<{ value: HealthStatus; label: string; description: string }> = [
  { value: 'Warning', label: 'Atenção', description: 'Respondeu, mas fora do que se espera.' },
  { value: 'Critical', label: 'Crítico', description: 'A verificação falhou.' },
  { value: 'Unknown', label: 'Desconhecido', description: 'Não deu para verificar: alvo inacessível ou sem permissão.' },
]

const severityOptions: Array<{ value: AlertSeverity; label: string }> = [
  { value: 'Critical', label: 'Crítico' },
  { value: 'Warning', label: 'Atenção' },
  { value: 'Info', label: 'Informativo' },
]

const cooldownOptions = [
  { value: 0, label: 'Sem espera' },
  { value: 60, label: '1 minuto' },
  { value: 300, label: '5 minutos' },
  { value: 900, label: '15 minutos' },
  { value: 1_800, label: '30 minutos' },
  { value: 3_600, label: '1 hora' },
  { value: 21_600, label: '6 horas' },
  { value: 86_400, label: '24 horas' },
]

const silenceDurationOptions = [
  { value: 30, label: '30 minutos' },
  { value: 60, label: '1 hora' },
  { value: 120, label: '2 horas' },
  { value: 360, label: '6 horas' },
  { value: 720, label: '12 horas' },
  { value: 1_440, label: '1 dia' },
  { value: 4_320, label: '3 dias' },
  { value: 10_080, label: '7 dias' },
]

const channelTypeOptions: Array<{ value: NotificationChannelType; label: string; hint: string }> = [
  { value: 'Webhook', label: 'Webhook genérico', hint: 'Endpoint HTTPS que recebe o evento em JSON.' },
  { value: 'Teams', label: 'Microsoft Teams', hint: 'URL do conector de entrada do canal.' },
  { value: 'Slack', label: 'Slack', hint: 'URL do incoming webhook do canal.' },
  { value: 'Discord', label: 'Discord', hint: 'URL do webhook do canal.' },
]

function probeTypeLabel(type: ProbeType) { return probeTypeOptions.find(item => item.value === type)?.label ?? type }
function severityLabel(severity: AlertSeverity) { return severityOptions.find(item => item.value === severity)?.label ?? severity }
/// "um alerta atenção" não existe em português; a severidade vira adjetivo na frase.
function severityAdjective(severity: AlertSeverity) {
  return severity === 'Critical' ? 'crítico' : severity === 'Warning' ? 'de atenção' : 'informativo'
}
function triggerStatusLabel(status: HealthStatus) { return triggerStatusOptions.find(item => item.value === status)?.label ?? status }
function channelTypeLabel(type: NotificationChannelType) { return channelTypeOptions.find(item => item.value === type)?.label ?? type }

function cooldownLabel(seconds: number) {
  const known = cooldownOptions.find(item => item.value === seconds)
  if (known) return known.label
  if (seconds < 60) return `${seconds} segundos`
  if (seconds < 3_600) return `${Math.round(seconds / 60)} minutos`
  return `${Math.round(seconds / 3_600)} horas`
}

function triggerStatusSentence(statuses: HealthStatus[]) {
  const labels = triggerStatusOptions.filter(item => statuses.includes(item.value)).map(item => item.label.toLowerCase())
  if (labels.length === 0) return 'nenhum estado'
  if (labels.length === 1) return labels[0]
  return `${labels.slice(0, -1).join(', ')} ou ${labels[labels.length - 1]}`
}

function ruleSentence(rule: AlertRule) {
  const collections = rule.minimumConsecutiveFailures === 1 ? 'na primeira coleta em falha' : `depois de ${rule.minimumConsecutiveFailures} coletas seguidas em falha`
  const cooldown = rule.cooldownSeconds === 0 ? 'sem espera para reabrir' : `reabre no mínimo ${cooldownLabel(rule.cooldownSeconds)} depois`
  const condition = rule.thresholdPercent != null
    ? `abre com uso acima de ${rule.thresholdPercent}%`
    : `abre em ${triggerStatusSentence(rule.triggerStatuses)}`
  return `${probeTypeLabel(rule.probeType)} · ${condition} ${collections} · ${cooldown}`
}

export function AlertsPage({ summary, refresh, goTo }: { summary: DashboardSummary; refresh: () => Promise<void>; goTo: (page: Page) => void }) {
  const [tab, setTab] = useState<AlertsTab>('occurrences')
  const isAdministrator = session.role === 'Administrator'
  const openCount = summary.totals.activeAlerts
  const current = alertsTabs.find(item => item.id === tab) ?? alertsTabs[0]
  return <div className="page-body">
    <nav className="subtabs" aria-label="Seções de alertas">
      {alertsTabs.map(({ id, label, icon: Icon }) => <button
        key={id}
        type="button"
        className={`subtab ${tab === id ? 'active' : ''}`}
        aria-current={tab === id ? 'page' : undefined}
        onClick={() => setTab(id)}
      ><Icon size={15} /><span>{label}</span>{id === 'occurrences' && openCount > 0 && <i>{openCount}</i>}</button>)}
    </nav>
    <p className="subtab-hint">{current.hint}</p>
    {tab === 'occurrences' && <AlertOccurrencesTab refresh={refresh} />}
    {tab === 'rules' && <AlertRulesTab components={summary.components} isAdministrator={isAdministrator} />}
    {tab === 'contacts' && <ContactPointsTab isAdministrator={isAdministrator} goTo={goTo} />}
    {tab === 'silences' && <SilencesTab components={summary.components} isAdministrator={isAdministrator} />}
  </div>
}

const occurrencePageSize = 50

const occurrencePeriodFilters = [
  { id: '24h', label: 'Últimas 24 horas', hours: 24 },
  { id: '7d', label: 'Últimos 7 dias', hours: 168 },
  { id: '30d', label: 'Últimos 30 dias', hours: 720 },
  { id: 'all', label: 'Todo o histórico', hours: 0 },
]

function AlertOccurrencesTab({ refresh }: { refresh: () => Promise<void> }) {
  const [page, setPage] = useState<AlertOccurrencePage>({ total: 0, byState: {}, items: [] })
  const [state, setState] = useState<AlertState | 'all'>('Active')
  const [period, setPeriod] = useState('7d')
  const [loading, setLoading] = useState(true)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const from = useMemo(() => {
    const hours = occurrencePeriodFilters.find(item => item.id === period)?.hours ?? 0
    return hours === 0 ? undefined : new Date(Date.now() - hours * 3_600_000).toISOString()
  }, [period])

  const load = useCallback(async (skip: number) => {
    setLoading(true)
    try {
      const result = await getAlerts({ state, from, take: occurrencePageSize, skip })
      setPage(previous => skip === 0 ? result : { ...result, items: [...previous.items, ...result.items] })
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível carregar as ocorrências.')
    } finally { setLoading(false) }
  }, [state, from])

  useEffect(() => { void load(0) }, [load])

  const acknowledge = async (id: string) => {
    setBusyId(id)
    setError(null)
    try {
      await acknowledgeAlert(id)
      await Promise.all([refresh(), load(0)])
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível reconhecer o alerta.')
    } finally {
      setBusyId(null)
    }
  }

  const shown = page.items.length
  const hasMore = shown < page.total
  const total = Object.values(page.byState).reduce((sum, count) => sum + (count ?? 0), 0)

  return <>
    {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
    <section className="summary-chips">
      {occurrenceFilters.map(filter => <button
        key={filter.value}
        type="button"
        className={state === filter.value ? 'active' : ''}
        onClick={() => setState(filter.value)}
      >{filter.label} <strong>{filter.value === 'all' ? total : page.byState[filter.value] ?? 0}</strong></button>)}
      <label className="toolbar-field chip-period">Período
        <select aria-label="Filtrar período das ocorrências" value={period} onChange={event => setPeriod(event.target.value)}>
          {occurrencePeriodFilters.map(item => <option key={item.id} value={item.id}>{item.label}</option>)}
        </select>
      </label>
    </section>
    <article className="panel">
      {loading && shown === 0 && <div className="modal-loading"><RefreshCw className="spin" size={20} /> Carregando ocorrências…</div>}
      {!loading && shown === 0 && <div className="empty-state"><Check size={22} /> Nenhum alerta nesse estado.</div>}
      {shown > 0 && <AlertList alerts={page.items} acknowledge={id => void acknowledge(id)} busyId={busyId} />}
      {hasMore && <button className="secondary-button load-more" disabled={loading} onClick={() => void load(shown)}>
        {loading ? 'Carregando…' : `Carregar mais (${(page.total - shown).toLocaleString('pt-BR')} restantes)`}
      </button>}
    </article>
  </>
}

interface RuleGroup {
  id: string
  name: string
  components: Array<{ id: string; name: string; rules: AlertRule[] }>
}

function groupRules(rules: AlertRule[], search: string, filter: 'all' | 'enabled' | 'disabled'): RuleGroup[] {
  const term = search.trim().toLowerCase()
  const groups = new Map<string, RuleGroup>()
  for (const rule of rules) {
    if (filter !== 'all' && rule.enabled !== (filter === 'enabled')) continue
    if (term && !`${rule.name} ${rule.componentName} ${rule.installationName} ${probeTypeLabel(rule.probeType)}`.toLowerCase().includes(term)) continue
    let group = groups.get(rule.installationId)
    if (!group) {
      group = { id: rule.installationId, name: rule.installationName, components: [] }
      groups.set(rule.installationId, group)
    }
    let componentGroup = group.components.find(item => item.id === rule.componentId)
    if (!componentGroup) {
      componentGroup = { id: rule.componentId, name: rule.componentName, rules: [] }
      group.components.push(componentGroup)
    }
    componentGroup.rules.push(rule)
  }
  return [...groups.values()]
}

function AlertRulesTab({ components, isAdministrator }: { components: ComponentSnapshot[]; isAdministrator: boolean }) {
  const [rules, setRules] = useState<AlertRule[] | null>(null)
  const [search, setSearch] = useState('')
  const [filter, setFilter] = useState<'all' | 'enabled' | 'disabled'>('all')
  const [busyId, setBusyId] = useState<string | null>(null)
  const [editing, setEditing] = useState<AlertRule | null | undefined>(undefined)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      setRules(await getAlertRules())
      setError(null)
    } catch (reason) {
      setRules([])
      setError(reason instanceof Error ? reason.message : 'Não foi possível carregar as regras de alerta.')
    }
  }, [])
  useEffect(() => { void load() }, [load])

  async function run(id: string, action: () => Promise<void>, success: string) {
    setBusyId(id)
    setMessage(null)
    try {
      await action()
      setMessage(success)
      setError(null)
      await load()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'A operação não foi concluída.')
    } finally { setBusyId(null) }
  }

  const remove = (rule: AlertRule) => {
    const question = rule.isAutomatic
      ? `Remover a regra padrão “${rule.name}”? O coletor cria a regra padrão dessa verificação de novo na próxima coleta. Para parar de alertar, desative em vez de remover.`
      : `Remover a regra “${rule.name}” e o histórico de ocorrências dela?`
    if (window.confirm(question)) void run(rule.id, () => deleteAlertRule(rule.id), 'Regra removida.')
  }

  const groups = useMemo(() => groupRules(rules ?? [], search, filter), [rules, search, filter])
  const visible = groups.reduce((total, group) => total + group.components.reduce((count, item) => count + item.rules.length, 0), 0)

  return <>
    {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
    {message && <div className="success-banner"><Check size={16} /> {message}</div>}
    <section className="toolbar">
      <div className="search-box">
        <Search size={16} />
        <input value={search} onChange={event => setSearch(event.target.value)} placeholder="Buscar por regra, componente ou instalação" aria-label="Buscar regra de alerta" />
      </div>
      <div className="chip-row">
        {([['all', 'Todas'], ['enabled', 'Ativas'], ['disabled', 'Desativadas']] as const).map(([value, label]) => <button
          key={value}
          type="button"
          className={`chip-button ${filter === value ? 'active' : ''}`}
          onClick={() => setFilter(value)}
        >{label}</button>)}
      </div>
      {isAdministrator && <button type="button" className="primary-button" disabled={components.length === 0} onClick={() => setEditing(null)}><Plus size={15} /> Nova regra</button>}
    </section>

    {rules === null && <article className="panel"><div className="modal-loading"><RefreshCw className="spin" size={20} /> Carregando regras…</div></article>}
    {rules !== null && rules.length === 0 && <article className="panel"><div className="tab-empty">
      <Siren size={22} />
      <div><strong>Nenhuma regra ainda</strong><p>No primeiro ciclo de cada componente o coletor cria uma regra padrão por verificação: abre depois de duas coletas seguidas em falha, com cooldown de 5 minutos. Cadastre uma instalação e espere a primeira coleta, ou crie uma regra própria agora.</p></div>
    </div></article>}
    {rules !== null && rules.length > 0 && visible === 0 && <article className="panel"><div className="tab-empty">
      <Search size={22} />
      <div><strong>Nenhuma regra corresponde ao filtro</strong><p>Ajuste a busca ou volte para “Todas”.</p></div>
    </div></article>}

    {groups.map(group => <article className="panel rule-group" key={group.id}>
      <header className="rule-group-head">
        <span><Server size={16} /></span>
        <div>
          <strong>{group.name}</strong>
          <small>{group.components.reduce((count, item) => count + item.rules.length, 0)} regra(s) em {group.components.length} componente(s)</small>
        </div>
      </header>
      {group.components.map(componentGroup => <div className="rule-subgroup" key={componentGroup.id}>
        <div className="rule-subgroup-head"><TerminalSquare size={13} /> {componentGroup.name}</div>
        {componentGroup.rules.map(rule => <div className="rule-row" key={rule.id}>
          <div className="rule-identity">
            <div className="rule-title">
              <strong>{rule.name}</strong>
              <span className={`severity-tag ${rule.severity.toLowerCase()}`}>{severityLabel(rule.severity)}</span>
              {rule.isAutomatic && <span className="origin-tag" title="Criada pelo coletor no primeiro ciclo do componente">Padrão</span>}
            </div>
            <p>{ruleSentence(rule)}</p>
          </div>
          <div className="rule-actions">
            <button
              type="button"
              className={`chip-button ${rule.enabled ? 'active' : ''}`}
              disabled={!isAdministrator || busyId === rule.id}
              onClick={() => void run(rule.id, () => setAlertRuleEnabled(rule.id, !rule.enabled), rule.enabled ? 'Regra desativada.' : 'Regra ativada.')}
            >{rule.enabled ? <Bell size={13} /> : <BellOff size={13} />}{rule.enabled ? 'Ativa' : 'Desativada'}</button>
            {isAdministrator && <>
              <button type="button" className="row-action" disabled={busyId === rule.id} onClick={() => setEditing(rule)} aria-label={`Editar regra ${rule.name}`}><Pencil size={14} /></button>
              <button type="button" className="row-action danger" disabled={busyId === rule.id} onClick={() => remove(rule)} aria-label={`Remover regra ${rule.name}`}><Trash2 size={14} /></button>
            </>}
          </div>
        </div>)}
      </div>)}
    </article>)}

    {!isAdministrator && <div className="read-only-notice"><LockKeyhole size={22} /><div><strong>Somente leitura</strong><p>Criar, editar, ativar e remover regras exige o perfil Administrator. Toda alteração fica registrada na auditoria.</p></div></div>}

    {editing !== undefined && <AlertRuleDialog
      key={editing?.id ?? 'nova'}
      rule={editing}
      components={components}
      rules={rules ?? []}
      close={() => setEditing(undefined)}
      onSaved={async saved => { setEditing(undefined); setMessage(saved); setError(null); await load() }}
    />}
  </>
}

function AlertRuleDialog({ rule, components, rules, close, onSaved }: {
  rule: AlertRule | null
  components: ComponentSnapshot[]
  rules: AlertRule[]
  close: () => void
  onSaved: (message: string) => Promise<void>
}) {
  const [name, setName] = useState(rule?.name ?? '')
  const [componentId, setComponentId] = useState(rule?.componentId ?? components[0]?.id ?? '')
  const [probeType, setProbeType] = useState<ProbeType>(() => rule?.probeType ?? (components[0]?.isSystem ? 'ServerCpu' : 'WindowsService'))
  const [threshold, setThreshold] = useState(rule?.thresholdPercent != null ? String(rule.thresholdPercent) : '')
  const [severity, setSeverity] = useState<AlertSeverity>(rule?.severity ?? 'Critical')
  const [triggerStatuses, setTriggerStatuses] = useState<HealthStatus[]>(rule?.triggerStatuses ?? ['Warning', 'Critical'])
  const [failures, setFailures] = useState(String(rule?.minimumConsecutiveFailures ?? 2))
  const [cooldown, setCooldown] = useState(String(rule?.cooldownSeconds ?? 300))
  const [enabled, setEnabled] = useState(rule?.enabled ?? true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const installations = useMemo(() => groupComponentsByInstallation(components), [components])
  const component = components.find(item => item.id === componentId)
  const duplicate = !rule && rules.some(item => item.componentId === componentId && item.probeType === probeType)
  const failureCount = Number(failures)
  // O coletor do servidor só responde pelo alvo da máquina, e os das demais verificações
  // só pelos componentes cadastrados: oferecer a combinação errada renderia regra muda.
  const probeTypesForTarget = probeTypeOptions.filter(option => isServerProbe(option.value) === Boolean(component?.isSystem))
  const serverProbe = isServerProbe(probeType)
  const thresholdValue = threshold.trim() === '' ? null : Number(threshold)

  const toggleStatus = (status: HealthStatus) => setTriggerStatuses(current =>
    current.includes(status) ? current.filter(item => item !== status) : [...current, status])

  const chooseComponent = (id: string) => {
    setComponentId(id)
    const target = components.find(item => item.id === id)
    const allowed = probeTypeOptions.filter(option => isServerProbe(option.value) === Boolean(target?.isSystem))
    if (!allowed.some(option => option.value === probeType)) setProbeType(allowed[0].value)
  }

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (serverProbe && thresholdValue !== null && (Number.isNaN(thresholdValue) || thresholdValue <= 0 || thresholdValue > 100)) {
      setError('O limite de uso deve ficar entre 1 e 100 por cento.')
      return
    }

    if (!serverProbe && triggerStatuses.length === 0) {
      setError('Escolha ao menos um estado que representa falha.')
      return
    }

    setBusy(true)
    setError(null)
    try {
      const settings = {
        name: name.trim(),
        severity,
        minimumConsecutiveFailures: failureCount,
        cooldownSeconds: Number(cooldown),
        triggerStatuses,
        thresholdPercent: serverProbe ? thresholdValue : null,
      }
      if (rule) await updateAlertRule(rule.id, { ...settings, enabled })
      else await createAlertRule({ ...settings, componentId, probeType })
      await onSaved(rule ? 'Regra atualizada.' : 'Regra criada.')
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível salvar a regra.')
    } finally { setBusy(false) }
  }

  const condition = serverProbe && thresholdValue !== null
    ? `passar de ${thresholdValue}% de uso`
    : `ficar em ${triggerStatusSentence(triggerStatuses)}`
  const preview = `Abre um alerta ${severityAdjective(severity)} quando a verificação ${probeTypeLabel(probeType)} de ${component?.name ?? 'componente'} ${condition} ${failureCount === 1 ? 'em uma coleta' : `em ${failureCount} coletas seguidas`}. ${Number(cooldown) === 0 ? 'Depois de resolvido, um novo alerta pode abrir já na coleta seguinte.' : `Depois de resolvido, um novo alerta só abre ${cooldownLabel(Number(cooldown))} mais tarde.`}`

  return <div className="modal-backdrop">
    <section className="modal-card" role="dialog" aria-modal="true" aria-labelledby="alert-rule-dialog-title">
      <header className="modal-header">
        <div>
          <span>Regra de alerta</span>
          <h2 id="alert-rule-dialog-title">{rule ? 'Editar regra' : 'Nova regra de alerta'}</h2>
          <p>A regra observa uma verificação já coletada; ela não faz consulta nova no ambiente monitorado.</p>
        </div>
        <button className="icon-button" onClick={close} disabled={busy} aria-label="Fechar regra"><X size={18} /></button>
      </header>
      <form onSubmit={event => void submit(event)}>
        {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
        <ol className="rule-steps">
          <li className="rule-step">
            <span className="rule-step-index">1</span>
            <div>
              <h3>Nome da regra</h3>
              <p>É o texto que aparece na lista de ocorrências e chega no e-mail e no webhook.</p>
              <input aria-label="Nome da regra" value={name} onChange={event => setName(event.target.value)} maxLength={200} placeholder="Ex.: AppServer REST fora do ar" required />
            </div>
          </li>
          <li className="rule-step">
            <span className="rule-step-index">2</span>
            <div>
              <h3>Condição</h3>
              <p>{rule
                ? 'O componente e a verificação não mudam depois de criada: crie outra regra para observar outra coisa.'
                : serverProbe
                  ? 'Escolha o recurso da máquina e a partir de quanto uso o alerta abre.'
                  : 'Escolha o componente, a verificação observada e os estados que contam como falha.'}</p>
              <div className="form-grid">
                <label>Componente
                  <select aria-label="Componente" value={componentId} onChange={event => chooseComponent(event.target.value)} disabled={Boolean(rule)} required>
                    {rule && <option value={rule.componentId}>{rule.componentName}</option>}
                    {!rule && installations.map(installation => <optgroup key={installation.id} label={installation.name}>
                      {installation.components.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
                    </optgroup>)}
                  </select>
                </label>
                <label>Verificação
                  <select aria-label="Verificação" value={probeType} onChange={event => setProbeType(event.target.value as ProbeType)} disabled={Boolean(rule)}>
                    {(rule ? probeTypeOptions : probeTypesForTarget).map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
                  </select>
                </label>
                {serverProbe && <label>Dispara com uso acima de (%)
                  <input type="number" aria-label="Limite de uso" min={1} max={100} value={threshold} onChange={event => setThreshold(event.target.value)} placeholder="Ex.: 90" />
                </label>}
              </div>
              {serverProbe
                ? <p className="field-hint">Em branco, a regra segue os limites de atenção e crítico que a aba Servidor mostra. Com um número aqui, ela compara a medida direto e ignora esses limites — dá para ter uma regra de atenção em 85% e outra, crítica, em 95%.</p>
                : <>
                  <p className="field-hint">Estados que contam como falha</p>
                  <div className="trigger-options">
                    {triggerStatusOptions.map(option => <label key={option.value} className={`trigger-option ${triggerStatuses.includes(option.value) ? 'checked' : ''}`}>
                      <input type="checkbox" checked={triggerStatuses.includes(option.value)} onChange={() => toggleStatus(option.value)} />
                      <span><strong>{option.label}</strong><small>{option.description}</small></span>
                    </label>)}
                  </div>
                </>}
              {duplicate && <div className="inline-warning"><AlertTriangle size={15} /> Esse componente já tem uma regra para essa verificação. As duas passam a abrir alerta juntas.</div>}
            </div>
          </li>
          <li className="rule-step">
            <span className="rule-step-index">3</span>
            <div>
              <h3>Comportamento da avaliação</h3>
              <p>Quantas coletas em falha antes de abrir e quanto tempo esperar antes de reabrir o mesmo alerta.</p>
              <div className="form-grid">
                <label>Falhas consecutivas
                  <input type="number" aria-label="Falhas consecutivas" min={1} max={20} value={failures} onChange={event => setFailures(event.target.value)} required />
                </label>
                <label>Cooldown
                  <select aria-label="Cooldown" value={cooldown} onChange={event => setCooldown(event.target.value)}>
                    {cooldownOptions.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
                  </select>
                </label>
              </div>
              <p className="field-hint">Entre 1 e 20 coletas. Com 1 o alerta abre no primeiro sinal e pega falha curta; com 2 ou mais ele ignora o pico isolado de uma coleta só. O cooldown vale a partir da resolução: sem ele, um serviço que oscila abre alerta a cada ciclo.</p>
            </div>
          </li>
          <li className="rule-step">
            <span className="rule-step-index">4</span>
            <div>
              <h3>Severidade e envio</h3>
              <p>A severidade define a cor e o texto do incidente. O envio segue os pontos de contato ativos.</p>
              <div className="form-grid">
                <label>Severidade
                  <select aria-label="Severidade" value={severity} onChange={event => setSeverity(event.target.value as AlertSeverity)}>
                    {severityOptions.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
                  </select>
                </label>
                {rule && <label className="checkbox-label">
                  <input type="checkbox" checked={enabled} onChange={event => setEnabled(event.target.checked)} /> Regra ativa
                </label>}
              </div>
              <p className="field-hint">Abrir, reativar e resolver são enviados para todos os pontos de contato ativos: a severidade não escolhe o destino.</p>
            </div>
          </li>
        </ol>
        <div className="rule-preview"><Siren size={17} /><span>{preview}</span></div>
        <footer className="modal-actions">
          <button type="button" className="secondary-button" onClick={close} disabled={busy}>Cancelar</button>
          <button className="primary-button" disabled={busy}>{busy ? <RefreshCw className="spin" size={16} /> : <Check size={16} />}{busy ? 'Salvando…' : 'Salvar regra'}</button>
        </footer>
      </form>
    </section>
  </div>
}

function ContactPointsTab({ isAdministrator, goTo }: { isAdministrator: boolean; goTo: (page: Page) => void }) {
  const [channels, setChannels] = useState<NotificationChannel[] | null>(null)
  const [draft, setDraft] = useState({ name: '', type: 'Webhook' as NotificationChannelType, url: '', enabled: true })
  const [busy, setBusy] = useState(false)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)

  const load = useCallback(async () => {
    if (!isAdministrator) return
    try {
      setChannels(await getNotificationChannels())
      setError(null)
    } catch (reason) {
      setChannels([])
      setError(reason instanceof Error ? reason.message : 'Não foi possível carregar os pontos de contato.')
    }
  }, [isAdministrator])
  useEffect(() => { void load() }, [load])

  async function run(id: string | null, action: () => Promise<void>, success: string) {
    if (id) setBusyId(id); else setBusy(true)
    setMessage(null)
    try {
      await action()
      setMessage(success)
      setError(null)
      await load()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'A operação não foi concluída.')
    } finally { setBusyId(null); setBusy(false) }
  }

  const create = (event: FormEvent) => {
    event.preventDefault()
    void run(null, async () => {
      await createNotificationChannel({ name: draft.name.trim(), type: draft.type, url: draft.url.trim(), enabled: draft.enabled })
      setDraft({ name: '', type: 'Webhook', url: '', enabled: true })
    }, 'Ponto de contato criado.')
  }

  if (!isAdministrator) {
    return <div className="read-only-notice"><LockKeyhole size={22} /><div><strong>Somente administradores</strong><p>Os pontos de contato guardam a URL de destino cifrada e só aparecem para o perfil Administrator.</p></div></div>
  }

  const selectedType = channelTypeOptions.find(option => option.value === draft.type)
  return <>
    {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
    {message && <div className="success-banner"><Check size={16} /> {message}</div>}

    <article className="panel settings-panel">
      <header className="panel-header"><div><h3>Pontos de contato</h3><p>Destinos que recebem abertura, reativação e resolução de alerta</p></div></header>
      {channels === null && <div className="modal-loading"><RefreshCw className="spin" size={20} /> Carregando destinos…</div>}
      {channels?.length === 0 && <div className="tab-empty">
        <Send size={22} />
        <div><strong>Nenhum destino externo</strong><p>Sem ponto de contato, o alerta continua aparecendo no painel e, se o envio de e-mail estiver ligado, também por e-mail. Um webhook manda o evento para Teams, Slack, Discord ou para a sua central.</p></div>
      </div>}
      {channels && channels.length > 0 && <ul className="entity-list">
        {channels.map(channel => <li className="entity-row" key={channel.id}>
          <div className="entity-main">
            <strong>{channel.name}</strong>
            <small>{channelTypeLabel(channel.type)} · {channel.configured ? 'URL cifrada guardada no servidor' : 'sem URL configurada'}</small>
          </div>
          <div className="entity-actions">
            <button
              type="button"
              className={`chip-button ${channel.enabled ? 'active' : ''}`}
              disabled={busyId === channel.id}
              onClick={() => void run(channel.id, () => setNotificationChannelEnabled(channel.id, !channel.enabled), channel.enabled ? 'Destino desativado.' : 'Destino ativado.')}
            >{channel.enabled ? <Bell size={13} /> : <BellOff size={13} />}{channel.enabled ? 'Ativo' : 'Inativo'}</button>
            <button
              type="button"
              className="row-action danger"
              disabled={busyId === channel.id}
              aria-label={`Remover ${channel.name}`}
              onClick={() => { if (window.confirm(`Remover o ponto de contato “${channel.name}”?`)) void run(channel.id, () => deleteNotificationChannel(channel.id), 'Ponto de contato removido.') }}
            ><Trash2 size={14} /></button>
          </div>
        </li>)}
      </ul>}
    </article>

    <article className="panel settings-panel">
      <header className="panel-header"><div><h3>Novo ponto de contato</h3><p>URL HTTPS, sem credenciais no endereço</p></div></header>
      <form className="settings-form" onSubmit={create}>
        <div className="form-grid">
          <label>Nome<input aria-label="Nome do ponto de contato" value={draft.name} onChange={event => setDraft({ ...draft, name: event.target.value })} maxLength={160} placeholder="Ex.: Plantão de infraestrutura" required /></label>
          <label>Tipo
            <select aria-label="Tipo do ponto de contato" value={draft.type} onChange={event => setDraft({ ...draft, type: event.target.value as NotificationChannelType })}>
              {channelTypeOptions.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
            </select>
          </label>
          <label className="wide-field">URL<input aria-label="URL do ponto de contato" type="url" value={draft.url} onChange={event => setDraft({ ...draft, url: event.target.value })} placeholder="https://" required /></label>
        </div>
        <p className="field-hint">{selectedType?.hint} A URL é cifrada com Data Protection e nunca volta pela API nem entra na auditoria. O envio resolve o IP antes de conectar, recusa endereço de link-local e de metadados, não segue redirecionamento e desiste em cinco segundos.</p>
        <div className="settings-options">
          <label className="checkbox-label"><input type="checkbox" checked={draft.enabled} onChange={event => setDraft({ ...draft, enabled: event.target.checked })} /> Já começar ativo</label>
        </div>
        <p className="field-hint">O corpo enviado leva só tipo do evento, correlação, severidade e estado — sem nome de servidor, caminho de arquivo ou evidência.</p>
        <div className="form-actions"><button className="primary-button" type="submit" disabled={busy}>{busy ? 'Salvando…' : 'Adicionar ponto de contato'}</button></div>
      </form>
    </article>

    <article className="panel setting-card">
      <span><Mail size={20} /></span>
      <div><h3>Envio por e-mail</h3><p>O SMTP, o remetente, os destinatários e o teste de envio ficam em Configurações → Envio de e-mail.</p></div>
      <button type="button" className="secondary-button" onClick={() => goTo('settings')}>Abrir configurações</button>
    </article>
  </>
}

function SilencesTab({ components, isAdministrator }: { components: ComponentSnapshot[]; isAdministrator: boolean }) {
  const [windows, setWindows] = useState<MaintenanceWindow[] | null>(null)
  const [target, setTarget] = useState('')
  const [name, setName] = useState('')
  const [startsAt, setStartsAt] = useState(() => toLocalInputValue(new Date()))
  const [durationMinutes, setDurationMinutes] = useState('120')
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)

  const installations = useMemo(() => groupComponentsByInstallation(components), [components])

  const load = useCallback(async () => {
    try {
      setWindows(await getMaintenanceWindows())
      setError(null)
    } catch (reason) {
      setWindows([])
      setError(reason instanceof Error ? reason.message : 'Não foi possível carregar os silenciamentos.')
    }
  }, [])
  useEffect(() => { void load() }, [load])

  useEffect(() => {
    if (!target && installations.length > 0) setTarget(`installation:${installations[0].id}`)
  }, [installations, target])

  async function run(id: string | null, action: () => Promise<void>, success: string) {
    if (id) setBusyId(id); else setBusy(true)
    setMessage(null)
    try {
      await action()
      setMessage(success)
      setError(null)
      await load()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'A operação não foi concluída.')
    } finally { setBusyId(null); setBusy(false) }
  }

  const create = (event: FormEvent) => {
    event.preventDefault()
    const [kind, id] = target.split(':')
    const start = new Date(startsAt)
    const end = new Date(start.getTime() + Number(durationMinutes) * 60_000)
    void run(null, async () => {
      await createMaintenanceWindow({
        installationId: kind === 'installation' ? id : undefined,
        componentId: kind === 'component' ? id : undefined,
        name: name.trim(),
        startsAt: start.toISOString(),
        endsAt: end.toISOString(),
        reason: reason.trim() || undefined,
      })
      setName('')
      setReason('')
      setStartsAt(toLocalInputValue(new Date()))
    }, 'Silenciamento criado.')
  }

  const now = Date.now()
  const endsAtPreview = new Date(new Date(startsAt).getTime() + Number(durationMinutes) * 60_000)
  const validPreview = !Number.isNaN(endsAtPreview.getTime())

  return <>
    {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
    {message && <div className="success-banner"><Check size={16} /> {message}</div>}

    <article className="panel settings-panel">
      <header className="panel-header"><div><h3>Silenciamentos</h3><p>Enquanto a janela vale, o componente aparece em manutenção e nenhuma ocorrência nova é aberta</p></div></header>
      {windows === null && <div className="modal-loading"><RefreshCw className="spin" size={20} /> Carregando janelas…</div>}
      {windows?.length === 0 && <div className="tab-empty">
        <BellOff size={22} />
        <div><strong>Nenhum silenciamento</strong><p>Abra uma janela antes de uma parada programada: a coleta continua registrando evidência, as ocorrências abertas ficam silenciadas e nenhuma nova é aberta. Ao terminar, uma falha que persiste reativa o incidente e uma recuperação o resolve.</p></div>
      </div>}
      {windows && windows.length > 0 && <ul className="entity-list">
        {windows.map(silence => {
          const start = new Date(silence.startsAt).getTime()
          const end = new Date(silence.endsAt).getTime()
          const state = end <= now ? { label: 'Encerrado', status: 'Unknown' as HealthStatus } : start > now ? { label: 'Agendado', status: 'Warning' as HealthStatus } : { label: 'Silenciando', status: 'Maintenance' as HealthStatus }
          return <li className="entity-row" key={silence.id}>
            <div className="entity-main">
              <strong>{silence.name}</strong>
              <small>{silence.componentName ? `${silence.componentName} · ${silence.installationName}` : `${silence.installationName} · instalação inteira`}</small>
              <small>{formatDateTime(silence.startsAt)} até {formatDateTime(silence.endsAt)}{silence.reason ? ` · ${silence.reason}` : ''}</small>
            </div>
            <div className="entity-actions">
              <StatusBadge status={state.status} label={state.label} />
              {isAdministrator && <button
                type="button"
                className="row-action danger"
                disabled={busyId === silence.id}
                aria-label={`Remover silenciamento ${silence.name}`}
                onClick={() => { if (window.confirm(`Encerrar o silenciamento “${silence.name}”?`)) void run(silence.id, () => deleteMaintenanceWindow(silence.id), 'Silenciamento encerrado.') }}
              ><Trash2 size={14} /></button>}
            </div>
          </li>
        })}
      </ul>}
    </article>

    {isAdministrator && <article className="panel settings-panel">
      <header className="panel-header"><div><h3>Novo silenciamento</h3><p>Uma instalação inteira ou um componente, nunca os dois</p></div></header>
      <form className="settings-form" onSubmit={create}>
        <div className="form-grid">
          <label>Alvo
            <select aria-label="Alvo do silenciamento" value={target} onChange={event => setTarget(event.target.value)} required>
              {installations.map(installation => <optgroup key={installation.id} label={installation.name}>
                <option value={`installation:${installation.id}`}>{installation.name} · instalação inteira</option>
                {installation.components.map(item => <option key={item.id} value={`component:${item.id}`}>{item.name}</option>)}
              </optgroup>)}
            </select>
          </label>
          <label>Nome<input aria-label="Nome do silenciamento" value={name} onChange={event => setName(event.target.value)} maxLength={160} placeholder="Ex.: Atualização do AppServer" required /></label>
          <label>Início<input type="datetime-local" aria-label="Início do silenciamento" value={startsAt} onChange={event => setStartsAt(event.target.value)} required /></label>
          <label>Duração
            <select aria-label="Duração do silenciamento" value={durationMinutes} onChange={event => setDurationMinutes(event.target.value)}>
              {silenceDurationOptions.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
            </select>
          </label>
          <label className="wide-field">Motivo opcional<input aria-label="Motivo do silenciamento" value={reason} onChange={event => setReason(event.target.value)} maxLength={500} placeholder="Ex.: aplicação de pacote e reinício dos serviços" /></label>
        </div>
        <p className="field-hint">{validPreview ? `Termina em ${formatDateTime(endsAtPreview.toISOString())}.` : 'Informe um início válido.'} O término precisa cair no futuro e a janela dura no máximo 90 dias. Silenciar não para serviço: para derrubar os ambientes de uma vez use o modo manutenção, na aba Instalações.</p>
        <div className="form-actions"><button className="primary-button" type="submit" disabled={busy || installations.length === 0}>{busy ? 'Salvando…' : 'Criar silenciamento'}</button></div>
      </form>
    </article>}

    {!isAdministrator && <div className="read-only-notice"><LockKeyhole size={22} /><div><strong>Somente leitura</strong><p>Abrir e encerrar janela de manutenção exige o perfil Administrator.</p></div></div>}
  </>
}
