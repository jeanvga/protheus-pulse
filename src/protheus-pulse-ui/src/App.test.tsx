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
  acknowledgeAlert: vi.fn(),
  login: vi.fn(),
  setup: vi.fn(),
  connectLiveUpdates: vi.fn(() => () => undefined),
}))

import { acknowledgeAlert, createInstallation } from './api'
import App from './App'

describe('App', () => {
  afterEach(cleanup)

  beforeEach(() => {
    sessionStorage.clear()
    sessionStorage.setItem('pulse.test.token', 'test-token')
    vi.mocked(createInstallation).mockReset().mockResolvedValue({
      id: 'new-installation',
      name: 'ERP Piloto',
      environment: 'Production',
      tags: ['piloto'],
      componentCount: 1,
      status: 'Unknown',
    })
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
    fireEvent.click(screen.getByRole('button', { name: 'Cadastrar instalação' }))

    await waitFor(() => expect(createInstallation).toHaveBeenCalledWith({
      name: 'ERP Piloto',
      environment: 'Production',
      customEnvironmentName: undefined,
      tags: ['piloto', 'servidor-a'],
      components: [{ name: 'AppServer REST', type: 'Rest', isRequired: true }],
    }))
    expect(await screen.findByText('Ambientes cadastrados')).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
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
