import type {
  AlertSnapshot, ComponentSnapshot, EnvironmentKind, HealthStatus,
} from '../types'

export function formatRelative(value?: string) {
  if (!value) return 'agora'
  const seconds = Math.max(0, (Date.now() - new Date(value).getTime()) / 1000)
  if (seconds < 60) return 'agora'
  if (seconds < 3600) return `há ${Math.floor(seconds / 60)} min`
  if (seconds < 86400) return `há ${Math.floor(seconds / 3600)} h`
  return `há ${Math.floor(seconds / 86400)} d`
}

const byteUnits = ['B', 'KB', 'MB', 'GB', 'TB', 'PB']

export function formatBytes(value: number) {
  if (!Number.isFinite(value) || value <= 0) return '0 B'
  const index = Math.min(byteUnits.length - 1, Math.floor(Math.log(value) / Math.log(1024)))
  const scaled = value / 1024 ** index
  return `${scaled.toFixed(index === 0 || scaled >= 100 ? 0 : 1)} ${byteUnits[index]}`
}

export function formatUptime(totalSeconds: number) {
  const days = Math.floor(totalSeconds / 86400)
  const hours = Math.floor((totalSeconds % 86400) / 3600)
  const minutes = Math.floor((totalSeconds % 3600) / 60)
  if (days > 0) return `${days} d ${hours} h`
  if (hours > 0) return `${hours} h ${minutes} min`
  return `${minutes} min`
}

export function formatPercent(value?: number | null) {
  return value == null ? '—' : `${value.toFixed(1)}%`
}

export function typeLabel(type: string) {
  const labels: Record<string, string> = { Rest: 'REST / WebApp', Worker: 'Worker', Job: 'Job / integração', HttpEndpoint: 'Endpoint HTTPS', Broker: 'Broker', Generic: 'Fonte de log' }
  return labels[type] ?? type
}

export function environmentLabel(environment?: EnvironmentKind) {
  return ({ Production: 'Produção', Homologation: 'Homologação', Development: 'Desenvolvimento', Custom: 'Personalizado' } as const)[environment ?? 'Custom']
}

export function stateLabel(state: AlertSnapshot['state']) {
  return ({ Active: 'Ativo', Acknowledged: 'Reconhecido', Resolved: 'Resolvido', Silenced: 'Silenciado' } as const)[state]
}

export function worstStatus(components: ComponentSnapshot[]): HealthStatus {
  const order: HealthStatus[] = ['Critical', 'Warning', 'Unknown', 'Maintenance', 'Healthy']
  return order.find(status => components.some(item => item.status === status)) ?? 'Unknown'
}

/// O input datetime-local trabalha em hora local; o construtor de Date também, então a volta fecha.
export function toLocalInputValue(date: Date) {
  return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16)
}

export function formatDateTime(value: string) {
  return new Date(value).toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })
}
