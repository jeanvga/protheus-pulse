# Agente de log do AppServer

Lê o `console.log` do AppServer, reconhece as linhas de erro e manda para o Protheus Pulse. O Pulse grava tudo na página **Logs** e dispara o e-mail configurado na aba **Configurações**.

Feito em Python puro, sem `pip install`: servidor de Protheus costuma não ter internet liberada.

## O que ele faz

- Lê só o pedaço novo do arquivo a cada ciclo, guardando o cursor em disco.
- Entende rotação e truncamento do log, e não reprocessa o que já enviou.
- Junta a linha de erro com a pilha de chamada ADVPL que vem logo abaixo (`Called from ...`).
- Agrupa mensagens iguais e envia a contagem, em vez de repetir a mesma linha.
- Mascara senha, token e `Bearer` antes de enviar. O Pulse mascara de novo do lado dele.
- Se o Pulse estiver fora do ar, não avança o cursor: no ciclo seguinte reenvia o mesmo trecho.
- Na primeira execução começa do **fim** do arquivo, para não despejar meses de histórico no seu e-mail.

## Antes de começar

1. Python 3.9 ou mais novo no servidor (`python --version`).
2. No Pulse, em **Configurações → Agentes de log**, clique em **Novo agente**, escolha o componente e informe o caminho do `console.log`.
3. Copie a **chave** e o **token** exibidos. O token aparece uma vez só.
4. Ainda em **Configurações**, preencha os **dados de envio de e-mail** e faça o teste de envio.

## Instalação

```powershell
# Copie a pasta agents\appserver-log-agent para o servidor, por exemplo:
mkdir C:\ProgramData\ProtheusPulse\agent
copy pulse_log_agent.py C:\ProgramData\ProtheusPulse\agent
copy config.example.ini C:\ProgramData\ProtheusPulse\agent\pulse-agent.ini

cd C:\ProgramData\ProtheusPulse\agent
notepad pulse-agent.ini
```

Preencha `agent_key`, `token` e `log_path`. Depois valide:

```powershell
python pulse_log_agent.py --config pulse-agent.ini --test-connection
python pulse_log_agent.py --config pulse-agent.ini --once --dry-run --from-start
```

O `--dry-run` mostra na tela o que seria enviado, sem enviar e sem mexer no cursor. Quando o resultado fizer sentido, rode de verdade:

```powershell
python pulse_log_agent.py --config pulse-agent.ini
```

## Deixar rodando sozinho

Tarefa agendada, executando na inicialização e reiniciando sozinha:

```powershell
$acao = New-ScheduledTaskAction -Execute "pythonw.exe" `
  -Argument "C:\ProgramData\ProtheusPulse\agent\pulse_log_agent.py --config C:\ProgramData\ProtheusPulse\agent\pulse-agent.ini" `
  -WorkingDirectory "C:\ProgramData\ProtheusPulse\agent"
$gatilho = New-ScheduledTaskTrigger -AtStartup
$config = New-ScheduledTaskSettingsSet -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 5) -ExecutionTimeLimit ([TimeSpan]::Zero)
Register-ScheduledTask -TaskName "ProtheusPulse-LogAgent" -Action $acao -Trigger $gatilho -Settings $config `
  -User "SYSTEM" -RunLevel Highest
```

A conta usada precisa de permissão de **leitura** no `console.log`. O agente nunca escreve no arquivo monitorado.

## Opções de linha de comando

| Opção | Para que serve |
| --- | --- |
| `--config CAMINHO` | Arquivo INI a usar (padrão: `pulse-agent.ini`). |
| `--once` | Um ciclo só e encerra. Bom para agendar de minuto em minuto. |
| `--dry-run` | Mostra o que seria enviado, sem enviar e sem gravar o cursor. |
| `--from-start` | Na primeira execução, lê o arquivo desde o início. |
| `--test-connection` | Valida endereço, chave e token de cada alvo. |
| `--verbose` | Inclui as mensagens de depuração. |

## Como o erro é reconhecido

| Nível | Reconhecido por |
| --- | --- |
| `Critical` | `fatal`, `critical`, `access violation`, `unhandled exception`, `out of memory` |
| `Error` | `error`, `erro`, `exception`, `thread error`, `helpstop`, `msgstop`, `cannot open`, `não foi possível`, `connection refused/failed/lost` |
| `Warning` | `warn`, `warning`, `aviso` — enviado apenas com `send_warnings = true` |

Linhas informativas são descartadas ainda no servidor de origem: elas não saem do AppServer.

## O e-mail

O agente **não** envia e-mail. Ele entrega os eventos ao Pulse, que:

1. grava na página **Logs**, com contagem de ocorrências;
2. junta os erros de uma janela (padrão: 120 s, ajustável em `Pulse:LogAlertDigestSeconds`);
3. manda **um** e-mail com o resumo, usando os dados SMTP da aba Configurações.

A senha do SMTP fica só no Pulse, cifrada. O agente carrega apenas o próprio token, que serve unicamente para enviar log de um componente.

## Problemas comuns

| Sintoma | Causa provável |
| --- | --- |
| `HTTP 401` | Chave ou token errados, ou o token foi rotacionado no painel. |
| `HTTP 409` | O agente foi desabilitado no Pulse. |
| `HTTP 429` | Intervalo curto demais; aumente `interval_seconds`. |
| `log não encontrado` | `log_path` errado ou sem permissão de leitura. |
| Chegou tudo de uma vez | Primeira execução com `--from-start`; sem ele o agente começa do fim. |
| Acento saiu trocado | Ajuste `encoding` (`cp1252` no Windows, `utf-8` em ambientes convertidos). |

## Testes

```bash
cd agents/appserver-log-agent
python3 -m unittest discover -s . -v
```
