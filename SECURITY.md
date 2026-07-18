# Política de segurança

## Versões suportadas

A série 1.x recebe correções de segurança na branch `main`. Versões anteriores à 1.0 não são mais suportadas.

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

Contribuições devem manter bind local por padrão, somente leitura, sanitização antes de persistência, timeout/cancelamento, menor privilégio e nenhuma execução automática de binários monitorados. Pacotes de release devem publicar SHA-256 e, em distribuição corporativa, assinatura Authenticode verificável.
