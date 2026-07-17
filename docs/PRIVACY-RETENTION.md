# Privacidade e retenção

O Pulse funciona localmente e não envia telemetria a nuvem. O administrador da instalação controla banco, logs e backups.

## Dados armazenados

- configuração técnica de instalações e componentes;
- resultados sanitizados de probes e métricas;
- ocorrências de alerta e correlação;
- usuários locais, papéis e hashes de senha;
- eventos de auditoria;
- cursores de leitura de log.

Não devem ser persistidos: senhas em claro, tokens, conteúdo integral de INI, corpos HTTP potencialmente sensíveis ou grandes trechos de log.

## Retenção

O padrão da Fase 1 reserva 30 dias para histórico detalhado e agregação após 7 dias. O job de expurgo/agregação será ativado na Fase 4. Até lá, o modo demo é pequeno; ambientes reais não devem ser cadastrados nesta fase.

Backups herdam a mesma classificação de segurança do banco ativo e devem ter retenção, criptografia e descarte definidos pela organização.

## Direitos operacionais

Administradores podem planejar exportação e exclusão conforme a política interna. A desinstalação futura perguntará antes de remover banco, histórico e configuração.
