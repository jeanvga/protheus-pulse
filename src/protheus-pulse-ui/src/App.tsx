import { useCallback, useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import {
  AlertTriangle, Bell, Check, ChevronDown, HeartPulse, LockKeyhole, LogOut, Menu, Moon, RefreshCw, ShieldCheck, Sun, X,
} from 'lucide-react'
import {
  connectLiveUpdates, getAuthStatus, getDashboard, login, refreshSession, session, setup,
} from './api'
import type {
  AuthStatus, AuthToken, DashboardSummary,
} from './types'
import { navItems, secondaryNav, pageTitles } from './lib/navigation'
import type { Page } from './lib/navigation'
import { NavItem, Splash, DashboardSkeleton } from './components/Primitives'
import { ServerPage } from './pages/ServerPage'
import { Overview } from './pages/Overview'
import { Installations, InstallationDialog } from './pages/Installations'
import { LogsPage } from './pages/LogsPage'
import { JobsPage } from './pages/JobsPage'
import { AlertsPage } from './pages/AlertsPage'
import { SettingsPage } from './pages/SettingsPage'
import { AuditPage } from './pages/AuditPage'
import { DiagnosticsPage } from './pages/DiagnosticsPage'

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
  const [expiresAt, setExpiresAt] = useState<string | null>(null)

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
    setExpiresAt(token.expiresAt)
    setAuthenticated(true)
  }

  // A sessão vale oito horas e caía sem aviso, levando junto o formulário aberto.
  // Renova com folga enquanto a pessoa está usando; se a conta foi desativada no
  // meio do caminho, o servidor recusa e a tela volta para o login.
  useEffect(() => {
    if (!authenticated || !expiresAt) return
    const remaining = new Date(expiresAt).getTime() - Date.now()
    const renewIn = Math.max(30_000, remaining - 10 * 60_000)
    const timer = setTimeout(() => {
      void refreshSession()
        .then(token => { session.token = token.accessToken; session.role = token.role; setExpiresAt(token.expiresAt) })
        .catch(() => { session.token = null; session.role = null; setAuthenticated(false) })
    }, renewIn)
    return () => clearTimeout(timer)
  }, [authenticated, expiresAt])

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

export function Sidebar({ active, setPage, open, close, logout, alertCount }: { active: Page; setPage: (page: Page) => void; open: boolean; close: () => void; logout: () => void; alertCount: number }) {
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

export function LoginScreen({ status, onAuthenticated, error: initialError }: { status: AuthStatus | null; onAuthenticated: (token: AuthToken) => void; error: string | null }) {
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

export function PageContent({ page, summary, refresh, goTo, addInstallation, editInstallation }: { page: Page; summary: DashboardSummary; refresh: () => Promise<void>; goTo: (page: Page) => void; addInstallation: () => void; editInstallation: (id: string) => void }) {
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
