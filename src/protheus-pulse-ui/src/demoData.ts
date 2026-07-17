import type { DashboardSummary } from './types'

const now = Date.now()

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
      id: 'rest', installationId: 'prod', installationName: 'ERP Produção · DEMO', name: 'AppServer REST', type: 'Rest', status: 'Healthy',
      lastStateChangeAt: new Date(now - 37 * 3600000).toISOString(), summary: 'HTTP 200 em 84 ms; TCP e serviço disponíveis.', metricLabel: 'Latência', metricValue: 84, metricUnit: 'ms', isDemo: true,
    },
    {
      id: 'worker', installationId: 'prod', installationName: 'ERP Produção · DEMO', name: 'Worker Financeiro', type: 'Worker', status: 'Warning',
      lastStateChangeAt: new Date(now - 43 * 60000).toISOString(), summary: 'Memória acima do limite por 12 minutos; processo responsivo.', metricLabel: 'Memória', metricValue: 87, metricUnit: '%', isDemo: true,
    },
    {
      id: 'job', installationId: 'prod', installationName: 'ERP Produção · DEMO', name: 'Job Fechamento', type: 'Job', status: 'Critical',
      lastStateChangeAt: new Date(now - 18 * 60000).toISOString(), summary: 'Heartbeat esperado há 18 minutos; tolerância excedida.', metricLabel: 'Atraso', metricValue: 18, metricUnit: 'min', isDemo: true,
    },
    {
      id: 'portal', installationId: 'hml', installationName: 'Integrações Homologação · DEMO', name: 'Portal HTTPS', type: 'HttpEndpoint', status: 'Warning',
      lastStateChangeAt: new Date(now - 2 * 3600000).toISOString(), summary: 'Certificado válido, com vencimento em 9 dias.', metricLabel: 'Validade TLS', metricValue: 9, metricUnit: 'dias', isDemo: true,
    },
    {
      id: 'broker', installationId: 'hml', installationName: 'Integrações Homologação · DEMO', name: 'Broker de Integrações', type: 'Broker', status: 'Healthy',
      lastStateChangeAt: new Date(now - 3 * 86400000).toISOString(), summary: 'Porta TCP disponível; latência dentro do esperado.', metricLabel: 'Latência', metricValue: 16, metricUnit: 'ms', isDemo: true,
    },
    {
      id: 'console', installationId: 'hml', installationName: 'Integrações Homologação · DEMO', name: 'Console de Integração', type: 'Generic', status: 'Warning',
      lastStateChangeAt: new Date(now - 26 * 60000).toISOString(), summary: '8 erros semelhantes agrupados nos últimos 15 minutos.', metricLabel: 'Erros agrupados', metricValue: 8, metricUnit: 'eventos', isDemo: true,
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
