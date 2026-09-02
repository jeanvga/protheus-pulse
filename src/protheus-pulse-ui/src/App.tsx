import { Fragment, FormEvent, ReactNode, useCallback, useEffect, useMemo, useState } from 'react'
import type { CSSProperties } from 'react'
import {
  Activity, AlertTriangle, Archive, Bell, BellOff, Boxes, BriefcaseBusiness, Check, ChevronDown, CircleHelp,
  Clock3, Cpu, Crown, FileText, FolderSearch, Gauge, HardDrive, HeartPulse, LockKeyhole, LogOut, Mail,
  Menu, MemoryStick, Moon, Pencil, Play, Plus, RefreshCw, RotateCw, Search, Send, Server, Settings,
  ShieldCheck, Siren, Square, Sun, TerminalSquare, Trash2, UserRound, Wrench, X, Zap,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import {
  acknowledgeAlert, collectNow, connectLiveUpdates, createAlertRule, createInstallation, createMaintenanceWindow,
  createNotificationChannel, deleteAlertRule, deleteInstallation, deleteMaintenanceWindow, deleteNotificationChannel, discoverPaths,
  discoverServices, enterMaintenance, executeServiceAction, exitMaintenance, getAlertRules, getAuthStatus, getDashboard,
  createHeartbeatDefinition, deleteHeartbeatDefinition, getAlerts, getServerThresholds, saveServerThresholds, getAuditEvents, getDiagnostics, getEmailSettings, getHeartbeatDefinitions, rotateHeartbeatToken, getInstallationConfiguration, getLogEvents, getMaintenanceStatus, getMaintenanceWindows, getNotificationChannels, browseFolders, getNetworkSettings, getRetentionSettings, getServerResources, getUsers, createUser, updateUser, resetUserPassword, deleteUser, proposeComponent, saveNetworkSettings, saveRetentionSettings,
  login, saveEmailSettings, sendTestEmail, session, setAlertRuleEnabled, setAutoStart, setExclusiveInstallation, setNotificationChannelEnabled, setup,
  updateAlertRule, updateInstallation,
} from './api'
import type {
  AlertOccurrencePage, AlertRule, AlertSeverity, AlertSnapshot, AlertState, AuditEventPage, AuthStatus, AuthToken, DiagnosticsInfo, HeartbeatDefinition, HeartbeatToken, ServerThresholdSettings, ComponentSnapshot, ComponentType, DashboardSummary, EmailSettings,
  BrowseResult, ComponentProposal, ComponentProposalResult, EnvironmentKind, HealthStatus, HttpCheckConfiguration, LogEventItem, LogEventPage, MaintenanceStatus, MaintenanceWindow, NetworkSettings, NotificationChannel, NotificationChannelType, PathCandidate, ProbeType, PulseUser, RetentionSettings,
  SaveInstallationInput, ServerDiskUsage, ServerResources, ServiceAction, ServiceCandidate, SmtpSecurity,
  TcpCheckConfiguration,
} from './types'

type Page = 'server' | 'overview' | 'installations' | 'logs' | 'jobs' | 'alerts' | 'settings' | 'audit' | 'diagnostics'

const navItems: Array<{ id: Page; label: string; icon: LucideIcon }> = [
  { id: 'server', label: 'Servidor', icon: Cpu },
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
  server: { title: 'Servidor', eyebrow: 'Processador, memória e discos' },
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
        <footer className="app-footer"><span><span className="live-dot" /> Atualização em tempo real</span><span>Protheus Pulse {authStatus?.version ?? ''} · produto independente</span></footer>
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
  // O alvo do servidor entra na coleta e nas regras de alerta, mas não é um ambiente
  // Protheus: nas telas de operação ele apareceria como uma instalação que ninguém cadastrou.
  const monitored = useMemo(
    () => ({ ...summary, components: summary.components.filter(item => !item.isSystem) }),
    [summary])
  switch (page) {
    case 'server': return <ServerPage />
    case 'overview': return <Overview summary={monitored} refresh={refresh} goTo={goTo} addInstallation={addInstallation} />
    case 'installations': return <Installations summary={monitored} refresh={refresh} addInstallation={addInstallation} editInstallation={editInstallation} />
    case 'logs': return <LogsPage components={monitored.components} />
    case 'jobs': return <JobsPage components={monitored.components} />
    case 'alerts': return <AlertsPage summary={summary} refresh={refresh} goTo={goTo} />
    case 'settings': return <SettingsPage />
    case 'audit': return <AuditPage />
    case 'diagnostics': return <DiagnosticsPage demo={summary.demoMode} />
  }
}

const serverRefreshMilliseconds = 5_000

function ServerPage() {
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

interface InstallationGroup {
  id: string
  name: string
  isExclusive: boolean
  autoStartEnabled: boolean
  components: ComponentSnapshot[]
}

/// Ação de serviço leva segundos e a tela não dizia nada: o operador clicava de novo,
/// ou clicava em outra coisa no meio. A camada cobre a página enquanto a chamada corre.
function BusyOverlay({ label }: { label: string }) {
  return <div className="busy-overlay" role="alert" aria-busy="true" aria-live="assertive">
    <div className="busy-card"><RefreshCw className="spin" size={22} /><strong>{label}</strong><span>Aguarde o servidor confirmar antes de continuar.</span></div>
  </div>
}

function Installations({ summary, refresh, addInstallation, editInstallation }: { summary: DashboardSummary; refresh: () => Promise<void>; addInstallation: () => void; editInstallation: (id: string) => void }) {
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [maintenance, setMaintenance] = useState<MaintenanceStatus | null>(null)
  const [serviceBusyId, setServiceBusyId] = useState<string | null>(null)
  const [automationBusyId, setAutomationBusyId] = useState<string | null>(null)
  const isAdministrator = session.role === 'Administrator'
  const groups = useMemo(() => Object.values(summary.components.reduce<Record<string, InstallationGroup>>((result, item) => {
    const group = (result[item.installationId] ??= {
      id: item.installationId,
      name: item.installationName,
      isExclusive: Boolean(item.installationIsExclusive),
      autoStartEnabled: Boolean(item.installationAutoStartEnabled),
      components: [],
    })
    group.components.push(item)
    return result
  }, {})), [summary.components])
  const exclusiveGroup = groups.find(group => group.isExclusive)

  const loadMaintenance = useCallback(async () => {
    try { setMaintenance(await getMaintenanceStatus()) } catch { setMaintenance(null) }
  }, [])
  useEffect(() => { void loadMaintenance() }, [loadMaintenance])

  const toggleMaintenance = async () => {
    const entering = !(maintenance?.active ?? false)
    const exclusiveName = exclusiveGroup?.name ?? maintenance?.exclusiveInstallation?.name
    const confirmed = window.confirm(entering
      ? exclusiveName
        ? `Entrar em modo manutenção? Todos os serviços Windows monitorados serão PARADOS e os de “${exclusiveName}” serão REINICIADOS, derrubando as sessões conectadas para que o ambiente fique exclusivo para compilar e salvar configurações. Os alertas ficarão suspensos.`
        : 'Entrar em modo manutenção? Todos os serviços Windows monitorados serão PARADOS e os alertas ficarão suspensos.'
      : 'Encerrar o modo manutenção? Os serviços monitorados serão iniciados novamente.')
    if (!confirmed) return
    setBusy(true); setError(null); setMessage(null)
    try {
      const result = entering ? await enterMaintenance() : await exitMaintenance()
      const failures = result.services.filter(item => !item.success)
      const stopped = result.services.filter(item => item.action === 'stop' && item.success).length
      const restarted = result.services.filter(item => item.action === 'restart' && item.success).length
      const summaryText = entering
        ? `Modo manutenção ativado. ${stopped} serviço(s) parado(s)${restarted > 0 ? ` e ${restarted} reiniciado(s) em “${result.exclusiveInstallation?.name ?? exclusiveName}”, sem sessões remanescentes` : ''}`
        : `Modo manutenção encerrado. ${result.services.length - failures.length} serviço(s) iniciado(s)`
      if (failures.length > 0) setError(`${summaryText}; falhas: ${failures.map(item => `${item.serviceName} (${item.message})`).join('; ')}`)
      else setMessage(`${summaryText}.`)
      await loadMaintenance()
      await refresh()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível alterar o modo manutenção.')
    } finally { setBusy(false) }
  }

  const toggleExclusive = async (group: InstallationGroup) => {
    const enabling = !group.isExclusive
    if (enabling && !window.confirm(`Tornar “${group.name}” a instalação exclusiva? No modo manutenção ela é reiniciada e permanece como o único ambiente no ar para compilar e salvar configurações.`)) return
    setAutomationBusyId(group.id); setError(null); setMessage(null)
    try {
      const result = await setExclusiveInstallation(group.id, enabling)
      setMessage(result.isExclusive
        ? `“${result.name}” agora é a instalação exclusiva do modo manutenção.`
        : `“${result.name}” não é mais a instalação exclusiva.`)
      await refresh()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível definir a instalação exclusiva.')
    } finally { setAutomationBusyId(null) }
  }

  const toggleAutoStart = async (group: InstallationGroup) => {
    setAutomationBusyId(group.id); setError(null); setMessage(null)
    try {
      const result = await setAutoStart(group.id, !group.autoStartEnabled)
      setMessage(result.autoStartEnabled
        ? `Auto-start ativado em “${result.name}”: serviços que caírem sobem automaticamente.`
        : `Auto-start desativado em “${result.name}”.`)
      await refresh()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível alterar o auto-start.')
    } finally { setAutomationBusyId(null) }
  }

  const runServiceAction = async (component: ComponentSnapshot, action: ServiceAction) => {
    const verb = action === 'start' ? 'Iniciar' : action === 'stop' ? 'Parar' : 'Reiniciar'
    if (!serviceActionAllowed(component.windowsServiceStatus, action)) return
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

  const busyLabel = serviceBusyId !== null
    ? 'Aplicando ação no serviço…'
    : automationBusyId !== null
      ? 'Aplicando automação…'
      : busy ? 'Aplicando alteração…' : null

  return <div className="page-body">
    {busyLabel && <BusyOverlay label={busyLabel} />}
    <section className="intro-row"><div><h2>Ambientes cadastrados</h2><p>Configure serviços, arquivos, portas e URLs sem sair do painel.</p></div><div className="intro-actions">{isAdministrator && <button className={maintenance?.active ? 'primary-button' : 'danger-button'} disabled={busy || summary.demoMode} onClick={() => void toggleMaintenance()}><Wrench size={16} /> {maintenance?.active ? 'Encerrar manutenção' : 'Modo manutenção'}</button>}<button className="secondary-button" disabled={busy || summary.demoMode} onClick={() => void runCollection()}><Play size={16} /> {busy ? 'Executando…' : 'Coletar agora'}</button><button className="primary-button" onClick={addInstallation}><Plus size={16} /> Adicionar instalação</button></div></section>
    {maintenance?.active && <div className="maintenance-banner"><Wrench size={16} /> Modo manutenção ativo{maintenance.endsAt ? ` até ${new Date(maintenance.endsAt).toLocaleString('pt-BR')}` : ''}: serviços monitorados parados e alertas suspensos.{maintenance.exclusiveInstallation ? ` Somente “${maintenance.exclusiveInstallation.name}” segue no ar, reiniciado no início da janela para compilar e salvar configurações sem sessões antigas.` : ''}</div>}
    {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
    {message && <div className="success-banner"><Check size={16} /> {message}</div>}
    <article className="panel installation-table">{groups.map(group => {
      const { id: installationId, name, components } = group
      const isDemo = components.every(item => item.isDemo)
      const canAutomate = isAdministrator && !isDemo && !summary.demoMode
      const healthy = components.filter(item => item.status === 'Healthy').length
      return <Fragment key={installationId}>
        <div className="install-line">
          <div className="install-line-identity">
            <strong>{name}</strong>
            <span className="environment-tag">{environmentLabel(components[0]?.installationEnvironment)}</span>
            {group.isExclusive && <span className="exclusive-tag"><Crown size={12} /> Exclusivo</span>}
            {group.autoStartEnabled && <span className="auto-start-tag"><Zap size={12} /> Auto-start</span>}
          </div>
          <span className="install-line-stat"><strong>{components.length}</strong> componentes · <strong>{healthy}</strong> saudáveis</span>
          <StatusBadge status={worstStatus(components)} />
          <div className="install-line-actions">
            {canAutomate && <button className={group.isExclusive ? 'chip-button active' : 'chip-button'} disabled={busy || automationBusyId === installationId} aria-pressed={group.isExclusive} title="Instalação reiniciada no início da manutenção, que permanece como o único ambiente no ar" onClick={() => void toggleExclusive(group)}><Crown size={13} /> Exclusivo</button>}
            {canAutomate && <button className={group.autoStartEnabled ? 'chip-button active' : 'chip-button'} disabled={busy || automationBusyId === installationId} aria-pressed={group.autoStartEnabled} title="Sobe automaticamente os serviços deste ambiente quando eles caem. Um serviço parado pelo painel fica de fora até ser iniciado pelo painel." onClick={() => void toggleAutoStart(group)}><Zap size={13} /> Auto-start</button>}
            {!isDemo && installationId && <button className="row-action" title={`Configurar ${name}`} aria-label={`Configurar ${name}`} onClick={() => editInstallation(installationId)}><Pencil size={15} /></button>}
            {!isDemo && installationId && <button className="row-action danger" title={`Remover ${name}`} aria-label={`Remover ${name}`} disabled={busy} onClick={() => void remove(installationId, name)}><Trash2 size={15} /></button>}
          </div>
        </div>
        {components.map(component => <div className="component-line" key={component.id}>
          <i className={`status-dot ${component.status.toLowerCase()}`} />
          <span className="component-line-name">{component.name}</span>
          {component.windowsServiceName && <small className={`service-state ${serviceStateTone(component.windowsServiceStatus)}`}><i />{serviceStatusLabel(component.windowsServiceStatus)}{group.autoStartEnabled ? autoStartNote(component) : ''}</small>}
          <small className="component-line-summary">{component.summary}</small>
          {isAdministrator && !component.isDemo && component.windowsServiceName && <span className="mini-component-actions"><ServiceActionButton component={component} action="start" icon={Play} busy={serviceBusyId === component.id} disabled={busy} run={runServiceAction} /><ServiceActionButton component={component} action="restart" icon={RotateCw} busy={serviceBusyId === component.id} disabled={busy} run={runServiceAction} /><ServiceActionButton component={component} action="stop" icon={Square} busy={serviceBusyId === component.id} disabled={busy} run={runServiceAction} /></span>}
        </div>)}
      </Fragment>
    })}</article>
  </div>
}

const serviceActionLabels: Record<ServiceAction, string> = { start: 'Iniciar', restart: 'Reiniciar', stop: 'Parar' }

function ServiceActionButton({ component, action, icon: Icon, busy, disabled, run }: {
  component: ComponentSnapshot
  action: ServiceAction
  icon: LucideIcon
  busy: boolean
  disabled: boolean
  run: (component: ComponentSnapshot, action: ServiceAction) => Promise<void>
}) {
  const verb = serviceActionLabels[action]
  const allowed = serviceActionAllowed(component.windowsServiceStatus, action)
  const title = allowed
    ? `${verb} ${component.windowsServiceName}`
    : `${component.windowsServiceName} já está ${serviceStatusLabel(component.windowsServiceStatus).toLowerCase()}`
  return <button
    className={`row-action ${allowed ? '' : 'is-current-state'}`}
    title={title}
    aria-label={`${verb} serviço de ${component.name}`}
    disabled={disabled || busy || !allowed}
    onClick={() => void run(component, action)}
  >{busy && action === 'start' ? <RefreshCw className="spin" size={14} /> : <Icon size={14} />}</button>
}

const transitioningServiceStates = ['StartPending', 'StopPending', 'ContinuePending', 'PausePending']

/**
 * Espelha ServiceStateRules do backend: a ação que corresponde ao estado atual do
 * serviço fica bloqueada, e um estado indefinido libera tudo para o operador agir.
 */
export function serviceActionAllowed(status: string | undefined, action: ServiceAction) {
  if (transitioningServiceStates.includes(status ?? '')) return false
  if (status === 'Running') return action !== 'start'
  if (status === 'Stopped') return action === 'start'
  return true
}

function serviceStateTone(status: string | undefined) {
  if (status === 'Running') return 'running'
  if (status === 'Stopped') return 'stopped'
  if (transitioningServiceStates.includes(status ?? '')) return 'pending'
  return 'unknown'
}

/**
 * Distingue as duas razões de o watchdog estar quieto: uma parada deliberada não
 * acumula falhas, enquanto a desistência vem sempre depois de tentativas.
 */
function autoStartNote(component: ComponentSnapshot) {
  if (!component.windowsServiceAutoStartSuspended) return ''
  const failures = component.windowsServiceAutoStartFailures ?? 0
  return failures > 0
    ? ` · auto-start pausado após ${failures} falha${failures > 1 ? 's' : ''}`
    : ' · auto-start suspenso'
}

export function serviceStatusLabel(status: string | undefined) {
  const labels: Record<string, string> = {
    Running: 'Em execução',
    Stopped: 'Parado',
    StartPending: 'Iniciando',
    StopPending: 'Parando',
    ContinuePending: 'Retomando',
    PausePending: 'Pausando',
    Paused: 'Pausado',
    NotFound: 'Serviço não encontrado',
  }
  return labels[status ?? ''] ?? 'Estado desconhecido'
}

/// Quem lista as pastas é o serviço, não o navegador: o browser não entrega caminho
/// absoluto por segurança, e um seletor nativo abriria o disco de quem está olhando a
/// tela — que, com o acesso remoto ligado, é outra máquina.
function FolderPicker({ initial, onPick, onClose }: { initial: string; onPick: (path: string) => void; onClose: () => void }) {
  const [result, setResult] = useState<BrowseResult | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const go = useCallback(async (path?: string) => {
    setLoading(true)
    try {
      setResult(await browseFolders(path))
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível listar a pasta.')
    } finally { setLoading(false) }
  }, [])
  useEffect(() => { void go(initial.trim() === '' ? undefined : initial.trim()) }, [go, initial])

  return <div className="modal-backdrop" onClick={onClose}>
    <section className="modal-card folder-picker" role="dialog" aria-modal="true" aria-labelledby="folder-picker-title" onClick={event => event.stopPropagation()}>
      <header className="modal-header">
        <div><span>Pastas do servidor</span><h2 id="folder-picker-title">Escolher pasta</h2><p>{result?.current ?? 'Unidades disponíveis no servidor onde o Pulse está instalado.'}</p></div>
        <button className="icon-button" onClick={onClose} aria-label="Fechar seleção de pasta"><X size={18} /></button>
      </header>
      {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
      {loading
        ? <div className="modal-loading"><RefreshCw className="spin" size={20} /> Lendo…</div>
        : <>
          {result?.looksLikeProtheus && <div className="success-banner"><Check size={16} /> Esta pasta tem arquivos de AppServer. Pode selecionar aqui.</div>}
          <ul className="folder-list">
            {result?.parent && <li><button type="button" onClick={() => void go(result.parent!)}><FolderSearch size={15} /> .. voltar</button></li>}
            {result?.current === null && result.entries.length === 0 && <li className="folder-empty">Nenhuma unidade disponível.</li>}
            {result?.current !== null && result?.entries.length === 0 && <li className="folder-empty">Nenhuma subpasta acessível aqui.</li>}
            {result?.entries.map(entry => <li key={entry.path}><button type="button" onClick={() => void go(entry.path)}><FolderSearch size={15} /> {entry.name}</button></li>)}
          </ul>
        </>}
      <footer className="modal-footer">
        <button type="button" className="secondary-button" onClick={onClose}>Cancelar</button>
        <button type="button" className="primary-button" disabled={!result?.current} onClick={() => { if (result?.current) { onPick(result.current); onClose() } }}>Usar esta pasta</button>
      </footer>
    </section>
  </div>
}

/// Aponte a pasta do Protheus e o Pulse propõe um componente por appserver.ini que achar,
/// com ambiente, executável, console.log, serviço do Windows e os alvos de rede que o
/// próprio INI declara. Preencher campo por campo era o passo mais lento do cadastro.
function FolderAutoDetect({ onDetected }: { onDetected: (proposal: ComponentProposal) => void }) {
  const [folder, setFolder] = useState('')
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<ComponentProposalResult | null>(null)
  const [picking, setPicking] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function detect() {
    if (folder.trim() === '') return
    setBusy(true)
    setResult(null)
    try {
      setResult(await proposeComponent(folder.trim()))
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível varrer a pasta.')
    } finally { setBusy(false) }
  }

  return <div className="auto-detect">
    <div className="auto-detect-row">
      <label className="wide-field">Pasta do Protheus
        <input
          aria-label="Pasta para detecção automática"
          value={folder}
          onChange={event => setFolder(event.target.value)}
          onKeyDown={event => { if (event.key === 'Enter') { event.preventDefault(); void detect() } }}
          placeholder={'D:\\Protheus\\Protheus'} />
      </label>
      <button type="button" className="secondary-button" onClick={() => setPicking(true)}><FolderSearch size={15} /> Procurar…</button>
      <button type="button" className="primary-button" disabled={busy || folder.trim() === ''} onClick={() => void detect()}>{busy ? 'Procurando…' : 'Detectar'}</button>
    </div>
    {picking && <FolderPicker initial={folder} onPick={setFolder} onClose={() => setPicking(false)} />}
    {result && result.proposals.length === 0 && <p className="field-hint">Nenhum appserver.ini reconhecido em {result.filesInspected} arquivos. Confira a pasta ou preencha manualmente.</p>}
    {result && result.proposals.length > 0 && <>
      <p className="field-hint">{result.proposals.length} encontrado(s) em {result.filesInspected} arquivos. Clique para preencher o formulário — nada é gravado antes de você revisar e salvar.</p>
      <ul className="proposal-list">{result.proposals.map(proposal => <li key={proposal.iniPath ?? proposal.suggestedName}>
        <button type="button" onClick={() => onDetected(proposal)}>
          <div className="proposal-identity">
            <strong>{proposal.suggestedName}</strong>
            <span>{[proposal.environmentName, proposal.databaseKind, proposal.windowsServiceName].filter(Boolean).join(' · ') || proposal.iniPath}</span>
          </div>
          <ul className="proposal-checks">{proposal.checks.map(check => <li key={`${check.host}:${check.port}`} className={check.isRequired ? 'required' : ''}>{check.label} <strong>{check.port}</strong></li>)}</ul>
        </button>
      </li>)}</ul>
    </>}
    {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
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
      <FolderAutoDetect onDetected={proposal => update({
        name: proposal.suggestedName,
        windowsServiceName: proposal.windowsServiceName ?? component.windowsServiceName,
        executablePath: proposal.executablePath ?? component.executablePath,
        iniPath: proposal.iniPath ?? component.iniPath,
        logPaths: proposal.logPaths.length > 0 ? proposal.logPaths : component.logPaths,
        tcpChecks: proposal.checks.length > 0
          ? proposal.checks.map(check => ({ key: nextDraftKey(), host: check.host, port: check.port, timeoutMs: 3_000, isRequired: check.isRequired }))
          : component.tcpChecks
      })} />
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

function LogsPage({ components }: { components: ComponentSnapshot[] }) {
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

function heartbeatIntervalLabel(seconds: number) {
  if (seconds < 60) return `${seconds} s`
  if (seconds < 3_600) return `${Math.round(seconds / 60)} min`
  if (seconds % 3_600 === 0) return `${seconds / 3_600} h`
  return `${(seconds / 3_600).toFixed(1)} h`
}

/// Atrasado é passar do intervalo esperado mais a tolerância cadastrada — a mesma
/// conta que o coletor faz. A tela mostrava sempre "5 min" independentemente disso.
function heartbeatStatus(definition: HeartbeatDefinition): { status: HealthStatus; label: string } {
  if (!definition.lastHeartbeatAt) return { status: 'Unknown', label: 'Sem sinal ainda' }
  const elapsed = (Date.now() - new Date(definition.lastHeartbeatAt).getTime()) / 1_000
  const limit = definition.expectedIntervalSeconds + definition.toleranceSeconds
  if (elapsed > limit * 2) return { status: 'Critical', label: 'Muito atrasado' }
  if (elapsed > limit) return { status: 'Warning', label: 'Atrasado' }
  return { status: 'Healthy', label: 'Em dia' }
}

function JobsPage({ components }: { components: ComponentSnapshot[] }) {
  const [definitions, setDefinitions] = useState<HeartbeatDefinition[] | null>(null)
  const [issued, setIssued] = useState<HeartbeatToken | null>(null)
  const [draft, setDraft] = useState({ componentId: '', name: '', jobKey: '', interval: '3600', tolerance: '600' })
  const [busy, setBusy] = useState(false)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const isAdministrator = session.role === 'Administrator'

  const installations = useMemo(() => groupComponentsByInstallation(components), [components])

  const load = useCallback(async () => {
    try {
      setDefinitions(await getHeartbeatDefinitions())
      setError(null)
    } catch (reason) {
      setDefinitions([])
      setError(reason instanceof Error ? reason.message : 'Não foi possível carregar os heartbeats.')
    }
  }, [])
  useEffect(() => { void load() }, [load])

  useEffect(() => {
    if (!draft.componentId && components.length > 0) setDraft(current => ({ ...current, componentId: components[0].id }))
  }, [components, draft.componentId])

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
      const token = await createHeartbeatDefinition({
        componentId: draft.componentId,
        name: draft.name.trim(),
        jobKey: draft.jobKey.trim(),
        expectedIntervalSeconds: Number(draft.interval),
        toleranceSeconds: Number(draft.tolerance),
      })
      setIssued(token)
      setDraft(current => ({ ...current, name: '', jobKey: '' }))
    }, 'Heartbeat cadastrado.')
  }

  return <div className="page-body">
    <section className="intro-row">
      <div><h2>Heartbeats de jobs</h2><p>O job avisa que rodou; o Pulse cobra quando o aviso não chega dentro do intervalo mais a tolerância.</p></div>
      <a className="secondary-button" href="https://github.com/jeanvga/protheus-pulse/blob/main/docs/HEARTBEATS.md" target="_blank" rel="noreferrer"><CircleHelp size={16} /> Como integrar</a>
    </section>

    {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
    {message && <div className="success-banner"><Check size={16} /> {message}</div>}

    {issued && <article className="panel settings-panel">
      <PanelHeader title="Token do heartbeat" subtitle="Aparece uma única vez; guarde antes de fechar" />
      <div className="settings-form">
        <div className="token-reveal">
          <span>Chave pública do job</span><code>{issued.jobKey}</code>
          <span>Token</span><code>{issued.token}</code>
        </div>
        <div className="inline-warning"><AlertTriangle size={15} /> {issued.warning}</div>
        <div className="form-actions"><button type="button" className="secondary-button" onClick={() => setIssued(null)}>Já guardei</button></div>
      </div>
    </article>}

    {definitions === null && <article className="panel"><div className="modal-loading"><RefreshCw className="spin" size={20} /> Carregando heartbeats…</div></article>}

    {definitions?.length === 0 && <article className="panel"><div className="tab-empty">
      <BriefcaseBusiness size={22} />
      <div><strong>Nenhum job monitorado</strong><p>Cadastre o job aqui, guarde o token e faça a rotina do Protheus chamar <code>POST /api/v1/heartbeats/&#123;chave&#125;</code> ao terminar. Sem heartbeat, um job que parou de rodar não gera alerta — nada falha, simplesmente nada acontece.</p></div>
    </div></article>}

    {definitions?.map(definition => {
      const state = heartbeatStatus(definition)
      return <article className="panel job-card" key={definition.id}>
        <div className="job-icon"><BriefcaseBusiness size={21} /></div>
        <div>
          <span>{definition.componentName} · {definition.installationName}</span>
          <h3>{definition.name}</h3>
          <p><code>{definition.jobKey}</code>{definition.windowStart && definition.windowEnd ? ` · janela ${definition.windowStart.slice(0, 5)}–${definition.windowEnd.slice(0, 5)}` : ''}</p>
        </div>
        <div className="job-metrics">
          <div><span>Último sinal</span><strong>{definition.lastHeartbeatAt ? formatRelative(definition.lastHeartbeatAt) : '—'}</strong></div>
          <div><span>Esperado a cada</span><strong>{heartbeatIntervalLabel(definition.expectedIntervalSeconds)}</strong></div>
          <div><span>Tolerância</span><strong>{heartbeatIntervalLabel(definition.toleranceSeconds)}</strong></div>
          <StatusBadge status={state.status} label={state.label} />
          {isAdministrator && <div className="mini-component-actions">
            <button type="button" className="row-action" disabled={busyId === definition.id} aria-label={`Rotacionar token de ${definition.name}`}
              onClick={() => { if (window.confirm(`Gerar um token novo para “${definition.name}”? O token atual para de funcionar assim que o novo for gerado.`)) void run(definition.id, async () => { setIssued(await rotateHeartbeatToken(definition.id)) }, 'Token rotacionado.') }}><RotateCw size={14} /></button>
            <button type="button" className="row-action danger" disabled={busyId === definition.id} aria-label={`Remover ${definition.name}`}
              onClick={() => { if (window.confirm(`Remover o heartbeat “${definition.name}”? O job deixa de ser cobrado.`)) void run(definition.id, () => deleteHeartbeatDefinition(definition.id), 'Heartbeat removido.') }}><Trash2 size={14} /></button>
          </div>}
        </div>
      </article>
    })}

    {isAdministrator && <article className="panel settings-panel">
      <PanelHeader title="Novo heartbeat" subtitle="A chave pública vai na URL que o job chama; o token vai no cabeçalho" />
      <form className="settings-form" onSubmit={create}>
        <div className="form-grid">
          <label>Componente
            <select aria-label="Componente do heartbeat" value={draft.componentId} onChange={event => setDraft({ ...draft, componentId: event.target.value })} required>
              {installations.map(installation => <optgroup key={installation.id} label={installation.name}>
                {installation.components.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
              </optgroup>)}
            </select>
          </label>
          <label>Nome<input aria-label="Nome do heartbeat" value={draft.name} onChange={event => setDraft({ ...draft, name: event.target.value })} maxLength={160} placeholder="Ex.: Fechamento contábil" required /></label>
          <label>Chave pública<input aria-label="Chave pública do job" value={draft.jobKey} onChange={event => setDraft({ ...draft, jobKey: event.target.value })} maxLength={80} placeholder="fechamento-contabil" required /></label>
          <label>Esperado a cada (segundos)<input type="number" aria-label="Intervalo esperado" min={30} max={86400} value={draft.interval} onChange={event => setDraft({ ...draft, interval: event.target.value })} required /></label>
          <label>Tolerância (segundos)<input type="number" aria-label="Tolerância" min={0} max={86400} value={draft.tolerance} onChange={event => setDraft({ ...draft, tolerance: event.target.value })} required /></label>
        </div>
        <p className="field-hint">O alerta abre quando passa do intervalo somado à tolerância sem sinal. A chave pública aparece na URL e não é segredo; o token, que autentica a chamada, é mostrado uma única vez ao salvar.</p>
        <div className="form-actions"><button className="primary-button" type="submit" disabled={busy || components.length === 0}>{busy ? 'Salvando…' : 'Cadastrar heartbeat'}</button></div>
      </form>
    </article>}

    {!isAdministrator && definitions !== null && definitions.length > 0 && <div className="read-only-notice"><LockKeyhole size={22} /><div><strong>Somente leitura</strong><p>Cadastrar heartbeat, rotacionar token e remover exige o perfil Administrator.</p></div></div>}
  </div>
}

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

/// O input datetime-local trabalha em hora local; o construtor de Date também, então a volta fecha.
function toLocalInputValue(date: Date) {
  return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16)
}

function formatDateTime(value: string) {
  return new Date(value).toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })
}

interface InstallationOption { id: string; name: string; components: ComponentSnapshot[] }

function groupComponentsByInstallation(components: ComponentSnapshot[]): InstallationOption[] {
  const groups = new Map<string, InstallationOption>()
  for (const component of components) {
    let group = groups.get(component.installationId)
    if (!group) {
      group = { id: component.installationId, name: component.installationName, components: [] }
      groups.set(component.installationId, group)
    }
    group.components.push(component)
  }
  return [...groups.values()]
}

function AlertsPage({ summary, refresh, goTo }: { summary: DashboardSummary; refresh: () => Promise<void>; goTo: (page: Page) => void }) {
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

/// Seções fechadas por padrão: a aba inteira aberta de uma vez virava uma tela sem fim.
function SettingsSection({ icon: Icon, title, summary, children }: { icon: LucideIcon; title: string; summary: string; children: ReactNode }) {
  const [open, setOpen] = useState(false)
  return <article className={`panel settings-section ${open ? 'is-open' : ''}`}>
    <button type="button" className="settings-section-head" aria-expanded={open} onClick={() => setOpen(current => !current)}>
      <span className="settings-section-icon"><Icon size={18} /></span>
      <div><h3>{title}</h3><p>{summary}</p></div>
      <ChevronDown className="settings-section-caret" size={17} />
    </button>
    {open && <div className="settings-section-body">{children}</div>}
  </article>
}

function ServerThresholdsCard() {
  const [draft, setDraft] = useState({ cpuWarning: '80', cpuCritical: '92', memoryWarning: '85', memoryCritical: '94', diskWarning: '15', diskCritical: '5' })
  const [updatedAt, setUpdatedAt] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const apply = (settings: ServerThresholdSettings) => {
    setDraft({
      cpuWarning: String(settings.cpuWarningPercent),
      cpuCritical: String(settings.cpuCriticalPercent),
      memoryWarning: String(settings.memoryWarningPercent),
      memoryCritical: String(settings.memoryCriticalPercent),
      diskWarning: String(settings.diskFreeWarningPercent),
      diskCritical: String(settings.diskFreeCriticalPercent),
    })
    setUpdatedAt(settings.updatedAt)
  }

  useEffect(() => {
    void (async () => {
      try {
        apply(await getServerThresholds())
      } catch (reason) {
        setError(reason instanceof Error ? reason.message : 'Não foi possível carregar os limites.')
      } finally { setLoading(false) }
    })()
  }, [])

  async function save(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setMessage(null)
    try {
      apply(await saveServerThresholds({
        cpuWarningPercent: Number(draft.cpuWarning),
        cpuCriticalPercent: Number(draft.cpuCritical),
        memoryWarningPercent: Number(draft.memoryWarning),
        memoryCriticalPercent: Number(draft.memoryCritical),
        diskFreeWarningPercent: Number(draft.diskWarning),
        diskFreeCriticalPercent: Number(draft.diskCritical),
      }))
      setMessage('Limites salvos. O próximo ciclo de coleta já usa os novos valores.')
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível salvar os limites.')
    } finally { setBusy(false) }
  }

  if (loading) return <div className="modal-loading"><RefreshCw className="spin" size={20} /> Carregando limites…</div>

  return <form className="settings-form" onSubmit={event => void save(event)}>
    <p className="field-hint">São os mesmos limites que pintam a aba Servidor e que classificam o estado das verificações de processador, memória e disco. Até aqui só mudavam editando o <code>appsettings.json</code> no servidor e reiniciando o serviço.</p>
    <div className="form-grid">
      <label>Processador · atenção (%)<input type="number" aria-label="Atenção do processador" min={1} max={100} value={draft.cpuWarning} onChange={event => setDraft({ ...draft, cpuWarning: event.target.value })} required /></label>
      <label>Processador · crítico (%)<input type="number" aria-label="Crítico do processador" min={1} max={100} value={draft.cpuCritical} onChange={event => setDraft({ ...draft, cpuCritical: event.target.value })} required /></label>
      <label>Memória · atenção (%)<input type="number" aria-label="Atenção da memória" min={1} max={100} value={draft.memoryWarning} onChange={event => setDraft({ ...draft, memoryWarning: event.target.value })} required /></label>
      <label>Memória · crítico (%)<input type="number" aria-label="Crítico da memória" min={1} max={100} value={draft.memoryCritical} onChange={event => setDraft({ ...draft, memoryCritical: event.target.value })} required /></label>
      <label>Disco livre · atenção (%)<input type="number" aria-label="Atenção do disco livre" min={0} max={100} value={draft.diskWarning} onChange={event => setDraft({ ...draft, diskWarning: event.target.value })} required /></label>
      <label>Disco livre · crítico (%)<input type="number" aria-label="Crítico do disco livre" min={0} max={100} value={draft.diskCritical} onChange={event => setDraft({ ...draft, diskCritical: event.target.value })} required /></label>
    </div>
    <p className="field-hint">Processador e memória medem <strong>uso</strong>: o crítico fica acima da atenção. Disco mede o espaço <strong>livre</strong>: o crítico fica abaixo. Uma regra de alerta com limite próprio ignora estes valores e usa o dela.</p>
    {updatedAt && <p className="field-hint">Alterado pela última vez em {new Date(updatedAt).toLocaleString('pt-BR')}.</p>}
    {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
    {message && <div className="success-banner"><Check size={16} /> {message}</div>}
    <div className="form-actions"><button className="primary-button" type="submit" disabled={busy}>{busy ? 'Salvando…' : 'Salvar limites'}</button></div>
  </form>
}

function RetentionSettingsCard() {
  const [settings, setSettings] = useState<RetentionSettings | null>(null)
  const [historyDays, setHistoryDays] = useState('30')
  const [aggregationDays, setAggregationDays] = useState('7')
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    void (async () => {
      try {
        const loaded = await getRetentionSettings()
        setSettings(loaded)
        setHistoryDays(String(loaded.historyRetentionDays))
        setAggregationDays(String(loaded.metricAggregationAfterDays))
      } catch (reason) {
        setError(reason instanceof Error ? reason.message : 'Não foi possível carregar a retenção.')
      }
    })()
  }, [])

  async function save(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setMessage(null)
    try {
      await saveRetentionSettings({ historyRetentionDays: Number(historyDays), metricAggregationAfterDays: Number(aggregationDays) })
      setMessage('Retenção salva. A limpeza roda diariamente e na próxima execução já usa o novo prazo.')
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível salvar a retenção.')
    } finally { setBusy(false) }
  }

  const stored = settings?.counts
  return <form className="settings-form" onSubmit={event => void save(event)}>
    <p className="field-hint">O Pulse guarda o histórico em SQLite no próprio servidor. Sem prazo, o arquivo cresce sem teto: a limpeza diária apaga probes, eventos de log e métricas mais antigos que o prazo, e preserva usuários, configuração e auditoria.</p>
    {stored && <div className="retention-counts">
      <div><span>Verificações</span><strong>{stored.probeResults.toLocaleString('pt-BR')}</strong></div>
      <div><span>Eventos de log</span><strong>{stored.logEvents.toLocaleString('pt-BR')}</strong></div>
      <div><span>Amostras de métrica</span><strong>{stored.metricSamples.toLocaleString('pt-BR')}</strong></div>
    </div>}
    <div className="form-grid">
      <label>Guardar histórico por (dias)<input type="number" min={1} max={365} value={historyDays} onChange={event => setHistoryDays(event.target.value)} /></label>
      <label>Agregar métricas por hora após (dias)<input type="number" min={1} max={365} value={aggregationDays} onChange={event => setAggregationDays(event.target.value)} /></label>
    </div>
    <p className="field-hint">Entre 1 e 365 dias. A agregação não pode passar do tamanho do histórico: amostras detalhadas viram médias por hora depois desse prazo, o que reduz o banco sem perder a tendência.</p>
    {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
    {message && <div className="success-banner"><Check size={16} /> {message}</div>}
    <div className="form-actions"><button className="primary-button" type="submit" disabled={busy}>{busy ? 'Salvando…' : 'Salvar retenção'}</button></div>
  </form>
}

function UsersSettingsCard() {
  const [users, setUsers] = useState<PulseUser[]>([])
  const [draft, setDraft] = useState({ username: '', displayName: '', email: '', password: '', role: 'Viewer' })
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      setUsers(await getUsers())
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível carregar as contas.')
    }
  }, [])
  useEffect(() => { void load() }, [load])

  async function run(action: () => Promise<void>, success: string) {
    setBusy(true)
    setMessage(null)
    try {
      await action()
      setMessage(success)
      setError(null)
      await load()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'A operação não foi concluída.')
    } finally { setBusy(false) }
  }

  return <div className="settings-form">
    <p className="field-hint">Administrator controla serviços e configuração; Operator reconhece alertas e opera a manutenção; Viewer só enxerga. O painel nunca fica sem um administrador ativo: a última conta com esse perfil não pode ser rebaixada, desativada nem removida.</p>
    <ul className="user-list">{users.map(user => <li key={user.id}>
      <div className="user-line-identity"><strong>{user.displayName}</strong><span>{user.username}{user.email ? ` · ${user.email}` : ''}</span></div>
      {!user.isActive && <span className="user-inactive">Inativa</span>}
      <select aria-label={`Perfil de ${user.username}`} value={user.role} disabled={busy} onChange={event => void run(() => updateUser(user.id, { role: event.target.value }), 'Perfil atualizado.')}>
        <option value="Administrator">Administrator</option>
        <option value="Operator">Operator</option>
        <option value="Viewer">Viewer</option>
      </select>
      <button type="button" className="row-action" title={user.isActive ? `Desativar ${user.username}` : `Reativar ${user.username}`} aria-label={user.isActive ? `Desativar ${user.username}` : `Reativar ${user.username}`} disabled={busy} onClick={() => void run(() => updateUser(user.id, { role: user.role, isActive: !user.isActive }), user.isActive ? 'Conta desativada.' : 'Conta reativada.')}><LockKeyhole size={15} /></button>
      <button type="button" className="row-action" title={`Trocar a senha de ${user.username}`} aria-label={`Trocar a senha de ${user.username}`} disabled={busy} onClick={() => {
        const password = window.prompt(`Nova senha para ${user.username}`)
        if (password) void run(() => resetUserPassword(user.id, password), 'Senha trocada.')
      }}><RefreshCw size={15} /></button>
      <button type="button" className="row-action danger" title={`Remover ${user.username}`} aria-label={`Remover ${user.username}`} disabled={busy} onClick={() => {
        if (window.confirm(`Remover a conta ${user.username}?`)) void run(() => deleteUser(user.id), 'Conta removida.')
      }}><Trash2 size={15} /></button>
    </li>)}</ul>
    <form className="form-grid" onSubmit={event => {
      event.preventDefault()
      void run(async () => {
        await createUser(draft)
        setDraft({ username: '', displayName: '', email: '', password: '', role: 'Viewer' })
      }, 'Conta criada.')
    }}>
      <label>Usuário<input required value={draft.username} onChange={event => setDraft({ ...draft, username: event.target.value })} placeholder="joao.silva" /></label>
      <label>Nome<input value={draft.displayName} onChange={event => setDraft({ ...draft, displayName: event.target.value })} placeholder="João Silva" /></label>
      <label>E-mail<input type="email" value={draft.email} onChange={event => setDraft({ ...draft, email: event.target.value })} placeholder="Opcional" /></label>
      <label>Senha<input required type="password" value={draft.password} onChange={event => setDraft({ ...draft, password: event.target.value })} /></label>
      <label>Perfil<select value={draft.role} onChange={event => setDraft({ ...draft, role: event.target.value })}><option value="Administrator">Administrator</option><option value="Operator">Operator</option><option value="Viewer">Viewer</option></select></label>
      <div className="form-actions"><button className="primary-button" type="submit" disabled={busy}>{busy ? 'Salvando…' : 'Criar conta'}</button></div>
    </form>
    {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
    {message && <div className="success-banner"><Check size={16} /> {message}</div>}
  </div>
}

function NetworkSettingsCard() {
  const [settings, setSettings] = useState<NetworkSettings | null>(null)
  const [allowRemote, setAllowRemote] = useState(false)
  const [port, setPort] = useState('5058')
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    void (async () => {
      try {
        const loaded = await getNetworkSettings()
        setSettings(loaded)
        setAllowRemote(loaded.allowRemoteAccess)
        setPort(String(loaded.port))
      } catch (reason) {
        setError(reason instanceof Error ? reason.message : 'Não foi possível carregar o acesso pela rede.')
      }
    })()
  }, [])

  async function save(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setMessage(null)
    try {
      setSettings(await saveNetworkSettings({ allowRemoteAccess: allowRemote, port: Number(port) }))
      setMessage('Salvo. Reinicie o serviço ProtheusPulse para o novo endereço valer.')
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível salvar.')
    } finally { setBusy(false) }
  }

  return <form className="settings-form" onSubmit={event => void save(event)}>
    <p className="field-hint">Por padrão o painel escuta apenas em <code>127.0.0.1</code> e só abre no próprio servidor. Liberar o acesso faz o serviço escutar em todas as interfaces, para abrir de outra máquina por <code>http://ip:porta</code>.</p>
    <div className="network-warning"><ShieldCheck size={18} /><div><strong>O tráfego é HTTP, sem TLS.</strong> Senha e token trafegam legíveis na rede, e por esta tela se controla serviço do Windows. Use apenas em rede interna confiável; para acesso amplo, publique por um proxy HTTPS e mantenha esta opção desligada.</div></div>
    <label className="switch-field"><input type="checkbox" checked={allowRemote} onChange={event => setAllowRemote(event.target.checked)} /> Permitir acesso de outros computadores</label>
    <div className="form-grid"><label>Porta<input type="number" min={1024} max={65535} value={port} onChange={event => setPort(event.target.value)} /></label></div>
    {settings && <div className="network-addresses">
      <span>Escutando agora em <code>{settings.boundUrl}</code></span>
      {allowRemote && settings.localAddresses.length > 0 && <ul>{settings.localAddresses.map(address => <li key={address}><code>{address}</code></li>)}</ul>}
    </div>}
    <p className="field-hint">O instalador não cria regra de firewall. Para liberar a porta no Windows, execute como administrador: <code>netsh advfirewall firewall add rule name="Protheus Pulse" dir=in action=allow protocol=TCP localport={port}</code></p>
    {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
    {message && <div className="success-banner"><Check size={16} /> {message}</div>}
    <div className="form-actions"><button className="primary-button" type="submit" disabled={busy}>{busy ? 'Salvando…' : 'Salvar acesso'}</button></div>
  </form>
}

function SettingsPage() {
  const isAdministrator = session.role === 'Administrator'
  const items = [{ icon: Clock3, title: 'Intervalos e retenção', text: '30 dias de histórico · agregação após 7 dias' }, { icon: UserRound, title: 'Usuários e perfis', text: 'Administrator, Operator e Viewer' }, { icon: Bell, title: 'Canais de notificação', text: 'Dashboard · E-mail · Webhook · Teams · Slack · Discord' }, { icon: ShieldCheck, title: 'Segurança', text: 'Bind local · HTTPS recomendado para acesso em rede' }]
  return <div className="page-body">
    {isAdministrator
      ? <><SettingsSection icon={Mail} title="Envio de e-mail" summary="Servidor SMTP, remetente, destinatários e teste de envio"><EmailSettingsCard /></SettingsSection><SettingsSection icon={Cpu} title="Limites do servidor" summary="A partir de quanto uso o processador, a memória e o disco entram em atenção e crítico"><ServerThresholdsCard /></SettingsSection><SettingsSection icon={Archive} title="Retenção de dados" summary="Por quanto tempo o histórico fica no banco antes de ser apagado"><RetentionSettingsCard /></SettingsSection><SettingsSection icon={UserRound} title="Usuários e perfis" summary="Contas de acesso ao painel e o que cada perfil pode fazer"><UsersSettingsCard /></SettingsSection><SettingsSection icon={Boxes} title="Acesso pela rede" summary="Abrir o painel de outro computador por http://ip:porta"><NetworkSettingsCard /></SettingsSection></>
      : <div className="read-only-notice"><LockKeyhole size={22} /><div><strong>Somente administradores</strong><p>Os dados de envio de e-mail e os tokens dos agentes de log só aparecem para o perfil Administrator.</p></div></div>}
    <div className="settings-grid">{items.map(({ icon: Icon, title, text }) => <article className="panel setting-card" key={title}><span><Icon size={20} /></span><div><h3>{title}</h3><p>{text}</p></div></article>)}</div>
    <div className="read-only-notice"><ShieldCheck size={22} /><div><strong>Coleta segura e ações auditadas</strong><p>A coleta é somente leitura e não escreve nas pastas monitoradas. Iniciar, reiniciar ou parar serviços exige perfil Administrator e fica registrado na auditoria.</p></div></div>
  </div>
}

const smtpSecurityOptions: Array<{ value: SmtpSecurity; label: string; port: number; hint: string }> = [
  { value: 'Auto', label: 'Automático', port: 587, hint: 'Escolhe TLS implícito ou STARTTLS conforme a porta e o servidor.' },
  { value: 'StartTls', label: 'STARTTLS', port: 587, hint: 'Conexão aberta em texto puro e promovida a TLS. Porta usual: 587.' },
  { value: 'SslOnConnect', label: 'SSL/TLS implícito', port: 465, hint: 'TLS desde o primeiro byte, sem texto puro. Porta usual: 465.' },
  { value: 'None', label: 'Sem criptografia', port: 25, hint: 'Só em relay interno na mesma rede. A senha trafega legível.' },
]

const knownSmtpPorts = smtpSecurityOptions.map(option => option.port)

interface EmailDraft {
  enabled: boolean
  host: string
  port: string
  security: SmtpSecurity
  username: string
  fromAddress: string
  fromName: string
  recipients: string
  timeoutSeconds: string
  allowInvalidCertificate: boolean
  notifyAlerts: boolean
  notifyLogErrors: boolean
}

const emptyEmailDraft: EmailDraft = {
  enabled: false, host: '', port: '587', security: 'Auto', username: '', fromAddress: '', fromName: 'Protheus Pulse',
  recipients: '', timeoutSeconds: '20', allowInvalidCertificate: false, notifyAlerts: true, notifyLogErrors: true,
}

function toDraft(settings: EmailSettings): EmailDraft {
  return {
    enabled: settings.enabled,
    host: settings.host,
    port: String(settings.port),
    security: settings.security,
    username: settings.username ?? '',
    fromAddress: settings.fromAddress,
    fromName: settings.fromName ?? '',
    recipients: settings.recipients.join('\n'),
    timeoutSeconds: String(settings.timeoutSeconds),
    allowInvalidCertificate: settings.allowInvalidCertificate,
    notifyAlerts: settings.notifyAlerts,
    notifyLogErrors: settings.notifyLogErrors,
  }
}

function EmailSettingsCard() {
  const [draft, setDraft] = useState<EmailDraft>(emptyEmailDraft)
  const [hasPassword, setHasPassword] = useState(false)
  const [password, setPassword] = useState('')
  const [clearPassword, setClearPassword] = useState(false)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [testing, setTesting] = useState(false)
  const [configured, setConfigured] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const settings = await getEmailSettings()
      setDraft(toDraft(settings))
      setHasPassword(settings.hasPassword)
      setConfigured(settings.configured)
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível carregar os dados de e-mail.')
    } finally {
      setLoading(false)
    }
  }, [])
  useEffect(() => { void load() }, [load])

  const update = (change: Partial<EmailDraft>) => setDraft(current => ({ ...current, ...change }))

  // Trocar o modo de segurança ajusta a porta, mas só quando ela ainda é uma das
  // portas padrão: uma porta digitada à mão não pode ser sobrescrita.
  const changeSecurity = (security: SmtpSecurity) => {
    const suggested = smtpSecurityOptions.find(option => option.value === security)?.port
    const keepPort = !knownSmtpPorts.includes(Number(draft.port))
    update({ security, port: keepPort || suggested === undefined ? draft.port : String(suggested) })
  }

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setBusy(true); setError(null); setMessage(null)
    try {
      await saveEmailSettings({
        enabled: draft.enabled,
        host: draft.host.trim(),
        port: Number(draft.port),
        security: draft.security,
        username: draft.username.trim() || undefined,
        password: clearPassword ? '' : password || undefined,
        fromAddress: draft.fromAddress.trim(),
        fromName: draft.fromName.trim() || undefined,
        recipients: draft.recipients.split(/[\n,;]/).map(item => item.trim()).filter(Boolean),
        timeoutSeconds: Number(draft.timeoutSeconds),
        allowInvalidCertificate: draft.allowInvalidCertificate,
        notifyAlerts: draft.notifyAlerts,
        notifyLogErrors: draft.notifyLogErrors,
      })
      setPassword('')
      setClearPassword(false)
      setMessage('Dados de envio salvos.')
      await load()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível salvar os dados de e-mail.')
    } finally { setBusy(false) }
  }

  const test = async () => {
    setTesting(true); setError(null); setMessage(null)
    try {
      const result = await sendTestEmail()
      if (result.success) setMessage(`Teste enviado: ${result.message}`)
      else setError(`O teste falhou: ${result.message}`)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Não foi possível enviar o teste.')
    } finally { setTesting(false) }
  }

  const securityHint = smtpSecurityOptions.find(option => option.value === draft.security)?.hint

  return <article className="panel settings-panel">
    <PanelHeader title="Dados para envio de e-mail" subtitle="Servidor SMTP usado nos alertas e nos erros de log enviados pelos agentes" />
    {loading ? <div className="modal-loading"><RefreshCw className="spin" size={20} /> Carregando…</div> : <form className="settings-form" onSubmit={submit}>
      {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}
      {message && <div className="success-banner"><Check size={16} /> {message}</div>}

      <label className="checkbox-label toggle-row">
        <input type="checkbox" aria-label="Ativar envio de e-mail" checked={draft.enabled} onChange={event => update({ enabled: event.target.checked })} />
        <span><strong>Enviar e-mail</strong><small>Desligado, o Pulse continua registrando tudo, só não avisa por e-mail.</small></span>
      </label>

      <div className="target-form-grid">
        <label>Servidor SMTP<input aria-label="Servidor SMTP" value={draft.host} onChange={event => update({ host: event.target.value })} placeholder="smtp.suaempresa.com.br" maxLength={253} required /></label>
        <label>Porta<input aria-label="Porta SMTP" type="number" min="1" max="65535" value={draft.port} onChange={event => update({ port: event.target.value })} required /></label>
        <label>Segurança
          <select aria-label="Segurança SMTP" value={draft.security} onChange={event => changeSecurity(event.target.value as SmtpSecurity)}>
            {smtpSecurityOptions.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
          </select>
        </label>
        <label>Tempo limite (s)<input aria-label="Tempo limite SMTP" type="number" min="5" max="120" value={draft.timeoutSeconds} onChange={event => update({ timeoutSeconds: event.target.value })} /></label>
        {securityHint && <p className="field-hint wide-field">{securityHint}</p>}

        <label>Usuário<input aria-label="Usuário SMTP" autoComplete="off" value={draft.username} onChange={event => update({ username: event.target.value })} placeholder="Deixe vazio para relay sem autenticação" maxLength={200} /></label>
        <label>Senha
          <input
            aria-label="Senha SMTP"
            type="password"
            autoComplete="new-password"
            value={password}
            disabled={clearPassword}
            onChange={event => setPassword(event.target.value)}
            placeholder={hasPassword ? 'Guardada — preencha só para trocar' : 'Senha do usuário SMTP'}
            maxLength={200}
          />
        </label>
        {hasPassword && <label className="checkbox-label wide-field">
          <input type="checkbox" checked={clearPassword} onChange={event => { setClearPassword(event.target.checked); setPassword('') }} /> Remover a senha guardada
        </label>}

        <label>Remetente<input aria-label="Endereço do remetente" type="email" value={draft.fromAddress} onChange={event => update({ fromAddress: event.target.value })} placeholder="pulse@suaempresa.com.br" maxLength={254} required /></label>
        <label>Nome do remetente<input aria-label="Nome do remetente" value={draft.fromName} onChange={event => update({ fromName: event.target.value })} placeholder="Protheus Pulse" maxLength={120} /></label>
        <label className="wide-field">Destinatários, um por linha
          <textarea aria-label="Destinatários" value={draft.recipients} onChange={event => update({ recipients: event.target.value })} placeholder={'ti@suaempresa.com.br\nplantao@suaempresa.com.br'} required />
        </label>
      </div>

      <div className="settings-options">
        <label className="checkbox-label"><input type="checkbox" checked={draft.notifyAlerts} onChange={event => update({ notifyAlerts: event.target.checked })} /> Avisar sobre alertas</label>
        <label className="checkbox-label"><input type="checkbox" checked={draft.notifyLogErrors} onChange={event => update({ notifyLogErrors: event.target.checked })} /> Avisar sobre erros de log dos agentes</label>
        <label className="checkbox-label"><input type="checkbox" checked={draft.allowInvalidCertificate} onChange={event => update({ allowInvalidCertificate: event.target.checked })} /> Aceitar certificado que não valida</label>
      </div>
      {draft.allowInvalidCertificate && <div className="inline-warning"><AlertTriangle size={14} /> Aceitar certificado inválido só faz sentido em relay interno com certificado próprio.</div>}

      <footer className="modal-actions">
        <button type="button" className="secondary-button" disabled={busy || testing || !configured} onClick={() => void test()}>
          {testing ? <RefreshCw className="spin" size={16} /> : <Send size={16} />}{testing ? 'Enviando…' : 'Enviar teste'}
        </button>
        <button className="primary-button" disabled={busy}>{busy ? <RefreshCw className="spin" size={16} /> : <Mail size={16} />}{busy ? 'Salvando…' : 'Salvar dados de envio'}</button>
      </footer>
      {!configured && <p className="field-hint">Salve os dados antes de enviar o teste.</p>}
    </form>}
  </article>
}

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

