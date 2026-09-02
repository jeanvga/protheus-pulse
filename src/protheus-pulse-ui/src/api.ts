import * as signalR from '@microsoft/signalr'
import { demoAlertRules, demoHeartbeats, demoMaintenanceWindows, demoNotificationChannels, demoServerResources, demoSummary } from './demoData'
import type {
  AlertOccurrencePage, AlertQuery, AlertRule, BackupFile, AuditEventPage, AuditQuery, AuthStatus, AuthToken, AutomationFlag, CollectionResult, CreateAlertRuleInput, CreateMaintenanceWindowInput,
  CreateHeartbeatInput, DiagnosticsInfo, HeartbeatDefinition, HeartbeatToken, ServerThresholdSettings,
  CreateNotificationChannelInput, DashboardSummary, EmailSettings, EmailTestResult,
  InstallationConfiguration, InstallationCreated, LogEventItem, LogEventPage, LogEventQuery, MaintenanceChangeResult, MaintenanceStatus, MaintenanceWindow, BrowseResult, ComponentProposal, ComponentProposalResult, NetworkSettings, NotificationChannel, SaveNetworkInput, SelfSignedCertificate, PulseUser, RetentionSettings, SaveRetentionRequest, SaveUserRequest,
  PathDiscoveryResult, SaveEmailSettingsInput, SaveInstallationInput, ServerResources, ServiceAction,
  ServiceActionResponse, ServiceDiscoveryResult, UpdateAlertRuleInput,
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

export async function getUsers(): Promise<PulseUser[]> {
  if (staticDemo) return []
  return request<PulseUser[]>('/api/v1/users')
}

export async function createUser(payload: SaveUserRequest): Promise<void> {
  await request('/api/v1/users', { method: 'POST', body: JSON.stringify(payload) })
}

export async function updateUser(id: string, payload: SaveUserRequest): Promise<void> {
  await request(`/api/v1/users/${id}`, { method: 'PUT', body: JSON.stringify(payload) })
}

export async function resetUserPassword(id: string, password: string): Promise<void> {
  await request(`/api/v1/users/${id}/password`, { method: 'POST', body: JSON.stringify({ password }) })
}

export async function deleteUser(id: string): Promise<void> {
  await request(`/api/v1/users/${id}`, { method: 'DELETE' })
}

export async function getNetworkSettings(): Promise<NetworkSettings> {
  if (staticDemo) return { allowRemoteAccess: false, port: 5058, boundUrl: 'http://127.0.0.1:5058', localAddresses: [], useHttps: false, hasCertificatePassword: false }
  return request<NetworkSettings>('/api/v1/settings/network')
}

export async function saveNetworkSettings(payload: SaveNetworkInput): Promise<NetworkSettings> {
  return request<NetworkSettings>('/api/v1/settings/network', { method: 'PUT', body: JSON.stringify(payload) })
}

export async function getBackups(): Promise<BackupFile[]> {
  if (staticDemo) return []
  return request<BackupFile[]>('/api/v1/settings/backups')
}

export async function createBackup(): Promise<BackupFile> {
  if (staticDemo) throw new Error('O backup não pode ser gerado na demonstração estática.')
  return request<BackupFile>('/api/v1/settings/backups', { method: 'POST', body: '{}' })
}

/// O download passa pelo mesmo token da sessão, então não dá para usar um link direto.
export async function downloadBackup(name: string): Promise<void> {
  const response = await fetch(`/api/v1/settings/backups/${encodeURIComponent(name)}`, {
    headers: session.token ? { Authorization: `Bearer ${session.token}` } : undefined,
  })
  if (!response.ok) throw new Error('Não foi possível baixar o backup.')
  const url = URL.createObjectURL(await response.blob())
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = name
  anchor.click()
  URL.revokeObjectURL(url)
}

export async function createSelfSignedCertificate(): Promise<SelfSignedCertificate> {
  if (staticDemo) throw new Error('O certificado não pode ser gerado na demonstração estática.')
  return request<SelfSignedCertificate>('/api/v1/settings/network/self-signed', { method: 'POST', body: '{}' })
}

export async function browseFolders(path?: string): Promise<BrowseResult> {
  const suffix = path ? `?path=${encodeURIComponent(path)}` : ''
  return request<BrowseResult>(`/api/v1/discovery/browse${suffix}`)
}

export async function proposeComponent(root: string): Promise<ComponentProposalResult> {
  return request<ComponentProposalResult>('/api/v1/discovery/component', { method: 'POST', body: JSON.stringify({ root }) })
}

export async function getRetentionSettings(): Promise<RetentionSettings> {
  if (staticDemo) return { historyRetentionDays: 30, metricAggregationAfterDays: 7, updatedAt: null, counts: { probeResults: 0, logEvents: 0, metricSamples: 0 } }
  return request<RetentionSettings>('/api/v1/settings/retention')
}

export async function saveRetentionSettings(payload: SaveRetentionRequest): Promise<void> {
  await request('/api/v1/settings/retention', { method: 'PUT', body: JSON.stringify(payload) })
}

export async function getLogEvents(query: LogEventQuery = {}): Promise<LogEventPage> {
  if (staticDemo) return { total: 0, byLevel: {}, items: [] }
  const parameters = new URLSearchParams()
  if (query.search?.trim()) parameters.set('search', query.search.trim())
  if (query.level && query.level !== 'all') parameters.set('level', query.level)
  if (query.componentId) parameters.set('componentId', query.componentId)
  if (query.from) parameters.set('from', query.from)
  if (query.take !== undefined) parameters.set('take', String(query.take))
  if (query.skip) parameters.set('skip', String(query.skip))
  const suffix = parameters.toString()
  return request<LogEventPage>(`/api/v1/log-events${suffix ? `?${suffix}` : ''}`)
}

export async function executeServiceAction(componentId: string, action: ServiceAction): Promise<ServiceActionResponse> {
  if (staticDemo) throw new Error('Ações de serviço não estão disponíveis na demonstração estática.')
  return request<ServiceActionResponse>(`/api/v1/components/${componentId}/service/${action}`, { method: 'POST', body: '{}' })
}

export async function setExclusiveInstallation(installationId: string, enabled: boolean): Promise<AutomationFlag> {
  if (staticDemo) throw new Error('A instalação exclusiva não pode ser definida na demonstração estática.')
  return request<AutomationFlag>(`/api/v1/installations/${installationId}/exclusive`, { method: 'POST', body: JSON.stringify({ enabled }) })
}

export async function setAutoStart(installationId: string, enabled: boolean): Promise<AutomationFlag> {
  if (staticDemo) throw new Error('O auto-start não pode ser alterado na demonstração estática.')
  return request<AutomationFlag>(`/api/v1/installations/${installationId}/auto-start`, { method: 'POST', body: JSON.stringify({ enabled }) })
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

export async function getAlertRules(): Promise<AlertRule[]> {
  if (staticDemo) return demoAlertRules
  return request<AlertRule[]>('/api/v1/alert-rules')
}

export async function createAlertRule(input: CreateAlertRuleInput): Promise<void> {
  if (staticDemo) throw new Error('As regras de alerta não podem ser criadas na demonstração estática.')
  await request<void>('/api/v1/alert-rules', { method: 'POST', body: JSON.stringify(input) })
}

export async function updateAlertRule(id: string, input: UpdateAlertRuleInput): Promise<void> {
  if (staticDemo) throw new Error('As regras de alerta não podem ser editadas na demonstração estática.')
  await request<void>(`/api/v1/alert-rules/${id}`, { method: 'PUT', body: JSON.stringify(input) })
}

export async function setAlertRuleEnabled(id: string, enabled: boolean): Promise<void> {
  if (staticDemo) throw new Error('As regras de alerta não podem ser alteradas na demonstração estática.')
  await request<void>(`/api/v1/alert-rules/${id}/enabled`, { method: 'PUT', body: JSON.stringify({ enabled }) })
}

export async function deleteAlertRule(id: string): Promise<void> {
  if (staticDemo) throw new Error('As regras de alerta não podem ser removidas na demonstração estática.')
  await request<void>(`/api/v1/alert-rules/${id}`, { method: 'DELETE' })
}

export async function getNotificationChannels(): Promise<NotificationChannel[]> {
  if (staticDemo) return demoNotificationChannels
  return request<NotificationChannel[]>('/api/v1/notification-channels')
}

export async function createNotificationChannel(input: CreateNotificationChannelInput): Promise<void> {
  if (staticDemo) throw new Error('Os pontos de contato não podem ser criados na demonstração estática.')
  await request<void>('/api/v1/notification-channels', { method: 'POST', body: JSON.stringify(input) })
}

export async function setNotificationChannelEnabled(id: string, enabled: boolean): Promise<void> {
  if (staticDemo) throw new Error('Os pontos de contato não podem ser alterados na demonstração estática.')
  await request<void>(`/api/v1/notification-channels/${id}/enabled`, { method: 'PUT', body: JSON.stringify({ enabled }) })
}

export async function deleteNotificationChannel(id: string): Promise<void> {
  if (staticDemo) throw new Error('Os pontos de contato não podem ser removidos na demonstração estática.')
  await request<void>(`/api/v1/notification-channels/${id}`, { method: 'DELETE' })
}

export async function getMaintenanceWindows(): Promise<MaintenanceWindow[]> {
  if (staticDemo) return demoMaintenanceWindows
  return request<MaintenanceWindow[]>('/api/v1/maintenance-windows')
}

export async function createMaintenanceWindow(input: CreateMaintenanceWindowInput): Promise<void> {
  if (staticDemo) throw new Error('Os silenciamentos não podem ser criados na demonstração estática.')
  await request<void>('/api/v1/maintenance-windows', { method: 'POST', body: JSON.stringify(input) })
}

export async function deleteMaintenanceWindow(id: string): Promise<void> {
  if (staticDemo) throw new Error('Os silenciamentos não podem ser removidos na demonstração estática.')
  await request<void>(`/api/v1/maintenance-windows/${id}`, { method: 'DELETE' })
}

export async function getAlerts(query: AlertQuery = {}): Promise<AlertOccurrencePage> {
  if (staticDemo) {
    const items = demoSummary.alerts
    const byState = items.reduce<Record<string, number>>((counts, item) => ({ ...counts, [item.state]: (counts[item.state] ?? 0) + 1 }), {})
    const filtered = !query.state || query.state === 'all' ? items : items.filter(item => item.state === query.state)
    return { total: filtered.length, byState, items: filtered }
  }

  const parameters = new URLSearchParams()
  if (query.state && query.state !== 'all') parameters.set('state', query.state)
  if (query.componentId) parameters.set('componentId', query.componentId)
  if (query.from) parameters.set('from', query.from)
  if (query.take !== undefined) parameters.set('take', String(query.take))
  if (query.skip) parameters.set('skip', String(query.skip))
  const suffix = parameters.toString()
  return request<AlertOccurrencePage>(`/api/v1/alerts${suffix ? `?${suffix}` : ''}`)
}

export async function getServerThresholds(): Promise<ServerThresholdSettings> {
  if (staticDemo) return { cpuWarningPercent: 80, cpuCriticalPercent: 92, memoryWarningPercent: 85, memoryCriticalPercent: 94, diskFreeWarningPercent: 15, diskFreeCriticalPercent: 5, updatedAt: null }
  return request<ServerThresholdSettings>('/api/v1/settings/server-thresholds')
}

export async function saveServerThresholds(input: Omit<ServerThresholdSettings, 'updatedAt'>): Promise<ServerThresholdSettings> {
  if (staticDemo) throw new Error('Os limites não podem ser salvos na demonstração estática.')
  return request<ServerThresholdSettings>('/api/v1/settings/server-thresholds', { method: 'PUT', body: JSON.stringify(input) })
}

export async function getHeartbeatDefinitions(): Promise<HeartbeatDefinition[]> {
  if (staticDemo) return demoHeartbeats
  return request<HeartbeatDefinition[]>('/api/v1/heartbeat-definitions')
}

export async function createHeartbeatDefinition(input: CreateHeartbeatInput): Promise<HeartbeatToken> {
  if (staticDemo) throw new Error('Os heartbeats não podem ser criados na demonstração estática.')
  return request<HeartbeatToken>('/api/v1/heartbeat-definitions', { method: 'POST', body: JSON.stringify(input) })
}

export async function rotateHeartbeatToken(id: string): Promise<HeartbeatToken> {
  if (staticDemo) throw new Error('O token não pode ser rotacionado na demonstração estática.')
  return request<HeartbeatToken>(`/api/v1/heartbeat-definitions/${id}/rotate`, { method: 'POST', body: '{}' })
}

export async function deleteHeartbeatDefinition(id: string): Promise<void> {
  if (staticDemo) throw new Error('Os heartbeats não podem ser removidos na demonstração estática.')
  await request<void>(`/api/v1/heartbeat-definitions/${id}`, { method: 'DELETE' })
}

export async function getAuditEvents(query: AuditQuery = {}): Promise<AuditEventPage> {
  if (staticDemo) return { total: 0, byAction: {}, items: [] }
  const parameters = new URLSearchParams()
  if (query.search?.trim()) parameters.set('search', query.search.trim())
  if (query.action && query.action !== 'all') parameters.set('action', query.action)
  if (query.from) parameters.set('from', query.from)
  if (query.take !== undefined) parameters.set('take', String(query.take))
  if (query.skip) parameters.set('skip', String(query.skip))
  const suffix = parameters.toString()
  return request<AuditEventPage>(`/api/v1/audit${suffix ? `?${suffix}` : ''}`)
}

export async function getDiagnostics(): Promise<DiagnosticsInfo> {
  if (staticDemo) throw new Error('O diagnóstico não está disponível na demonstração estática.')
  return request<DiagnosticsInfo>('/api/v1/diagnostics')
}

export async function getServerResources(): Promise<ServerResources> {
  if (staticDemo) return demoServerResources
  return request<ServerResources>('/api/v1/server/resources')
}

export async function getEmailSettings(): Promise<EmailSettings> {
  if (staticDemo) throw new Error('Os dados de e-mail não estão disponíveis na demonstração estática.')
  return request<EmailSettings>('/api/v1/settings/email')
}

export async function saveEmailSettings(input: SaveEmailSettingsInput): Promise<void> {
  if (staticDemo) throw new Error('Os dados de e-mail não podem ser salvos na demonstração estática.')
  await request<void>('/api/v1/settings/email', { method: 'PUT', body: JSON.stringify(input) })
}

export async function sendTestEmail(): Promise<EmailTestResult> {
  if (staticDemo) throw new Error('O teste de envio não está disponível na demonstração estática.')
  return request<EmailTestResult>('/api/v1/settings/email/test', { method: 'POST', body: '{}' })
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
