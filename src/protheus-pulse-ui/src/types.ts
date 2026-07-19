export type HealthStatus = 'Healthy' | 'Warning' | 'Critical' | 'Unknown' | 'Maintenance'
export type AlertSeverity = 'Info' | 'Warning' | 'Critical'
export type AlertState = 'Active' | 'Acknowledged' | 'Resolved' | 'Silenced'
export type EnvironmentKind = 'Production' | 'Homologation' | 'Development' | 'Custom'
export type ComponentType = 'AppServer' | 'Broker' | 'Worker' | 'Rest' | 'WebApp' | 'DbAccess' | 'LicenseServer' | 'Tss' | 'Job' | 'HttpEndpoint' | 'WindowsService' | 'Generic'

export interface DashboardTotals {
  installations: number
  components: number
  healthy: number
  warning: number
  critical: number
  unknown: number
  activeAlerts: number
  availabilityPercent: number
}

export interface ComponentSnapshot {
  id: string
  installationId: string
  installationName: string
  installationEnvironment: EnvironmentKind
  name: string
  type: string
  status: HealthStatus
  lastStateChangeAt?: string
  summary: string
  metricLabel?: string
  metricValue?: number
  metricUnit?: string
  isDemo: boolean
  windowsServiceName?: string
}

export interface LogEventItem {
  id: string
  componentId: string
  installationName: string
  componentName: string
  observedAt: string
  level: string
  message: string
  occurrenceCount: number
}

export type ServiceAction = 'start' | 'stop' | 'restart'

export interface ServiceActionOutcome {
  serviceName: string
  success: boolean
  status: string
  message: string
}

export interface ServiceActionResponse {
  results: ServiceActionOutcome[]
}

export interface MaintenanceStatus {
  active: boolean
  endsAt?: string
}

export interface MaintenanceChangeResult {
  services: ServiceActionOutcome[]
  endsAt?: string
}

export interface AlertSnapshot {
  id: string
  correlationId: string
  installationName: string
  componentName: string
  ruleName: string
  severity: AlertSeverity
  state: AlertState
  startedAt: string
  resolvedAt?: string
  evidence: string
}

export interface DashboardSummary {
  generatedAt: string
  demoMode: boolean
  totals: DashboardTotals
  components: ComponentSnapshot[]
  alerts: AlertSnapshot[]
  availability: Array<{ at: string; value: number }>
}

export interface AuthStatus {
  requiresSetup: boolean
  demoMode: boolean
  demoUsername?: string
  demoPassword?: string
}

export interface AuthToken {
  accessToken: string
  expiresAt: string
  username: string
  displayName: string
  role: string
}

export interface TcpCheckConfiguration {
  host: string
  port: number
  timeoutMs: number
  isRequired: boolean
}

export interface HttpCheckConfiguration {
  url: string
  method: 'GET' | 'HEAD'
  expectedStatusMin: number
  expectedStatusMax: number
  timeoutMs: number
  bodyPattern?: string
  validateTls: boolean
  certificateWarningDays: number
  isRequired: boolean
}

export interface ComponentConfigurationInput {
  id?: string
  name: string
  type: ComponentType
  isRequired: boolean
  windowsServiceName?: string
  executablePath?: string
  iniPath?: string
  logPaths: string[]
  tcpChecks: TcpCheckConfiguration[]
  httpChecks: HttpCheckConfiguration[]
}

export interface SaveInstallationInput {
  name: string
  environment: EnvironmentKind
  customEnvironmentName?: string
  tags: string[]
  components: ComponentConfigurationInput[]
}

export type CreateInstallationInput = SaveInstallationInput

export interface InstallationCreated {
  id: string
  name: string
  environment: EnvironmentKind
  customEnvironmentName?: string
  tags: string[]
  componentCount: number
  status: HealthStatus
}

export interface InstallationConfiguration extends Omit<SaveInstallationInput, 'components'> {
  id: string
  isDemo: boolean
  components: Array<ComponentConfigurationInput & { id: string; status: HealthStatus }>
}

export interface ServiceCandidate {
  serviceName: string
  displayName: string
  status: string
}

export interface ServiceDiscoveryResult {
  supported: boolean
  dryRun: boolean
  candidates: ServiceCandidate[]
}

export interface PathCandidate {
  path: string
  fileName: string
}

export interface PathDiscoveryResult {
  dryRun: boolean
  timedOut: boolean
  durationMs: number
  candidates: PathCandidate[]
}

export interface CollectionResult {
  processedComponents: number
  completedAt: string
}
