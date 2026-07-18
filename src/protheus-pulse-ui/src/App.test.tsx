import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { demoSummary } from './demoData'

vi.mock('./api', () => ({
  session: {
    get token() { return sessionStorage.getItem('pulse.test.token') },
    set token(value: string | null) { if (value) sessionStorage.setItem('pulse.test.token', value); else sessionStorage.removeItem('pulse.test.token') },
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
  login: vi.fn(),
  setup: vi.fn(),
  connectLiveUpdates: vi.fn(() => () => undefined),
}))

import {
  acknowledgeAlert, collectNow, createInstallation, deleteInstallation, discoverPaths,
  discoverServices, getDashboard, getInstallationConfiguration, updateInstallation,
} from './api'
import App from './App'

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
    fireEvent.click(await screen.findByRole('button', { name: 'Configurar' }))
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
    fireEvent.click(screen.getByRole('button', { name: 'Remover' }))
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
})
