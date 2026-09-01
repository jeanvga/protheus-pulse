# OpenTelemetry e Grafana self-hosted — desenho do MVP

## Objetivo

Evoluir o Protheus Pulse para exportar telemetria operacional por um protocolo aberto e permitir que uma instalação Grafana self-hosted, dentro da rede do cliente, mantenha histórico, dashboards e correlação entre Pulse, Windows e AppServers. O Pulse continua funcionando integralmente sem a pilha externa e permanece responsável por saúde, alertas imediatos, manutenção, automação e auditoria.

## Decisão de arquitetura

O MVP usa uma arquitetura híbrida. O processo .NET do Pulse emite métricas semânticas e telemetria do próprio serviço por OTLP. Um Grafana Alloy no mesmo host recebe OTLP por loopback, coleta métricas Windows e encaminha dados para a pilha central. Prometheus armazena métricas e Grafana as apresenta. Loki fica preparado para a fase de logs sanitizados; Tempo não faz parte do primeiro incremento porque não produziria tracing interno do AppServer sem instrumentação específica.

O endpoint OTLP do Pulse é genérico e não conhece Grafana. A integração fica desabilitada por padrão. Quando habilitada, o valor recomendado aponta para `http://127.0.0.1:4318`; somente o Alloy realiza comunicação remota com autenticação e TLS. O backend central deve rodar fora dos servidores Protheus.

```text
Protheus Pulse --OTLP/HTTP em loopback--> Grafana Alloy por host
Grafana Alloy --rede interna/TLS-------> Prometheus e Loki centrais
Prometheus/Loki ------------------------> Grafana self-hosted
```

## Responsabilidades e propriedade dos dados

- SQLite permanece como fonte de verdade local para configuração, resultados de probes, alertas, manutenção e auditoria.
- OpenTelemetry transporta métricas de observabilidade; ele não substitui o modelo de domínio nem altera o cálculo de saúde.
- Grafana/Prometheus mantêm histórico de alta resolução e tendências, mas uma indisponibilidade deles não muda o estado do Pulse.
- Falhas e atrasos de exportação nunca bloqueiam coleta, SignalR, alertas, watchdog ou ações de serviço.
- Alertas de indisponibilidade e automação continuam no Pulse. Grafana Alerting fica reservado a capacidade, tendência e SLO para evitar notificações duplicadas.

## Sinais do MVP

O primeiro incremento exporta métricas de baixa cardinalidade:

- duração de cada tipo de probe;
- resultado do probe como `up` binário, separado do score visual atual;
- estado do componente e indicação de manutenção;
- total de eventos de log por severidade já sanitizados e agrupados;
- atraso de heartbeat;
- transições e quantidade de alertas;
- duração, sucesso e falhas dos ciclos de coleta;
- métricas padrão do runtime .NET, ASP.NET Core, Kestrel e HTTP client.

Os atributos estáveis são `service.name`, `service.instance.id`, `service.version`, `host.name`, ambiente, instalação, componente e tipo de probe. Mensagens de log, caminhos, nomes de usuário, SQL, tokens, URLs completas e identificadores de thread não podem virar atributos de métricas.

O modelo `MetricSample` do SQLite não será transformado em um modelo OpenTelemetry. Um adaptador de telemetria independente observará o resultado dos coletores e registrará instrumentos OTel, preservando a simplicidade do armazenamento local.

## Stack self-hosted do piloto

O repositório fornecerá uma stack de desenvolvimento e homologação com versões fixadas:

- Grafana para dashboards;
- Prometheus para métricas;
- Loki para futura ingestão de logs;
- Grafana Alloy como gateway OTLP e agente de exemplo.

Os serviços centrais escutam apenas o necessário para a rede configurada. Arquivos de exemplo não contêm senhas, tokens ou certificados. Produção exige autenticação, TLS, firewall e segredos fora do repositório. Mimir e Tempo permanecem fora do MVP.

## Configuração e segurança

A seção `Observability` terá pelo menos `Enabled`, `OtlpEndpoint`, `ServiceNamespace` e `ExportIntervalSeconds`. O endpoint deve ser uma URI HTTP ou HTTPS absoluta. HTTP remoto não será aceito: sem TLS, somente loopback é permitido. Cabeçalhos de autenticação não entram em `appsettings.json`; uma fase posterior pode reutilizar Data Protection para configuração administrativa, mas o MVP usa variáveis de ambiente padrão do OpenTelemetry/Alloy.

O exportador usa batch e limites internos do SDK. Exceções do pipeline são registradas sem revelar endpoint completo, credenciais ou payloads. Logs do `console.log` não serão relidos diretamente pelo Alloy no MVP, evitando duplicidade de cursores e bypass da sanitização existente.

A divergência atual entre `LocalSystem` no instalador e `LocalService` na documentação deve ser corrigida no mesmo programa de trabalho, documentando a realidade e o risco residual. O Alloy deve preferencialmente usar conta dedicada com apenas os grupos e ACLs necessários à coleta Windows.

## Tratamento de falhas

- configuração inválida impede apenas a ativação explícita da observabilidade e produz mensagem de inicialização segura;
- endpoint indisponível causa perda ou retry limitado de telemetria, nunca falha do monitoramento local;
- ausência do Alloy mantém todo o comportamento atual do Pulse;
- dashboards exibem ausência de dados de forma distinta de componente Protheus indisponível;
- métricas possuem identidade de escritor única por instância para evitar séries conflitantes.

## Testes e validação

- testes unitários cobrem validação segura das opções e tradução de estados para métricas;
- testes unitários verificam nomes, unidades e atributos permitidos;
- testes de integração validam que o host inicia com observabilidade desabilitada e com um coletor em memória habilitado;
- build e suíte existente garantem que o modo local não mudou;
- validação da stack verifica sintaxe das configurações e provisionamento de datasources/dashboard;
- um smoke test documentado confirma que uma métrica do Pulse chega ao Prometheus e aparece no Grafana.

## Fora do escopo do MVP

- métricas do mecanismo SQL Server, PostgreSQL ou Oracle;
- leitura de usuários, threads ou rotinas pelo WebMonitor/DBMonitor;
- tracing interno de ADVPL/TLPP;
- instalação automática ou atualização do Alloy pelo instalador do Pulse;
- incorporação de painéis Grafana por iframe;
- dependência do Grafana para o dashboard local;
- Grafana Cloud, Mimir, Tempo, alta disponibilidade e multi-tenancy.

## Critérios de sucesso

1. Sem configuração adicional, a versão nova se comporta como a atual e não abre porta nem envia dados.
2. Com `Observability:Enabled=true`, métricas do Pulse chegam via OTLP ao Alloy local.
3. Grafana apresenta saúde, duração de probes, alertas e ciclos por instalação/componente.
4. Queda da stack de observabilidade não interfere no monitoramento ou nas ações do Pulse.
5. Nenhuma métrica ou configuração de exemplo expõe segredo, mensagem bruta de log ou SQL.
