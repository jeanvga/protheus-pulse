import { FormEvent, useCallback, useEffect, useMemo, useState } from 'react'
import type { CSSProperties } from 'react'
import {
  Activity, AlertTriangle, Archive, Bell, Boxes, BriefcaseBusiness, Check, ChevronDown, CircleHelp,
  Clock3, FileText, FolderSearch, Gauge, HeartPulse, LockKeyhole, LogOut, Menu, Moon,
  Pencil, Play, Plus, RefreshCw, RotateCw, Search, Server, Settings, ShieldCheck, Square, Sun,
  TerminalSquare, Trash2, UserRound, Wrench, X,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import {
  acknowledgeAlert, collectNow, connectLiveUpdates, createInstallation, deleteInstallation, discoverPaths,
  discoverServices, enterMaintenance, executeServiceAction, exitMaintenance, getAuthStatus, getDashboard,
  getInstallationConfiguration, getLogEvents, getMaintenanceStatus, login, session, setup,
  updateInstallation,
} from './api'
import type {
  AlertSnapshot, AuthStatus, AuthToken, ComponentSnapshot, ComponentType, DashboardSummary, EnvironmentKind,
  HealthStatus, HttpCheckConfiguration, LogEventItem, MaintenanceStatus, PathCandidate, SaveInstallationInput,
  ServiceAction, ServiceCandidate, TcpCheckConfiguration,
} from './types'

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
  const [installationEditorId, setInstallationEditorId] = useState<string | null | undefined>(undefined)
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

  const onAuthenticated = (token: AuthToken) => {
    session.token = token.accessToken
    session.role = token.role
    setAuthenticated(true)
  }

  const logout = () => {
    session.token = null
    session.role = null
    setAuthenticated(false)
    setSummary(null)
  }

  const installationCreated = async () => {
    setInstallationEditorId(undefined)
    setPage('installations')
    await loadSummary()
  }

  if (loading) return <Splash />
  if (!authenticated) return <LoginScreen status={authStatus} onAuthenticated={onAuthenticated} error={error} />

  const title = pageTitles[page]
  return (
    <div className="app-shell">
      <Sidebar active={page} setPage={setPage} open={mobileMenu} close={() => setMobileMenu(false)} logout={logout} alertCount={summary?.totals.activeAlerts ?? 0} />
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
            <button className="icon-button notification-button" aria-label="Notificações" onClick={() => setPage('alerts')}><Bell size={18} /><i>{summary?.totals.activeAlerts ?? 0}</i></button>
            <div className="user-chip"><span>AD</span><div><strong>Administrador</strong><small>Operação local</small></div><ChevronDown size={15} /></div>
          </div>
        </header>

        {error && <div className="error-banner"><AlertTriangle size={18} /><span>{error}</span><button onClick={() => void loadSummary()}><RefreshCw size={15} /> Tentar novamente</button></div>}
        {!summary ? <DashboardSkeleton /> : <PageContent page={page} summary={summary} refresh={loadSummary} goTo={setPage} addInstallation={() => setInstallationEditorId(null)} editInstallation={setInstallationEditorId} />}
        <footer className="app-footer"><span><span className="live-dot" /> Atualização em tempo real</span><span>Protheus Pulse 1.1.0 · produto independente</span></footer>
      </main>
      {installationEditorId !== undefined && <InstallationDialog installationId={installationEditorId} close={() => setInstallationEditorId(undefined)} onSaved={installationCreated} />}
    </div>
  )
}

function Sidebar({ active, setPage, open, close, logout, alertCount }: { active: Page; setPage: (page: Page) => void; open: boolean; close: () => void; logout: () => void; alertCount: number }) {
  const choose = (page: Page) => { setPage(page); close() }
  return <>
    {open && <button className="sidebar-backdrop" onClick={close} aria-label="Fechar menu" />}
    <aside className={`sidebar ${open ? 'open' : ''}`}>
      <div className="brand"><div className="brand-mark"><HeartPulse size={24} /></div><div><strong>Protheus</strong><span>Pulse</span></div><button className="mobile-close" onClick={close}><X size={20} /></button></div>
      <nav aria-label="Navegação principal">
        <span className="nav-section-label">Monitoramento</span>
        {navItems.map(item => <NavItem key={item.id} {...item} active={active === item.id} badge={item.id === 'alerts' ? alertCount : undefined} onClick={() => choose(item.id)} />)}
        <span className="nav-section-label secondary">Sistema</span>
        {secondaryNav.map(item => <NavItem key={item.id} {...item} active={active === item.id} onClick={() => choose(item.id)} />)}
      </nav>
      <div className="sidebar-callout"><ShieldCheck size={19} /><div><strong>Operação controlada</strong><span>Coleta somente leitura; ações de serviço são restritas a administradores e auditadas.</span></div></div>
      <button className="logout-button" onClick={logout}><LogOut size={17} /> Encerrar sessão</button>
    </aside>
  </>
}

