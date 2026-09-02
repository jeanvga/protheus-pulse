# Changelog

O projeto segue [Semantic Versioning](https://semver.org/) e o formato [Keep a Changelog](https://keepachangelog.com/).

## [1.6.0] - 2026-09-02

### Added

- Leitura do `console.log` no formato de registro que o AppServer realmente grava: o cabeçalho `2026-01-15T09:12:33.400000-03:00 4321|` abre o evento e as linhas seguintes pertencem a ele. O horário guardado passa a ser o que o AppServer escreveu, e não a hora em que o Pulse leu o arquivo — sem isso não dava para correlacionar um erro com o incidente relatado. Um bloco `THREAD ERROR`, que chega a passar de dez mil linhas com a pilha ADVPL inteira, vira um evento só. Arquivo sem esse cabeçalho continua lido linha a linha.
- Informações estruturadas em cada evento de log de erro: usuário, máquina, thread, ambiente, empresa/filial, módulo, rotina e o fonte ADVPL com a linha onde estourou. A página de Logs mostra esses dados junto da mensagem, então "argument error in function Len()" passa a vir com `MNTR676.PRX:1249` e o módulo em que aconteceu.
- Busca e filtros de log no servidor: texto, nível, componente e período, com paginação e contagem por nível. Antes a busca era feita no navegador sobre as 200 linhas já carregadas, então encontrava apenas o que estava na tela enquanto o banco guardava trinta dias.
- Prazo de retenção editável em **Configurações → Retenção de dados**, entre 1 e 365 dias, com a contagem de verificações, eventos de log e amostras guardadas. O valor só existia no `appsettings.json`, fora do alcance de quem opera, e sem ele o SQLite crescia sem teto no servidor do cliente.

### Changed

- A aba Instalações passou de cartões em duas colunas para lista: uma linha por ambiente, com nome, situação, contagem de componentes e as etiquetas de exclusivo e auto-start visíveis de imediato, e os detalhes recolhíveis por linha.
- A aba Configurações abre com as seções fechadas. O formulário de e-mail vinha sempre aberto e empurrava o resto da tela para baixo.
- Acabamento geral da interface: foco visível por teclado em todo controle, transições de estado e realce de linha na lista de eventos.

### Fixed

- Encoding do `console.log`. O `EncodingName` das origens nascia `auto`, mas não havia caso para `auto` na resolução e tudo caía em UTF-8; nos arquivos reais usados para mapeamento nenhum é UTF-8 válido — são CP1252, e todo acento virava caractere de substituição, inclusive dentro da assinatura usada para agrupar mensagens iguais. Agora `auto` tenta UTF-8 estrito e usa CP1252 quando ele falha, e `cp1252`, `latin1`, `utf-8`, `unicode`, `utf-16be` e `ascii` podem ser escolhidos explicitamente.

## [1.5.0] - 2026-09-01

### Added

- Exportação opcional de métricas por OpenTelemetry, desligada por padrão (`Observability:Enabled`). Ligada, a coleta passa a publicar métricas semânticas do próprio Pulse — `protheus.pulse.probe.duration` e `protheus.pulse.probe.up` por probe, `protheus.pulse.component.health` por componente, `protheus.pulse.collection.cycles`, `.duration` e `.components` por ciclo e `protheus.pulse.log.events` por severidade — junto das métricas padrão de runtime .NET, ASP.NET Core e cliente HTTP. O envio é em lote por OTLP/HTTP para um Alloy no mesmo host (`Observability:OtlpEndpoint`, padrão `http://127.0.0.1:4318`, a cada `ExportIntervalSeconds`, padrão 10 s). Alloy, Prometheus ou Grafana fora do ar não interrompem coleta, persistência, alerta ou controle de serviços: o painel continua sendo a fonte de verdade e a telemetria é acréscimo.
- Stack de referência self-hosted em `deploy/observability`, com Grafana, Prometheus, Loki e Alloy em versões fixadas, data sources e dashboard provisionados por arquivo e um painel **Protheus Pulse — Visão geral** com disponibilidade dos probes, saúde dos componentes, latência por percentil e volume de eventos de log. Serve como base reproduzível para desenvolvimento e homologação; produção continua exigindo TLS, autenticação, firewall, backup e capacidade administrados pelo cliente.
- Configuração de Alloy para o servidor Windows do Protheus (`deploy/observability/agent-windows`): recebe o OTLP do Pulse em loopback e faz `remote_write` autenticado por HTTPS para a rede central de observabilidade.
- Guia de operação em `docs/OBSERVABILITY.md`: topologia recomendada, tabela de portas e fluxos, subida da stack central, ligação do Pulse e endurecimento para produção.

### Security

- A seção `Observability` é validada na inicialização e o serviço não sobe com configuração inválida: o endpoint precisa ser `http`/`https` absoluto e não pode carregar credenciais, query string ou fragmento; o namespace aceita de 1 a 64 caracteres alfanuméricos, ponto, hífen ou sublinhado; o intervalo de exportação fica entre 1 e 300 segundos.
- O Pulse exporta apenas contadores agregados por severidade, nunca conteúdo de log. Loki já vem provisionado como data source, mas nada no MVP envia linhas de log por esse caminho — isso evita ler o `console.log` duas vezes e impede que o Alloy contorne a sanitização que já existe no coletor.
- A credencial do backend central mora no Alloy de cada host, lida de arquivo protegido; o Pulse nunca conhece usuário, senha ou certificado do destino. No Compose, Prometheus e Loki não publicam porta no host, e Grafana e o receiver OTLP de laboratório ficam vinculados a `127.0.0.1`.
- `nanoid` 3.3.16 → 3.3.18 e `postcss` 8.5.19 → 8.5.26 no lockfile, fechando os dois avisos transitivos do `npm audit` (alto e moderado) vindos da cadeia do Vite. São dependências de desenvolvimento e a mudança fica contida no `package-lock.json`.

## [1.4.0] - 2026-08-02

### Added

- O resumo de erros por e-mail passou a valer para todo log monitorado: os eventos de nível `Error` e `Critical` encontrados pela coleta interna são agrupados por janela e enviados com a mensagem, a contagem de ocorrências e o componente. Antes o alerta avisava que o componente tinha caído, mas era preciso abrir o painel para saber o que o Protheus escreveu.
- Supressão de repetição no e-mail de erros: a mesma assinatura de mensagem só volta a ser enviada depois de 30 minutos, e o e-mail informa quantas repetições foram omitidas. Um incidente que despeja erro a cada ciclo rende um e-mail em vez de um a cada janela; a página de Logs continua registrando todas as ocorrências.

### Removed

- Agente externo de log em Python, junto com a API de ingestão (`/api/v1/log-agents`), a tabela `LogAgents`, os tokens de agente e a seção correspondente na aba Configurações. O agente existia para levar o `console.log` até o Pulse, mas o coletor interno já lê o mesmo arquivo e agora produz o mesmo e-mail — a superfície extra (token, endpoint anônimo, tabela, processo Python no servidor) não pagava o que entregava. Para AppServer em outro servidor, a rota continua sendo montar o compartilhamento e apontar o caminho no componente.

## [1.3.0] - 2026-08-02

### Added

- Aba **Servidor**, primeira da navegação: uso de processador, memória física e espaço em todos os discos fixos da máquina onde o Pulse roda, com gráfico dos últimos minutos, barras por disco e classificação em saudável, atenção e crítico. A amostragem roda em segundo plano (`Pulse:ServerSampleIntervalSeconds`, padrão 5 s) e fica em memória: a tela lê a última amostra em vez de consultar o sistema operacional a cada requisição. Limites configuráveis em `Pulse:CpuWarningPercent`, `CpuCriticalPercent`, `MemoryWarningPercent` e `MemoryCriticalPercent`.
- Envio de e-mail configurável na aba **Configurações**: servidor, porta, modo de segurança (automático, STARTTLS, SSL/TLS implícito ou sem criptografia), usuário, senha, remetente, destinatários, tempo limite e aceite opcional de certificado que não valida. Trocar o modo de segurança ajusta a porta padrão, e o botão de teste envia uma mensagem real e mostra o motivo exato de uma eventual falha. Alertas passam a sair por e-mail além do webhook, com uma mensagem por ciclo em vez de uma por alerta.
- Agente de log em Python (`agents/appserver-log-agent`), sem dependências externas: acompanha o `console.log` do AppServer, reconhece os erros do Protheus junto com a pilha ADVPL das linhas seguintes, agrupa as repetições, mascara segredos e envia para o Pulse. Entende rotação e truncamento do arquivo, começa do fim na primeira execução e só avança o cursor depois que o Pulse confirma o recebimento — se o painel estiver fora do ar, o mesmo trecho é reenviado no ciclo seguinte.
- Ingestão autenticada de eventos de log em `POST /api/v1/log-agents/{chave}/events`, com token exibido uma única vez, guardado como hash SHA-256, comparado em tempo constante e rotacionável pelo painel. Os eventos recebidos aparecem na página de Logs e, quando são erro ou crítico, viram um resumo por e-mail agrupado por janela (`Pulse:LogAlertDigestSeconds`, padrão 120 s).
- Gestão dos agentes de log na aba Configurações: criar, rotacionar token e remover, com registro em auditoria (`LogAgentCreated`, `LogAgentTokenRotated`, `LogAgentDeleted`).

### Security

- A senha do SMTP é cifrada com Data Protection junto do restante da configuração do canal e nunca volta pela API: o `GET /api/v1/settings/email` informa apenas se existe uma senha guardada.
- Tudo que chega dos agentes é saneado de novo no servidor, com as mesmas regras do coletor interno. O agente é tratado como fonte, não como autoridade: nível desconhecido é reclassificado pela mensagem, horário fora de faixa é substituído pelo relógio do servidor e o lote é limitado a 200 eventos.
- Nova política de limite de taxa para a ingestão dos agentes: 60 requisições por minuto por origem e chave.
- Corrigida a ordem de mascaramento nas linhas de log: em `Authorization: Bearer <token>`, a regra de atribuição consumia apenas a palavra `Bearer` e deixava o token visível na página de Logs. O token agora é mascarado primeiro.

### Changed

- Origens de log alimentadas por agente são marcadas no banco e ignoradas pela leitura incremental do próprio Pulse, para que o mesmo arquivo não seja contado duas vezes.
- A aba Configurações deixou de ser informativa: administradores veem os formulários de e-mail e de agentes; os demais perfis continuam vendo apenas o resumo das políticas.

## [1.2.0] - 2026-07-25

### Added

- Instalação exclusiva: uma instalação pode ser marcada como exclusiva e, ao entrar em modo manutenção, todos os demais ambientes são parados enquanto ela é **reiniciada** — o restart derruba as sessões já conectadas, que é o que torna o ambiente exclusivo de fato — e permanece como o único no ar para compilar e salvar configurações. Serviços compartilhados com a instalação exclusiva nunca são parados.
- Auto-start por instalação: um watchdog verifica os serviços Windows dos ambientes marcados e os religa automaticamente quando caem, com orçamento de três tentativas a cada quinze minutos, registro em auditoria (`AutoStartRecovered`/`AutoStartFailed`) e atualização do painel em tempo real. Ambientes suspensos pela manutenção ficam de fora; a instalação exclusiva continua protegida durante a janela.
- Intervalo do watchdog configurável em `Pulse:AutoStartIntervalSeconds` (padrão 60 s, limites 15 s–3600 s).
- Parada manual vence o watchdog: parar um serviço pelo painel suspende o auto-start daquele serviço até que alguém o inicie pelo painel novamente. A suspensão é gravada no banco, então sobrevive a um reinício do próprio Pulse; o painel mostra "auto-start suspenso" ao lado do componente.
- Registro de ações em andamento compartilhado entre o painel e o watchdog: o auto-start não tenta religar um serviço enquanto uma ação manual ou o restart da manutenção está executando nele, evitando erros de tempo limite causados pela própria concorrência.
- Backoff e desistência no auto-start: cada falha dobra a espera da próxima tentativa (1 min até o teto de 30 min) e, após cinco falhas consecutivas, o watchdog para de tentar e registra `AutoStartGaveUp`. Um serviço que não sobe por configuração, licença ou dependência deixa de ser iniciado indefinidamente; o painel mostra "auto-start pausado após N falhas" e um start pelo painel zera o contador e retoma a proteção.

### Security

- Rate limit nas rotas que alteram estado — ações de serviço, entrada e saída da manutenção, marcação de exclusivo/auto-start e coleta imediata — com 20 requisições por minuto por origem. Antes apenas login e heartbeat eram limitados, então um token vazado de administrador podia disparar start/stop em rajada no SCM.
- Novos cabeçalhos de isolamento nas respostas: `Permissions-Policy` (nega câmera, microfone, geolocalização e afins), `Cross-Origin-Opener-Policy`, `Cross-Origin-Resource-Policy` e `X-Permitted-Cross-Domain-Policies`.

### Changed

- Os botões iniciar, reiniciar e parar reconhecem o estado real do serviço no SCM: a ação equivalente ao estado atual fica desabilitada (serviço em execução não pode ser iniciado; serviço parado não pode ser parado nem reiniciado) e estados de transição bloqueiam todas as ações. Cada componente exibe o estado atual do serviço ao lado do nome.
- O estado do serviço é gravado a cada coleta e após cada ação, de modo que o painel reflete a nova situação imediatamente, sem esperar o próximo ciclo.

## [1.1.0] - 2026-07-18

### Added

- Iniciar, reiniciar e parar o serviço Windows de um componente diretamente pelo painel, com confirmação, restrição ao perfil `Administrator` e registro em auditoria.
- Modo manutenção: para todos os serviços monitorados (exceto o próprio Pulse), abre janelas de manutenção que suspendem alertas e reinicia os serviços ao encerrar.
- Página de logs com eventos reais coletados: busca por mensagem/componente/instalação, filtro por severidade, contagem de ocorrências e horário.

### Changed

- Serviço Windows passa a executar como `LocalSystem`, exigido pelas ações de serviço e pela leitura de processos e pastas do Protheus de outros usuários.
- Botões decorativos removidos ou ligados a ações reais: cabeçalhos de painéis navegam, sino abre alertas, "Como integrar" abre a documentação de heartbeats.
- Textos do painel e README refletem o novo modelo: coleta somente leitura com ações operacionais explícitas e auditadas.

### Fixed

- Componentes com executável, INI e logs não ficam mais permanentemente como `Desconhecido`: processos de outros usuários com o nome esperado agora contam como em execução mesmo quando o caminho completo não pode ser lido.

## [1.0.4] - 2026-07-18

### Fixed

- Instalador encerra instâncias remanescentes do `ProtheusPulse.Service.exe` (por exemplo, execuções manuais em sessão RDP) antes de configurar o serviço, evitando que segurem o banco ou arquivos com ACL quebrada.
- Arquivos `pulse.db*` recebem `icacls /reset` individual e atributos normalizados; bancos com DACL vazia deixados por instalações com falha voltam a herdar as permissões corretas.
- Novo teste de abertura do banco antes de iniciar o serviço: se `pulse.db` não puder ser aberto para leitura e escrita, a instalação falha imediatamente com mensagem explicando como resolver, em vez de estourar no health check.

## [1.0.3] - 2026-07-18

### Fixed

- Serviço não falha mais com `SQLite Error 14` ao criar `pulse.db`: o instalador agora executa `icacls /reset` no diretório de dados antes de aplicar o DACL final, removendo ACEs explícitas (inclusive Deny) herdadas de instalações antigas que `/grant:r` não substitui.
- Instalador normaliza atributos somente leitura de `pulse.db*` durante a configuração.
- `install-diagnostics.txt` agora inclui a ACL efetiva do diretório de dados e do banco, os processos `ProtheusPulse.Service.exe` em execução e os atributos do arquivo, eliminando diagnósticos às cegas.

## [1.0.2] - 2026-07-18

### Fixed

- Instalador agora trata serviço marcado para exclusão (erro 1072): aguarda o Windows concluir a remoção pendente e recria o serviço; quando um console administrativo segura a exclusão, a mensagem orienta fechar o services.msc ou reiniciar o servidor.
- Instalador assume a propriedade administrativa de `C:\ProgramData\ProtheusPulse` antes de aplicar ACLs, corrigindo permissões herdadas de versões antigas que negavam acesso até ao administrador.
- Gravação do `install-diagnostics.txt` repara a ACL da pasta de logs quando a escrita é negada e usa `%TEMP%` como último recurso, para o diagnóstico nunca se perder.

## [1.0.1] - 2026-07-18

### Fixed

- Serviço Windows não falha mais com o erro 1053 na instalação: a migração do banco e o seed demonstrativo saíram do caminho crítico de inicialização e agora executam como hosted service, permitindo que o processo se registre no SCM imediatamente.
- Falhas de inicialização passam a ser gravadas em `logs/startup-crash.log`, mesmo quando o Serilog ainda não subiu.
- Diagnóstico do instalador agora captura os eventos recentes do Service Control Manager, inclui o log de crash e recupera a leitura do log da aplicação quando a ACL herdada nega acesso administrativo.
- Publicação com ReadyToRun reduz o tempo de primeira inicialização do serviço em servidores sem cache JIT.

## [1.0.0] - 2026-07-18

### Fixed

- Instalações que permaneciam como `Unknown` por falta de alvos agora podem ser completadas e corrigidas integralmente pelo painel local.
- Instalador Windows agora recupera propriedade e ACL da pasta gerenciada antes da atualização, usa `robocopy` e inclui iniciador elevado que evita bloqueio por marca de download.
- Diretórios do instalador não dependem mais de `Join-Path` com variáveis de ambiente durante a carga; o CMD passa caminhos explícitos e o PowerShell usa as pastas especiais do Windows como fallback validado.
- Payload agora é copiado para uma pasta de runtime nova e versionada, sem sobrescrever arquivos de tentativas anteriores; o Robocopy mantém log de diagnóstico e o CI instala, inicia, valida e remove um serviço Windows real.

### Added

- Cadastro e edição completos no navegador para serviço Windows, executável, INI, logs, TCP e HTTP/HTTPS, com descoberta assistida, coleta imediata e remoção de instalações.
- Endpoints administrativos para consultar, atualizar e excluir a configuração técnica preservando IDs e histórico dos componentes mantidos.
- Fundação modular em .NET 8 com Domain, Application, Infrastructure e Service.
- Modelo mínimo completo, SQLite/EF Core e migration inicial.
- Host ASP.NET Core preparado para Windows Service e bind loopback.
- Autenticação JWT, RBAC, configuração inicial e auditoria de login.
- Dashboard React/TypeScript responsivo, temas claro/escuro e SignalR.
- Modo demonstração com incidentes, alertas, métricas e resolução automática.
- Health checks, Swagger, Serilog rotativo e cabeçalhos de segurança.
- Testes xUnit, Vitest, Playwright e CI para Windows.
- Documentação de arquitetura, instalação, privacidade e threat model.
- Cadastro manual de instalações e componentes com validação, autorização administrativa e auditoria sanitizada.
- Importação JSON/YAML com schema estrito, prévia e confirmação explícita.
- Descoberta somente leitura de serviços e caminhos com filtros, limites, timeout e proteção contra reparse points.
- Inspeção de INI restrita a raiz autorizada, com limites e mascaramento de valores sensíveis.
- Ciclo real de monitoramento com timeout, concorrência limitada, SignalR e execução manual administrativa.
- Coletores passivos de serviço/processo Windows, TCP, HTTP sem redirects, TLS, arquivo e espaço em disco.
- Leitura incremental de logs com cursor, agrupamento por fingerprint, limites e remoção de segredos.
- Migration para eventos de log sanitizados e endpoint autenticado de consulta.
- Motor de alertas com regras automáticas/customizadas, falhas consecutivas, cooldown e resolução automática.
- Reconhecimento por operador, janelas de manutenção e supressão de incidentes durante manutenção.
- Canais HTTPS para Webhook, Teams, Slack e Discord com URL protegida e payload sem topologia/evidência.
- Job diário e execução administrativa de retenção, agregação horária e expurgo de histórico vencido.
- Ação de reconhecimento de alerta no dashboard.
- Heartbeats autenticados com token de uso único, hash SHA-256, rotação, janelas diárias e detecção de atraso.
- Pacote Windows self-contained, scripts idempotentes de instalação/desinstalação e fonte Inno Setup.
- Checklist operacional de implantação, health check pós-instalação e procedimentos de rollback.

### Security

- PBKDF2-SHA256 com salt aleatório para senhas.
- Chave JWT externa obrigatória fora de desenvolvimento/demo.
- Limites de corpo, rate limit de autenticação e CSP restritiva.
- Conexões HTTP/TCP usam resolução própria e bloqueiam endereços link-local, multicast e não especificados.
- Binário SQLite nativo atualizado para uma versão sem vulnerabilidades conhecidas na auditoria NuGet.
- Serviço Windows sob `LocalService`, ACLs mínimas, chave JWT em arquivo restrito e Data Protection protegido por DPAPI.
