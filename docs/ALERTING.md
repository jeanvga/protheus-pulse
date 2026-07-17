# Alertas, manutenção e notificações

## Regras

O primeiro ciclo real cria uma regra padrão para cada tipo de probe encontrado no componente. O padrão abre um alerta após duas observações consecutivas em `Warning` ou `Critical`, com cooldown de 300 segundos.

Administradores podem consultar e criar regras em `/api/v1/alert-rules`. Uma regra customizada define:

- componente e tipo de probe;
- severidade;
- de 1 a 20 falhas consecutivas;
- cooldown de 0 a 86400 segundos;
- estados que representam falha (`Warning`, `Critical` ou `Unknown`).

Quando o probe recupera, a ocorrência é resolvida automaticamente. Operadores podem reconhecer um alerta ativo com `POST /api/v1/alerts/{id}/acknowledge`; essa ação apenas registra ciência e não altera o ambiente monitorado.

## Janelas de manutenção

`POST /api/v1/maintenance-windows` cria uma janela para uma instalação ou componente, nunca para ambos ao mesmo tempo. A duração máxima é 90 dias e o término precisa estar no futuro.

Durante a janela:

- coletores continuam registrando evidência;
- o componente aparece como `Maintenance`;
- ocorrências abertas ficam `Silenced`;
- nenhuma nova ocorrência é aberta.

Ao terminar, uma falha persistente reativa o incidente; uma recuperação o resolve.

## Notificações

Administradores configuram canais em `/api/v1/notification-channels`. O MVP aceita `Webhook`, `Teams`, `Slack` e `Discord`, sempre com URL HTTPS sem credenciais embutidas ou fragmento.

A URL é cifrada com ASP.NET Core Data Protection e nunca é devolvida pela API ou gravada em auditoria/log. O envio:

- resolve e conecta no IP aprovado;
- bloqueia link-local/metadados, multicast e endereços não especificados;
- não segue redirects;
- usa timeout de cinco segundos;
- envia somente tipo do evento, correlação, severidade e estado, sem nomes, caminhos ou evidência.

Falha no canal não interrompe a coleta nem o estado local do alerta.
