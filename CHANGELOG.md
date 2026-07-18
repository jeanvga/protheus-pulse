# Changelog

O projeto segue [Semantic Versioning](https://semver.org/) e o formato [Keep a Changelog](https://keepachangelog.com/).

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
