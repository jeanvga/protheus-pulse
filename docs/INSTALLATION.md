# Instalação no Windows Server

## Estado atual

A Fase 1 produz binários publicáveis e compatíveis com Windows Service. O instalador Inno Setup e os scripts assináveis completos pertencem à Fase 5. Até lá, use o procedimento manual abaixo apenas em laboratório.

## Pré-requisitos

- Windows Server suportado pelo .NET 8.
- ASP.NET Core Runtime 8 x64 ou publicação self-contained.
- Conta de serviço dedicada e sem privilégio administrativo, quando possível.
- Permissão de modificação em `C:\ProgramData\ProtheusPulse`.
- Apenas leitura nas futuras raízes locais/UNC monitoradas.

Para UNC, conceda acesso tanto no compartilhamento quanto no NTFS à conta do serviço. Não dependa de unidades mapeadas e não grave credenciais de compartilhamento em configuração.

## Publicar

```powershell
npm ci
npm run ui:build
dotnet publish .\src\ProtheusPulse.Service `
  --configuration Release `
  --runtime win-x64 `
  --self-contained false `
  --output .\artifacts\publish `
  /p:SkipFrontendBuild=true
```

Copie o conteúdo publicado para `C:\Program Files\Protheus Pulse`. Crie `C:\ProgramData\ProtheusPulse` e proteja suas ACLs.

## Configuração segura

Defina `PULSE_JWT_SIGNING_KEY` no ambiente da conta de serviço, com um segredo aleatório de pelo menos 32 caracteres. O bind padrão é `127.0.0.1:5058`.

Para acesso pela LAN:

1. configure explicitamente o endpoint Kestrel;
2. configure HTTPS com certificado confiável;
3. restrinja firewall à rede administrativa;
4. revise reverse proxy e encaminhamento de cabeçalhos;
5. nunca exponha diretamente à internet.

## Registro manual do serviço (laboratório)

```powershell
sc.exe create ProtheusPulse binPath= '"C:\Program Files\Protheus Pulse\ProtheusPulse.Service.exe"' start= delayed-auto
sc.exe description ProtheusPulse "Monitoramento técnico local e somente leitura"
sc.exe start ProtheusPulse
```

Configure a conta dedicada pelo painel de Serviços ou por sua ferramenta corporativa. Não use `LocalSystem` sem necessidade comprovada.

## Dados

Fora de desenvolvimento/demo, o padrão é:

- banco e configuração: `C:\ProgramData\ProtheusPulse`;
- logs internos: `C:\ProgramData\ProtheusPulse\logs`;
- binários: `C:\Program Files\Protheus Pulse`.

O serviço não cria regra de firewall automaticamente.
