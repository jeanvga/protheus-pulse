import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { demoAlertRules, demoHeartbeats, demoMaintenanceWindows, demoNotificationChannels, demoServerResources, demoSummary } from './demoData'

vi.mock('./api', () => ({
  session: {
    get token() { return sessionStorage.getItem('pulse.test.token') },
    set token(value: string | null) { if (value) sessionStorage.setItem('pulse.test.token', value); else sessionStorage.removeItem('pulse.test.token') },
    get role() { return sessionStorage.getItem('pulse.test.role') },
    set role(value: string | null) { if (value) sessionStorage.setItem('pulse.test.role', value); else sessionStorage.removeItem('pulse.test.role') },
  },
  getAuthStatus: vi.fn().mockResolvedValue({ requiresSetup: false, demoMode: true, version: '1.10.0' }),
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
  refreshSession: vi.fn(),
  setup: vi.fn(),
  connectLiveUpdates: vi.fn(() => () => undefined),
  getServerResources: vi.fn(),
  getEmailSettings: vi.fn(),
  saveEmailSettings: vi.fn(),
  sendTestEmail: vi.fn(),
  getAlertRules: vi.fn(),
  createAlertRule: vi.fn(),
  updateAlertRule: vi.fn(),
  setAlertRuleEnabled: vi.fn(),
  deleteAlertRule: vi.fn(),
  getNotificationChannels: vi.fn(),
  createNotificationChannel: vi.fn(),
  setNotificationChannelEnabled: vi.fn(),
  deleteNotificationChannel: vi.fn(),
  getMaintenanceWindows: vi.fn(),
  createMaintenanceWindow: vi.fn(),
  deleteMaintenanceWindow: vi.fn(),
  getAuditEvents: vi.fn(),
  getDiagnostics: vi.fn(),
  getAlerts: vi.fn(),
  getHeartbeatDefinitions: vi.fn(),
  previewInstallationImport: vi.fn(),
  applyInstallationImport: vi.fn(),
  getBackups: vi.fn(),
  createBackup: vi.fn(),
  downloadBackup: vi.fn(),
  getServerThresholds: vi.fn(),
  saveServerThresholds: vi.fn(),
  createSelfSignedCertificate: vi.fn(),
  createHeartbeatDefinition: vi.fn(),
  rotateHeartbeatToken: vi.fn(),
  deleteHeartbeatDefinition: vi.fn(),
}))

