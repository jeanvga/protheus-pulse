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

A camada sugerida `ProtheusPulse.Web` foi incorporada a `ProtheusPulse.Service` nesta fase. Como API, SignalR e Worker são publicados no mesmo processo, um projeto Web separado criaria outra fronteira de implantação sem isolamento real. Os endpoints permanecem separados por módulos no código; a camada pode ser extraída depois sem mover regras de domínio ou infraestrutura.

## Fluxo de monitoramento planejado

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

## Decisões da Fase 1

- Porta padrão `5058`, somente em loopback.
- Swagger disponível apenas em desenvolvimento ou demo.
- Chave JWT obrigatória por variável de ambiente fora de desenvolvimento/demo.
- Dados demonstrativos persistidos e marcados com `IsDemo`.
- Endpoints de fases futuras retornam `501`, evitando uma aparência enganosa de funcionalidade.
- Inno Setup foi reservado para a Fase 5 por ser a alternativa mais simples e reproduzível para serviço único.

## Decisões da Fase 2

- Importações usam schema versionado e rejeitam propriedades desconhecidas, reduzindo configuração ambígua e entrada acidental de segredos.
- A prévia nunca persiste; a aplicação exige `confirm: true` e grava instalações, alvos e auditoria em uma única operação.
- Descoberta de arquivos recebe raízes e nomes exatos, recusa raiz de volume/compartilhamento, ignora reparse points e limita profundidade, resultados e duração.
- Descoberta de serviços exige filtro textual e retorna apenas nome, nome de exibição e estado; não lê linha de comando nem altera o serviço.
- A inspeção de INI só aceita arquivo contido em raiz autorizada e remove comentários, limita tamanho/linhas e mascara chaves sensíveis antes da resposta.

## Decisões da Fase 3

- Um único worker agenda componentes não demonstrativos; cada componente usa escopo de banco isolado, timeout por coletor e concorrência global limitada.
- Os coletores devolvem um contrato comum e não recebem operações de escrita no alvo. Apenas resultados, métricas e cursores locais são alterados.
- TCP e HTTP conectam no endereço IP previamente resolvido e aprovado. Loopback e redes privadas são permitidos; link-local/metadados, multicast e endereços não especificados são bloqueados.
- HTTP aceita somente `GET`/`HEAD`, não segue redirects e lê no máximo 64 KiB quando precisa validar conteúdo.
- Logs são lidos a partir de cursor persistido, no máximo 256 KiB por ciclo por padrão. Linhas são limitadas, redigidas e agrupadas por fingerprint antes da persistência.

## Decisões da Fase 4

- O primeiro ciclo cria uma regra padrão por tipo de probe; por padrão, duas falhas consecutivas abrem o incidente e cinco minutos de cooldown evitam reabertura imediata.
- Regras customizadas podem escolher estados de falha, severidade, quantidade consecutiva e cooldown. Recuperação resolve automaticamente; operador pode reconhecer sem alterar o alvo.
- Manutenção continua coletando evidência, mas marca o componente como `Maintenance` e silencia ocorrências até o fim da janela.
- Configuração de webhook é protegida pelo ASP.NET Core Data Protection e nunca volta pela API. Notificações externas contêm apenas tipo de evento, correlação, severidade e estado.
- Métricas detalhadas com mais de sete dias são agregadas por hora. Probes, logs, métricas agregadas, alertas resolvidos e janelas expiradas são removidos após a retenção configurada.
