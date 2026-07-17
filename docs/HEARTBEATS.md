# Heartbeats autenticados

Heartbeats permitem acompanhar jobs e rotinas que não ficam continuamente em execução. Cada definição pertence a um componente e possui intervalo esperado, tolerância e, opcionalmente, janela diária no horário local do servidor.

## Segurança do token

- o servidor gera 256 bits aleatórios;
- o token aparece somente na criação ou rotação;
- o SQLite recebe apenas SHA-256 do token;
- a comparação usa tempo constante;
- o endpoint aplica rate limit por job e origem;
- o corpo da chamada é ignorado e nenhum texto arbitrário é persistido.

Guarde o token no cofre usado pelo agendador. Não o coloque em fonte ADVPL/TLPP, Git, linha de comando registrada, log ou ticket.

## Criar uma definição

Autentique-se como administrador pela API, selecione o componente do job e envie:

```http
POST /api/v1/heartbeat-definitions
Authorization: Bearer <sessão-administrativa>
Content-Type: application/json

{
  "componentId": "00000000-0000-0000-0000-000000000000",
  "name": "Fechamento diário",
  "expectedIntervalSeconds": 3600,
  "toleranceSeconds": 600,
  "windowStart": "22:00:00",
  "windowEnd": "03:00:00"
}
```

O `jobKey` pode ser omitido para geração segura. Capture `jobKey` e `token` da resposta uma única vez.

## Enviar

```http
POST /api/v1/heartbeats/<jobKey>
X-Pulse-Heartbeat-Token: <token-do-cofre>
```

Sucesso retorna `202 Accepted`. O horário aceito é sempre o relógio do servidor Pulse, evitando adulteração pelo cliente. Um job sem evento na janela ativa fica `Unknown`; depois de `intervalo + tolerância`, fica `Critical`. Fora da janela, o atraso não abre incidente.

## Rotação e revogação

`POST /api/v1/heartbeat-definitions/{id}/rotate` invalida imediatamente o token anterior e retorna um novo token uma única vez. `DELETE /api/v1/heartbeat-definitions/{id}` revoga o endpoint daquele job. Ambas as ações exigem administrador e entram na auditoria sem o segredo.
