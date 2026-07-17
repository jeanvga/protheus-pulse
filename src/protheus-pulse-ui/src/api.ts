import * as signalR from '@microsoft/signalr'
import { demoSummary } from './demoData'
import type { AuthStatus, AuthToken, CreateInstallationInput, DashboardSummary, InstallationCreated } from './types'

const staticDemo = import.meta.env.VITE_DEMO_STATIC === 'true'
const tokenKey = 'pulse.accessToken'

export const session = {
  get token() { return sessionStorage.getItem(tokenKey) },
  set token(value: string | null) {
    if (value) sessionStorage.setItem(tokenKey, value)
    else sessionStorage.removeItem(tokenKey)
  },
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const headers = new Headers(init?.headers)
  headers.set('Content-Type', 'application/json')
  if (session.token) headers.set('Authorization', `Bearer ${session.token}`)
  const response = await fetch(path, { ...init, headers })
  if (response.status === 401) session.token = null
  if (!response.ok) {
    const payload = await response.json().catch(() => ({ message: 'Falha inesperada na comunicação.' })) as {
      message?: string
      errors?: Record<string, string[]>
    }
    const validationMessage = Object.values(payload.errors ?? {}).flat()[0]
    throw new Error(payload.message ?? validationMessage ?? `A API retornou ${response.status}.`)
  }
  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

export async function getAuthStatus(): Promise<AuthStatus> {
  if (staticDemo) return { requiresSetup: false, demoMode: true, demoUsername: 'demo.admin', demoPassword: 'PulseDemo!2026' }
  return request<AuthStatus>('/api/v1/auth/status')
}

export async function login(username: string, password: string): Promise<AuthToken> {
  if (staticDemo) return { accessToken: 'static-demo', expiresAt: new Date(Date.now() + 3600000).toISOString(), username, displayName: 'Administrador da demonstração', role: 'Administrator' }
  return request<AuthToken>('/api/v1/auth/login', { method: 'POST', body: JSON.stringify({ username, password }) })
}

export async function setup(username: string, displayName: string, password: string): Promise<AuthToken> {
  return request<AuthToken>('/api/v1/auth/setup', { method: 'POST', body: JSON.stringify({ username, displayName, password }) })
}

export async function getDashboard(): Promise<DashboardSummary> {
  if (staticDemo) return demoSummary
  return request<DashboardSummary>('/api/v1/dashboard/summary')
}

export async function createInstallation(input: CreateInstallationInput): Promise<InstallationCreated> {
  if (staticDemo) throw new Error('O cadastro persistente não está disponível na demonstração estática.')
  return request<InstallationCreated>('/api/v1/installations', { method: 'POST', body: JSON.stringify(input) })
}

export async function acknowledgeAlert(id: string): Promise<void> {
  if (staticDemo) throw new Error('O reconhecimento não está disponível na demonstração estática.')
  await request<void>(`/api/v1/alerts/${id}/acknowledge`, { method: 'POST', body: '{}' })
}

export function connectLiveUpdates(onUpdate: () => void): () => void {
  if (staticDemo || !session.token) return () => undefined
  const connection = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/pulse', { accessTokenFactory: () => session.token ?? '' })
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Warning)
    .build()
  connection.on('dashboardUpdated', onUpdate)
  void connection.start().catch(() => undefined)
  return () => { void connection.stop() }
}
