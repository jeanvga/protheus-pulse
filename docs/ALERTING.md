# Alertas, manutenção e notificações

## Onde configurar

A aba **Alertas** do painel tem quatro seções, na mesma divisão que o Grafana usa: a regra diz o que abre o incidente, o ponto de contato diz para onde ele vai e o silenciamento diz quando ninguém deve ser avisado.

| Seção | O que faz | Perfil |
| --- | --- | --- |
| Ocorrências | Lista os incidentes por estado e permite reconhecer os ativos. | Operator para reconhecer |
| Regras de alerta | Cria, edita, ativa e remove regra, agrupada por instalação e componente. | Administrator para alterar |
| Pontos de contato | Cadastra e ativa webhook, Teams, Slack e Discord. | Administrator |
| Silenciamentos | Abre e encerra janela de manutenção. | Administrator para alterar |

A aba **Ocorrências** consulta `GET /api/v1/alerts`, com filtro por estado, componente e período e paginação; a contagem por estado é calculada ignorando o filtro de estado, porque alimenta os próprios botões.

O editor de regra pede, em quatro passos, o nome, a condição (componente, verificação e estados que contam como falha), o comportamento da avaliação (falhas consecutivas e cooldown) e a severidade. Antes de salvar, a tela mostra em uma frase o que a regra vai fazer.

Regra criada pelo coletor aparece marcada como **Padrão**. Removê-la não a elimina para sempre: no ciclo seguinte o coletor recria a regra padrão daquela verificação, porque o componente ficou sem nenhuma. Para parar de alertar, desative em vez de remover.

## Recursos do servidor

O Pulse mantém um alvo próprio, **Servidor local**, com um componente que leva o nome da máquina. Ele não aparece na aba Instalações nem entra nos totais do painel, mas passa pela coleta como qualquer outro: cada ciclo grava um probe de `ServerCpu`, `ServerMemory` e `ServerDisk`, com o uso em percentual nas métricas `cpuUsage`, `memoryUsage` e `diskUsage`. É por isso que existe alerta de processador, memória e disco cheio — antes disso o número só pintava de vermelho na aba Servidor.

O probe de disco olha **todos os volumes fixos**, inclusive os que nenhum componente usa, e o volume mais cheio decide o estado e a métrica. A verificação `Disk` continua existindo em separado e é a que enxerga o volume dos caminhos configurados em cada componente.

Uma regra sobre essas três verificações pode definir `thresholdPercent`, de 1 a 100:

- **com limite**, a regra compara a medida direto e ignora os limites globais — dá para ter uma regra de atenção em 85% e outra, crítica, em 95%, sobre a mesma verificação;
- **sem limite**, ela segue o estado que o coletor classificou pelos limites de `Pulse:CpuWarningPercent`, `Pulse:MemoryWarningPercent` e `Pulse:DiskWarningPercent`, que são os mesmos que a aba Servidor mostra.

A contagem de falhas consecutivas acompanha: com limite próprio ela olha as últimas medidas gravadas, porque o estado do histórico foi classificado pelo limite global e não serviria. O limite só é aceito nas verificações de servidor; nas demais a API responde 400.

## Regras

O primeiro ciclo real cria uma regra padrão para cada tipo de probe encontrado no componente. O padrão abre um alerta após duas observações consecutivas em `Warning` ou `Critical`, com cooldown de 300 segundos.

Administradores podem consultar, criar, editar e remover regras em `/api/v1/alert-rules`: `GET` lista (perfil Viewer), `POST` cria, `PUT /{id}` altera, `PUT /{id}/enabled` liga e desliga e `DELETE /{id}` remove com as ocorrências dela. O componente e o tipo de probe ficam fixos depois da criação; para observar outra coisa, crie outra regra. Uma regra define:

- componente e tipo de probe;
- severidade;
- de 1 a 20 falhas consecutivas;
- cooldown de 0 a 86400 segundos;
- estados que representam falha (`Warning`, `Critical` ou `Unknown`).

`GET` devolve também `triggerStatuses` e `isAutomatic`, que a tela usa para reabrir a regra no editor e para marcar as regras criadas pelo coletor.

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

Administradores configuram canais em `/api/v1/notification-channels`. A versão 1.0 aceita `Webhook`, `Teams`, `Slack` e `Discord`, sempre com URL HTTPS sem credenciais embutidas ou fragmento.

A URL é cifrada com ASP.NET Core Data Protection e nunca é devolvida pela API ou gravada em auditoria/log. O envio:

- resolve e conecta no IP aprovado;
- bloqueia link-local/metadados, multicast e endereços não especificados;
- não segue redirects;
- usa timeout de cinco segundos;
- envia somente tipo do evento, correlação, severidade e estado, sem nomes, caminhos ou evidência.

Falha no canal não interrompe a coleta nem o estado local do alerta.
