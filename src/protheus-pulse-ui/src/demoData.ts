import type { AlertRule, DashboardSummary, HeartbeatDefinition, MaintenanceWindow, NotificationChannel, ServerResources } from './types'

const now = Date.now()

const gigabyte = 1024 ** 3

export const demoSummary: DashboardSummary = {
  generatedAt: new Date(now).toISOString(),
  demoMode: true,
  totals: {
    installations: 2,
    components: 6,
    healthy: 2,
    warning: 3,
    critical: 1,
    unknown: 0,
    activeAlerts: 3,
    availabilityPercent: 97.8,
  },
  components: [
    {
      id: 'rest', installationId: 'prod', installationName: 'ERP Produção · DEMO', installationEnvironment: 'Production', name: 'AppServer REST', type: 'Rest', status: 'Healthy',
      lastStateChangeAt: new Date(now - 37 * 3600000).toISOString(), summary: 'HTTP 200 em 84 ms; TCP e serviço disponíveis.', metricLabel: 'Latência', metricValue: 84, metricUnit: 'ms', isDemo: true,
    },
    {
      id: 'worker', installationId: 'prod', installationName: 'ERP Produção · DEMO', installationEnvironment: 'Production', name: 'Worker Financeiro', type: 'Worker', status: 'Warning',
      lastStateChangeAt: new Date(now - 43 * 60000).toISOString(), summary: 'Memória acima do limite por 12 minutos; processo responsivo.', metricLabel: 'Memória', metricValue: 87, metricUnit: '%', isDemo: true,
    },
    {
      id: 'job', installationId: 'prod', installationName: 'ERP Produção · DEMO', installationEnvironment: 'Production', name: 'Job Fechamento', type: 'Job', status: 'Critical',
      lastStateChangeAt: new Date(now - 18 * 60000).toISOString(), summary: 'Heartbeat esperado há 18 minutos; tolerância excedida.', metricLabel: 'Atraso', metricValue: 18, metricUnit: 'min', isDemo: true,
    },
    {
      id: 'portal', installationId: 'hml', installationName: 'Integrações Homologação · DEMO', installationEnvironment: 'Homologation', name: 'Portal HTTPS', type: 'HttpEndpoint', status: 'Warning',
      lastStateChangeAt: new Date(now - 2 * 3600000).toISOString(), summary: 'Certificado válido, com vencimento em 9 dias.', metricLabel: 'Validade TLS', metricValue: 9, metricUnit: 'dias', isDemo: true,
    },
    {
      id: 'broker', installationId: 'hml', installationName: 'Integrações Homologação · DEMO', installationEnvironment: 'Homologation', name: 'Broker de Integrações', type: 'Broker', status: 'Healthy',
      lastStateChangeAt: new Date(now - 3 * 86400000).toISOString(), summary: 'Porta TCP disponível; latência dentro do esperado.', metricLabel: 'Latência', metricValue: 16, metricUnit: 'ms', isDemo: true,
    },
    {
      id: 'console', installationId: 'hml', installationName: 'Integrações Homologação · DEMO', installationEnvironment: 'Homologation', name: 'Console de Integração', type: 'Generic', status: 'Warning',
      lastStateChangeAt: new Date(now - 26 * 60000).toISOString(), summary: '8 erros semelhantes agrupados nos últimos 15 minutos.', metricLabel: 'Erros agrupados', metricValue: 8, metricUnit: 'eventos', isDemo: true,
    },
    {
      id: 'server', installationId: 'system', installationName: 'Servidor local', installationEnvironment: 'Custom', name: 'SRV-PROTHEUS-DEMO', type: 'Generic', status: 'Warning',
      lastStateChangeAt: new Date(now - 9 * 60000).toISOString(), summary: 'Volume mais cheio: E:\\ em 97% de uso, com 3% livre, entre 3 volumes.', metricLabel: 'Disco em uso', metricValue: 97, metricUnit: '%', isDemo: true, isSystem: true,
    },
  ],
  alerts: [
    { id: 'a1', correlationId: '0d8f-demo', installationName: 'ERP Produção · DEMO', componentName: 'Job Fechamento', ruleName: 'Heartbeat atrasado', severity: 'Critical', state: 'Active', startedAt: new Date(now - 18 * 60000).toISOString(), evidence: 'Último sinal recebido fora da janela esperada.' },
    { id: 'a2', correlationId: 'a451-demo', installationName: 'Integrações Homologação · DEMO', componentName: 'Portal HTTPS', ruleName: 'Certificado próximo do vencimento', severity: 'Warning', state: 'Active', startedAt: new Date(now - 2 * 3600000).toISOString(), evidence: 'Restam 9 dias de validade; limite configurado: 30 dias.' },
    { id: 'a3', correlationId: '933a-demo', installationName: 'ERP Produção · DEMO', componentName: 'Worker Financeiro', ruleName: 'Memória sustentada acima de 85%', severity: 'Warning', state: 'Acknowledged', startedAt: new Date(now - 43 * 60000).toISOString(), evidence: 'Uso médio de memória em 87% durante a janela.' },
    { id: 'a4', correlationId: '271e-demo', installationName: 'Integrações Homologação · DEMO', componentName: 'Broker de Integrações', ruleName: 'Instabilidade de porta', severity: 'Critical', state: 'Resolved', startedAt: new Date(now - 5 * 3600000).toISOString(), resolvedAt: new Date(now - 4.7 * 3600000).toISOString(), evidence: 'Recuperação detectada automaticamente.' },
  ],
  availability: Array.from({ length: 12 }, (_, index) => ({
    at: new Date(now - (11 - index) * 3600000).toISOString(),
    value: Number((98.1 + Math.sin(index * 0.8) * 0.8 - (index === 8 ? 2.4 : 0)).toFixed(1)),
  })),
}

