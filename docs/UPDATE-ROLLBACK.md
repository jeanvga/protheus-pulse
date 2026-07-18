# Atualização e rollback

## Antes de atualizar

1. registre versão atual e versão alvo;
2. valide assinatura, origem e SHA-256 do pacote;
3. crie uma janela de manutenção do próprio Pulse;
4. pare brevemente o serviço e copie `C:\ProgramData\ProtheusPulse` para um backup protegido;
5. preserve o pacote/binários anteriores;
6. leia o `CHANGELOG.md` e confirme se há migration de banco.

Não copie o backup para repositórios, tickets ou pastas de acesso amplo: ele contém topologia, hashes, chaves protegidas e histórico.

## Atualizar

Execute preferencialmente o novo `setup.exe`; use `install.cmd` do ZIP apenas como alternativa técnica. O procedimento para o serviço, repara a pasta gerenciada, substitui binários, mantém o diretório de dados e a chave JWT existente, reaplica ACLs, inicia e valida o banco.

Depois valide:

```powershell
Invoke-WebRequest 'http://127.0.0.1:5058/health/live' -UseBasicParsing
Invoke-WebRequest 'http://127.0.0.1:5058/health/ready' -UseBasicParsing
Get-Service ProtheusPulse
```

Confirme também login, dashboard, coleta manual, alerta de teste e último heartbeat dos jobs configurados.

## Rollback

Se a aplicação não iniciou e nenhuma migration nova foi aplicada, reinstale o pacote anterior preservando `C:\ProgramData\ProtheusPulse`.

Se houve migration incompatível:

1. pare somente `ProtheusPulse`;
2. mova o diretório de dados atual para uma pasta de quarentena protegida;
3. restaure o backup correspondente à versão anterior;
4. reinstale o pacote anterior;
5. valide os dois health checks e o login.

Nunca abra um banco migrado com binários antigos sem confirmar compatibilidade. Registre data, versões, operador, resultado e motivo do rollback.
