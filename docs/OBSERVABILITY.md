# Observabilidade com OpenTelemetry e Grafana self-hosted

## O que este primeiro incremento entrega

A integração é opcional e permanece desabilitada por padrão. Quando ativada, o Protheus Pulse:

- registra métricas semânticas dos probes, componentes, ciclos de coleta e eventos de log já sanitizados;
- adiciona métricas padrão do runtime .NET, ASP.NET Core e clientes HTTP;
- envia métricas em batch por OTLP/HTTP para um Grafana Alloy no mesmo host;
- continua coletando, persistindo no SQLite, alertando e operando serviços se Alloy, Prometheus ou Grafana estiverem fora do ar.

A stack de referência em [`deploy/observability`](../deploy/observability) contém Grafana, Prometheus, Loki e um Alloy central com versões fixadas. Ela serve como base reproduzível para desenvolvimento e homologação. Produção requer TLS, autenticação, firewall, backup, capacidade e atualização administrados pelo cliente.

Loki já é provisionado como data source, mas o MVP não envia conteúdo de logs. O Pulse exporta somente contadores agregados por severidade. Isso evita duplicar a leitura do `console.log` e impede que o Alloy contorne a sanitização existente.

## Topologia recomendada

```text
Servidor Windows Protheus
┌──────────────────────────────────────────────────────────────┐
│ Protheus Pulse ── OTLP/HTTP 127.0.0.1:4318 ──> Alloy local  │
│                                      │                       │
│ Windows, serviços e AppServers ──────┘                       │
└──────────────────────────────────────┬───────────────────────┘
                                       │ remote_write HTTPS + auth
                                       ▼
Rede central de observabilidade
reverse proxy TLS ──> Alloy central ──> Prometheus ──> Grafana
                                      Loki ──────────> Grafana
```

O Pulse nunca precisa conhecer usuário, senha ou certificado do backend central. A autenticação remota fica no Alloy por host, e a senha é lida de arquivo protegido.

O Compose também publica um receiver OTLP em loopback para smoke tests na máquina central. Não use essa entrada HTTP para atravessar a rede.

## Portas e fluxos

| Origem | Destino | Porta padrão | Regra |
| --- | --- | ---: | --- |
| Navegador local | Pulse | 5058/TCP | Loopback por padrão; reverse proxy HTTPS para acesso remoto |
| Pulse | Alloy do mesmo host | 4318/TCP | Somente `127.0.0.1`, OTLP/HTTP protobuf |
| Alloy Windows | Proxy central | 443/TCP | Saída HTTPS autenticada |
| Proxy central | Alloy central | 12347/TCP | Loopback; caminho `/api/v1/metrics/write` |
| Navegador | Proxy do Grafana | 443/TCP | HTTPS autenticado |
| Grafana | Prometheus/Loki | 9090/3100 | Somente rede interna do Compose |

Prometheus e Loki não possuem porta publicada no host. Grafana, OTLP de laboratório e remote write central ficam vinculados a `127.0.0.1` por padrão.

## Subir a stack central de homologação

Pré-requisitos:

- host separado dos servidores Protheus;
- Docker Engine e Docker Compose v2;
- armazenamento persistente e política de backup;
- DNS e certificado confiável quando houver acesso pela rede.

No host central:

```bash
cd deploy/observability
cp .env.example .env
# Defina pelo menos GRAFANA_ADMIN_PASSWORD e ajuste a URL externa.
docker compose config
docker compose up -d
docker compose ps
```

A senha deve ser longa, exclusiva e permanecer fora do repositório. O arquivo `.env` é ignorado pelo Git; `.env.example` não contém segredo. Em produção, injete a credencial pelo gerenciador de segredos da plataforma e espelhe/verifique as imagens em um registry corporativo; tags de versão sozinhas não substituem controle de digest e atualização de segurança.

O acesso inicial é `http://127.0.0.1:3000`. Para homologação remota, prefira túnel administrativo ou reverse proxy HTTPS. Em produção:

1. termine TLS em proxy corporativo;
2. encaminhe o Grafana para `127.0.0.1:3000`;
3. proteja com autenticação básica ou mecanismo corporativo o caminho de ingestão;
4. encaminhe `/api/v1/metrics/write` para `http://127.0.0.1:12347/api/v1/metrics/write`;
5. limite o firewall aos hosts/agentes autorizados;
6. mantenha `OTLP_BIND_ADDRESS` e `REMOTE_WRITE_BIND_ADDRESS` em loopback.

