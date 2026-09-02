import {
  Activity, Archive, Bell, BriefcaseBusiness, Cpu, FileText, Gauge, Server, Settings,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'

export type Page = 'server' | 'overview' | 'installations' | 'logs' | 'jobs' | 'alerts' | 'settings' | 'audit' | 'diagnostics'

export const navItems: Array<{ id: Page; label: string; icon: LucideIcon }> = [
  { id: 'server', label: 'Servidor', icon: Cpu },
  { id: 'overview', label: 'Visão geral', icon: Gauge },
  { id: 'installations', label: 'Instalações', icon: Server },
  { id: 'logs', label: 'Logs', icon: FileText },
  { id: 'jobs', label: 'Jobs', icon: BriefcaseBusiness },
  { id: 'alerts', label: 'Alertas', icon: Bell },
]

export const secondaryNav: Array<{ id: Page; label: string; icon: LucideIcon }> = [
  { id: 'settings', label: 'Configurações', icon: Settings },
  { id: 'audit', label: 'Auditoria', icon: Archive },
  { id: 'diagnostics', label: 'Diagnóstico', icon: Activity },
]

export const pageTitles: Record<Page, { title: string; eyebrow: string }> = {
  server: { title: 'Servidor', eyebrow: 'Processador, memória e discos' },
  overview: { title: 'Visão geral', eyebrow: 'Operação em tempo real' },
  installations: { title: 'Instalações', eyebrow: 'Ambientes e componentes' },
  logs: { title: 'Logs', eyebrow: 'Eventos sanitizados' },
  jobs: { title: 'Jobs', eyebrow: 'Heartbeats e execução' },
  alerts: { title: 'Alertas', eyebrow: 'Incidentes e resolução' },
  settings: { title: 'Configurações', eyebrow: 'Políticas do Pulse' },
  audit: { title: 'Auditoria', eyebrow: 'Rastreabilidade administrativa' },
  diagnostics: { title: 'Diagnóstico', eyebrow: 'Saúde interna e permissões' },
}
