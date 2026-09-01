# OpenTelemetry + Grafana Self-Hosted MVP Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use executing-plans to implement this plan task-by-task.

**Goal:** Entregar um primeiro caminho opt-in e seguro que exporte métricas semânticas do Protheus Pulse por OTLP e forneça uma stack Grafana self-hosted reproduzível para homologação dentro da rede do cliente.

**Architecture:** O Pulse continua sendo a fonte operacional local e registra métricas com `System.Diagnostics.Metrics`; quando habilitado, o SDK OpenTelemetry exporta por OTLP/HTTP para um Grafana Alloy local. O Alloy encaminha as métricas ao Prometheus central, enquanto o Grafana usa Prometheus e Loki como data sources provisionados. Falhas ou ausência do backend de observabilidade não podem interromper coleta, persistência, alertas nem automação do Pulse.

**Tech Stack:** .NET 8, OpenTelemetry .NET 1.18, OTLP/HTTP protobuf, Grafana Alloy, Prometheus, Loki, Grafana, Docker Compose, xUnit.

---

### Task 1: Opções de observabilidade seguras e opt-in

**Files:**
- Create: `src/ProtheusPulse.Service/Configuration/ObservabilityOptions.cs`
- Create: `tests/ProtheusPulse.UnitTests/ObservabilityOptionsTests.cs`
- Modify: `tests/ProtheusPulse.UnitTests/ProtheusPulse.UnitTests.csproj`
- Modify: `src/ProtheusPulse.Service/appsettings.json`

**Step 1: Write the failing tests**

Cobrir defaults desabilitados, endpoint HTTP loopback aceito, endpoint HTTPS remoto aceito, HTTP remoto rejeitado, URI relativa/inválida rejeitada, user-info/query/fragment rejeitados e limites de intervalo/namespace.

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/ProtheusPulse.UnitTests/ProtheusPulse.UnitTests.csproj --filter FullyQualifiedName~ObservabilityOptionsTests`

Expected: FAIL porque `ObservabilityOptions` ainda não existe.

**Step 3: Write minimal implementation**

Criar `ObservabilityOptions` com seção `Observability`, defaults `Enabled=false`, `OtlpEndpoint=http://127.0.0.1:4318`, `ServiceNamespace=protheus` e `ExportIntervalSeconds=10`. Implementar validação sem DNS ou I/O: quando habilitado, aceitar somente URI absoluta HTTP(S), exigir loopback para HTTP, recusar credenciais/query/fragment e validar limites.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/ProtheusPulse.UnitTests/ProtheusPulse.UnitTests.csproj --filter FullyQualifiedName~ObservabilityOptionsTests`

Expected: PASS.

**Step 5: Commit**

```bash
git add src/ProtheusPulse.Service/Configuration/ObservabilityOptions.cs src/ProtheusPulse.Service/appsettings.json tests/ProtheusPulse.UnitTests/ObservabilityOptionsTests.cs tests/ProtheusPulse.UnitTests/ProtheusPulse.UnitTests.csproj
git commit -m "feat: add secure observability configuration"
```

### Task 2: Métricas semânticas do Pulse

**Files:**
- Create: `src/ProtheusPulse.Service/Observability/PulseTelemetry.cs`
- Create: `tests/ProtheusPulse.UnitTests/PulseTelemetryTests.cs`

**Step 1: Write the failing tests**