function AuditPage() {
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

function DiagnosticsPage({ demo }: { demo: boolean }) {
  const [info, setInfo] = useState<DiagnosticsInfo | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const isAdministrator = session.role === 'Administrator'

  const load = useCallback(async () => {
    if (!isAdministrator) { setLoading(false); return }
    setLoading(true)
    try {
      setInfo(await getDiagnostics())
      setError(null)
    } catch (reason) {
      setInfo(null)
      setError(reason instanceof Error ? reason.message : 'Não foi possível consultar o diagnóstico.')
    } finally { setLoading(false) }
  }, [isAdministrator])

  useEffect(() => { void load() }, [load])

  if (!isAdministrator) {
    return <div className="page-body"><div className="read-only-notice"><LockKeyhole size={22} /><div><strong>Somente administradores</strong><p>O diagnóstico expõe caminho de dados, plataforma e estado do banco. Só o perfil Administrator pode consultá-lo.</p></div></div></div>
  }

  // Sem resposta do servidor não há como afirmar que algo está saudável.
  const reachable = info !== null
  const databaseStatus: HealthStatus = !reachable ? 'Unknown' : info.status
  const collectorStatus: HealthStatus = !reachable ? 'Unknown' : demo ? 'Unknown' : 'Healthy'

  return <div className="page-body">
    <section className="toolbar">
      <span className="refresh-hint"><Activity size={15} /> Consultado a cada abertura da aba</span>
      <button className="secondary-button" disabled={loading} onClick={() => void load()}><RefreshCw size={16} /> {loading ? 'Consultando…' : 'Consultar de novo'}</button>
    </section>

    {error && <div className="form-error"><AlertTriangle size={16} /> {error}</div>}

    <div className="diagnostic-grid">
      <Diagnostic
        title="Serviço web"
        status={reachable ? 'Healthy' : 'Critical'}
        detail={reachable ? `${info.service} respondeu a esta consulta` : 'A API não respondeu; o painel está sem contato com o serviço.'} />
      <Diagnostic
        title="Banco local"
        status={databaseStatus}
        detail={reachable ? `${info.database} ${info.status === 'Healthy' ? 'disponível com o esquema aplicado' : 'não respondeu à verificação de conexão'}` : 'Sem resposta do serviço.'} />
      <Diagnostic
        title="Plataforma"
        status={reachable ? 'Healthy' : 'Unknown'}
        detail={reachable ? `${info.platform} · versão ${info.version}` : 'Sem resposta do serviço.'} />
      <Diagnostic
        title="Coletores reais"
        status={collectorStatus}
        detail={!reachable ? 'Sem resposta do serviço.' : demo ? 'Desativados no modo demonstração.' : 'Agendador somente leitura ativo.'} />
    </div>

    {reachable && info.notes.length > 0 && <article className="panel settings-panel">
      <PanelHeader title="O que este serviço faz e não faz" subtitle="Limites declarados pelo próprio serviço" />
      <ul className="diagnostic-notes">{info.notes.map(note => <li key={note}><Check size={14} /> {note}</li>)}</ul>
    </article>}

    {demo && <div className="demo-notice"><HeartPulse size={22} /><div><strong>Modo demonstração ativo</strong><p>Todos os alvos e eventos exibidos são sintéticos e claramente marcados.</p></div></div>}
  </div>
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

const byteUnits = ['B', 'KB', 'MB', 'GB', 'TB', 'PB']

function formatBytes(value: number) {
  if (!Number.isFinite(value) || value <= 0) return '0 B'
  const index = Math.min(byteUnits.length - 1, Math.floor(Math.log(value) / Math.log(1024)))
  const scaled = value / 1024 ** index
  return `${scaled.toFixed(index === 0 || scaled >= 100 ? 0 : 1)} ${byteUnits[index]}`
}

function formatUptime(totalSeconds: number) {
  const days = Math.floor(totalSeconds / 86400)
  const hours = Math.floor((totalSeconds % 86400) / 3600)
  const minutes = Math.floor((totalSeconds % 3600) / 60)
  if (days > 0) return `${days} d ${hours} h`
  if (hours > 0) return `${hours} h ${minutes} min`
  return `${minutes} min`
}

function formatPercent(value?: number | null) {
  return value == null ? '—' : `${value.toFixed(1)}%`
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
