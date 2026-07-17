# Cadastro de instalações

Os fluxos de cadastro e descoberta serão implementados na Fase 2. A fundação já permite múltiplas instalações e componentes de tipos variados, sem nomes fixos de serviço ou executável.

## Modos planejados

- descoberta somente leitura por serviços Windows;
- cadastro manual com apenas nome e tipo obrigatórios;
- busca limitada a raízes autorizadas, profundidade, quantidade e tempo;
- importação JSON/YAML com prévia e validação de schema;
- CLI com descoberta em `--dry-run` por padrão.

Até a Fase 2, os endpoints de descoberta retornam `501 Not Implemented`; não varrem discos nem modificam o servidor.

Exemplos sintéticos estão em [samples/import](../samples/import). Não substitua os valores por caminhos, IPs ou nomes reais de clientes ao contribuir.

## Regras que permanecerão invariantes

- nunca varrer `C:\`, todos os discos ou compartilhamentos automaticamente;
- confirmar candidatos antes de persistir;
- não executar o AppServer para obter versão;
- aceitar caminhos locais e UNC, sem depender de unidade mapeada;
- exibir `Unknown` quando a evidência não for confiável;
- sanitizar qualquer dado antes de enviá-lo ao frontend.