Usar `MeterListener` para verificar duração e resultado de probes, estado do componente, ciclos de coleta e eventos de log. As tags permitidas são limitadas a `installation`, `component`, `probe.type`, `required`, `status`, `maintenance`, `outcome` e `level`; mensagem, evidência, caminho ou conteúdo de log não podem ser tags.

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/ProtheusPulse.UnitTests/ProtheusPulse.UnitTests.csproj --filter FullyQualifiedName~PulseTelemetryTests`

Expected: FAIL porque `PulseTelemetry` ainda não existe.

**Step 3: Write minimal implementation**

Criar um único `Meter` (`ProtheusPulse.Service`) e instrumentos `protheus.pulse.probe.duration`, `protheus.pulse.probe.up`, `protheus.pulse.component.health`, `protheus.pulse.collection.cycles`, `protheus.pulse.collection.duration` e `protheus.pulse.log.events`. Manter apenas o último estado necessário aos gauges em dicionários concorrentes e implementar `IDisposable`.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/ProtheusPulse.UnitTests/ProtheusPulse.UnitTests.csproj --filter FullyQualifiedName~PulseTelemetryTests`

Expected: PASS.

**Step 5: Commit**

```bash
git add src/ProtheusPulse.Service/Observability/PulseTelemetry.cs tests/ProtheusPulse.UnitTests/PulseTelemetryTests.cs
git commit -m "feat: add semantic pulse telemetry"
```

### Task 3: Integrar telemetria ao ciclo e exportador OTLP

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/ProtheusPulse.Service/ProtheusPulse.Service.csproj`
- Modify: `src/ProtheusPulse.Service/Program.cs`
- Modify: `src/ProtheusPulse.Service/HostedServices/MonitoringWorker.cs`
- Modify: `tests/ProtheusPulse.IntegrationTests/PulseApiFactory.cs`
- Create: `tests/ProtheusPulse.IntegrationTests/ObservabilityStartupTests.cs`

**Step 1: Write the failing integration tests**

Com o exportador desabilitado, iniciar a aplicação sem dependência externa. Com configuração insegura, confirmar falha de startup. Com endpoint loopback e exportador habilitado, iniciar e responder ao health check mesmo sem coletor OTLP disponível.

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/ProtheusPulse.IntegrationTests/ProtheusPulse.IntegrationTests.csproj --filter FullyQualifiedName~ObservabilityStartupTests`

Expected: FAIL porque a configuração ainda não é aplicada no startup.

**Step 3: Write minimal implementation**

Adicionar os pacotes OpenTelemetry 1.18 centralmente, registrar recurso e métricas de ASP.NET Core, HTTP client, runtime e `PulseTelemetry`, e configurar OTLP/HTTP somente quando habilitado. Injetar `PulseTelemetry` no worker para registrar probes, logs, estado dos componentes e resultado/duração dos ciclos. Qualquer indisponibilidade do destino permanece assíncrona e não bloqueia o fluxo local.

**Step 4: Run focused and full .NET tests**

Run:

```bash
dotnet test tests/ProtheusPulse.IntegrationTests/ProtheusPulse.IntegrationTests.csproj --filter FullyQualifiedName~ObservabilityStartupTests
dotnet test ProtheusPulse.sln -c Release
```

Expected: PASS.

**Step 5: Commit**

```bash
git add Directory.Packages.props src/ProtheusPulse.Service tests/ProtheusPulse.IntegrationTests
git commit -m "feat: export pulse metrics with opentelemetry"
```

### Task 4: Stack central de homologação e dashboard inicial

**Files:**
- Create: `deploy/observability/compose.yaml`
- Create: `deploy/observability/.env.example`
- Create: `deploy/observability/alloy/config.alloy`
- Create: `deploy/observability/prometheus/prometheus.yml`
- Create: `deploy/observability/loki/config.yml`
- Create: `deploy/observability/grafana/provisioning/datasources/datasources.yml`
- Create: `deploy/observability/grafana/provisioning/dashboards/dashboards.yml`
- Create: `deploy/observability/grafana/dashboards/protheus-pulse-overview.json`

**Step 1: Add the smallest runnable topology**

Fixar versões verificadas de Alloy, Prometheus, Loki e Grafana. Expor Grafana apenas pela porta configurável; manter Prometheus e Loki em rede interna. Habilitar o receiver de remote write do Prometheus. Exigir credenciais Grafana por ambiente e não versionar segredo real.

**Step 2: Configure the telemetry flow**