export const demoServerResources: ServerResources = {
  server: {
    observedAt: new Date(now).toISOString(),
    hostName: 'SRV-PROTHEUS-DEMO',
    operatingSystem: 'Microsoft Windows Server 2022 Standard',
    processorCount: 16,
    uptimeSeconds: 9 * 86400 + 7 * 3600 + 12 * 60,
    cpuUsagePercent: 38.4,
    cpuStatus: 'Healthy',
    memory: {
      totalBytes: 64 * gigabyte,
      usedBytes: Math.round(39.2 * gigabyte),
      availableBytes: Math.round(24.8 * gigabyte),
      usedPercent: 61.2,
    },
    memoryStatus: 'Healthy',
    disks: [
      { name: 'C:\\', label: 'Sistema', format: 'NTFS', totalBytes: 240 * gigabyte, usedBytes: Math.round(186.2 * gigabyte), freeBytes: Math.round(53.8 * gigabyte), usedPercent: 77.6, freePercent: 22.4, status: 'Healthy' },
      { name: 'D:\\', label: 'TOTVS', format: 'NTFS', totalBytes: 2048 * gigabyte, usedBytes: Math.round(1863 * gigabyte), freeBytes: Math.round(185 * gigabyte), usedPercent: 91, freePercent: 9, status: 'Warning' },
      { name: 'E:\\', label: 'Backup', format: 'NTFS', totalBytes: 4096 * gigabyte, usedBytes: Math.round(3973 * gigabyte), freeBytes: Math.round(123 * gigabyte), usedPercent: 97, freePercent: 3, status: 'Critical' },
    ],
    history: Array.from({ length: 24 }, (_, index) => ({
      at: new Date(now - (23 - index) * 5000).toISOString(),
      cpuPercent: Number((38 + Math.sin(index * 0.7) * 14 + (index === 17 ? 22 : 0)).toFixed(1)),
      memoryPercent: Number((61 + Math.sin(index * 0.35) * 3).toFixed(1)),
    })),
    notice: null,
  },
  thresholds: {
    cpuWarningPercent: 80,
    cpuCriticalPercent: 92,
    memoryWarningPercent: 85,
    memoryCriticalPercent: 94,
    diskFreeWarningPercent: 15,
    diskFreeCriticalPercent: 5,
  },
}

