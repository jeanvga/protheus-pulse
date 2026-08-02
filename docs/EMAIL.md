# Envio de e-mail

O Pulse manda e-mail em duas situações: quando um alerta abre, resolve ou reabre, e quando um agente de log encontra erros no `console.log` do AppServer. Os dois usam o mesmo servidor SMTP, configurado em **Configurações → Dados para envio de e-mail**.

Só o perfil `Administrator` vê e altera esses dados.

## Campos

| Campo | O que é |
| --- | --- |
| **Enviar e-mail** | Desligado, o Pulse continua registrando alertas e logs, só não avisa por e-mail. |
| **Servidor SMTP** | Endereço do servidor, por exemplo `smtp.suaempresa.com.br`. |
| **Porta** | 25, 465, 587 ou a porta do seu relay. |
| **Segurança** | `Automático`, `STARTTLS`, `SSL/TLS implícito` ou `Sem criptografia`. |
| **Tempo limite** | De 5 a 120 segundos para o envio inteiro. |
| **Usuário** e **Senha** | Deixe vazios em relay interno que não pede autenticação. |
| **Remetente** e **Nome do remetente** | Endereço que aparece no "De". Alguns provedores exigem que seja o mesmo do usuário autenticado. |
| **Destinatários** | Um por linha, até 20. |
| **Avisar sobre alertas** | Manda e-mail nas mudanças de alerta. |
| **Avisar sobre erros de log** | Manda o resumo dos erros recebidos dos agentes. |
| **Aceitar certificado que não valida** | Só para relay interno com certificado próprio. |

## Qual segurança escolher

| Porta | Escolha | Como funciona |
| --- | --- | --- |
| 587 | `STARTTLS` | A conexão abre em texto puro e é promovida a TLS antes do login. É o mais comum. |
| 465 | `SSL/TLS implícito` | TLS desde o primeiro byte, sem texto puro em momento algum. |
| 25 | `Sem criptografia` | Relay interno na mesma rede. A senha trafega legível — não use com credencial de provedor. |
| qualquer | `Automático` | O Pulse decide entre TLS implícito e STARTTLS conforme a porta e o que o servidor anuncia. Bom para começar. |

Trocar a segurança ajusta a porta sozinho, desde que ela ainda seja uma das portas padrão. Uma porta digitada à mão é preservada.

## Testar

Salve primeiro e clique em **Enviar teste**. O Pulse envia uma mensagem curta para todos os destinatários e mostra o resultado na tela:

- `O servidor SMTP recusou as credenciais informadas` — usuário ou senha errados. Provedores com verificação em duas etapas costumam exigir uma senha de aplicativo.
- `Falha no handshake TLS` — quase sempre porta e modo trocados (465 com STARTTLS, ou 587 com TLS implícito).
- `O servidor SMTP recusou a mensagem (...)` — o servidor respondeu, mas rejeitou; o código SMTP vem junto. Remetente não autorizado é a causa mais comum.
- `Não foi possível concluir o envio` — nome não resolveu, porta fechada no firewall ou servidor fora do ar.

Cada teste fica registrado na auditoria como `EmailSettingsTested`, com o resultado e sem nenhum dado sensível.

## Como a senha é guardada

A configuração inteira é serializada e cifrada com o Data Protection do ASP.NET Core, com as chaves em `%ProgramData%\ProtheusPulse\keys` protegidas por DPAPI da máquina. No banco fica apenas o texto cifrado.

A API nunca devolve a senha: o `GET /api/v1/settings/email` informa somente `hasPassword`. Na tela, o campo fica vazio com o aviso de que a senha está guardada — preencher troca, e o botão **Remover a senha guardada** apaga.

Consequência do DPAPI: restaurar o banco em **outro** servidor invalida o segredo cifrado. Depois de migrar de máquina, salve a senha de novo.

## Quantos e-mails esperar

- **Alertas:** um e-mail por ciclo de monitoramento, com todas as mudanças daquele ciclo. Não é um e-mail por alerta.
- **Erros de log:** os eventos que chegam dos agentes são agrupados por uma janela (padrão de 120 segundos, ajustável em `Pulse:LogAlertDigestSeconds`) e viram um único resumo, com contagem de ocorrências por mensagem.
- Mensagens iguais são agrupadas por assinatura antes de contar, então uma falha que repete mil vezes vira uma linha com `1000x`.

Se o SMTP estiver fora do ar, o Pulse registra o motivo no log do serviço e segue. Alerta e evento de log já estão gravados no banco: o e-mail é aviso, não é o registro.

## Antes de expor em rede

O Pulse escuta em `127.0.0.1:5058` por padrão. Se você publicar o painel na rede, coloque HTTPS na frente antes de digitar credenciais de e-mail — veja [INSTALLATION.md](INSTALLATION.md).
