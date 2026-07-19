import * as signalR from '@microsoft/signalr'
import { demoSummary } from './demoData'
import type {
  AuthStatus, AuthToken, CollectionResult, DashboardSummary, InstallationConfiguration,
  InstallationCreated, LogEventItem, MaintenanceChangeResult, MaintenanceStatus,
  PathDiscoveryResult, SaveInstallationInput, ServiceAction, ServiceActionResponse,
  ServiceDiscoveryResult,
} from './types'

const staticDemo = import.meta.env.VITE_DEMO_STATIC === 'true'
const tokenKey = 'pulse.accessToken'
const roleKey = 'pulse.role'

export const session = {
  get token() { return sessionStorage.getItem(tokenKey) },
  set token(value: string | null) {
    if (value) sessionStorage.setItem(tokenKey, value)
    else sessionStorage.removeItem(tokenKey)
  },
  get role() { return sessionStorage.getItem(roleKey) },
  set role(value: string | null) {
    if (value) sessionStorage.setItem(roleKey, value)
    else sessionStorage.removeItem(roleKey)
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

export async function createInstallation(input: SaveInstallationInput): Promise<InstallationCreated> {
  if (staticDemo) throw new Error('O cadastro persistente não está disponível na demonstração estática.')
  return request<InstallationCreated>('/api/v1/installations', { method: 'POST', body: JSON.stringify(input) })
}

export async function getInstallationConfiguration(id: string): Promise<InstallationConfiguration> {
  return request<InstallationConfiguration>(`/api/v1/installations/${id}/configuration`)
}

export async function updateInstallation(id: string, input: SaveInstallationInput): Promise<InstallationConfiguration> {
  return request<InstallationConfiguration>(`/api/v1/installations/${id}`, { method: 'PUT', body: JSON.stringify(input) })
}

export async function deleteInstallation(id: string): Promise<void> {
  await request<void>(`/api/v1/installations/${id}`, { method: 'DELETE' })
}

export async function discoverServices(nameContains: string): Promise<ServiceDiscoveryResult> {
  const query = new URLSearchParams({ nameContains, limit: '100' })
  return request<ServiceDiscoveryResult>(`/api/v1/discovery/services?${query}`)
}

export async function discoverPaths(root: string, fileNames: string[]): Promise<PathDiscoveryResult> {
  return request<PathDiscoveryResult>('/api/v1/discovery/paths', {
    method: 'POST',
    body: JSON.stringify({ roots: [root], fileNames, maxDepth: 6, maxResults: 100, timeoutSeconds: 15 }),
  })
}

export async function collectNow(): Promise<CollectionResult> {
  return request<CollectionResult>('/api/v1/diagnostics/collect-now', { method: 'POST', body: '{}' })
}

export async function getLogEvents(): Promise<LogEventItem[]> {
  if (staticDemo) return []
  return request<LogEventItem[]>('/api/v1/log-events')
}

export async function executeServiceAction(componentId: string, action: ServiceAction): Promise<ServiceActionResponse> {
  if (staticDemo) throw new Error('Ações de serviço não estão disponíveis na demonstração estática.')
  return request<ServiceActionResponse>(`/api/v1/components/${componentId}/service/${action}`, { method: 'POST', body: '{}' })
}

export async function getMaintenanceStatus(): Promise<MaintenanceStatus> {
  if (staticDemo) return { active: false }
  return request<MaintenanceStatus>('/api/v1/maintenance/status')
}

export async function enterMaintenance(): Promise<MaintenanceChangeResult> {
  if (staticDemo) throw new Error('O modo manutenção não está disponível na demonstração estática.')
  return request<MaintenanceChangeResult>('/api/v1/maintenance/enter', { method: 'POST', body: '{}' })
}

export async function exitMaintenance(): Promise<MaintenanceChangeResult> {
  if (staticDemo) throw new Error('O modo manutenção não está disponível na demonstração estática.')
  return request<MaintenanceChangeResult>('/api/v1/maintenance/exit', { method: 'POST', body: '{}' })
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
