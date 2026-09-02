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
  windowsServiceStatus?: string
  windowsServiceAutoStartSuspended?: boolean
  windowsServiceAutoStartFailures?: number
  installationIsExclusive?: boolean
  installationAutoStartEnabled?: boolean
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
  threadId?: string | null
  user?: string | null
  computer?: string | null
  sourceFile?: string | null
  sourceLine?: number | null
  environment?: string | null
  company?: string | null
  module?: string | null
  routine?: string | null
}

export interface RetentionCounts {
  probeResults: number
  logEvents: number
  metricSamples: number
}

export interface RetentionSettings {
  historyRetentionDays: number
  metricAggregationAfterDays: number
  updatedAt: string | null
  counts: RetentionCounts
}

export interface SaveRetentionRequest {
  historyRetentionDays: number
  metricAggregationAfterDays: number
}

export interface LogEventQuery {
  search?: string
  level?: string
  componentId?: string
  from?: string
  take?: number
  skip?: number
}

export interface LogEventPage {
  total: number
  byLevel: Record<string, number>
  items: LogEventItem[]
}

export type ServiceAction = 'start' | 'stop' | 'restart'

export interface ServiceActionOutcome {
  serviceName: string
  action: ServiceAction
  success: boolean
  status: string
  message: string
}

export interface ExclusiveInstallation {
  id: string
  name: string
}

export interface AutomationFlag {
  id: string
  name: string
  isExclusive: boolean
  autoStartEnabled: boolean
}

export interface ServiceActionResponse {
  results: ServiceActionOutcome[]
}

export interface MaintenanceStatus {
  active: boolean
  endsAt?: string
  exclusiveInstallation?: ExclusiveInstallation | null
}

export interface MaintenanceChangeResult {
  services: ServiceActionOutcome[]
  endsAt?: string
  exclusiveInstallation?: ExclusiveInstallation | null
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

export interface ServerMemoryUsage {
  totalBytes: number
  usedBytes: number
  availableBytes: number
  usedPercent: number
}

export interface ServerDiskUsage {
  name: string
  label?: string | null
  format: string
  totalBytes: number
  usedBytes: number
  freeBytes: number
  usedPercent: number
  freePercent: number
  status: HealthStatus
}

export interface ServerResourceSample {
  at: string
  cpuPercent?: number | null
  memoryPercent?: number | null
}

export interface ServerSnapshot {
  observedAt: string
  hostName: string
  operatingSystem: string
  processorCount: number
  uptimeSeconds: number
  cpuUsagePercent?: number | null
  cpuStatus: HealthStatus
  memory?: ServerMemoryUsage | null
  memoryStatus: HealthStatus
  disks: ServerDiskUsage[]
  history: ServerResourceSample[]
  notice?: string | null
}

export interface ServerThresholds {
  cpuWarningPercent: number
  cpuCriticalPercent: number
  memoryWarningPercent: number
  memoryCriticalPercent: number
  diskFreeWarningPercent: number
  diskFreeCriticalPercent: number
}

export interface ServerResources {
  server: ServerSnapshot
  thresholds: ServerThresholds
}

export type SmtpSecurity = 'Auto' | 'None' | 'StartTls' | 'SslOnConnect'

export interface EmailSettings {
  configured: boolean
  enabled: boolean
  host: string
  port: number
  security: SmtpSecurity
  username?: string | null
  hasPassword: boolean
  fromAddress: string
  fromName?: string | null
  recipients: string[]
  timeoutSeconds: number
  allowInvalidCertificate: boolean
  notifyAlerts: boolean
  notifyLogErrors: boolean
}

export interface SaveEmailSettingsInput {
  enabled: boolean
  host: string
  port: number
  security: SmtpSecurity
  username?: string
  /** Ausente mantém a senha guardada; string vazia apaga. */
  password?: string
  fromAddress: string
  fromName?: string
  recipients: string[]
  timeoutSeconds: number
  allowInvalidCertificate: boolean
  notifyAlerts: boolean
  notifyLogErrors: boolean
}

export interface EmailTestResult {
  success: boolean
  message: string
}
