import { FormEvent, useCallback, useEffect, useMemo, useState } from 'react'
import type { CSSProperties } from 'react'
import {
  Activity, AlertTriangle, Archive, Bell, Boxes, BriefcaseBusiness, Check, ChevronDown, CircleHelp,
  Clock3, FileText, Gauge, HeartPulse, ListFilter, LockKeyhole, LogOut, Menu, Moon, MoreHorizontal,
  Plus, RefreshCw, Search, Server, Settings, ShieldCheck, Sun, TerminalSquare, UserRound, X,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { acknowledgeAlert, connectLiveUpdates, createInstallation, getAuthStatus, getDashboard, login, session, setup } from './api'
import type { AlertSnapshot, AuthStatus, ComponentSnapshot, ComponentType, DashboardSummary, EnvironmentKind, HealthStatus } from './types'

type Page = 'overview' | 'installations' | 'logs' | 'jobs' | 'alerts' | 'settings' | 'audit' | 'diagnostics'

const navItems: Array<{ id: Page; label: string; icon: LucideIcon }> = [
  { id: 'overview', label: 'Visão geral', icon: Gauge },
  { id: 'installations', label: 'Instalações', icon: Server },
  { id: 'logs', label: 'Logs', icon: FileText },
  { id: 'jobs', label: 'Jobs', icon: BriefcaseBusiness },
  { id: 'alerts', label: 'Alertas', icon: Bell },
]

const secondaryNav: Array<{ id: Page; label: string; icon: LucideIcon }> = [
  { id: 'settings', label: 'Configurações', icon: Settings },
  { id: 'audit', label: 'Auditoria', icon: Archive },
  { id: 'diagnostics', label: 'Diagnóstico', icon: Activity },
]

const pageTitles: Record<Page, { title: string; eyebrow: string }> = {
  overview: { title: 'Visão geral', eyebrow: 'Operação em tempo real' },
  installations: { title: 'Instalações', eyebrow: 'Ambientes e componentes' },
  logs: { title: 'Logs', eyebrow: 'Eventos sanitizados' },
  jobs: { title: 'Jobs', eyebrow: 'Heartbeats e execução' },
  alerts: { title: 'Alertas', eyebrow: 'Incidentes e resolução' },
  settings: { title: 'Configurações', eyebrow: 'Políticas do Pulse' },
  audit: { title: 'Auditoria', eyebrow: 'Rastreabilidade administrativa' },
  diagnostics: { title: 'Diagnóstico', eyebrow: 'Saúde interna e permissões' },
}

const componentTypeOptions: Array<{ value: ComponentType; label: string }> = [
  { value: 'AppServer', label: 'AppServer' },
  { value: 'Rest', label: 'REST / WebApp' },
  { value: 'Broker', label: 'Broker' },
  { value: 'Worker', label: 'Worker' },
  { value: 'Job', label: 'Job / integração' },
  { value: 'Tss', label: 'TSS' },
  { value: 'DbAccess', label: 'DBAccess' },
  { value: 'LicenseServer', label: 'License Server' },
  { value: 'HttpEndpoint', label: 'Endpoint HTTP(S)' },
  { value: 'WindowsService', label: 'Serviço Windows' },
  { value: 'WebApp', label: 'Aplicação web' },
  { value: 'Generic', label: 'Genérico' },
]

export default function App() {
  const [authStatus, setAuthStatus] = useState<AuthStatus | null>(null)
  const [authenticated, setAuthenticated] = useState(Boolean(session.token))
  const [summary, setSummary] = useState<DashboardSummary | null>(null)
  const [page, setPage] = useState<Page>('overview')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [mobileMenu, setMobileMenu] = useState(false)
  const [installationDialog, setInstallationDialog] = useState(false)
  const [theme, setTheme] = useState(() => localStorage.getItem('pulse.theme') ?? 'dark')

  useEffect(() => {
    document.documentElement.dataset.theme = theme
    localStorage.setItem('pulse.theme', theme)
  }, [theme])

  useEffect(() => {
    getAuthStatus()
      .then(setAuthStatus)
      .catch((reason: Error) => setError(reason.message))
      .finally(() => setLoading(false))
  }, [])

  const loadSummary = useCallback(async () => {
    if (!session.token) return
    try {
      const data = await getDashboard()
      setSummary(data)
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível carregar o dashboard.')
      if (!session.token) setAuthenticated(false)
    }
  }, [])

  useEffect(() => {
    if (!authenticated) return
    void loadSummary()
    return connectLiveUpdates(() => void loadSummary())
  }, [authenticated, loadSummary])

  const onAuthenticated = (token: string) => {
    session.token = token
    setAuthenticated(true)
  }

  const logout = () => {
    session.token = null
    setAuthenticated(false)
    setSummary(null)
  }

  const installationCreated = async () => {
    setInstallationDialog(false)
    setPage('installations')
    await loadSummary()
  }

  if (loading) return <Splash />
  if (!authenticated) return <LoginScreen status={authStatus} onAuthenticated={onAuthenticated} error={error} />

  const title = pageTitles[page]
  return (
    <div className="app-shell">
      <Sidebar active={page} setPage={setPage} open={mobileMenu} close={() => setMobileMenu(false)} logout={logout} />
      <main className="main-content">
        <header className="topbar">
          <button className="icon-button menu-button" onClick={() => setMobileMenu(true)} aria-label="Abrir menu"><Menu size={20} /></button>
          <div className="page-heading">
            <span>{title.eyebrow}</span>
            <h1>{title.title}</h1>
          </div>
          <div className="topbar-actions">
            {summary?.demoMode && <span className="demo-pill"><span /> Modo demonstração</span>}
            <button className="icon-button" onClick={() => setTheme(theme === 'dark' ? 'light' : 'dark')} aria-label="Alternar tema">
              {theme === 'dark' ? <Sun size={18} /> : <Moon size={18} />}
            </button>
            <button className="icon-button notification-button" aria-label="Notificações"><Bell size={18} /><i>{summary?.totals.activeAlerts ?? 0}</i></button>
            <div className="user-chip"><span>AD</span><div><strong>Administrador</strong><small>Operação local</small></div><ChevronDown size={15} /></div>
          </div>
        </header>

        {error && <div className="error-banner"><AlertTriangle size={18} /><span>{error}</span><button onClick={() => void loadSummary()}><RefreshCw size={15} /> Tentar novamente</button></div>}
        {!summary ? <DashboardSkeleton /> : <PageContent page={page} summary={summary} refresh={loadSummary} addInstallation={() => setInstallationDialog(true)} />}
        <footer className="app-footer"><span><span className="live-dot" /> Atualização em tempo real</span><span>Protheus Pulse 0.1.0 · produto independente</span></footer>
      </main>
      {installationDialog && <InstallationDialog close={() => setInstallationDialog(false)} onCreated={installationCreated} />}
    </div>
  )
}

function Sidebar({ active, setPage, open, close, logout }: { active: Page; setPage: (page: Page) => void; open: boolean; close: () => void; logout: () => void }) {
  const choose = (page: Page) => { setPage(page); close() }
  return <>
    {open && <button className="sidebar-backdrop" onClick={close} aria-label="Fechar menu" />}
    <aside className={`sidebar ${open ? 'open' : ''}`}>
      <div className="brand"><div className="brand-mark"><HeartPulse size={24} /></div><div><strong>Protheus</strong><span>Pulse</span></div><button className="mobile-close" onClick={close}><X size={20} /></button></div>
      <nav aria-label="Navegação principal">
        <span className="nav-section-label">Monitoramento</span>
        {navItems.map(item => <NavItem key={item.id} {...item} active={active === item.id} onClick={() => choose(item.id)} />)}
        <span className="nav-section-label secondary">Sistema</span>
        {secondaryNav.map(item => <NavItem key={item.id} {...item} active={active === item.id} onClick={() => choose(item.id)} />)}
      </nav>
      <div className="sidebar-callout"><ShieldCheck size={19} /><div><strong>Somente leitura</strong><span>Nenhum serviço ou arquivo monitorado é modificado.</span></div></div>
      <button className="logout-button" onClick={logout}><LogOut size={17} /> Encerrar sessão</button>
    </aside>
  </>
}

function NavItem({ label, icon: Icon, active, onClick }: { label: string; icon: LucideIcon; active: boolean; onClick: () => void }) {
  return <button className={`nav-item ${active ? 'active' : ''}`} onClick={onClick}><Icon size={18} /><span>{label}</span>{label === 'Alertas' && <i>3</i>}</button>
}

function LoginScreen({ status, onAuthenticated, error: initialError }: { status: AuthStatus | null; onAuthenticated: (token: string) => void; error: string | null }) {
  const [username, setUsername] = useState(status?.demoUsername ?? '')
  const [displayName, setDisplayName] = useState('Administrador')
  const [password, setPassword] = useState(status?.demoPassword ?? '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(initialError)
  const isSetup = status?.requiresSetup ?? false

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const token = isSetup ? await setup(username, displayName, password) : await login(username, password)
      onAuthenticated(token.accessToken)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Falha na autenticação.')
    } finally {
      setBusy(false)
    }
  }

  return <div className="login-page">
    <section className="login-story">
      <div className="story-grid" />
      <div className="story-brand"><div className="brand-mark"><HeartPulse size={28} /></div><span>Protheus <strong>Pulse</strong></span></div>
      <div className="story-copy">
        <span className="story-kicker"><span /> Observabilidade local</span>
        <h1>Seu ambiente.<br /><em>No ritmo certo.</em></h1>
        <p>Estado atual, evidência e histórico técnico em um único painel — sem depender da nuvem.</p>
        <div className="story-features"><span><Check size={15} /> Somente leitura</span><span><Check size={15} /> Dados no seu servidor</span><span><Check size={15} /> Tempo real</span></div>
      </div>
      <small>Projeto independente e não afiliado à TOTVS.</small>
    </section>
    <section className="login-form-area">
      <form className="login-card" onSubmit={submit}>
        <div className="login-icon"><LockKeyhole size={22} /></div>
        <span className="form-eyebrow">Acesso seguro</span>
        <h2>{isSetup ? 'Criar administrador' : 'Bem-vindo de volta'}</h2>
        <p>{isSetup ? 'Conclua a configuração inicial deste servidor.' : 'Entre para acompanhar seus ambientes.'}</p>
        {status?.demoMode && <div className="demo-credentials"><strong>Ambiente de demonstração</strong><span>As credenciais já foram preenchidas para você.</span></div>}
        {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
        <label>Usuário<input aria-label="Usuário" autoComplete="username" value={username} onChange={event => setUsername(event.target.value)} required /></label>
        {isSetup && <label>Nome de exibição<input aria-label="Nome de exibição" autoComplete="name" value={displayName} onChange={event => setDisplayName(event.target.value)} required /></label>}
        <label>Senha<input aria-label="Senha" type="password" autoComplete={isSetup ? 'new-password' : 'current-password'} value={password} onChange={event => setPassword(event.target.value)} required /></label>
        <button className="primary-button login-submit" disabled={busy}>{busy ? <RefreshCw className="spin" size={17} /> : <LockKeyhole size={17} />}{busy ? 'Validando…' : isSetup ? 'Criar e acessar' : 'Acessar dashboard'}</button>
        <div className="login-security"><ShieldCheck size={16} /> Senha protegida com hash forte. Sessão restrita a este Pulse.</div>
      </form>
    </section>
  </div>
}

function PageContent({ page, summary, refresh, addInstallation }: { page: Page; summary: DashboardSummary; refresh: () => Promise<void>; addInstallation: () => void }) {
  switch (page) {
    case 'overview': return <Overview summary={summary} refresh={refresh} addInstallation={addInstallation} />
    case 'installations': return <Installations summary={summary} addInstallation={addInstallation} />
    case 'logs': return <LogsPage />
    case 'jobs': return <JobsPage components={summary.components} />
    case 'alerts': return <AlertsPage alerts={summary.alerts} refresh={refresh} />
    case 'settings': return <SettingsPage />
    case 'audit': return <AuditPage />
    case 'diagnostics': return <DiagnosticsPage demo={summary.demoMode} />
  }
}

function Overview({ summary, refresh, addInstallation }: { summary: DashboardSummary; refresh: () => Promise<void>; addInstallation: () => void }) {
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
      <article className="panel availability-panel"><PanelHeader title="Disponibilidade consolidada" subtitle="Últimas 12 horas" action="12 horas" /><AvailabilityChart values={summary.availability} /><div className="chart-legend"><span><i className="legend-green" /> Disponibilidade</span><strong>{summary.totals.availabilityPercent}% <small>média</small></strong></div></article>
      <article className="panel status-panel"><PanelHeader title="Estado dos componentes" subtitle="Distribuição atual" /><div className="donut-wrap"><div className="donut" style={{ '--healthy': summary.totals.healthy, '--warning': summary.totals.warning, '--critical': summary.totals.critical, '--total': Math.max(summary.totals.components, 1) } as CSSProperties}><div><strong>{summary.totals.components}</strong><span>total</span></div></div><div className="status-legend"><StatusLegend label="Saudável" value={summary.totals.healthy} status="Healthy" /><StatusLegend label="Atenção" value={summary.totals.warning} status="Warning" /><StatusLegend label="Crítico" value={summary.totals.critical} status="Critical" /><StatusLegend label="Desconhecido" value={summary.totals.unknown} status="Unknown" /></div></div></article>
    </section>
    <section className="panel component-panel"><PanelHeader title="Componentes que pedem atenção" subtitle="Ordenados por impacto operacional" action="Ver instalações" /><ComponentTable components={summary.components.filter(item => item.status !== 'Healthy')} /></section>
    <section className="panel alert-panel"><PanelHeader title="Alertas recentes" subtitle="Evidência sanitizada e resolução automática" action="Ver todos" /><AlertList alerts={summary.alerts.slice(0, 4)} /></section>
  </div>
}

function MetricCard({ icon: Icon, label, value, detail, tone }: { icon: LucideIcon; label: string; value: string | number; detail: string; tone: string }) {
  return <article className={`metric-card ${tone}`}><div className="metric-icon"><Icon size={20} /></div><div><span>{label}</span><strong>{value}</strong><small>{detail}</small></div><MoreHorizontal size={18} className="more" /></article>
}

function PanelHeader({ title, subtitle, action }: { title: string; subtitle: string; action?: string }) {
  return <header className="panel-header"><div><h3>{title}</h3><p>{subtitle}</p></div>{action && <button>{action} <ChevronDown size={14} /></button>}</header>
}

function AvailabilityChart({ values }: { values: DashboardSummary['availability'] }) {
  const width = 640
  const height = 184
  const min = Math.min(...values.map(item => item.value), 90)
  const range = Math.max(100 - min, 1)
  const points = values.map((item, index) => `${(index / Math.max(values.length - 1, 1)) * width},${height - ((item.value - min) / range) * (height - 22) - 8}`).join(' ')
  const area = `0,${height} ${points} ${width},${height}`
  return <div className="chart"><div className="chart-y"><span>100%</span><span>98%</span><span>96%</span><span>94%</span></div><svg viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="none" role="img" aria-label="Disponibilidade ao longo das últimas doze horas"><defs><linearGradient id="area" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stopColor="#32d6a0" stopOpacity=".28" /><stop offset="1" stopColor="#32d6a0" stopOpacity="0" /></linearGradient></defs><line x1="0" y1="42" x2={width} y2="42" /><line x1="0" y1="88" x2={width} y2="88" /><line x1="0" y1="134" x2={width} y2="134" /><polygon points={area} fill="url(#area)" /><polyline points={points} fill="none" stroke="#32d6a0" strokeWidth="3" vectorEffect="non-scaling-stroke" /></svg><div className="chart-x">{values.filter((_, index) => index % 2 === 0).map(item => <span key={item.at}>{new Date(item.at).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}</span>)}</div></div>
}

function StatusLegend({ label, value, status }: { label: string; value: number; status: HealthStatus }) {
  return <div><span><i className={`status-dot ${status.toLowerCase()}`} />{label}</span><strong>{value}</strong></div>
}

function ComponentTable({ components }: { components: ComponentSnapshot[] }) {
  return <div className="table-wrap"><table><thead><tr><th>Componente</th><th>Instalação</th><th>Estado</th><th>Evidência atual</th><th>Métrica</th><th /></tr></thead><tbody>{components.map(item => <tr key={item.id}><td><div className="component-name"><span><TerminalSquare size={17} /></span><div><strong>{item.name}</strong><small>{typeLabel(item.type)}</small></div></div></td><td><div className="installation-name">{item.installationName}<small>{item.isDemo ? 'Dado demonstrativo' : 'Monitoramento real'}</small></div></td><td><StatusBadge status={item.status} /></td><td><div className="evidence">{item.summary}<small>desde {formatRelative(item.lastStateChangeAt)}</small></div></td><td><div className="metric-value">{item.metricValue ?? '—'} <small>{item.metricUnit}</small><span>{item.metricLabel}</span></div></td><td><button className="row-action"><MoreHorizontal size={18} /></button></td></tr>)}</tbody></table>{components.length === 0 && <div className="empty-state"><Check size={22} /> Nenhum componente pede atenção agora.</div>}</div>
}

function AlertList({ alerts, acknowledge, busyId }: { alerts: AlertSnapshot[]; acknowledge?: (id: string) => void; busyId?: string | null }) {
  return <div className="alert-list">{alerts.map(alert => <div className="alert-row" key={alert.id}><div className={`alert-symbol ${alert.severity.toLowerCase()}`}>{alert.state === 'Resolved' ? <Check size={17} /> : <AlertTriangle size={17} />}</div><div className="alert-main"><div><strong>{alert.ruleName}</strong><StatusBadge status={alert.state === 'Resolved' ? 'Healthy' : alert.severity === 'Critical' ? 'Critical' : 'Warning'} label={stateLabel(alert.state)} /></div><span>{alert.componentName} · {alert.installationName}</span><p>{alert.evidence}</p></div><div className="alert-time"><strong>{formatRelative(alert.startedAt)}</strong><span>#{alert.correlationId.slice(0, 8)}</span>{alert.state === 'Active' && acknowledge && <button className="secondary-button alert-action" disabled={busyId === alert.id} onClick={() => acknowledge(alert.id)}>{busyId === alert.id ? 'Salvando…' : 'Reconhecer'}</button>}</div></div>)}</div>
}

function Installations({ summary, addInstallation }: { summary: DashboardSummary; addInstallation: () => void }) {
  const groups = useMemo(() => Object.entries(summary.components.reduce<Record<string, ComponentSnapshot[]>>((result, item) => {
    ;(result[item.installationName] ??= []).push(item)
    return result
  }, {})), [summary.components])
  return <div className="page-body"><section className="intro-row"><div><h2>Ambientes cadastrados</h2><p>Componentes agrupados por instalação e impacto.</p></div><button className="primary-button" onClick={addInstallation}><Plus size={16} /> Adicionar instalação</button></section><div className="installation-grid">{groups.map(([name, components]) => <article className="panel installation-card" key={name}><header><div><span className="environment-tag">{environmentLabel(components[0]?.installationEnvironment)}</span><h3>{name}</h3></div><StatusBadge status={worstStatus(components ?? [])} /></header><div className="installation-stat"><span>{components?.length ?? 0} componentes</span><span>{components?.filter(item => item.status === 'Healthy').length ?? 0} saudáveis</span></div>{components?.map(component => <div className="mini-component" key={component.id}><div><i className={`status-dot ${component.status.toLowerCase()}`} /><span>{component.name}</span></div><small>{component.summary}</small></div>)}</article>)}</div></div>
}

interface ComponentDraft {
  key: number
  name: string
  type: ComponentType
  isRequired: boolean
}

function InstallationDialog({ close, onCreated }: { close: () => void; onCreated: () => Promise<void> }) {
  const [name, setName] = useState('')
  const [environment, setEnvironment] = useState<EnvironmentKind>('Production')
  const [customEnvironmentName, setCustomEnvironmentName] = useState('')
  const [tags, setTags] = useState('')
  const [components, setComponents] = useState<ComponentDraft[]>([{ key: 1, name: '', type: 'AppServer', isRequired: true }])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const updateComponent = (key: number, update: Partial<Omit<ComponentDraft, 'key'>>) => {
    setComponents(current => current.map(item => item.key === key ? { ...item, ...update } : item))
  }

  const addComponent = () => {
    setComponents(current => {
      const key = Math.max(...current.map(item => item.key), 0) + 1
      return [...current, { key, name: '', type: 'AppServer', isRequired: true }]
    })
  }

  const removeComponent = (key: number) => {
    setComponents(current => current.filter(item => item.key !== key))
  }

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await createInstallation({
        name,
        environment,
        customEnvironmentName: environment === 'Custom' ? customEnvironmentName : undefined,
        tags: tags.split(',').map(item => item.trim()).filter(Boolean),
        components: components.map(item => ({ name: item.name, type: item.type, isRequired: item.isRequired })),
      })
      await onCreated()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível cadastrar a instalação.')
    } finally {
      setBusy(false)
    }
  }

  return <div className="modal-backdrop">
    <section className="modal-card" role="dialog" aria-modal="true" aria-labelledby="installation-dialog-title">
      <header className="modal-header"><div><span>Fase 2 · Cadastro manual</span><h2 id="installation-dialog-title">Adicionar instalação</h2><p>Cadastre somente metadados técnicos. Nenhuma conexão será feita agora.</p></div><button className="icon-button" onClick={close} disabled={busy} aria-label="Fechar cadastro"><X size={18} /></button></header>
      <form onSubmit={submit}>
        {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
        <div className="form-grid">
          <label>Nome da instalação<input aria-label="Nome da instalação" value={name} onChange={event => setName(event.target.value)} maxLength={160} placeholder="Ex.: ERP Produção" required /></label>
          <label>Ambiente<select aria-label="Ambiente" value={environment} onChange={event => setEnvironment(event.target.value as EnvironmentKind)}><option value="Production">Produção</option><option value="Homologation">Homologação</option><option value="Development">Desenvolvimento</option><option value="Custom">Personalizado</option></select></label>
          {environment === 'Custom' && <label>Nome do ambiente<input aria-label="Nome do ambiente personalizado" value={customEnvironmentName} onChange={event => setCustomEnvironmentName(event.target.value)} maxLength={80} required /></label>}
          <label className={environment === 'Custom' ? '' : 'wide-field'}>Tags opcionais<input aria-label="Tags opcionais" value={tags} onChange={event => setTags(event.target.value)} placeholder="matriz, servidor-a" /></label>
        </div>
        <div className="component-editor">
          <div className="component-editor-heading"><div><h3>Componentes</h3><p>Informe ao menos um item que será monitorado nas próximas etapas.</p></div><button type="button" className="secondary-button" onClick={addComponent}><Plus size={15} /> Adicionar componente</button></div>
          {components.map((component, index) => <div className="component-editor-row" key={component.key}>
            <span>{index + 1}</span>
            <label>Nome<input aria-label={`Nome do componente ${index + 1}`} value={component.name} onChange={event => updateComponent(component.key, { name: event.target.value })} maxLength={160} placeholder="Ex.: AppServer REST" required /></label>
            <label>Tipo<select aria-label={`Tipo do componente ${index + 1}`} value={component.type} onChange={event => updateComponent(component.key, { type: event.target.value as ComponentType })}>{componentTypeOptions.map(option => <option value={option.value} key={option.value}>{option.label}</option>)}</select></label>
            <label className="checkbox-label"><input type="checkbox" checked={component.isRequired} onChange={event => updateComponent(component.key, { isRequired: event.target.checked })} /> Obrigatório</label>
            <button type="button" className="row-action remove-component" onClick={() => removeComponent(component.key)} disabled={components.length === 1} aria-label={`Remover componente ${index + 1}`}><X size={17} /></button>
          </div>)}
        </div>
        <div className="modal-safety"><ShieldCheck size={18} /><span>Este cadastro não inicia, para ou consulta serviços e não lê arquivos do servidor.</span></div>
        <footer className="modal-actions"><button type="button" className="secondary-button" onClick={close} disabled={busy}>Cancelar</button><button className="primary-button" disabled={busy}>{busy ? <RefreshCw className="spin" size={16} /> : <Plus size={16} />}{busy ? 'Cadastrando…' : 'Cadastrar instalação'}</button></footer>
      </form>
    </section>
  </div>
}

function LogsPage() {
  return <div className="page-body"><section className="toolbar"><div className="search-box"><Search size={17} /><input aria-label="Pesquisar logs" placeholder="Pesquisar eventos sanitizados…" /></div><button className="secondary-button"><ListFilter size={16} /> Filtros</button></section><article className="panel"><PanelHeader title="Grupos de erro" subtitle="Fixtures sintéticas · nenhuma informação de cliente" /><div className="log-group"><span className="log-count">8×</span><div><strong>Falha transitória ao consultar serviço externo</strong><p>Mensagem agrupada por assinatura; tokens e documentos foram removidos.</p><small>Console de Integração · há 11 min</small></div><StatusBadge status="Warning" label="Warning" /></div><div className="log-group"><span className="log-count muted">3×</span><div><strong>Tempo de resposta acima do esperado</strong><p>Latência observada na janela de demonstração.</p><small>AppServer REST · há 2 h</small></div><StatusBadge status="Healthy" label="Resolvido" /></div></article></div>
}

function JobsPage({ components }: { components: ComponentSnapshot[] }) {
  const jobs = components.filter(item => item.type === 'Job')
  return <div className="page-body"><section className="intro-row"><div><h2>Heartbeats de jobs</h2><p>Frequência, duração e atraso sem exigir customização do ERP.</p></div><button className="secondary-button"><CircleHelp size={16} /> Como integrar</button></section>{jobs.map(job => <article className="panel job-card" key={job.id}><div className="job-icon"><BriefcaseBusiness size={21} /></div><div><span>{job.installationName}</span><h3>{job.name}</h3><p>{job.summary}</p></div><div className="job-metrics"><div><span>Último sinal</span><strong>há {job.metricValue} min</strong></div><div><span>Tolerância</span><strong>5 min</strong></div><StatusBadge status={job.status} /></div></article>)}</div>
}

function AlertsPage({ alerts, refresh }: { alerts: AlertSnapshot[]; refresh: () => Promise<void> }) {
  const [busyId, setBusyId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const acknowledge = async (id: string) => {
    setBusyId(id)
    setError(null)
    try {
      await acknowledgeAlert(id)
      await refresh()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível reconhecer o alerta.')
    } finally {
      setBusyId(null)
    }
  }
  return <div className="page-body">{error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}<section className="summary-chips"><button className="active">Ativos <strong>{alerts.filter(item => item.state === 'Active').length}</strong></button><button>Reconhecidos <strong>{alerts.filter(item => item.state === 'Acknowledged').length}</strong></button><button>Resolvidos <strong>{alerts.filter(item => item.state === 'Resolved').length}</strong></button><button>Silenciados <strong>{alerts.filter(item => item.state === 'Silenced').length}</strong></button></section><article className="panel"><AlertList alerts={alerts} acknowledge={id => void acknowledge(id)} busyId={busyId} /></article></div>
}

function SettingsPage() {
  const items = [{ icon: Clock3, title: 'Intervalos e retenção', text: '30 dias de histórico · agregação após 7 dias' }, { icon: UserRound, title: 'Usuários e perfis', text: 'Administrator, Operator e Viewer' }, { icon: Bell, title: 'Canais de notificação', text: 'Dashboard · Webhook · Teams · Slack · Discord' }, { icon: ShieldCheck, title: 'Segurança', text: 'Bind local · HTTPS recomendado para acesso em rede' }]
  return <div className="page-body"><div className="settings-grid">{items.map(({ icon: Icon, title, text }) => <article className="panel setting-card" key={title}><span><Icon size={20} /></span><div><h3>{title}</h3><p>{text}</p></div><ChevronDown size={17} /></article>)}</div><div className="read-only-notice"><ShieldCheck size={22} /><div><strong>Princípio de menor privilégio</strong><p>O Pulse não inicia, para ou reinicia serviços e não escreve nas pastas monitoradas.</p></div></div></div>
}

function AuditPage() {
  return <div className="page-body"><article className="panel"><PanelHeader title="Eventos administrativos" subtitle="Horários em UTC com origem sanitizada" /><div className="audit-line"><span><LockKeyhole size={16} /></span><div><strong>LoginSucceeded</strong><p>Administrador da demonstração iniciou uma sessão local.</p><small>Agora · 127.0.0.1</small></div></div><div className="audit-line"><span><ShieldCheck size={16} /></span><div><strong>InitialAdministratorCreated</strong><p>Conta administrativa inicial criada com hash PBKDF2-SHA256.</p><small>Na primeira inicialização</small></div></div></article></div>
}

function DiagnosticsPage({ demo }: { demo: boolean }) {
  return <div className="page-body"><div className="diagnostic-grid"><Diagnostic title="Serviço web" status="Healthy" detail="Respondendo em 127.0.0.1:5058" /><Diagnostic title="Banco local" status="Healthy" detail="SQLite disponível e migration aplicada" /><Diagnostic title="Atualização em tempo real" status="Healthy" detail="Hub SignalR ativo" /><Diagnostic title="Coletores reais" status={demo ? "Unknown" : "Healthy"} detail={demo ? "Desativados no modo demonstração" : "Agendador somente leitura ativo"} /></div>{demo && <div className="demo-notice"><HeartPulse size={22} /><div><strong>Modo demonstração ativo</strong><p>Todos os alvos e eventos exibidos são sintéticos e claramente marcados.</p></div></div>}</div>
}

function Diagnostic({ title, status, detail }: { title: string; status: HealthStatus; detail: string }) {
  return <article className="panel diagnostic-card"><div><span className={`status-dot ${status.toLowerCase()}`} /><strong>{title}</strong></div><StatusBadge status={status} /><p>{detail}</p></article>
}

function StatusBadge({ status, label }: { status: HealthStatus; label?: string }) {
  const labels: Record<HealthStatus, string> = { Healthy: 'Saudável', Warning: 'Atenção', Critical: 'Crítico', Unknown: 'Desconhecido', Maintenance: 'Manutenção' }
  return <span className={`status-badge ${status.toLowerCase()}`}><i />{label ?? labels[status]}</span>
}

function Splash() { return <div className="splash"><div className="brand-mark"><HeartPulse size={28} /></div><span>Iniciando o Pulse…</span></div> }
function DashboardSkeleton() { return <div className="page-body skeleton"><div /><div /><div /><div className="wide" /></div> }

function formatRelative(value?: string) {
  if (!value) return 'agora'
  const seconds = Math.max(0, (Date.now() - new Date(value).getTime()) / 1000)
  if (seconds < 60) return 'agora'
  if (seconds < 3600) return `há ${Math.floor(seconds / 60)} min`
  if (seconds < 86400) return `há ${Math.floor(seconds / 3600)} h`
  return `há ${Math.floor(seconds / 86400)} d`
}

function typeLabel(type: string) {
  const labels: Record<string, string> = { Rest: 'REST / WebApp', Worker: 'Worker', Job: 'Job / integração', HttpEndpoint: 'Endpoint HTTPS', Broker: 'Broker', Generic: 'Fonte de log' }
  return labels[type] ?? type
}

function environmentLabel(environment?: EnvironmentKind) {
  return ({ Production: 'Produção', Homologation: 'Homologação', Development: 'Desenvolvimento', Custom: 'Personalizado' } as const)[environment ?? 'Custom']
}

function stateLabel(state: AlertSnapshot['state']) {
  return ({ Active: 'Ativo', Acknowledged: 'Reconhecido', Resolved: 'Resolvido', Silenced: 'Silenciado' } as const)[state]
}

function worstStatus(components: ComponentSnapshot[]): HealthStatus {
  const order: HealthStatus[] = ['Critical', 'Warning', 'Unknown', 'Maintenance', 'Healthy']
  return order.find(status => components.some(item => item.status === status)) ?? 'Unknown'
}
