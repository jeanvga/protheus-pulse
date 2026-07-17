# Cadastro de instalações

O cadastro manual é o primeiro incremento funcional da Fase 2. A fundação permite múltiplas instalações e componentes de tipos variados, sem nomes fixos de serviço ou executável. Importação e descoberta permanecem planejadas para os próximos incrementos.

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

## Modos planejados

- descoberta somente leitura por serviços Windows;
- cadastro manual com apenas os metadados necessários;
- busca limitada a raízes autorizadas, profundidade, quantidade e tempo;
- importação JSON/YAML com prévia e validação de schema;
- CLI com descoberta em `--dry-run` por padrão.

Até o incremento de descoberta, os endpoints correspondentes retornam `501 Not Implemented`; não varrem discos nem modificam o servidor.

Exemplos sintéticos estão em [samples/import](../samples/import). Não substitua os valores por caminhos, IPs ou nomes reais de clientes ao contribuir.

## Regras que permanecerão invariantes

- nunca varrer `C:\`, todos os discos ou compartilhamentos automaticamente;
- confirmar candidatos antes de persistir;
- não executar o AppServer para obter versão;
- aceitar caminhos locais e UNC, sem depender de unidade mapeada;
- exibir `Unknown` quando a evidência não for confiável;
- sanitizar qualquer dado antes de enviá-lo ao frontend.
