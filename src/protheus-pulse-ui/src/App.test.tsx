import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { demoServerResources, demoSummary } from './demoData'

vi.mock('./api', () => ({
  session: {
    get token() { return sessionStorage.getItem('pulse.test.token') },
    set token(value: string | null) { if (value) sessionStorage.setItem('pulse.test.token', value); else sessionStorage.removeItem('pulse.test.token') },
    get role() { return sessionStorage.getItem('pulse.test.role') },
    set role(value: string | null) { if (value) sessionStorage.setItem('pulse.test.role', value); else sessionStorage.removeItem('pulse.test.role') },
  },
  getAuthStatus: vi.fn().mockResolvedValue({ requiresSetup: false, demoMode: true }),
  getDashboard: vi.fn().mockResolvedValue(demoSummary),
  createInstallation: vi.fn(),
  getInstallationConfiguration: vi.fn(),
  updateInstallation: vi.fn(),
  deleteInstallation: vi.fn(),
  discoverServices: vi.fn(),
  discoverPaths: vi.fn(),
  collectNow: vi.fn(),
  acknowledgeAlert: vi.fn(),
  getLogEvents: vi.fn().mockResolvedValue([]),
  executeServiceAction: vi.fn(),
  enterMaintenance: vi.fn(),
  exitMaintenance: vi.fn(),
  getMaintenanceStatus: vi.fn().mockResolvedValue({ active: false }),
  setExclusiveInstallation: vi.fn(),
  setAutoStart: vi.fn(),
  login: vi.fn(),
  setup: vi.fn(),
  connectLiveUpdates: vi.fn(() => () => undefined),
  getServerResources: vi.fn(),
  getEmailSettings: vi.fn(),
  saveEmailSettings: vi.fn(),
  sendTestEmail: vi.fn(),
}))

import {
  acknowledgeAlert, collectNow, createInstallation, deleteInstallation, discoverPaths,
  discoverServices, executeServiceAction, getDashboard, getEmailSettings, getInstallationConfiguration,
  getServerResources, saveEmailSettings, sendTestEmail, setAutoStart, setExclusiveInstallation,
  updateInstallation,
} from './api'
import App, { serviceActionAllowed } from './App'

const emailSettings = {
  configured: true,
  enabled: true,
  host: 'smtp.exemplo.com.br',
  port: 587,
  security: 'StartTls' as const,
  username: 'pulse@exemplo.com.br',
  hasPassword: true,
  fromAddress: 'pulse@exemplo.com.br',
  fromName: 'Protheus Pulse',
  recipients: ['ti@exemplo.com.br'],
  timeoutSeconds: 20,
  allowInvalidCertificate: false,
  notifyAlerts: true,
  notifyLogErrors: true,
}

const realSummary = {
  ...demoSummary,
  demoMode: false,
  totals: { ...demoSummary.totals, installations: 1, components: 1, unknown: 1, healthy: 0, warning: 0, critical: 0, activeAlerts: 0 },
  components: [{
    id: 'component-real', installationId: 'installation-real', installationName: 'ERP Produção',
    installationEnvironment: 'Production' as const, name: 'AppServer REST', type: 'Rest', status: 'Unknown' as const,
    summary: 'Aguardando a primeira coleta.', isDemo: false,
  }],
  alerts: [],
}

