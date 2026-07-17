# Cadastro de instalações

A Fase 2 oferece cadastro manual, importação versionada e descoberta somente leitura. A aplicação permite múltiplas instalações e componentes de tipos variados, sem nomes fixos de serviço ou executável.

## Cadastro manual

Administradores podem cadastrar uma instalação pelo dashboard ou por `POST /api/v1/installations`. O cadastro exige:

- nome e ambiente da instalação;
- de 1 a 50 componentes com nomes únicos dentro da instalação;
- nome, tipo e indicação de obrigatoriedade para cada componente;
- até 20 tags opcionais, com no máximo 40 caracteres cada.

O nome da instalação é comparado sem diferença entre maiúsculas e minúsculas. Instalação, componentes e evento `InstallationCreated` são persistidos na mesma operação. A auditoria registra somente identificadores, ambiente e contagens; nomes e futuros caminhos técnicos não são copiados para os detalhes do evento.

Esse fluxo apenas grava configuração local. Ele não consulta serviços Windows, não lê INIs e não acessa o servidor Protheus.

Exemplo:

```json
{
  "name": "ERP Producao",
  "environment": "Production",
  "tags": ["matriz"],
  "components": [
    {
      "name": "AppServer REST",
      "type": "AppServer",
      "isRequired": true
    }
  ]
}
```

## Importação JSON/YAML

O fluxo possui duas chamadas administrativas:

- `POST /api/v1/installations/import/preview`: valida e resume sem persistir;
- `POST /api/v1/installations/import`: repete a validação e só persiste com `confirm: true`.

O corpo usa `format` (`json`, `yaml` ou `yml`) e `content`. O limite é 512 KiB, `schemaVersion` deve ser `1` e propriedades desconhecidas são rejeitadas. O schema aceita alvos de serviço Windows, processo, INI, log, TCP e HTTP. Não há campos de senha ou token.

Exemplo de prévia:

```json
{
  "format": "json",
  "content": "{\"schemaVersion\":1,\"installations\":[]}",
  "confirm": false
}
```

Use os documentos sintéticos de [samples/import](../samples/import) como base. Revise a prévia antes de repetir o conteúdo com `confirm: true`.

## Descoberta em modo de prévia

- `GET /api/v1/discovery/services?nameContains=AppServer&limit=100` lista candidatos no Windows;
- `POST /api/v1/discovery/paths` procura nomes exatos em até cinco raízes explícitas;
- `POST /api/v1/discovery/ini` inspeciona um único INI contido em uma raiz autorizada.

A descoberta de caminhos recusa raiz de disco/compartilhamento, curingas e reparse points. Profundidade máxima é 8, resultados são limitados a 200 e o timeout máximo é 15 segundos. Todos os endpoints são `dry-run`: candidatos descobertos não são cadastrados automaticamente.

Exemplo:

```json
{
  "roots": ["D:\\TOTVS\\Protheus"],
  "fileNames": ["appserver.ini", "appserver.exe"],
  "maxDepth": 4,
  "maxResults": 100,
  "timeoutSeconds": 10
}
```

Para o INI, envie `root` e `path`. Comentários não são retornados e propriedades com nomes como `Password`, `Token`, `Secret`, `Credential`, `PrivateKey` e equivalentes recebem o valor `[REDACTED]` antes de sair do processo.

Não substitua os exemplos versionados por caminhos, IPs ou nomes reais de clientes ao contribuir.

## Regras que permanecerão invariantes

- nunca varrer `C:\`, todos os discos ou compartilhamentos automaticamente;
- confirmar candidatos antes de persistir;
- não executar o AppServer para obter versão;
- aceitar caminhos locais e UNC, sem depender de unidade mapeada;
- exibir `Unknown` quando a evidência não for confiável;
- sanitizar qualquer dado antes de enviá-lo ao frontend.
