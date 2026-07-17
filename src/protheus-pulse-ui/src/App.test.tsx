import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { demoSummary } from './demoData'

vi.mock('./api', () => ({
  session: {
    get token() { return sessionStorage.getItem('pulse.test.token') },
    set token(value: string | null) { if (value) sessionStorage.setItem('pulse.test.token', value); else sessionStorage.removeItem('pulse.test.token') },
  },
  getAuthStatus: vi.fn().mockResolvedValue({ requiresSetup: false, demoMode: true }),
  getDashboard: vi.fn().mockResolvedValue(demoSummary),
  login: vi.fn(),
  setup: vi.fn(),
  connectLiveUpdates: vi.fn(() => () => undefined),
}))

import App from './App'

describe('App', () => {
  beforeEach(() => {
    sessionStorage.clear()
    sessionStorage.setItem('pulse.test.token', 'test-token')
  })

  it('exibe o resumo demonstrativo depois da autenticação', async () => {
    render(<App />)
    expect(await screen.findByText('Panorama dos ambientes')).toBeInTheDocument()
    expect(screen.getByText('Modo demonstração')).toBeInTheDocument()
    expect(screen.getByText('Job Fechamento')).toBeInTheDocument()
    expect(screen.getAllByText('97.8%')).toHaveLength(2)
  })
})
