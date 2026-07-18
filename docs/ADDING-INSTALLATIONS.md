# Cadastro de instalações

A Fase 2 oferece cadastro manual, importação versionada e descoberta somente leitura. A aplicação permite múltiplas instalações e componentes de tipos variados, sem nomes fixos de serviço ou executável.

## Cadastro pelo painel local

Administradores cadastram e corrigem instalações em [http://127.0.0.1:5058](http://127.0.0.1:5058), sem PowerShell, arquivo JSON ou edição manual do banco:

1. abra **Instalações** e selecione **Adicionar instalação**;
2. informe o nome, o ambiente e as tags;
3. crie um ou mais componentes;
4. em cada componente, configure pelo menos um alvo real;
5. selecione **Salvar e monitorar** e depois **Coletar agora**.

Os alvos disponíveis na tela são:

- serviço Windows, pesquisado pelo próprio painel ou informado pelo nome interno;
- executável, INI e logs, localizados dentro de uma pasta explícita ou informados manualmente;
- host e porta TCP;
- URL HTTP/HTTPS com método `GET` ou `HEAD`, faixa de status, timeout, texto esperado e validação TLS.

O botão **Configurar** reabre todos esses dados para edição. O botão **Remover** exclui a instalação após confirmação. Componentes mantidos durante uma edição preservam seus identificadores e histórico.

O cadastro exige:

- nome e ambiente da instalação;
- de 1 a 50 componentes com nomes únicos dentro da instalação;
- nome, tipo e indicação de obrigatoriedade para cada componente;
- até 20 tags opcionais, com no máximo 40 caracteres cada.

O nome da instalação é comparado sem diferença entre maiúsculas e minúsculas. Instalação, componentes e evento administrativo são persistidos na mesma operação. A auditoria registra somente identificadores, ambiente e contagens; nomes e caminhos técnicos não são copiados para os detalhes do evento.

A descoberta somente lista candidatos e nunca inicia serviços, executa binários ou altera arquivos. A coleta começa depois que a configuração é salva. Um componente aparece como `Unknown` enquanto ainda não existe evidência confiável; use **Coletar agora** para não esperar o próximo ciclo automático.

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
      "isRequired": true,
      "windowsServiceName": "NomeInternoDoServico",
      "executablePath": "D:\\TOTVS\\Protheus\\bin\\appserver.exe",
      "iniPath": "D:\\TOTVS\\Protheus\\bin\\appserver.ini",
      "logPaths": ["D:\\TOTVS\\Protheus\\logs\\console.log"],
      "tcpChecks": [
        { "host": "127.0.0.1", "port": 1234, "timeoutMs": 3000, "isRequired": true }
      ],
      "httpChecks": []
    }
  ]
}
```

Esse mesmo formato é aceito por `POST /api/v1/installations`; `GET /api/v1/installations/{id}/configuration`, `PUT /api/v1/installations/{id}` e `DELETE /api/v1/installations/{id}` sustentam a tela de configuração.

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
