# Changelog

O projeto segue [Semantic Versioning](https://semver.org/) e o formato [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Fixed

- Instalador Windows agora recupera propriedade e ACL da pasta gerenciada antes da atualização, usa `robocopy` e inclui iniciador elevado que evita bloqueio por marca de download.

### Added

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
- Checklist operacional do piloto, health check pós-instalação e procedimentos atualizados de rollback.

### Security

- PBKDF2-SHA256 com salt aleatório para senhas.
- Chave JWT externa obrigatória fora de desenvolvimento/demo.
- Limites de corpo, rate limit de autenticação e CSP restritiva.
- Conexões HTTP/TCP usam resolução própria e bloqueiam endereços link-local, multicast e não especificados.
- Binário SQLite nativo atualizado para uma versão sem vulnerabilidades conhecidas na auditoria NuGet.
- Serviço Windows sob `LocalService`, ACLs mínimas, chave JWT em arquivo restrito e Data Protection protegido por DPAPI.
