# Política de segurança

## Versões suportadas

Enquanto o projeto estiver antes da versão 1.0, apenas o código da branch principal recebe correções de segurança.

## Relatar uma vulnerabilidade

Não publique vulnerabilidades, credenciais, caminhos, IPs, logs ou configurações reais em uma issue pública. Use o recurso **Report a vulnerability / Security Advisories** do repositório GitHub.

Inclua, quando possível:

- versão/commit afetado;
- cenário e impacto;
- passos mínimos com dados sintéticos;
- mitigação temporária;
- sugestão de correção, se houver.

Não inclua dumps de clientes nem execute testes destrutivos em ambientes de terceiros. A triagem buscará confirmar o recebimento em até 5 dias úteis; prazos de correção dependem da severidade e da complexidade.

## Padrão esperado

Contribuições devem manter bind local por padrão, somente leitura, sanitização antes de persistência, timeout/cancelamento, menor privilégio e nenhuma execução automática de binários monitorados.
