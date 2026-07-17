export type HealthStatus = 'Healthy' | 'Warning' | 'Critical' | 'Unknown' | 'Maintenance'
export type AlertSeverity = 'Info' | 'Warning' | 'Critical'
export type AlertState = 'Active' | 'Acknowledged' | 'Resolved' | 'Silenced'

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
