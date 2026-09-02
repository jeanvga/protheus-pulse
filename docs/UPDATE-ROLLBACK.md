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

## Migração demorada

Desde a 1.9.1 a migração do banco roda em segundo plano: o serviço se registra no Gerenciador de Serviços de imediato e o
painel só atende quando o esquema termina. Num banco com meses de histórico isso leva minutos, e o instalador mostra o
progresso enquanto espera o `/health/ready`. Antes a migração rodava antes do registro no SCM e estourava a janela de 30
segundos, derrubando a instalação com o erro 1053.

Se o `/health/ready` continuar recusando, o motivo aparece no próprio corpo da resposta e no log da aplicação em
`C:\ProgramData\ProtheusPulse\logs`. Uma causa comum é outro `ProtheusPulse.Service.exe` ainda em execução — iniciado
manualmente em uma sessão interativa, por exemplo — segurando `pulse.db`. Confira com `tasklist /fi "imagename eq
ProtheusPulse.Service.exe"` e encerre o processo que não for o do serviço antes de repetir a instalação.

## Quando o start falha com 1053

O 1053 é tempo limite: o Gerenciador de Serviços desiste após 30 segundos se o processo ainda não se registrou. A partir da
1.9.3 o instalador repete o start até três vezes antes de desistir, porque a segunda tentativa parte com binário e disco já
aquecidos.

Se as três falharem, `C:\ProgramData\ProtheusPulse\logs\startup-trace.log` mostra em que fase o tempo foi gasto, com o
decorrido desde o início do processo:

```
2026-01-15T09:12:33.400Z +     8ms  processo iniciado (argumentos: 0)
2026-01-15T09:12:33.500Z +   110ms  construindo a configuração
2026-01-15T09:12:33.560Z +   170ms  diretório de dados pronto
2026-01-15T09:12:33.620Z +   230ms  montando o host
2026-01-15T09:12:33.700Z +   310ms  host montado
2026-01-15T09:12:33.710Z +   320ms  entregando ao Gerenciador de Serviços
2026-01-15T09:12:33.900Z +   510ms  serviço registrado e ouvindo
```

A última linha gravada é a fase que travou. Sem nenhuma linha, o processo não chegou a executar código — verifique antivírus,
bloqueio de execução e permissão de `C:\Program Files\Protheus Pulse`. Para ver o mesmo caminho ao vivo, execute o binário
em console numa sessão administrativa:

```powershell
& 'C:\Program Files\Protheus Pulse\ProtheusPulse.Service.exe'
```