import {
  acknowledgeAlert, collectNow, createAlertRule, createInstallation, createMaintenanceWindow,
  createNotificationChannel, deleteInstallation, discoverPaths,
  discoverServices, executeServiceAction, getAlertRules, getDashboard, getEmailSettings, getInstallationConfiguration,
  applyInstallationImport, createHeartbeatDefinition, getAlerts, previewInstallationImport, getAuditEvents, getDiagnostics, getHeartbeatDefinitions, getMaintenanceWindows, getNotificationChannels, getServerResources, saveEmailSettings, sendTestEmail, setAlertRuleEnabled,
  setAutoStart, setExclusiveInstallation, updateInstallation,
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
    vi.mocked(getAlertRules).mockReset().mockResolvedValue(demoAlertRules)
    vi.mocked(createAlertRule).mockReset().mockResolvedValue(undefined)
    vi.mocked(setAlertRuleEnabled).mockReset().mockResolvedValue(undefined)
    vi.mocked(getNotificationChannels).mockReset().mockResolvedValue(demoNotificationChannels)
    vi.mocked(createNotificationChannel).mockReset().mockResolvedValue(undefined)
    vi.mocked(getMaintenanceWindows).mockReset().mockResolvedValue(demoMaintenanceWindows)
    vi.mocked(createMaintenanceWindow).mockReset().mockResolvedValue(undefined)
    vi.mocked(getAuditEvents).mockReset().mockResolvedValue({
      total: 2,
      byAction: { ServiceActionExecuted: 1, LoginSucceeded: 1 },
      items: [
        {
          id: 'audit-1', action: 'ServiceActionExecuted', entityType: 'Component', entityId: 'c1f2a3b4-0000-0000-0000-000000000000',
          occurredAt: new Date('2026-09-02T12:00:00Z').toISOString(), remoteAddress: '192.168.0.10',
          details: '{"action":"restart","serviceName":"AppServerProd"}', userDisplayName: 'Jean Mendes', username: 'jean',
        },
        {
          id: 'audit-2', action: 'LoginSucceeded', entityType: 'User', entityId: null,
          occurredAt: new Date('2026-09-02T11:00:00Z').toISOString(), remoteAddress: '127.0.0.1',
          details: null, userDisplayName: 'Jean Mendes', username: 'jean',
        },
      ],
    })
    vi.mocked(getHeartbeatDefinitions).mockReset().mockResolvedValue(demoHeartbeats)
    vi.mocked(previewInstallationImport).mockReset().mockResolvedValue({
      valid: true, schemaVersion: 1, installationCount: 2, componentCount: 5, errors: [], warnings: ['Componente sem alvo configurado.'],
    })
    vi.mocked(applyInstallationImport).mockReset().mockResolvedValue({
      valid: true, schemaVersion: 1, installationCount: 2, componentCount: 5, errors: [], warnings: [],
    })
    vi.mocked(createHeartbeatDefinition).mockReset().mockResolvedValue({
      id: 'hb-novo', jobKey: 'carga-noturna', token: 'hbt_exemplo_para_teste',
      tokenShownOnce: true, warning: 'Armazene o token agora; ele não poderá ser consultado novamente.',
    })
    vi.mocked(getAlerts).mockReset().mockImplementation(async ({ state } = {}) => {
      const items = state && state !== 'all' ? demoSummary.alerts.filter(item => item.state === state) : demoSummary.alerts
      return { total: items.length, byState: { Active: 2, Acknowledged: 1, Resolved: 1 }, items }
    })
    vi.mocked(getDiagnostics).mockReset().mockResolvedValue({
      service: 'Protheus Pulse', status: 'Healthy', database: 'SQLite', demoMode: true,
      platform: 'Win32NT', version: '1.10.0', notes: ['A coleta é somente leitura.'],
    })
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

  it('busca as ocorrências no servidor em vez das oito do resumo', async () => {
    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /Alertas/ }))

    // O contador vem do byState do servidor, que conta o histórico inteiro.
    await waitFor(() => expect(getAlerts).toHaveBeenCalledWith(expect.objectContaining({ state: 'Active', take: 50, skip: 0 })))
    expect(await screen.findByRole('button', { name: /Resolvidos 1/ })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /Resolvidos 1/ }))
    await waitFor(() => expect(getAlerts).toHaveBeenCalledWith(expect.objectContaining({ state: 'Resolved' })))

    fireEvent.change(screen.getByLabelText('Filtrar período das ocorrências'), { target: { value: 'all' } })
    await waitFor(() => expect(getAlerts).toHaveBeenCalledWith(expect.objectContaining({ from: undefined })))
  })

  it('lista as regras agrupadas por instalação e liga o interruptor de uma delas', async () => {
    sessionStorage.setItem('pulse.test.role', 'Administrator')
    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /Alertas/ }))
    fireEvent.click(await screen.findByRole('button', { name: 'Regras de alerta' }))

    expect(await screen.findByText('Heartbeat atrasado')).toBeInTheDocument()
    expect(screen.getByText('Certificado próximo do vencimento')).toBeInTheDocument()
    expect(screen.getAllByText('Padrão')).toHaveLength(3)
    expect(screen.getByText(/Heartbeat de job · abre em atenção ou crítico depois de 2 coletas seguidas/)).toBeInTheDocument()

    fireEvent.click(screen.getAllByRole('button', { name: 'Ativa' })[0])
    await waitFor(() => expect(setAlertRuleEnabled).toHaveBeenCalledWith('rule-heartbeat', false))
  })

  it('cria uma regra de alerta pelo formulário em etapas', async () => {
    sessionStorage.setItem('pulse.test.role', 'Administrator')
    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /Alertas/ }))
    fireEvent.click(await screen.findByRole('button', { name: 'Regras de alerta' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Nova regra' }))

    fireEvent.change(screen.getByLabelText('Nome da regra'), { target: { value: 'Broker fora do ar' } })
    fireEvent.change(screen.getByLabelText('Componente'), { target: { value: 'broker' } })
    fireEvent.change(screen.getByLabelText('Verificação'), { target: { value: 'Tcp' } })
    fireEvent.change(screen.getByLabelText('Falhas consecutivas'), { target: { value: '3' } })
    fireEvent.change(screen.getByLabelText('Cooldown'), { target: { value: '900' } })
    fireEvent.click(screen.getByRole('checkbox', { name: /Desconhecido/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Salvar regra' }))

    await waitFor(() => expect(createAlertRule).toHaveBeenCalledWith({
      componentId: 'broker',
      name: 'Broker fora do ar',
      probeType: 'Tcp',
      severity: 'Critical',
      minimumConsecutiveFailures: 3,
      cooldownSeconds: 900,
      triggerStatuses: ['Warning', 'Critical', 'Unknown'],
      thresholdPercent: null,
    }))
  })

  it('cria uma regra de servidor com limite de uso em percentual', async () => {
    sessionStorage.setItem('pulse.test.role', 'Administrator')
    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /Alertas/ }))
    fireEvent.click(await screen.findByRole('button', { name: 'Regras de alerta' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Nova regra' }))

    fireEvent.change(screen.getByLabelText('Componente'), { target: { value: 'server' } })
    // O alvo da máquina só aceita as verificações de servidor: as de componente somem da lista.
    const verifications = screen.getByLabelText('Verificação') as HTMLSelectElement
    expect([...verifications.options].map(option => option.value)).toEqual(['ServerCpu', 'ServerMemory', 'ServerDisk'])
    expect(screen.queryByRole('checkbox', { name: /Desconhecido/ })).not.toBeInTheDocument()

    fireEvent.change(screen.getByLabelText('Nome da regra'), { target: { value: 'Memória acima de 90%' } })
    fireEvent.change(verifications, { target: { value: 'ServerMemory' } })
    fireEvent.change(screen.getByLabelText('Limite de uso'), { target: { value: '90' } })
    fireEvent.change(screen.getByLabelText('Falhas consecutivas'), { target: { value: '3' } })
    fireEvent.change(screen.getByLabelText('Severidade'), { target: { value: 'Warning' } })
    fireEvent.click(screen.getByRole('button', { name: 'Salvar regra' }))

    await waitFor(() => expect(createAlertRule).toHaveBeenCalledWith({
      componentId: 'server',
      name: 'Memória acima de 90%',
      probeType: 'ServerMemory',
      severity: 'Warning',
      minimumConsecutiveFailures: 3,
      cooldownSeconds: 300,
      triggerStatuses: ['Warning', 'Critical'],
      thresholdPercent: 90,
    }))
  })

  it('mantém o alvo do servidor fora da tabela de componentes e das instalações', async () => {
    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()
    expect(screen.queryByText('SRV-PROTHEUS-DEMO')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Instalações' }))
    expect(await screen.findByText('ERP Produção · DEMO')).toBeInTheDocument()
    expect(screen.queryByText('Servidor local')).not.toBeInTheDocument()
  })

  it('cadastra um ponto de contato na aba de alertas', async () => {
    sessionStorage.setItem('pulse.test.role', 'Administrator')
    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /Alertas/ }))
    fireEvent.click(await screen.findByRole('button', { name: 'Pontos de contato' }))

    expect(await screen.findByText('Plantão de infraestrutura')).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Nome do ponto de contato'), { target: { value: 'Central NOC' } })
    fireEvent.change(screen.getByLabelText('Tipo do ponto de contato'), { target: { value: 'Slack' } })
    fireEvent.change(screen.getByLabelText('URL do ponto de contato'), { target: { value: 'https://hooks.exemplo.invalid/pulse' } })
    fireEvent.click(screen.getByRole('button', { name: 'Adicionar ponto de contato' }))

    await waitFor(() => expect(createNotificationChannel).toHaveBeenCalledWith({
      name: 'Central NOC',
      type: 'Slack',
      url: 'https://hooks.exemplo.invalid/pulse',
      enabled: true,
    }))
  })

  it('abre um silenciamento com início e duração', async () => {
    sessionStorage.setItem('pulse.test.role', 'Administrator')
    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /Alertas/ }))
    fireEvent.click(await screen.findByRole('button', { name: 'Silenciamentos' }))

    expect(await screen.findByText('Virada de mês')).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Alvo do silenciamento'), { target: { value: 'component:worker' } })
    fireEvent.change(screen.getByLabelText('Nome do silenciamento'), { target: { value: 'Atualização do worker' } })
    fireEvent.change(screen.getByLabelText('Início do silenciamento'), { target: { value: '2026-09-10T22:00' } })
    fireEvent.change(screen.getByLabelText('Duração do silenciamento'), { target: { value: '360' } })
    fireEvent.click(screen.getByRole('button', { name: 'Criar silenciamento' }))

    await waitFor(() => expect(createMaintenanceWindow).toHaveBeenCalledWith({
      installationId: undefined,
      componentId: 'worker',
      name: 'Atualização do worker',
      startsAt: new Date('2026-09-10T22:00').toISOString(),
      endsAt: new Date(new Date('2026-09-10T22:00').getTime() + 360 * 60_000).toISOString(),
      reason: undefined,
    }))
  })

  it('mostra na auditoria os eventos que o servidor devolve', async () => {
    sessionStorage.setItem('pulse.test.role', 'Administrator')
    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Auditoria' }))

    expect(await screen.findByText('Executou ação em serviço Windows', { selector: 'strong' })).toBeInTheDocument()
    expect(screen.getByText('Entrou no painel', { selector: 'strong' })).toBeInTheDocument()
    expect(screen.getAllByText(/Jean Mendes \(jean\)/)).toHaveLength(2)
    expect(screen.getByText(/action: restart · serviceName: AppServerProd/)).toBeInTheDocument()
    expect(getAuditEvents).toHaveBeenCalled()
  })

  it('esconde a auditoria de quem não é administrador', async () => {
    sessionStorage.setItem('pulse.test.role', 'Operator')
    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Auditoria' }))

    expect(await screen.findByText('Somente administradores')).toBeInTheDocument()
    expect(getAuditEvents).not.toHaveBeenCalled()
  })

  it('mostra o diagnóstico como crítico quando o serviço não responde', async () => {
    sessionStorage.setItem('pulse.test.role', 'Administrator')
    vi.mocked(getDiagnostics).mockRejectedValue(new Error('A API retornou 503.'))
    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Diagnóstico' }))

    // A tela antiga afirmava "Healthy" mesmo com o serviço fora.
    expect(await screen.findByText('A API retornou 503.')).toBeInTheDocument()
    expect(screen.getByText('A API não respondeu; o painel está sem contato com o serviço.')).toBeInTheDocument()
    expect(screen.getAllByText('Crítico').length).toBeGreaterThan(0)
  })

  it('mostra no rodapé a versão informada pelo servidor', async () => {
    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()
    expect(screen.getByText(/Protheus Pulse 1\.10\.0 · produto independente/)).toBeInTheDocument()
  })

  it('mostra a tolerância cadastrada de cada heartbeat, não um valor fixo', async () => {
    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Jobs' }))

    expect(await screen.findByText('Fechamento contábil')).toBeInTheDocument()
    // A tela antiga escrevia "5 min" para todo job; estes são 10 min e 2 min.
    expect(screen.getByText('10 min')).toBeInTheDocument()
    expect(screen.getByText('2 min')).toBeInTheDocument()
    expect(screen.getByText('fila-integracao')).toBeInTheDocument()
    expect(getHeartbeatDefinitions).toHaveBeenCalled()
  })

  it('cadastra um heartbeat e mostra o token uma única vez', async () => {
    sessionStorage.setItem('pulse.test.role', 'Administrator')
    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Jobs' }))
    expect(await screen.findByText('Fechamento contábil')).toBeInTheDocument()

    fireEvent.change(screen.getByLabelText('Componente do heartbeat'), { target: { value: 'job' } })
    fireEvent.change(screen.getByLabelText('Nome do heartbeat'), { target: { value: 'Carga noturna' } })
    fireEvent.change(screen.getByLabelText('Chave pública do job'), { target: { value: 'carga-noturna' } })
    fireEvent.change(screen.getByLabelText('Intervalo esperado'), { target: { value: '7200' } })
    fireEvent.change(screen.getByLabelText('Tolerância'), { target: { value: '900' } })
    fireEvent.click(screen.getByRole('button', { name: 'Cadastrar heartbeat' }))

    await waitFor(() => expect(createHeartbeatDefinition).toHaveBeenCalledWith({
      componentId: 'job',
      name: 'Carga noturna',
      jobKey: 'carga-noturna',
      expectedIntervalSeconds: 7200,
      toleranceSeconds: 900,
    }))
    expect(await screen.findByText('hbt_exemplo_para_teste')).toBeInTheDocument()
    expect(screen.getByText(/não poderá ser consultado novamente/)).toBeInTheDocument()
  })

  it('confere o arquivo antes de deixar importar instalações', async () => {
    sessionStorage.setItem('pulse.test.role', 'Administrator')
    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Instalações' }))
    fireEvent.click(await screen.findByRole('button', { name: /Importar arquivo/ }))

    const importButton = screen.getByRole('button', { name: 'Importar' })
    // Sem conferir, não dá para aplicar: metade gravada seria pior que não importar.
    expect(importButton).toBeDisabled()

    fireEvent.change(screen.getByLabelText('Conteúdo do arquivo'), { target: { value: 'schemaVersion: 1' } })
    fireEvent.click(screen.getByRole('button', { name: 'Conferir arquivo' }))

    await waitFor(() => expect(previewInstallationImport).toHaveBeenCalledWith('yaml', 'schemaVersion: 1'))
    expect(await screen.findByText('2 instalação(ões) e 5 componente(s) prontos para importar.')).toBeInTheDocument()
    expect(screen.getByText('Componente sem alvo configurado.')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Importar' }))
    await waitFor(() => expect(applyInstallationImport).toHaveBeenCalledWith('yaml', 'schemaVersion: 1'))
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
