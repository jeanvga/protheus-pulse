# Arquitetura e decisões técnicas

## Contexto

O Protheus Pulse é um monólito modular local. Um único processo hospeda a API, o Worker Service, o SignalR e os arquivos estáticos do frontend. Essa escolha reduz instalação, superfície operacional e consumo de recursos em Windows Server.

## Camadas

- **Domain:** entidades, enums e agregação de saúde. Não conhece EF Core, Windows, HTTP nem arquivos.
- **Application:** contratos dos coletores, relógio, senha e consultas. Define os DTOs expostos à camada web.
- **Infrastructure:** `PulseDbContext`, migrations, consultas EF Core, hash de senha e cenário demo.
- **Service:** composição, Windows Service, autenticação/autorização, endpoints, health checks, Serilog, SignalR e arquivos estáticos.
- **UI:** React/TypeScript sem CDN; o build é copiado para `wwwroot` durante a publicação.

Dependências apontam para dentro: `Service → Application/Infrastructure → Domain`.

API, SignalR e Worker são publicados pelo projeto `ProtheusPulse.Service`. Os endpoints permanecem separados por módulos no código, mantendo a organização interna sem criar outra unidade de implantação.

## Fluxo de monitoramento

1. Um agendador seleciona alvos habilitados e respeita limites de concorrência.
2. Um `IProbeCollector` executa leitura com timeout e cancelamento.
3. O resultado é sanitizado antes de persistir.
4. O motor agrega verificações obrigatórias e opcionais.
5. Mudanças passam por debounce, falhas consecutivas e cooldown.
6. Histórico e alertas são gravados em uma transação SQLite.
7. O SignalR publica apenas que há uma atualização; o cliente relê DTOs autorizados.

Coletores nunca recebem uma operação de escrita no ambiente monitorado.

## Persistência

SQLite usa WAL e migrations versionadas. `DateTimeOffset` é convertido para ticks UTC (`INTEGER`), permitindo ordenação consistente no provedor SQLite. Métricas possuem índice por componente, nome e horário. A política alvo é reter dados detalhados por um período configurável e agregar amostras antigas.

## API e autorização

JWT é enviado pelo cabeçalho `Authorization`, não por cookie; isso reduz a superfície de CSRF. Políticas são cumulativas:

- `Viewer`: leitura de dashboard e histórico autorizado.
- `Operator`: operações de alerta que não alteram o Protheus.
- `Administrator`: configuração, descoberta e diagnóstico.

O dashboard não recebe conteúdo integral de INI, tokens, senhas nem caminhos sensíveis desnecessários.

## Implantação e segurança básica

- Porta padrão `5058`, somente em loopback.
- Swagger disponível apenas em desenvolvimento ou demo.
- Chave JWT obrigatória por variável de ambiente ou arquivo secreto fora de desenvolvimento/demo.
- Dados demonstrativos persistidos e marcados com `IsDemo`.

## Configuração e descoberta

- Importações usam schema versionado e rejeitam propriedades desconhecidas, reduzindo configuração ambígua e entrada acidental de segredos.
- A prévia nunca persiste; a aplicação exige `confirm: true` e grava instalações, alvos e auditoria em uma única operação.
- Descoberta de arquivos recebe raízes e nomes exatos, recusa raiz de volume/compartilhamento, ignora reparse points e limita profundidade, resultados e duração.
- Descoberta de serviços exige filtro textual e retorna apenas nome, nome de exibição e estado; não lê linha de comando nem altera o serviço.
- A inspeção de INI só aceita arquivo contido em raiz autorizada e remove comentários, limita tamanho/linhas e mascara chaves sensíveis antes da resposta.

## Coleta e acesso à rede

- Um único worker agenda componentes não demonstrativos; cada componente usa escopo de banco isolado, timeout por coletor e concorrência global limitada.
- Os coletores devolvem um contrato comum e não recebem operações de escrita no alvo. Apenas resultados, métricas e cursores locais são alterados.
- TCP e HTTP conectam no endereço IP previamente resolvido e aprovado. Loopback e redes privadas são permitidos; link-local/metadados, multicast e endereços não especificados são bloqueados.
- HTTP aceita somente `GET`/`HEAD`, não segue redirects e lê no máximo 64 KiB quando precisa validar conteúdo.
- Logs são lidos a partir de cursor persistido, no máximo 256 KiB por ciclo por padrão. Linhas são limitadas, redigidas e agrupadas por fingerprint antes da persistência.

## Alertas, notificações e retenção

- O primeiro ciclo cria uma regra padrão por tipo de probe; por padrão, duas falhas consecutivas abrem o incidente e cinco minutos de cooldown evitam reabertura imediata.
- Regras customizadas podem escolher estados de falha, severidade, quantidade consecutiva e cooldown. Recuperação resolve automaticamente; operador pode reconhecer sem alterar o alvo.
- Manutenção continua coletando evidência, mas marca o componente como `Maintenance` e silencia ocorrências até o fim da janela.
- Configuração de webhook é protegida pelo ASP.NET Core Data Protection e nunca volta pela API. Notificações externas contêm apenas tipo de evento, correlação, severidade e estado.
- Métricas detalhadas com mais de sete dias são agregadas por hora. Probes, logs, métricas agregadas, alertas resolvidos e janelas expiradas são removidos após a retenção configurada.

## Heartbeats e instalação no Windows

- Heartbeats recebem um token de 256 bits uma única vez e persistem somente SHA-256. A comparação é constante, a rotação revoga imediatamente o valor anterior e o endpoint limita chamadas por job/origem.
- O relógio do servidor define o evento; payload livre do cliente não é persistido. Janelas diárias podem atravessar meia-noite e atrasos fora da janela não geram incidente.
- O serviço usa `LocalService`, binários somente leitura e dados com modificação limitada. Chave JWT, banco, logs e Data Protection ficam fora de `Program Files`.
- Data Protection usa DPAPI da máquina mais ACL do diretório. O `setup.exe` chama um modo administrativo restrito do próprio binário publicado; o PowerShell permanece apenas como alternativa técnica no ZIP.