function NavItem({ label, icon: Icon, active, badge, onClick }: { label: string; icon: LucideIcon; active: boolean; badge?: number; onClick: () => void }) {
  return <button className={`nav-item ${active ? 'active' : ''}`} onClick={onClick}><Icon size={18} /><span>{label}</span>{badge != null && badge > 0 && <i>{badge}</i>}</button>
}

function LoginScreen({ status, onAuthenticated, error: initialError }: { status: AuthStatus | null; onAuthenticated: (token: AuthToken) => void; error: string | null }) {
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
      onAuthenticated(token)
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

function PageContent({ page, summary, refresh, goTo, addInstallation, editInstallation }: { page: Page; summary: DashboardSummary; refresh: () => Promise<void>; goTo: (page: Page) => void; addInstallation: () => void; editInstallation: (id: string) => void }) {
  switch (page) {
    case 'overview': return <Overview summary={summary} refresh={refresh} goTo={goTo} addInstallation={addInstallation} />
    case 'installations': return <Installations summary={summary} refresh={refresh} addInstallation={addInstallation} editInstallation={editInstallation} />
    case 'logs': return <LogsPage />
    case 'jobs': return <JobsPage components={summary.components} />
    case 'alerts': return <AlertsPage alerts={summary.alerts} refresh={refresh} />
    case 'settings': return <SettingsPage />
    case 'audit': return <AuditPage />
    case 'diagnostics': return <DiagnosticsPage demo={summary.demoMode} />
  }
}

function Overview({ summary, refresh, goTo, addInstallation }: { summary: DashboardSummary; refresh: () => Promise<void>; goTo: (page: Page) => void; addInstallation: () => void }) {
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

function MetricCard({ icon: Icon, label, value, detail, tone }: { icon: LucideIcon; label: string; value: string | number; detail: string; tone: string }) {
  return <article className={`metric-card ${tone}`}><div className="metric-icon"><Icon size={20} /></div><div><span>{label}</span><strong>{value}</strong><small>{detail}</small></div></article>
}

function PanelHeader({ title, subtitle, action, onAction }: { title: string; subtitle: string; action?: string; onAction?: () => void }) {
  return <header className="panel-header"><div><h3>{title}</h3><p>{subtitle}</p></div>{action && onAction && <button onClick={onAction}>{action} <ChevronDown size={14} /></button>}</header>
}

function AvailabilityChart({ values }: { values: DashboardSummary['availability'] }) {
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

function StatusLegend({ label, value, status }: { label: string; value: number; status: HealthStatus }) {
  return <div><span><i className={`status-dot ${status.toLowerCase()}`} />{label}</span><strong>{value}</strong></div>
}

function ComponentTable({ components }: { components: ComponentSnapshot[] }) {
  return <div className="table-wrap"><table><thead><tr><th>Componente</th><th>Instalação</th><th>Estado</th><th>Evidência atual</th><th>Métrica</th></tr></thead><tbody>{components.map(item => <tr key={item.id}><td><div className="component-name"><span><TerminalSquare size={17} /></span><div><strong>{item.name}</strong><small>{typeLabel(item.type)}</small></div></div></td><td><div className="installation-name">{item.installationName}<small>{item.isDemo ? 'Dado demonstrativo' : 'Monitoramento real'}</small></div></td><td><StatusBadge status={item.status} /></td><td><div className="evidence">{item.summary}<small>desde {formatRelative(item.lastStateChangeAt)}</small></div></td><td><div className="metric-value">{item.metricValue ?? '—'} <small>{item.metricUnit}</small><span>{item.metricLabel}</span></div></td></tr>)}</tbody></table>{components.length === 0 && <div className="empty-state"><Check size={22} /> Nenhum componente pede atenção agora.</div>}</div>
}

function AlertList({ alerts, acknowledge, busyId }: { alerts: AlertSnapshot[]; acknowledge?: (id: string) => void; busyId?: string | null }) {
  return <div className="alert-list">{alerts.map(alert => <div className="alert-row" key={alert.id}><div className={`alert-symbol ${alert.severity.toLowerCase()}`}>{alert.state === 'Resolved' ? <Check size={17} /> : <AlertTriangle size={17} />}</div><div className="alert-main"><div><strong>{alert.ruleName}</strong><StatusBadge status={alert.state === 'Resolved' ? 'Healthy' : alert.severity === 'Critical' ? 'Critical' : 'Warning'} label={stateLabel(alert.state)} /></div><span>{alert.componentName} · {alert.installationName}</span><p>{alert.evidence}</p></div><div className="alert-time"><strong>{formatRelative(alert.startedAt)}</strong><span>#{alert.correlationId.slice(0, 8)}</span>{alert.state === 'Active' && acknowledge && <button className="secondary-button alert-action" disabled={busyId === alert.id} onClick={() => acknowledge(alert.id)}>{busyId === alert.id ? 'Salvando…' : 'Reconhecer'}</button>}</div></div>)}</div>
}

function Installations({ summary, refresh, addInstallation, editInstallation }: { summary: DashboardSummary; refresh: () => Promise<void>; addInstallation: () => void; editInstallation: (id: string) => void }) {
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [maintenance, setMaintenance] = useState<MaintenanceStatus | null>(null)
  const [serviceBusyId, setServiceBusyId] = useState<string | null>(null)
  const isAdministrator = session.role === 'Administrator'
  const groups = useMemo(() => Object.values(summary.components.reduce<Record<string, { name: string; components: ComponentSnapshot[] }>>((result, item) => {
    ;(result[item.installationId] ??= { name: item.installationName, components: [] }).components.push(item)
    return result
  }, {})), [summary.components])

  const loadMaintenance = useCallback(async () => {
    try { setMaintenance(await getMaintenanceStatus()) } catch { setMaintenance(null) }
  }, [])
  useEffect(() => { void loadMaintenance() }, [loadMaintenance])

  const toggleMaintenance = async () => {
    const entering = !(maintenance?.active ?? false)
    const confirmed = window.confirm(entering
      ? 'Entrar em modo manutenção? Todos os serviços Windows monitorados serão PARADOS e os alertas ficarão suspensos.'
      : 'Encerrar o modo manutenção? Os serviços monitorados serão iniciados novamente.')
    if (!confirmed) return
    setBusy(true); setError(null); setMessage(null)
    try {
      const result = entering ? await enterMaintenance() : await exitMaintenance()
      const failures = result.services.filter(item => !item.success)
      const summaryText = entering
        ? `Modo manutenção ativado. ${result.services.length - failures.length} serviço(s) parado(s)`
        : `Modo manutenção encerrado. ${result.services.length - failures.length} serviço(s) iniciado(s)`
      if (failures.length > 0) setError(`${summaryText}; falhas: ${failures.map(item => `${item.serviceName} (${item.message})`).join('; ')}`)
      else setMessage(`${summaryText}.`)
      await loadMaintenance()
      await refresh()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível alterar o modo manutenção.')
    } finally { setBusy(false) }
  }

  const runServiceAction = async (component: ComponentSnapshot, action: ServiceAction) => {
    const verb = action === 'start' ? 'Iniciar' : action === 'stop' ? 'Parar' : 'Reiniciar'
    if (action !== 'start' && !window.confirm(`${verb} o serviço “${component.windowsServiceName}” de ${component.name}?`)) return
    setServiceBusyId(component.id); setError(null); setMessage(null)
    try {
      const outcome = await executeServiceAction(component.id, action)
      const failures = outcome.results.filter(item => !item.success)
      if (failures.length > 0) setError(`Falha em ${component.name}: ${failures.map(item => `${item.serviceName}: ${item.message}`).join('; ')}`)
      else setMessage(`${verb} concluído em ${component.name}: ${outcome.results.map(item => `${item.serviceName} → ${item.status}`).join(', ')}.`)
      await refresh()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível executar a ação de serviço.')
    } finally { setServiceBusyId(null) }
  }

  const runCollection = async () => {
    setBusy(true); setError(null); setMessage(null)
    try {
      const result = await collectNow()
      setMessage(`Coleta concluída em ${result.processedComponents} componente(s).`)
      await refresh()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível executar a coleta.')
    } finally { setBusy(false) }
  }

  const remove = async (id: string, name: string) => {
    if (!window.confirm(`Remover a instalação “${name}” e seu histórico?`)) return
    setBusy(true); setError(null); setMessage(null)
    try {
      await deleteInstallation(id)
      setMessage('Instalação removida.')
      await refresh()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível remover a instalação.')
    } finally { setBusy(false) }
  }

  return <div className="page-body">
    <section className="intro-row"><div><h2>Ambientes cadastrados</h2><p>Configure serviços, arquivos, portas e URLs sem sair do painel.</p></div><div className="intro-actions">{isAdministrator && <button className={maintenance?.active ? 'primary-button' : 'danger-button'} disabled={busy || summary.demoMode} onClick={() => void toggleMaintenance()}><Wrench size={16} /> {maintenance?.active ? 'Encerrar manutenção' : 'Modo manutenção'}</button>}<button className="secondary-button" disabled={busy || summary.demoMode} onClick={() => void runCollection()}><Play size={16} /> {busy ? 'Executando…' : 'Coletar agora'}</button><button className="primary-button" onClick={addInstallation}><Plus size={16} /> Adicionar instalação</button></div></section>
    {maintenance?.active && <div className="maintenance-banner"><Wrench size={16} /> Modo manutenção ativo{maintenance.endsAt ? ` até ${new Date(maintenance.endsAt).toLocaleString('pt-BR')}` : ''}: serviços monitorados parados e alertas suspensos.</div>}
    {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
    {message && <div className="success-banner"><Check size={16} /> {message}</div>}
    <div className="installation-grid">{groups.map(({ name, components }) => {
      const installationId = components[0]?.installationId
      const isDemo = components.every(item => item.isDemo)
      return <article className="panel installation-card" key={installationId}>
        <header><div><span className="environment-tag">{environmentLabel(components[0]?.installationEnvironment)}</span><h3>{name}</h3></div><StatusBadge status={worstStatus(components)} /></header>
        <div className="installation-stat"><span>{components.length} componentes</span><span>{components.filter(item => item.status === 'Healthy').length} saudáveis</span></div>
        {components.map(component => <div className="mini-component" key={component.id}><div><i className={`status-dot ${component.status.toLowerCase()}`} /><span>{component.name}</span>{isAdministrator && !component.isDemo && component.windowsServiceName && <span className="mini-component-actions"><button className="row-action" title={`Iniciar ${component.windowsServiceName}`} aria-label={`Iniciar serviço de ${component.name}`} disabled={busy || serviceBusyId === component.id} onClick={() => void runServiceAction(component, 'start')}>{serviceBusyId === component.id ? <RefreshCw className="spin" size={14} /> : <Play size={14} />}</button><button className="row-action" title={`Reiniciar ${component.windowsServiceName}`} aria-label={`Reiniciar serviço de ${component.name}`} disabled={busy || serviceBusyId === component.id} onClick={() => void runServiceAction(component, 'restart')}><RotateCw size={14} /></button><button className="row-action" title={`Parar ${component.windowsServiceName}`} aria-label={`Parar serviço de ${component.name}`} disabled={busy || serviceBusyId === component.id} onClick={() => void runServiceAction(component, 'stop')}><Square size={14} /></button></span>}</div><small>{component.summary}</small></div>)}
        {!isDemo && installationId && <footer className="installation-actions"><button className="secondary-button" onClick={() => editInstallation(installationId)}><Pencil size={15} /> Configurar</button><button className="danger-button" disabled={busy} onClick={() => void remove(installationId, name)}><Trash2 size={15} /> Remover</button></footer>}
      </article>
    })}</div>
  </div>
}

interface TcpCheckDraft extends TcpCheckConfiguration { key: number }
interface HttpCheckDraft extends HttpCheckConfiguration { key: number }
interface ComponentDraft {
  key: number
  id?: string
  name: string
  type: ComponentType
  isRequired: boolean
  windowsServiceName: string
  executablePath: string
  iniPath: string
  logPaths: string[]
  tcpChecks: TcpCheckDraft[]
  httpChecks: HttpCheckDraft[]
}

let draftKey = 0
const nextDraftKey = () => ++draftKey
const emptyComponent = (): ComponentDraft => ({
  key: nextDraftKey(), name: '', type: 'AppServer', isRequired: true, windowsServiceName: '',
  executablePath: '', iniPath: '', logPaths: [], tcpChecks: [], httpChecks: [],
})

function InstallationDialog({ installationId, close, onSaved }: { installationId: string | null; close: () => void; onSaved: () => Promise<void> }) {
  const [name, setName] = useState('')
  const [environment, setEnvironment] = useState<EnvironmentKind>('Production')
  const [customEnvironmentName, setCustomEnvironmentName] = useState('')
  const [tags, setTags] = useState('')
  const [components, setComponents] = useState<ComponentDraft[]>([emptyComponent()])
  const [loading, setLoading] = useState(Boolean(installationId))
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!installationId) return
    setLoading(true)
    getInstallationConfiguration(installationId).then(configuration => {
      setName(configuration.name)
      setEnvironment(configuration.environment)
      setCustomEnvironmentName(configuration.customEnvironmentName ?? '')
      setTags(configuration.tags.join(', '))
      setComponents(configuration.components.map(component => ({
        ...component,
        key: nextDraftKey(),
        windowsServiceName: component.windowsServiceName ?? '',
        executablePath: component.executablePath ?? '',
        iniPath: component.iniPath ?? '',
        tcpChecks: component.tcpChecks.map(check => ({ ...check, key: nextDraftKey() })),
        httpChecks: component.httpChecks.map(check => ({ ...check, key: nextDraftKey() })),
      })))
    }).catch(reason => setError(reason instanceof Error ? reason.message : 'Não foi possível carregar a configuração.'))
      .finally(() => setLoading(false))
  }, [installationId])

  const updateComponent = (key: number, update: Partial<Omit<ComponentDraft, 'key'>>) => setComponents(current => current.map(item => item.key === key ? { ...item, ...update } : item))
  const addComponent = () => setComponents(current => [...current, emptyComponent()])
  const removeComponent = (key: number) => setComponents(current => current.filter(item => item.key !== key))

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setError(null)
    const missingTarget = components.find(component => !component.windowsServiceName.trim()
      && !component.executablePath.trim() && !component.iniPath.trim()
      && component.logPaths.every(path => !path.trim())
      && component.tcpChecks.length === 0 && component.httpChecks.length === 0)
    if (missingTarget) {
      setError(`Configure ao menos um alvo no componente “${missingTarget.name || 'sem nome'}”.`)
      return
    }

    const input: SaveInstallationInput = {
      name,
      environment,
      customEnvironmentName: environment === 'Custom' ? customEnvironmentName : undefined,
      tags: tags.split(',').map(item => item.trim()).filter(Boolean),
      components: components.map(component => ({
        id: component.id,
        name: component.name,
        type: component.type,
        isRequired: component.isRequired,
        windowsServiceName: component.windowsServiceName.trim() || undefined,
        executablePath: component.executablePath.trim() || undefined,
        iniPath: component.iniPath.trim() || undefined,
        logPaths: component.logPaths.map(path => path.trim()).filter(Boolean),
        tcpChecks: component.tcpChecks.map(({ key: _, ...check }) => check),
        httpChecks: component.httpChecks.map(({ key: _, ...check }) => ({ ...check, bodyPattern: check.bodyPattern?.trim() || undefined })),
      })),
    }
    setBusy(true)
    try {
      if (installationId) await updateInstallation(installationId, input)
      else await createInstallation(input)
      await onSaved()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível salvar a instalação.')
    } finally { setBusy(false) }
  }

  return <div className="modal-backdrop">
    <section className="modal-card configuration-modal" role="dialog" aria-modal="true" aria-labelledby="installation-dialog-title">
      <header className="modal-header"><div><span>Configuração local completa</span><h2 id="installation-dialog-title">{installationId ? 'Configurar instalação' : 'Adicionar instalação'}</h2><p>Defina os alvos reais que serão consultados em modo somente leitura.</p></div><button className="icon-button" onClick={close} disabled={busy} aria-label="Fechar cadastro"><X size={18} /></button></header>
      {loading ? <div className="modal-loading"><RefreshCw className="spin" size={20} /> Carregando configuração…</div> : <form onSubmit={submit}>
        {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
        <div className="form-grid">
          <label>Nome da instalação<input aria-label="Nome da instalação" value={name} onChange={event => setName(event.target.value)} maxLength={160} placeholder="Ex.: ERP Produção" required /></label>
          <label>Ambiente<select aria-label="Ambiente" value={environment} onChange={event => setEnvironment(event.target.value as EnvironmentKind)}><option value="Production">Produção</option><option value="Homologation">Homologação</option><option value="Development">Desenvolvimento</option><option value="Custom">Personalizado</option></select></label>
          {environment === 'Custom' && <label>Nome do ambiente<input aria-label="Nome do ambiente personalizado" value={customEnvironmentName} onChange={event => setCustomEnvironmentName(event.target.value)} maxLength={80} required /></label>}
          <label className={environment === 'Custom' ? '' : 'wide-field'}>Tags opcionais<input aria-label="Tags opcionais" value={tags} onChange={event => setTags(event.target.value)} placeholder="matriz, servidor-a" /></label>
        </div>
        <div className="component-editor">
          <div className="component-editor-heading"><div><h3>Componentes e alvos</h3><p>Serviço, executável, INI, logs, TCP e HTTP podem ser combinados.</p></div><button type="button" className="secondary-button" onClick={addComponent}><Plus size={15} /> Adicionar componente</button></div>
          {components.map((component, index) => <ComponentConfigurationEditor key={component.key} component={component} index={index} update={update => updateComponent(component.key, update)} remove={() => removeComponent(component.key)} canRemove={components.length > 1} />)}
        </div>
        <div className="modal-safety"><ShieldCheck size={18} /><span>A descoberta apenas lista candidatos e a coleta é somente leitura. Ações de iniciar ou parar serviços são explícitas, auditadas e restritas a administradores.</span></div>
        <footer className="modal-actions"><button type="button" className="secondary-button" onClick={close} disabled={busy}>Cancelar</button><button className="primary-button" disabled={busy}>{busy ? <RefreshCw className="spin" size={16} /> : <Check size={16} />}{busy ? 'Salvando…' : 'Salvar e monitorar'}</button></footer>
      </form>}
    </section>
  </div>
}

function ComponentConfigurationEditor({ component, index, update, remove, canRemove }: { component: ComponentDraft; index: number; update: (update: Partial<Omit<ComponentDraft, 'key'>>) => void; remove: () => void; canRemove: boolean }) {
  const [serviceQuery, setServiceQuery] = useState(component.windowsServiceName || component.name)
  const [serviceCandidates, setServiceCandidates] = useState<ServiceCandidate[]>([])
  const [pathRoot, setPathRoot] = useState('')
  const [fileNames, setFileNames] = useState(defaultFileNames(component.type))
  const [pathCandidates, setPathCandidates] = useState<PathCandidate[]>([])
  const [discoveryBusy, setDiscoveryBusy] = useState(false)
  const [discoveryError, setDiscoveryError] = useState<string | null>(null)

  const findServices = async () => {
    setDiscoveryError(null)
    if (serviceQuery.trim().length < 2) { setDiscoveryError('Informe ao menos dois caracteres para buscar serviços.'); return }
    setDiscoveryBusy(true)
    try { setServiceCandidates((await discoverServices(serviceQuery.trim())).candidates) }
    catch (reason) { setDiscoveryError(reason instanceof Error ? reason.message : 'Falha na descoberta de serviços.') }
    finally { setDiscoveryBusy(false) }
  }

  const findPaths = async () => {
    setDiscoveryError(null)
    const names = fileNames.split(',').map(item => item.trim()).filter(Boolean)
    if (!pathRoot.trim() || names.length === 0) { setDiscoveryError('Informe uma pasta inicial e ao menos um nome de arquivo.'); return }
    setDiscoveryBusy(true)
    try { setPathCandidates((await discoverPaths(pathRoot.trim(), names)).candidates) }
    catch (reason) { setDiscoveryError(reason instanceof Error ? reason.message : 'Falha na descoberta de caminhos.') }
    finally { setDiscoveryBusy(false) }
  }

  const addLog = (path: string) => update({ logPaths: [...new Set([...component.logPaths, path])] })
  const addTcp = () => update({ tcpChecks: [...component.tcpChecks, { key: nextDraftKey(), host: '127.0.0.1', port: 0, timeoutMs: 3000, isRequired: true }] })
  const addHttp = () => update({ httpChecks: [...component.httpChecks, { key: nextDraftKey(), url: '', method: 'GET', expectedStatusMin: 200, expectedStatusMax: 399, timeoutMs: 5000, validateTls: true, certificateWarningDays: 30, isRequired: true }] })

  return <article className="component-config-card">
    <header><span>{index + 1}</span><div><strong>{component.name || 'Novo componente'}</strong><small>Configure um ou mais alvos de leitura</small></div><button type="button" className="row-action remove-component" onClick={remove} disabled={!canRemove} aria-label={`Remover componente ${index + 1}`}><X size={17} /></button></header>
    <div className="target-form-grid basic-target-grid">
      <label>Nome<input aria-label={`Nome do componente ${index + 1}`} value={component.name} onChange={event => update({ name: event.target.value })} maxLength={160} placeholder="Ex.: AppServer REST" required /></label>
      <label>Tipo<select aria-label={`Tipo do componente ${index + 1}`} value={component.type} onChange={event => { const type = event.target.value as ComponentType; update({ type }); setFileNames(defaultFileNames(type)) }}>{componentTypeOptions.map(option => <option value={option.value} key={option.value}>{option.label}</option>)}</select></label>
      <label className="checkbox-label"><input type="checkbox" checked={component.isRequired} onChange={event => update({ isRequired: event.target.checked })} /> Obrigatório</label>
    </div>

    <section className="target-section"><div className="target-section-heading"><div><h4>Serviço Windows</h4><p>Use o nome interno do serviço, não apenas o nome exibido.</p></div></div>
      <div className="discovery-row"><input aria-label={`Buscar serviço do componente ${index + 1}`} value={serviceQuery} onChange={event => setServiceQuery(event.target.value)} placeholder="Ex.: AppServer" /><button type="button" className="secondary-button" disabled={discoveryBusy} onClick={() => void findServices()}><Search size={14} /> Buscar no servidor</button></div>
      {serviceCandidates.length > 0 && <div className="candidate-list">{serviceCandidates.slice(0, 20).map(candidate => <button type="button" key={candidate.serviceName} onClick={() => update({ windowsServiceName: candidate.serviceName })}><span><strong>{candidate.displayName}</strong><small>{candidate.serviceName} · {candidate.status}</small></span><Check size={14} /></button>)}</div>}
      <label className="target-field">Serviço selecionado<input aria-label={`Nome do serviço Windows ${index + 1}`} value={component.windowsServiceName} onChange={event => update({ windowsServiceName: event.target.value })} placeholder="Opcional" /></label>
    </section>

    <section className="target-section"><div className="target-section-heading"><div><h4>Arquivos e logs</h4><p>Informe caminhos absolutos locais ou UNC. Nenhuma unidade mapeada é usada.</p></div><FolderSearch size={17} /></div>
      <div className="path-discovery-grid"><label>Pasta inicial<input aria-label={`Pasta para descoberta ${index + 1}`} value={pathRoot} onChange={event => setPathRoot(event.target.value)} placeholder="D:\TOTVS\Protheus" /></label><label>Nomes exatos<input aria-label={`Arquivos para descoberta ${index + 1}`} value={fileNames} onChange={event => setFileNames(event.target.value)} /></label><button type="button" className="secondary-button" disabled={discoveryBusy} onClick={() => void findPaths()}><FolderSearch size={14} /> Localizar</button></div>
      {pathCandidates.length > 0 && <div className="path-candidates">{pathCandidates.slice(0, 20).map(candidate => <div key={candidate.path}><span title={candidate.path}>{candidate.path}</span><div>{candidate.fileName.toLowerCase().endsWith('.exe') && <button type="button" onClick={() => update({ executablePath: candidate.path })}>Executável</button>}{candidate.fileName.toLowerCase().endsWith('.ini') && <button type="button" onClick={() => update({ iniPath: candidate.path })}>INI</button>}<button type="button" onClick={() => addLog(candidate.path)}>Log</button></div></div>)}</div>}
      <div className="target-form-grid"><label>Executável<input aria-label={`Caminho do executável ${index + 1}`} value={component.executablePath} onChange={event => update({ executablePath: event.target.value })} placeholder="Opcional" /></label><label>Arquivo INI<input aria-label={`Caminho do INI ${index + 1}`} value={component.iniPath} onChange={event => update({ iniPath: event.target.value })} placeholder="Opcional" /></label><label className="wide-field">Logs, um caminho por linha<textarea aria-label={`Caminhos de log ${index + 1}`} value={component.logPaths.join('\n')} onChange={event => update({ logPaths: event.target.value.split(/\r?\n/) })} placeholder={'D:\\TOTVS\\Protheus\\logs\\console.log'} /></label></div>
    </section>

    <section className="target-section"><div className="target-section-heading"><div><h4>Portas TCP</h4><p>Verifica conectividade sem enviar comandos ao Protheus.</p></div><button type="button" className="secondary-button" onClick={addTcp}><Plus size={14} /> Porta</button></div>
      {component.tcpChecks.map((check, checkIndex) => <div className="check-row tcp-row" key={check.key}><label>Host<input aria-label={`Host TCP ${index + 1}.${checkIndex + 1}`} value={check.host} onChange={event => update({ tcpChecks: component.tcpChecks.map(item => item.key === check.key ? { ...item, host: event.target.value } : item) })} required /></label><label>Porta<input aria-label={`Porta TCP ${index + 1}.${checkIndex + 1}`} type="number" min="1" max="65535" value={check.port || ''} onChange={event => update({ tcpChecks: component.tcpChecks.map(item => item.key === check.key ? { ...item, port: Number(event.target.value) } : item) })} required /></label><label>Timeout (ms)<input type="number" min="250" max="30000" value={check.timeoutMs} onChange={event => update({ tcpChecks: component.tcpChecks.map(item => item.key === check.key ? { ...item, timeoutMs: Number(event.target.value) } : item) })} /></label><button type="button" className="row-action remove-component" aria-label={`Remover porta TCP ${checkIndex + 1}`} onClick={() => update({ tcpChecks: component.tcpChecks.filter(item => item.key !== check.key) })}><X size={16} /></button></div>)}
    </section>

    <section className="target-section"><div className="target-section-heading"><div><h4>Endpoints HTTP/HTTPS</h4><p>Somente GET ou HEAD, sem redirecionamentos.</p></div><button type="button" className="secondary-button" onClick={addHttp}><Plus size={14} /> Endpoint</button></div>
      {component.httpChecks.map((check, checkIndex) => <div className="http-check" key={check.key}><div className="check-row http-row"><label>URL<input aria-label={`URL HTTP ${index + 1}.${checkIndex + 1}`} type="url" value={check.url} onChange={event => update({ httpChecks: component.httpChecks.map(item => item.key === check.key ? { ...item, url: event.target.value } : item) })} placeholder="http://127.0.0.1:porta/rota" required /></label><label>Método<select value={check.method} onChange={event => update({ httpChecks: component.httpChecks.map(item => item.key === check.key ? { ...item, method: event.target.value as 'GET' | 'HEAD' } : item) })}><option>GET</option><option>HEAD</option></select></label><label>Status mínimo<input type="number" min="100" max="599" value={check.expectedStatusMin} onChange={event => update({ httpChecks: component.httpChecks.map(item => item.key === check.key ? { ...item, expectedStatusMin: Number(event.target.value) } : item) })} /></label><label>Status máximo<input type="number" min="100" max="599" value={check.expectedStatusMax} onChange={event => update({ httpChecks: component.httpChecks.map(item => item.key === check.key ? { ...item, expectedStatusMax: Number(event.target.value) } : item) })} /></label><button type="button" className="row-action remove-component" aria-label={`Remover endpoint HTTP ${checkIndex + 1}`} onClick={() => update({ httpChecks: component.httpChecks.filter(item => item.key !== check.key) })}><X size={16} /></button></div><div className="http-options"><label>Texto esperado<input value={check.bodyPattern ?? ''} onChange={event => update({ httpChecks: component.httpChecks.map(item => item.key === check.key ? { ...item, bodyPattern: event.target.value } : item) })} placeholder="Opcional" /></label><label>Timeout (ms)<input type="number" min="250" max="30000" value={check.timeoutMs} onChange={event => update({ httpChecks: component.httpChecks.map(item => item.key === check.key ? { ...item, timeoutMs: Number(event.target.value) } : item) })} /></label><label className="checkbox-label"><input type="checkbox" checked={check.validateTls} onChange={event => update({ httpChecks: component.httpChecks.map(item => item.key === check.key ? { ...item, validateTls: event.target.checked } : item) })} /> Validar TLS</label><label className="checkbox-label"><input type="checkbox" checked={check.isRequired} onChange={event => update({ httpChecks: component.httpChecks.map(item => item.key === check.key ? { ...item, isRequired: event.target.checked } : item) })} /> Obrigatório</label></div></div>)}
    </section>
    {discoveryError && <div className="inline-error"><AlertTriangle size={14} /> {discoveryError}</div>}
  </article>
}

function defaultFileNames(type: ComponentType) {
  if (type === 'DbAccess') return 'dbaccess.exe, dbaccess.ini, dbaccess.log'
  if (type === 'LicenseServer') return 'licenseserver.exe, appserver.ini, console.log'
  if (type === 'Tss') return 'appserver.exe, appserver.ini, console.log'
  return 'appserver.exe, appserver.ini, console.log'
}

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

function LogsPage() {
  const [events, setEvents] = useState<LogEventItem[]>([])
  const [search, setSearch] = useState('')
  const [level, setLevel] = useState('all')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setEvents(await getLogEvents())
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível carregar os logs.')
    } finally { setLoading(false) }
  }, [])
  useEffect(() => { void load() }, [load])

  const normalizedSearch = search.trim().toLowerCase()
  const filtered = events.filter(item => (level === 'all' || item.level === level)
    && (normalizedSearch === ''
      || item.message.toLowerCase().includes(normalizedSearch)
      || item.componentName.toLowerCase().includes(normalizedSearch)
      || item.installationName.toLowerCase().includes(normalizedSearch)))

  return <div className="page-body">
    <section className="toolbar"><div className="search-box"><Search size={17} /><input aria-label="Pesquisar logs" placeholder="Pesquisar mensagem, componente ou instalação…" value={search} onChange={event => setSearch(event.target.value)} /></div><button className="secondary-button" disabled={loading} onClick={() => void load()}><RefreshCw size={16} /> Atualizar</button></section>
    <section className="summary-chips">{logLevelFilters.map(item => <button key={item.id} className={level === item.id ? 'active' : ''} onClick={() => setLevel(item.id)}>{item.label} <strong>{item.id === 'all' ? events.length : events.filter(event => event.level === item.id).length}</strong></button>)}</section>
    {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
    <article className="panel"><PanelHeader title="Eventos coletados dos logs" subtitle="Mensagens sanitizadas e agrupadas por assinatura · mais recentes primeiro" />
      {loading
        ? <div className="modal-loading"><RefreshCw className="spin" size={20} /> Carregando eventos…</div>
        : filtered.length === 0
          ? <div className="empty-state"><Check size={22} /> Nenhum evento de log para os filtros atuais.</div>
          : filtered.map(item => <div className="log-group" key={item.id}><span className={`log-count ${item.level === 'Information' ? 'muted' : ''}`}>{item.occurrenceCount}×</span><div><strong>{item.message}</strong><p>{item.componentName} · {item.installationName}</p><small>{new Date(item.observedAt).toLocaleString('pt-BR')} · {formatRelative(item.observedAt)}</small></div><StatusBadge status={logLevelStatus(item.level)} label={logLevelLabel(item.level)} /></div>)}
    </article>
  </div>
}

function JobsPage({ components }: { components: ComponentSnapshot[] }) {
  const jobs = components.filter(item => item.type === 'Job')
  return <div className="page-body"><section className="intro-row"><div><h2>Heartbeats de jobs</h2><p>Frequência, duração e atraso sem exigir customização do ERP.</p></div><a className="secondary-button" href="https://github.com/jeanvga/protheus-pulse/blob/main/docs/HEARTBEATS.md" target="_blank" rel="noreferrer"><CircleHelp size={16} /> Como integrar</a></section>{jobs.map(job => <article className="panel job-card" key={job.id}><div className="job-icon"><BriefcaseBusiness size={21} /></div><div><span>{job.installationName}</span><h3>{job.name}</h3><p>{job.summary}</p></div><div className="job-metrics"><div><span>Último sinal</span><strong>há {job.metricValue} min</strong></div><div><span>Tolerância</span><strong>5 min</strong></div><StatusBadge status={job.status} /></div></article>)}</div>
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
  return <div className="page-body"><div className="settings-grid">{items.map(({ icon: Icon, title, text }) => <article className="panel setting-card" key={title}><span><Icon size={20} /></span><div><h3>{title}</h3><p>{text}</p></div></article>)}</div><div className="read-only-notice"><ShieldCheck size={22} /><div><strong>Coleta segura e ações auditadas</strong><p>A coleta é somente leitura e não escreve nas pastas monitoradas. Iniciar, reiniciar ou parar serviços exige perfil Administrator e fica registrado na auditoria.</p></div></div></div>
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
