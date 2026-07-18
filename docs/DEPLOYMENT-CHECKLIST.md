# Checklist de implantação no Windows Server

Este checklist orienta a implantação segura do Protheus Pulse. Use dados sintéticos na validação inicial e nunca envie ao repositório senhas, tokens, INI, logs reais, IPs públicos, nomes de clientes ou backups.

## 1. Preparar

- [ ] Confirmar backup e janela de mudança do servidor.
- [ ] Confirmar porta local `5058` livre.
- [ ] Obter ZIP e `.sha256` por canal confiável.
- [ ] Comparar SHA-256 antes de extrair.
- [ ] Confirmar espaço em disco para binários, SQLite, logs e retenção.
- [ ] Definir quem será administrador, operador e visualizador do Pulse.

## 2. Instalar

- [ ] Executar `protheus-pulse-1.0.0-win-x64-setup.exe` e aprovar a elevação solicitada.
- [ ] Confirmar serviço `ProtheusPulse` em execução como `LocalService`.
- [ ] Confirmar `http://127.0.0.1:5058/health/live` com HTTP 200.
- [ ] Confirmar `http://127.0.0.1:5058/health/ready` com HTTP 200.
- [ ] Confirmar que nenhuma regra de firewall foi criada.
- [ ] Criar o administrador inicial com senha exclusiva no cofre.

## 3. Cadastrar sem impacto

- [ ] Começar por uma instalação de desenvolvimento/homologação.
- [ ] Fazer todo o cadastro em `http://127.0.0.1:5058`, sem scripts ou edição manual de configuração.
- [ ] Cadastrar um componente por vez.
- [ ] Conceder à conta do serviço apenas leitura nos caminhos necessários.
- [ ] Usar UNC, nunca unidade mapeada; revisar permissões do compartilhamento e NTFS.
- [ ] Executar descoberta limitada e revisar a prévia antes de importar.
- [ ] Não cadastrar endpoint que execute ação ou altere estado ao receber GET/HEAD.

## 4. Validar coletores

- [ ] Serviço Windows: comparar com `Get-Service`.
- [ ] Processo: confirmar caminho exato do executável.
- [ ] TCP/HTTP/TLS: testar apenas endpoints autorizados e observar timeout.
- [ ] Arquivo/disco: usar inicialmente um arquivo sintético somente leitura.
- [ ] Log: usar cópia sintética com segredo fictício e confirmar `[REDACTED]`.
- [ ] Rodar coleta manual e comparar dashboard com o estado real.

## 5. Validar alertas e heartbeat

- [ ] Criar janela de manutenção curta e confirmar supressão.
- [ ] Provocar falha sintética reversível, aguardar falhas consecutivas e confirmar alerta.
- [ ] Reconhecer como operador, restaurar alvo e confirmar resolução.
- [ ] Configurar canal de notificação de teste sem topologia sensível.
- [ ] Criar heartbeat sintético, guardar token no cofre e enviar uma chamada válida.
- [ ] Confirmar rejeição com token inválido e rotação do token.
- [ ] Confirmar atraso somente dentro da janela configurada.

## 6. Aceite e operação

- [ ] Reiniciar o servidor ou serviço em janela aprovada e confirmar início automático atrasado.
- [ ] Criar backup protegido de `C:\ProgramData\ProtheusPulse` com o serviço parado.
- [ ] Testar restauração/rollback em laboratório.
- [ ] Definir responsável por alertas, retenção, atualização e revisão de acesso.
- [ ] Registrar versão, SHA-256, data, operador e resultado da implantação.

Critério de aceite: health checks, login, coleta, alerta, resolução, heartbeat, reinício e backup funcionam sem escrita nos recursos Protheus e sem segredo em UI, log ou repositório.
