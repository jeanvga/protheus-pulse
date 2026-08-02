# Agentes de log

O `console.log` do AppServer é o lugar onde o Protheus conta o que deu errado. O Pulse já lê logs sozinho pelo coletor incremental; o **agente de log** existe para quando ler de dentro não é suficiente:

- o AppServer está em **outro servidor**, e o Pulse não tem acesso ao arquivo;
- a conta do serviço não pode ler a pasta;
- você quer reconhecimento de erro mais específico, com a pilha ADVPL junto da mensagem.

O agente pronto fica em [`agents/appserver-log-agent`](../agents/appserver-log-agent/README.md), escrito em Python puro. A API é aberta: qualquer coisa capaz de fazer POST pode alimentar o Pulse.

## O caminho do erro até o e-mail

```
console.log  ──►  agente (lê, reconhece, agrupa, sanitiza)
                     │  POST /api/v1/log-agents/{chave}/events
                     ▼
                  Pulse  ──►  página de Logs (evento gravado)
                         └─►  fila de resumo ──► e-mail (SMTP das Configurações)
```

O agente **não** envia e-mail. Quem envia é o Pulse, com os dados de [EMAIL.md](EMAIL.md). Assim a senha do SMTP fica em um lugar só, cifrada, e o agente carrega apenas um token que serve para enviar log de um componente.

## Criar um agente

Em **Configurações → Agentes de log**: escolha o componente, dê um nome e informe o caminho do log. O Pulse devolve uma **chave** (pública, vai na URL) e um **token** (secreto, vai no cabeçalho). O token aparece uma vez só; se perder, use **rotacionar**, que invalida o anterior na hora.

A criação, a rotação e a remoção ficam na auditoria como `LogAgentCreated`, `LogAgentTokenRotated` e `LogAgentDeleted`.

## O contrato

```http
POST /api/v1/log-agents/agt_9x2Kd.../events
X-Pulse-Agent-Token: <token>
Content-Type: application/json

{
  "source": "D:\\TOTVS\\Protheus\\appserver\\console.log",
  "events": [
    {
      "observedAt": "2026-08-02T12:00:00Z",
      "level": "Error",
      "message": "Thread Error: variable does not exist CNAME | Called from U_MEUFONTE(120)",
      "occurrenceCount": 3
    }
  ]
}
```

Resposta `202` com `{ "accepted": true, "stored": 1, "queuedForEmail": 1 }`.

| Regra | Valor |
| --- | --- |
| Autenticação | Cabeçalho `X-Pulse-Agent-Token`, comparado em tempo constante contra o hash SHA-256 guardado. |
| Limite de taxa | 60 requisições por minuto por origem e chave. |
| Eventos por requisição | 200; o excedente é descartado. |
| Tamanho da mensagem | 1000 caracteres após o saneamento. |
| `level` | `Critical`, `Error`, `Warning` ou `Information`. Valor desconhecido é reclassificado pela própria mensagem. |
| `observedAt` | Aceito entre 24 h atrás e 5 min à frente; fora disso vale o relógio do servidor. |
| `occurrenceCount` | De 1 a 10000. |

Erros: `401` token inválido, `409` agente desabilitado, `429` limite estourado.

## O que o Pulse faz com o que chega

1. **Sanea de novo.** Senha, token, `Bearer` e caracteres de controle são mascarados no servidor, com as mesmas regras do coletor interno. O agente é uma fonte, não uma autoridade.
2. **Agrupa por assinatura.** Números viram `#` antes do hash, então a mesma falha com IDs diferentes conta como uma mensagem só.
3. **Grava** um `LogEvent` por assinatura e um `ProbeResult` do tipo `Log`, que aparecem na página de Logs e no histórico do componente.
4. **Enfileira** o que for `Error` ou `Critical` para o resumo por e-mail.

A origem informada em `source` vira um `LogSource` marcado como alimentado por agente, e o coletor interno passa a ignorar esse caminho — sem isso, o mesmo arquivo seria contado duas vezes.

## Retenção

Eventos vindos de agente seguem a mesma retenção dos demais: `Pulse:HistoryRetentionDays`, 30 dias por padrão. Veja [PRIVACY-RETENTION.md](PRIVACY-RETENTION.md).