export const demoAlertRules: AlertRule[] = [
  {
    id: 'rule-heartbeat', componentId: 'job', installationId: 'prod', installationName: 'ERP Produção · DEMO', componentName: 'Job Fechamento',
    name: 'Heartbeat atrasado', probeType: 'Heartbeat', severity: 'Critical', enabled: true,
    minimumConsecutiveFailures: 2, cooldownSeconds: 300, triggerStatuses: ['Warning', 'Critical'], isAutomatic: true,
  },
  {
    id: 'rule-tls', componentId: 'portal', installationId: 'hml', installationName: 'Integrações Homologação · DEMO', componentName: 'Portal HTTPS',
    name: 'Certificado próximo do vencimento', probeType: 'TlsCertificate', severity: 'Warning', enabled: true,
    minimumConsecutiveFailures: 1, cooldownSeconds: 86_400, triggerStatuses: ['Warning', 'Critical'], isAutomatic: false,
  },
  {
    id: 'rule-http', componentId: 'rest', installationId: 'prod', installationName: 'ERP Produção · DEMO', componentName: 'AppServer REST',
    name: 'Falha no coletor Http', probeType: 'Http', severity: 'Critical', enabled: true,
    minimumConsecutiveFailures: 2, cooldownSeconds: 300, triggerStatuses: ['Warning', 'Critical'], isAutomatic: true,
  },
  {
    id: 'rule-log', componentId: 'console', installationId: 'hml', installationName: 'Integrações Homologação · DEMO', componentName: 'Console de Integração',
    name: 'Erros agrupados no console', probeType: 'Log', severity: 'Warning', enabled: false,
    minimumConsecutiveFailures: 3, cooldownSeconds: 1_800, triggerStatuses: ['Critical'], isAutomatic: false,
  },
  {
    id: 'rule-server-memory', componentId: 'server', installationId: 'system', installationName: 'Servidor local', componentName: 'SRV-PROTHEUS-DEMO',
    name: 'Memória acima de 90%', probeType: 'ServerMemory', severity: 'Warning', enabled: true,
    minimumConsecutiveFailures: 3, cooldownSeconds: 1_800, triggerStatuses: ['Warning', 'Critical'], thresholdPercent: 90, isAutomatic: false,
  },
  {
    id: 'rule-server-disk', componentId: 'server', installationId: 'system', installationName: 'Servidor local', componentName: 'SRV-PROTHEUS-DEMO',
    name: 'Disco acima de 95%', probeType: 'ServerDisk', severity: 'Critical', enabled: true,
    minimumConsecutiveFailures: 2, cooldownSeconds: 3_600, triggerStatuses: ['Warning', 'Critical'], thresholdPercent: 95, isAutomatic: false,
  },
  {
    id: 'rule-server-cpu', componentId: 'server', installationId: 'system', installationName: 'Servidor local', componentName: 'SRV-PROTHEUS-DEMO',
    name: 'Processador do servidor', probeType: 'ServerCpu', severity: 'Critical', enabled: true,
    minimumConsecutiveFailures: 2, cooldownSeconds: 300, triggerStatuses: ['Warning', 'Critical'], isAutomatic: true,
  },
]

export const demoNotificationChannels: NotificationChannel[] = [
  { id: 'channel-teams', name: 'Plantão de infraestrutura', type: 'Teams', enabled: true, configured: true },
  { id: 'channel-webhook', name: 'Central de monitoramento', type: 'Webhook', enabled: false, configured: true },
]

export const demoMaintenanceWindows: MaintenanceWindow[] = [
  {
    id: 'window-virada', installationId: 'prod', componentId: null, installationName: 'ERP Produção · DEMO', componentName: null,
    name: 'Virada de mês', startsAt: new Date(now + 3 * 86400000).toISOString(), endsAt: new Date(now + 3 * 86400000 + 4 * 3600000).toISOString(),
    reason: 'Fechamento contábil com o ERP parado.',
  },
]

export const demoHeartbeats: HeartbeatDefinition[] = [
  {
    id: 'hb-fechamento', componentId: 'job', installationName: 'ERP Produção · DEMO', componentName: 'Job Fechamento',
    name: 'Fechamento contábil', jobKey: 'fechamento-contabil', expectedIntervalSeconds: 3_600, toleranceSeconds: 600,
    windowStart: '22:00:00', windowEnd: '06:00:00', lastHeartbeatAt: new Date(now - 18 * 60000).toISOString(),
  },
  {
    id: 'hb-integracao', componentId: 'broker', installationName: 'Integrações Homologação · DEMO', componentName: 'Broker de Integrações',
    name: 'Fila de integração', jobKey: 'fila-integracao', expectedIntervalSeconds: 300, toleranceSeconds: 120,
    windowStart: null, windowEnd: null, lastHeartbeatAt: new Date(now - 2 * 60000).toISOString(),
  },
]
