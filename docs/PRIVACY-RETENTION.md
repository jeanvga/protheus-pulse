# Privacidade e retenção

O Pulse funciona localmente e não envia telemetria a nuvem. O administrador da instalação controla banco, logs e backups.

## Dados armazenados

- configuração técnica de instalações e componentes;
- resultados sanitizados de probes e métricas;
- ocorrências de alerta e correlação;
- usuários locais, papéis e hashes de senha;
- eventos de auditoria;
- cursores de leitura de log;
- definições e hash SHA-256 de tokens de heartbeat;
- URLs de canais protegidas com ASP.NET Core Data Protection.

Não devem ser persistidos: senhas em claro, tokens, conteúdo integral de INI, corpos HTTP potencialmente sensíveis ou grandes trechos de log.

## Retenção

O padrão reserva 30 dias de histórico e agrega métricas detalhadas por hora após 7 dias. O job inicia cinco minutos após o serviço e roda diariamente; administradores podem executá-lo com `POST /api/v1/maintenance/retention/run`.

Cada execução:

- agrega até 50 mil amostras detalhadas por lote;
- remove probes, eventos de log e métricas anteriores à retenção;
- remove alertas resolvidos e janelas de manutenção já expirados além da retenção;
- preserva usuários, configuração atual e auditoria administrativa.

Backups herdam a mesma classificação de segurança do banco ativo e devem ter retenção, criptografia e descarte definidos pela organização.

## Direitos operacionais

Administradores podem planejar exportação e exclusão conforme a política interna. A desinstalação pergunta antes de remover banco, histórico e configuração.
