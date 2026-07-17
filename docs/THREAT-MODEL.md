# Threat model

## Escopo e objetivos

O modelo cobre o processo Protheus Pulse, dashboard, API local, SQLite, logs internos e leituras futuras de recursos Protheus. O objetivo principal é observar sem ampliar materialmente o risco do ambiente monitorado.

## Ativos

- disponibilidade e integridade do Windows Server;
- caminhos, topologia e metadados técnicos do Protheus;
- trechos sanitizados de logs e resultados de probes;
- contas, papéis, hashes de senha e tokens de sessão;
- chaves de heartbeat e configuração de notificações futuras;
- histórico, alertas e auditoria local.

## Atores

- administrador legítimo do Pulse;
- operador e visualizador autenticados;
- usuário local não autorizado;
- atacante na rede interna;
- endpoint, log ou arquivo monitorado malicioso/comprometido;
- remetente de webhook/HTTP que tenta provocar SSRF ou exfiltração.

## Limites de confiança

1. Navegador ↔ API: toda entrada é não confiável; HTTPS é obrigatório quando atravessa rede.
2. API ↔ SQLite/ProgramData: somente a conta do serviço deve escrever.
3. Coletores ↔ recursos monitorados: conteúdo lido é hostil e deve ser limitado/sanitizado.
4. Host ↔ rede/UNC: DNS, TLS, compartilhamentos e respostas remotas não são confiáveis.
5. Canal de notificação: a saída deixa o limite local e nunca deve conter segredo.

## Ameaças e mitigação

| Ameaça | Impacto | Mitigações atuais ou obrigatórias |
|---|---|---|
| Acesso não autorizado ao dashboard | Exposição de topologia e incidentes | Bind loopback, JWT, RBAC, hash forte, rate limit e auditoria |
| CSRF | Ação em nome do usuário | Token em `Authorization`, sem cookie de autenticação |
| XSS por log/INI | Roubo de sessão ou ação indevida | React escapa texto, CSP, sanitização antes de persistir e DTOs limitados |
| SSRF em checagem HTTP | Acesso a metadados/serviços internos | Allowlist de hosts/URLs, resolução validada, bloqueio de esquemas e redirects controlados na Fase 3 |
| Path traversal/junction loop | Leitura fora da raiz ou DoS | Raízes explícitas, canonicalização, recusa de raiz de volume, limites e proteção contra reparse points |
| Regex catastrófica | CPU/DoS | Tamanho, timeout e regras aprovadas; engine segura quando disponível |
| Vazamento de segredo | Comprometimento de ERP/canais | Lista extensível de chaves, redaction de logs/HTTP, nunca enviar INI integral |
| Roubo do SQLite | Exposição de histórico e hashes | ACL mínima, backups protegidos, conta dedicada; criptografia em repouso conforme política do host |
| Token JWT forjado | Elevação de privilégio | HMAC >= 256 bits, segredo fora do arquivo, validação de issuer/audience/expiração |
| Abuso de SignalR | Consumo de conexão/dados | Autenticação no hub, mensagens sem payload sensível e limites do host |
| Coletor bloqueado | Exaustão de threads/recursos | Async, `CancellationToken`, timeout e concorrência limitada |
| Conta de serviço privilegiada | Movimento lateral | Nunca exigir LocalSystem; conceder somente leitura nos alvos |
| Supply chain | Execução de código malicioso | Lockfiles, auditoria npm, versões fixadas do .NET e CI limpa |

## Segredos

Na Fase 1, a chave JWT de produção vem de `PULSE_JWT_SIGNING_KEY`; não é persistida pelo aplicativo. Senhas são armazenadas apenas como PBKDF2-SHA256. Na evolução, segredos persistentes serão protegidos com Windows DPAPI sob a identidade do serviço.

## Riscos residuais da Fase 1

- Não há ainda bloqueio SSRF porque checagens HTTP reais não estão ativas.
- Retenção automática e DPAPI para canais serão entregues antes de uso real dessas funções.
- O instalador ainda não configura ACL/conta; o guia manual exige revisão explícita.
- Windows Authentication está fora do MVP inicial.

## Regras de segurança invariantes

O Pulse não escreve em pastas monitoradas, não altera serviços Protheus, não executa rotinas ERP, não compila fontes e não modifica INI, RPO, banco ou dicionário.
