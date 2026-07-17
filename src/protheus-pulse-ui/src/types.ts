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

export interface CreateInstallationInput {
  name: string
  environment: EnvironmentKind
  customEnvironmentName?: string
  tags: string[]
  components: Array<{
    name: string
    type: ComponentType
    isRequired: boolean
  }>
}

export interface InstallationCreated {
  id: string
  name: string
  environment: EnvironmentKind
  customEnvironmentName?: string
  tags: string[]
  componentCount: number
  status: HealthStatus
}
