import type {
  ComponentSnapshot, ServiceAction,
} from '../types'

export const serviceActionLabels: Record<ServiceAction, string> = { start: 'Iniciar', restart: 'Reiniciar', stop: 'Parar' }

const transitioningServiceStates = ['StartPending', 'StopPending', 'ContinuePending', 'PausePending']

/**
 * Espelha ServiceStateRules do backend: a ação que corresponde ao estado atual do
 * serviço fica bloqueada, e um estado indefinido libera tudo para o operador agir.
 */
export function serviceActionAllowed(status: string | undefined, action: ServiceAction) {
  if (transitioningServiceStates.includes(status ?? '')) return false
  if (status === 'Running') return action !== 'start'
  if (status === 'Stopped') return action === 'start'
  return true
}

export function serviceStateTone(status: string | undefined) {
  if (status === 'Running') return 'running'
  if (status === 'Stopped') return 'stopped'
  if (transitioningServiceStates.includes(status ?? '')) return 'pending'
  return 'unknown'
}

/**
 * Distingue as duas razões de o watchdog estar quieto: uma parada deliberada não
 * acumula falhas, enquanto a desistência vem sempre depois de tentativas.
 */
export function autoStartNote(component: ComponentSnapshot) {
  if (!component.windowsServiceAutoStartSuspended) return ''
  const failures = component.windowsServiceAutoStartFailures ?? 0
  return failures > 0
    ? ` · auto-start pausado após ${failures} falha${failures > 1 ? 's' : ''}`
    : ' · auto-start suspenso'
}

export function serviceStatusLabel(status: string | undefined) {
  const labels: Record<string, string> = {
    Running: 'Em execução',
    Stopped: 'Parado',
    StartPending: 'Iniciando',
    StopPending: 'Parando',
    ContinuePending: 'Retomando',
    PausePending: 'Pausando',
    Paused: 'Pausado',
    NotFound: 'Serviço não encontrado',
  }
  return labels[status ?? ''] ?? 'Estado desconhecido'
}