No perfil central de homologação, receber OTLP/HTTP no Alloy, aplicar `memory_limiter` e `batch`, converter para Prometheus e enviar a `/api/v1/write`. Provisionar Prometheus e Loki no Grafana; o Loki fica pronto para a fase posterior de logs sanitizados, sem tail duplicado de `console.log`.

**Step 3: Provision the initial dashboard**

Criar visão por instalação/componente com disponibilidade observada, duração de probes, estado atual, ciclos de coleta e eventos de log sanitizados. Incluir variáveis de instalação, componente e tipo de probe.

**Step 4: Validate configuration**

Run:

```bash
GRAFANA_ADMIN_USER=admin GRAFANA_ADMIN_PASSWORD=test-only docker compose -f deploy/observability/compose.yaml config
docker run --rm -v "$PWD/deploy/observability/alloy:/etc/alloy" grafana/alloy:v1.18.0 validate /etc/alloy/config.alloy
docker run --rm -v "$PWD/deploy/observability/prometheus:/etc/prometheus" prom/prometheus:v3.13.1 promtool check config /etc/prometheus/prometheus.yml
```

Expected: configurações válidas. Se Docker não estiver disponível, registrar a limitação e validar YAML/JSON localmente.

**Step 5: Commit**

```bash
git add deploy/observability
git commit -m "feat: add self-hosted grafana observability stack"
```

### Task 5: Documentação operacional e segurança

**Files:**
- Modify: `README.md`
- Modify: `docs/DEPLOYMENT-WINDOWS.md`
- Modify: `docs/THREAT-MODEL.md`
- Create: `docs/OBSERVABILITY.md`

**Step 1: Document prerequisites and rollout**

Explicar topologia recomendada por cliente, dimensionamento inicial, portas, certificados, retenção, credenciais, firewall, backup e procedimento de rollback (`Observability:Enabled=false`). Marcar o Compose como homologação/base de referência, não como produção pronta sem TLS, proxy e política de backup.

**Step 2: Document semantics and boundaries**

Registrar catálogo de métricas, significado dos valores, cardinalidade, separação de alertas Pulse/Grafana e exclusões do MVP (traces, banco específico, logs brutos). Corrigir a documentação de conta do serviço Windows para `LocalSystem`, refletindo a implementação atual e explicitando o risco/permissões necessárias.

**Step 3: Review secrets and sensitive data**

Run:

```bash
rg -n "password|secret|token|api[_-]?key|console\.log|Message|Evidence" deploy/observability docs/OBSERVABILITY.md src/ProtheusPulse.Service/Observability src/ProtheusPulse.Service/appsettings.json
```

Expected: nenhum segredo real e nenhum atributo de métrica com conteúdo sensível.

**Step 4: Commit**

```bash
git add README.md docs
git commit -m "docs: add self-hosted observability operations guide"
```

### Task 6: Verificação final

**Files:**
- Modify only if verification exposes defects.

**Step 1: Run all automated checks**

Run:

```bash
dotnet restore ProtheusPulse.sln
dotnet build ProtheusPulse.sln -c Release --no-restore
dotnet test ProtheusPulse.sln -c Release --no-build
npm run ui:test
npm run ui:build
```

Expected: tudo PASS, sem novos warnings.

**Step 2: Smoke-test the local stack when Docker is available**

Subir o Compose com credenciais temporárias, apontar uma instância demo do Pulse ao Alloy, confirmar as séries no Prometheus e o dashboard provisionado no Grafana, então encerrar a stack preservando volumes por padrão.

**Step 3: Inspect the final diff and status**

Run:

```bash
git diff --check
git status --short
git log --oneline --decorate -8
```

Expected: somente mudanças intencionais, sem artefatos de build versionados.

**Step 4: Request code review before integration**

Revisar segurança, compatibilidade com Windows Service, resiliência sem backend e consistência das métricas. Corrigir achados relevantes e repetir a suíte completa antes do handoff.

