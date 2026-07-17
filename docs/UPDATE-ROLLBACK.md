# Atualização e rollback

O instalador automatizado será entregue na Fase 5. Enquanto isso, trate atualização manual como uma janela de manutenção do próprio Pulse.

## Antes de atualizar

1. verifique a assinatura/hash do pacote;
2. exporte uma cópia consistente de `pulse.db`, configurações e chaves protegidas;
3. guarde os binários da versão anterior;
4. leia o `CHANGELOG.md` e confirme migrations;
5. pare somente o serviço `ProtheusPulse`.

## Atualizar

Substitua apenas os binários em `C:\Program Files\Protheus Pulse`. Preserve integralmente `C:\ProgramData\ProtheusPulse`. Inicie o serviço e valide `/health/live`, `/health/ready`, login e dashboard.

## Rollback

Se a versão nova não migrou o banco, restaure os binários anteriores. Se aplicou uma migration incompatível, pare o serviço e restaure o backup do banco correspondente. Nunca tente abrir um banco migrado com binários antigos sem confirmar compatibilidade.

Registre data, versão, operador, resultado e motivo do rollback.