describe('App', () => {
  afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
  })

  beforeEach(() => {
    sessionStorage.clear()
    sessionStorage.setItem('pulse.test.token', 'test-token')
    vi.mocked(getDashboard).mockReset().mockResolvedValue(demoSummary)
    vi.mocked(createInstallation).mockReset().mockResolvedValue({
      id: 'new-installation',
      name: 'ERP Piloto',
      environment: 'Production',
      tags: ['piloto'],
      componentCount: 1,
      status: 'Unknown',
    })
    vi.mocked(getInstallationConfiguration).mockReset()
    vi.mocked(updateInstallation).mockReset()
    vi.mocked(deleteInstallation).mockReset().mockResolvedValue(undefined)
    vi.mocked(discoverServices).mockReset().mockResolvedValue({ supported: true, dryRun: true, candidates: [] })
    vi.mocked(discoverPaths).mockReset().mockResolvedValue({ dryRun: true, timedOut: false, durationMs: 1, candidates: [] })
    vi.mocked(collectNow).mockReset().mockResolvedValue({ processedComponents: 1, completedAt: new Date().toISOString() })
    vi.mocked(acknowledgeAlert).mockReset().mockResolvedValue(undefined)
    vi.mocked(executeServiceAction).mockReset().mockResolvedValue({ results: [] })
    vi.mocked(setExclusiveInstallation).mockReset().mockResolvedValue({
      id: 'installation-real', name: 'ERP Produção', isExclusive: true, autoStartEnabled: false,
    })
    vi.mocked(setAutoStart).mockReset().mockResolvedValue({
      id: 'installation-real', name: 'ERP Produção', isExclusive: false, autoStartEnabled: true,
    })
    vi.mocked(getServerResources).mockReset().mockResolvedValue(demoServerResources)
    vi.mocked(getEmailSettings).mockReset().mockResolvedValue(emailSettings)
    vi.mocked(saveEmailSettings).mockReset().mockResolvedValue(undefined)
    vi.mocked(sendTestEmail).mockReset().mockResolvedValue({ success: true, message: 'Mensagem entregue a 1 destinatário(s).' })
  })

  it('exibe o resumo demonstrativo depois da autenticação', async () => {
    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()
    expect(screen.getByText('Modo demonstração')).toBeInTheDocument()
    expect(screen.getByText('Job Fechamento')).toBeInTheDocument()
    expect(screen.getAllByText('97.8%')).toHaveLength(2)
  })

  it('envia o cadastro manual da instalação e seus componentes', async () => {
    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Adicionar instalação' }))
    fireEvent.change(screen.getByLabelText('Nome da instalação'), { target: { value: 'ERP Piloto' } })
    fireEvent.change(screen.getByLabelText('Tags opcionais'), { target: { value: 'piloto, servidor-a' } })
    fireEvent.change(screen.getByLabelText('Nome do componente 1'), { target: { value: 'AppServer REST' } })
    fireEvent.change(screen.getByLabelText('Tipo do componente 1'), { target: { value: 'Rest' } })
    fireEvent.change(screen.getByLabelText('Nome do serviço Windows 1'), { target: { value: 'PulseAppServer' } })
    fireEvent.click(screen.getByRole('button', { name: 'Salvar e monitorar' }))

    await waitFor(() => expect(createInstallation).toHaveBeenCalledWith({
      name: 'ERP Piloto',
      environment: 'Production',
      customEnvironmentName: undefined,
      tags: ['piloto', 'servidor-a'],
      components: [{
        id: undefined,
        name: 'AppServer REST',
        type: 'Rest',
        isRequired: true,
        windowsServiceName: 'PulseAppServer',
        executablePath: undefined,
        iniPath: undefined,
        logPaths: [],
        tcpChecks: [],
        httpChecks: [],
      }],
    }))
    expect(await screen.findByText('Ambientes cadastrados')).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('edita os alvos de uma instalação existente pelo navegador', async () => {
    vi.mocked(getDashboard).mockResolvedValue(realSummary)
    vi.mocked(getInstallationConfiguration).mockResolvedValue({
      id: 'installation-real',
      name: 'ERP Produção',
      environment: 'Production',
      tags: ['matriz'],
      isDemo: false,
      components: [{
        id: 'component-real', name: 'AppServer REST', type: 'Rest', isRequired: true, status: 'Unknown',
        windowsServiceName: undefined, executablePath: undefined, iniPath: undefined,
        logPaths: [], tcpChecks: [], httpChecks: [],
      }],
    })
    vi.mocked(updateInstallation).mockResolvedValue({
      id: 'installation-real', name: 'ERP Produção', environment: 'Production', tags: ['matriz'], isDemo: false,
      components: [{
        id: 'component-real', name: 'AppServer REST', type: 'Rest', isRequired: true, status: 'Unknown',
        windowsServiceName: 'PulseAppServer', executablePath: undefined, iniPath: undefined,
        logPaths: [], tcpChecks: [], httpChecks: [],
      }],
    })

    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Instalações' }))
    fireEvent.click(await screen.findByRole('button', { name: /^Configurar / }))
    expect(await screen.findByRole('heading', { name: 'Configurar instalação' })).toBeInTheDocument()

    fireEvent.change(screen.getByLabelText('Nome do serviço Windows 1'), { target: { value: 'PulseAppServer' } })
    fireEvent.click(screen.getByRole('button', { name: 'Salvar e monitorar' }))

    await waitFor(() => expect(updateInstallation).toHaveBeenCalledWith('installation-real', expect.objectContaining({
      name: 'ERP Produção',
      components: [expect.objectContaining({ id: 'component-real', windowsServiceName: 'PulseAppServer' })],
    })))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('permite descobrir um serviço sem usar PowerShell', async () => {
    vi.mocked(discoverServices).mockResolvedValue({
      supported: true,
      dryRun: true,
      candidates: [{ serviceName: 'Protheus-AppServer', displayName: 'TOTVS AppServer', status: 'Running' }],
    })

    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Adicionar instalação' }))
    fireEvent.change(screen.getByLabelText('Buscar serviço do componente 1'), { target: { value: 'AppServer' } })
    fireEvent.click(screen.getByRole('button', { name: 'Buscar no servidor' }))
    fireEvent.click(await screen.findByRole('button', { name: /TOTVS AppServer/ }))

    expect(discoverServices).toHaveBeenCalledWith('AppServer')
    expect(screen.getByLabelText('Nome do serviço Windows 1')).toHaveValue('Protheus-AppServer')
  })

  it('executa coleta e remove uma instalação pelo painel', async () => {
    vi.mocked(getDashboard).mockResolvedValue(realSummary)
    vi.spyOn(window, 'confirm').mockReturnValue(true)

    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Instalações' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Coletar agora' }))
    await waitFor(() => expect(collectNow).toHaveBeenCalled())
    fireEvent.click(screen.getByRole('button', { name: /^Remover / }))
    await waitFor(() => expect(deleteInstallation).toHaveBeenCalledWith('installation-real'))
  })

  it('permite reconhecer um alerta ativo', async () => {
    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /Alertas/ }))
    const acknowledgeButtons = await screen.findAllByRole('button', { name: 'Reconhecer' })
    fireEvent.click(acknowledgeButtons[0])

    await waitFor(() => expect(acknowledgeAlert).toHaveBeenCalled())
  })

  it('abre a aba Servidor com processador, memória e discos', async () => {
    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Servidor' }))

    expect(await screen.findByText('SRV-PROTHEUS-DEMO')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Processador' })).toBeInTheDocument()
    expect(screen.getByText('38.4%')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Memória' })).toBeInTheDocument()
    expect(screen.getByText('61.2%')).toBeInTheDocument()
    expect(screen.getByText(/C:\\ · Sistema/)).toBeInTheDocument()
    expect(screen.getByText(/E:\\ · Backup/)).toBeInTheDocument()
    expect(getServerResources).toHaveBeenCalled()
  })

  it('marca o disco sem espaço como crítico na aba Servidor', async () => {
    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Servidor' }))

    await screen.findByText('Discos fixos')
    expect(screen.getAllByText('Crítico').length).toBeGreaterThan(0)
    expect(screen.getByText('97.0% usado')).toBeInTheDocument()
  })

  it('salva os dados de envio de e-mail sem reenviar a senha guardada', async () => {
    sessionStorage.setItem('pulse.test.role', 'Administrator')
    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Configurações' }))
    fireEvent.click(await screen.findByRole('button', { name: /Envio de e-mail/ }))
    expect(await screen.findByLabelText('Servidor SMTP')).toHaveValue('smtp.exemplo.com.br')

    fireEvent.change(screen.getByLabelText('Segurança SMTP'), { target: { value: 'SslOnConnect' } })
    expect(screen.getByLabelText('Porta SMTP')).toHaveValue(465)

    fireEvent.click(screen.getByRole('button', { name: /Salvar dados de envio/ }))

    await waitFor(() => expect(saveEmailSettings).toHaveBeenCalledWith(expect.objectContaining({
      host: 'smtp.exemplo.com.br',
      port: 465,
      security: 'SslOnConnect',
      recipients: ['ti@exemplo.com.br'],
      password: undefined,
    })))
  })

  it('envia o e-mail de teste pela aba Configurações', async () => {
    sessionStorage.setItem('pulse.test.role', 'Administrator')
    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Configurações' }))

    fireEvent.click(await screen.findByRole('button', { name: /Envio de e-mail/ }))
    fireEvent.click(await screen.findByRole('button', { name: /Enviar teste/ }))

    await waitFor(() => expect(sendTestEmail).toHaveBeenCalled())
    expect(await screen.findByText(/Teste enviado/)).toBeInTheDocument()
  })

  it('esconde os dados de e-mail de quem não é administrador', async () => {
    sessionStorage.setItem('pulse.test.role', 'Viewer')
    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Configurações' }))

    expect(await screen.findByText('Somente administradores')).toBeInTheDocument()
    expect(screen.queryByLabelText('Servidor SMTP')).not.toBeInTheDocument()
    expect(getEmailSettings).not.toHaveBeenCalled()
  })

  it('bloqueia a ação que corresponde ao estado atual do serviço', () => {
    expect(serviceActionAllowed('Running', 'start')).toBe(false)
    expect(serviceActionAllowed('Running', 'stop')).toBe(true)
    expect(serviceActionAllowed('Running', 'restart')).toBe(true)
    expect(serviceActionAllowed('Stopped', 'start')).toBe(true)
    expect(serviceActionAllowed('Stopped', 'stop')).toBe(false)
    expect(serviceActionAllowed('Stopped', 'restart')).toBe(false)
    expect(serviceActionAllowed('StartPending', 'start')).toBe(false)
    expect(serviceActionAllowed('StartPending', 'stop')).toBe(false)
    expect(serviceActionAllowed(undefined, 'start')).toBe(true)
    expect(serviceActionAllowed('NotFound', 'stop')).toBe(true)
  })

  it('desabilita iniciar quando o serviço já está em execução', async () => {
    sessionStorage.setItem('pulse.test.role', 'Administrator')
    vi.mocked(getDashboard).mockResolvedValue({
      ...realSummary,
      components: [{ ...realSummary.components[0], windowsServiceName: 'PulseAppServer', windowsServiceStatus: 'Running' }],
    })

    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Instalações' }))

    const start = await screen.findByRole('button', { name: 'Iniciar serviço de AppServer REST' })
    expect(start).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Parar serviço de AppServer REST' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Reiniciar serviço de AppServer REST' })).toBeEnabled()
    expect(screen.getByText('Em execução')).toBeInTheDocument()

    fireEvent.click(start)
    expect(executeServiceAction).not.toHaveBeenCalled()
  })

  it('desabilita parar e reiniciar quando o serviço está parado', async () => {
    sessionStorage.setItem('pulse.test.role', 'Administrator')
    vi.mocked(getDashboard).mockResolvedValue({
      ...realSummary,
      components: [{ ...realSummary.components[0], windowsServiceName: 'PulseAppServer', windowsServiceStatus: 'Stopped' }],
    })

    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Instalações' }))

    expect(await screen.findByRole('button', { name: 'Iniciar serviço de AppServer REST' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Parar serviço de AppServer REST' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Reiniciar serviço de AppServer REST' })).toBeDisabled()
  })

  it('sinaliza o auto-start suspenso por uma parada manual', async () => {
    sessionStorage.setItem('pulse.test.role', 'Administrator')
    vi.mocked(getDashboard).mockResolvedValue({
      ...realSummary,
      components: [{
        ...realSummary.components[0],
        windowsServiceName: 'PulseAppServer',
        windowsServiceStatus: 'Stopped',
        windowsServiceAutoStartSuspended: true,
        installationAutoStartEnabled: true,
      }],
    })

    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Instalações' }))

    expect(await screen.findByText(/Parado · auto-start suspenso/)).toBeInTheDocument()
  })

  it('mostra o auto-start pausado depois das falhas consecutivas', async () => {
    sessionStorage.setItem('pulse.test.role', 'Administrator')
    vi.mocked(getDashboard).mockResolvedValue({
      ...realSummary,
      components: [{
        ...realSummary.components[0],
        windowsServiceName: 'PulseAppServer',
        windowsServiceStatus: 'Stopped',
        windowsServiceAutoStartSuspended: true,
        windowsServiceAutoStartFailures: 5,
        installationAutoStartEnabled: true,
      }],
    })

    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Instalações' }))

    expect(await screen.findByText(/auto-start pausado após 5 falhas/)).toBeInTheDocument()
  })

  it('marca a instalação exclusiva e ativa o auto-start', async () => {
    sessionStorage.setItem('pulse.test.role', 'Administrator')
    vi.mocked(getDashboard).mockResolvedValue(realSummary)
    vi.spyOn(window, 'confirm').mockReturnValue(true)

    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Instalações' }))

    fireEvent.click(await screen.findByRole('button', { name: /^Exclusivo$/ }))
    await waitFor(() => expect(setExclusiveInstallation).toHaveBeenCalledWith('installation-real', true))
    expect(await screen.findByText(/agora é a instalação exclusiva/)).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /^Auto-start$/ }))
    await waitFor(() => expect(setAutoStart).toHaveBeenCalledWith('installation-real', true))
    expect(await screen.findByText(/Auto-start ativado/)).toBeInTheDocument()
  })
})
