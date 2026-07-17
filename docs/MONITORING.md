# Coletores e ciclo de monitoramento

## Execução

Fora do modo demonstração, o `MonitoringWorker` executa um ciclo ao iniciar e repete a cada 30 segundos por padrão. Administradores também podem solicitar uma coleta imediata com `POST /api/v1/diagnostics/collect-now`.

Os limites ficam em `Pulse` no `appsettings.json`:

- `CollectionIntervalSeconds`: de 10 a 3600 segundos;
- `CollectorTimeoutSeconds`: de 1 a 120 segundos;
- `MaximumConcurrentCollectors`: de 1 a 16 componentes;
- `MaximumLogBytesPerCycle`: aplicado entre 4 KiB e 1 MiB;
- `DiskWarningPercent` e `DiskCriticalPercent`: percentuais de espaço livre.

Cada componente recebe um escopo isolado do SQLite. Mudanças de estado são persistidas com probes e métricas, e o dashboard é avisado pelo SignalR para reler os DTOs autorizados.

## Coletores

- **Windows Service:** consulta nome e estado; nunca inicia, para ou reconfigura serviços.
- **Processo:** procura o executável configurado e compara o caminho; nunca executa o binário.
- **TCP:** resolve o host, bloqueia endereços link-local/metadados e conecta no IP aprovado com timeout.
- **HTTP:** apenas `GET` ou `HEAD`, sem redirects; valida faixa de status e, opcionalmente, texto literal nos primeiros 64 KiB.
- **TLS:** usa TLS 1.2/1.3, valida a cadeia quando configurado e mede dias até o vencimento.
- **Arquivo:** verifica existência e sinaliza reparse points; não altera nem interpreta o conteúdo.
- **Disco:** calcula o menor percentual livre entre os volumes dos alvos cadastrados.
- **Log:** lê somente bytes novos, mantém cursor local, limita volume/linha, mascara segredos e agrupa mensagens equivalentes.
- **Heartbeat:** compara o último evento autenticado com intervalo e tolerância; respeita janela diária no horário local e nunca aceita horário fornecido pelo cliente.

## Estados

Uma falha em alvo obrigatório torna o componente `Critical`. Falha em alvo opcional é agregada como `Warning`. Evidência inconclusiva gera `Unknown`; o Pulse não converte ausência de permissão em falso sucesso.

Os resultados expostos por `/api/v1/checks` e `/api/v1/log-events` já estão sanitizados. Conteúdo integral de resposta HTTP, INI ou log não é armazenado.