O Compose não implementa TLS, multi-tenancy, alta disponibilidade nem um provedor de identidade. Não basta trocar o bind para `0.0.0.0` e considerar a instalação pronta para produção.

## Instalar o Alloy nos servidores Windows

Use o instalador oficial do [Grafana Alloy para Windows](https://grafana.com/docs/alloy/latest/set-up/install/windows/) na versão compatível com o arquivo de referência. O perfil [`agent-windows/config.alloy`](../deploy/observability/agent-windows/config.alloy):

- recebe OTLP do Pulse somente em `127.0.0.1:4318`;
- aplica limitador de memória e batch;
- usa o exporter Windows para CPU, memória, disco, rede, TCP e serviços;
- limita métricas de processo a nomes relacionados a AppServer, Broker, DBAccess, License, TOTVS e WebApp;
- mantém WAL para o envio por remote write;
- envia somente por uma URL HTTPS configurada.

Ajuste a expressão do bloco `process` se os executáveis usam nomes diferentes. Não habilite coletores caros ou específicos de banco sem medir custo e definir permissões.

Configure as variáveis para a conta/serviço do Alloy usando a ferramenta corporativa de implantação:

| Variável | Exemplo não real |
| --- | --- |
| `PULSE_REMOTE_WRITE_URL` | `https://observability.example.invalid/api/v1/metrics/write` |
| `PULSE_REMOTE_WRITE_USERNAME` | `alloy-servidor-a` |
| `PULSE_REMOTE_WRITE_PASSWORD_FILE` | `C:\ProgramData\GrafanaLabs\Alloy\secrets\remote-write-password` |

O arquivo de senha deve conter somente o segredo e ter ACL limitada à conta do Alloy e administradores autorizados. Não coloque a senha no `config.alloy`, no registro do projeto ou em script versionado. Depois de aplicar a configuração e as variáveis, reinicie o serviço Alloy e valide que `127.0.0.1:4318` está escutando.

## Habilitar no Protheus Pulse

A configuração equivalente no `appsettings.json` é:

```json
{
  "Observability": {
    "Enabled": true,
    "OtlpEndpoint": "http://127.0.0.1:4318",
    "ServiceNamespace": "protheus",
    "ExportIntervalSeconds": 10
  }
}
```

Em instalação administrada, prefira configuração externa:

```powershell
[Environment]::SetEnvironmentVariable('Observability__Enabled', 'true', 'Machine')
[Environment]::SetEnvironmentVariable('Observability__OtlpEndpoint', 'http://127.0.0.1:4318', 'Machine')
[Environment]::SetEnvironmentVariable('Observability__ServiceNamespace', 'protheus', 'Machine')
[Environment]::SetEnvironmentVariable('Observability__ExportIntervalSeconds', '10', 'Machine')
Restart-Service ProtheusPulse
```

Regras de validação:

- quando `Enabled=false`, nenhum provider/exporter OpenTelemetry é registrado;
- o intervalo aceito é de 1 a 300 segundos;
- HTTP é aceito somente para loopback;
- destinos fora do host exigem HTTPS;
- endpoint não aceita credenciais, query string ou fragmento;
- namespace aceita até 64 caracteres alfanuméricos, ponto, hífen ou sublinhado.

`OtlpEndpoint` representa a base OTLP. Para HTTP/protobuf, o Pulse acrescenta o caminho de sinal `/v1/metrics` uma única vez.

A indisponibilidade do receiver não impede o health check nem a coleta. O SDK possui fila em memória limitada; se o Alloy ficar fora do ar por tempo suficiente, métricas podem ser descartadas. O SQLite local não é reenviado retroativamente.

## Catálogo de métricas do Pulse

O Alloy de referência desabilita sufixos automáticos para manter os nomes Prometheus previsíveis. Pontos nos nomes/atributos são convertidos em sublinhado.

| Instrumento OpenTelemetry | Nome no Prometheus | Tipo/unidade | Significado |
| --- | --- | --- | --- |
| `protheus.pulse.probe.duration` | `protheus_pulse_probe_duration` | Histogram, segundos | Tempo gasto pelo coletor |
| `protheus.pulse.probe.up` | `protheus_pulse_probe_up` | Gauge, 0/1 | 1 somente quando o probe está `Healthy` |
| `protheus.pulse.component.health` | `protheus_pulse_component_health` | Gauge | Unknown=0, Healthy=1, Warning=2, Critical=3, Maintenance=4 |
| `protheus.pulse.collection.cycles` | `protheus_pulse_collection_cycles` | Counter | Ciclos concluídos por `outcome` |
| `protheus.pulse.collection.duration` | `protheus_pulse_collection_duration` | Histogram, segundos | Duração total do ciclo |
| `protheus.pulse.collection.components` | `protheus_pulse_collection_components` | Histogram | Componentes processados por ciclo |
| `protheus.pulse.log.events` | `protheus_pulse_log_events` | Counter | Ocorrências sanitizadas por nível |

As séries de histogramas recebem `_bucket`, `_sum` e `_count` conforme o formato Prometheus.

Dimensões permitidas nas métricas próprias:

- `installation`;
- `component`;
- `probe_type` (origem `probe.type`);
- `required`;
- `status`;
- `maintenance`;
- `outcome`;
- `level`.

Mensagens, evidências, caminhos, linha de comando, conteúdo de log, usuário, SQL, token e URL completa não são exportados. Nomes de instalação/componente ainda podem revelar topologia de negócio; trate Prometheus e Grafana como dados internos.

## Dashboard provisionado

O dashboard **Protheus Pulse — Visão geral** apresenta:

- disponibilidade observada de probes, sem transformar manutenção em disponibilidade artificial;
- componentes críticos e estado atual;
- falhas e duração dos ciclos de coleta;
- taxa de erros sanitizados encontrados em logs;
- duração p95 por componente/tipo de probe;
- CPU, memória e disco dos hosts Windows;
- CPU dos processos AppServer selecionados.

Use **Explore → Prometheus** para confirmar primeiro:

```promql
protheus_pulse_component_health
```

Depois valide:

```promql
windows_cpu_time_total
```

Ausência da primeira série indica o caminho Pulse → Alloy local → central. Ausência apenas da segunda indica o perfil Windows/exporter. “Sem dados” não equivale a componente crítico.

## Alertas: quem é responsável por quê

- **Pulse:** incidentes imediatos, manutenção, falhas consecutivas, cooldown, SMTP/webhook e auto-start.
- **Grafana:** tendência, capacidade, retenção longa e SLO.

Não duplique no Grafana os mesmos alertas imediatos sem definir roteamento e deduplicação. Comece com alertas de espaço em disco, uso sustentado de CPU/memória, ausência prolongada do agente e SLO de probes obrigatórios.

## Retenção, capacidade e backup

Os defaults de homologação mantêm Prometheus por 30 dias e Loki por 720 horas. Antes de produção:

- dimensione por séries ativas, frequência e retenção, medindo `prometheus_tsdb_head_series` e crescimento dos volumes;
- defina limites de CPU/memória no runtime escolhido;
- faça backup consistente dos volumes `prometheus-data`, `grafana-data` e, quando logs forem habilitados, `loki-data`;
- teste restauração em ambiente separado;
- não execute `docker compose down -v` durante operação ou rollback, pois a opção `-v` remove os volumes;
- monitore o próprio Alloy e Prometheus.

O Loki está vazio no MVP; ainda assim, sua retenção e volume devem ser revistos antes de ativar ingestão futura.

## Rollback e diagnóstico

Para interromper exportação sem afetar o Pulse:

```powershell
[Environment]::SetEnvironmentVariable('Observability__Enabled', 'false', 'Machine')
Restart-Service ProtheusPulse
```

A remoção das variáveis também restaura os defaults do `appsettings.json`.

Diagnóstico rápido:

1. confirme `Test-NetConnection 127.0.0.1 -Port 4318` no servidor;
2. valide a configuração com `alloy validate config.alloy`;
3. confirme a URL, certificado, credencial e firewall do remote write;
4. confirme `up{job="alloy-central"}` no Prometheus/Grafana;
5. verifique espaço do WAL do Alloy e volumes centrais;
6. mantenha o Pulse habilitado durante uma queda controlada da stack e confirme `/health/live`, coleta manual e alertas locais.

## Limites e próximos incrementos

Ainda não fazem parte deste MVP:

- conteúdo sanitizado de logs no Loki;
- traces internos de ADVPL/TLPP ou AppServer;
- métricas específicas de SQL Server, PostgreSQL ou Oracle;
- WebMonitor/DBMonitor e métricas de usuários/threads;
- instalação/atualização automática do Alloy pelo setup do Pulse;
- Mimir, Tempo, alta disponibilidade e multi-tenancy.

Para banco, a próxima fase deve selecionar o mecanismo e usar credencial exclusiva de leitura mínima. O collector `mssql` do exporter Windows permanece desabilitado; PostgreSQL e Oracle exigem adapters/exporters próprios. Consultas, nomes de usuário e texto SQL não devem virar labels.
