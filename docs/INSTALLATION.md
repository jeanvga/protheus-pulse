# Instalação no Windows Server

## Pacote recomendado

Use o ZIP self-contained `protheus-pulse-<versão>-win-x64.zip` ou o instalador Inno Setup da mesma versão. Valide o SHA-256 recebido por um canal confiável antes de executar qualquer arquivo.

O padrão seguro instala:

- binários em `C:\Program Files\Protheus Pulse`;
- banco, logs e chaves em `C:\ProgramData\ProtheusPulse`;
- serviço `ProtheusPulse`, automático atrasado, sob `NT AUTHORITY\LocalService`;
- endpoint somente em `http://127.0.0.1:5058`.

O aplicativo é independente do Protheus e não altera serviços, INI, RPO, banco ou arquivos monitorados.

## Pré-requisitos

- Windows Server x64 com PowerShell 5.1 ou mais recente;
- sessão administrativa apenas durante instalação/atualização;
- porta local 5058 livre;
- acesso de leitura da conta do serviço aos recursos monitorados;
- espaço e política de backup para `C:\ProgramData\ProtheusPulse`.

A publicação é self-contained e não exige instalação separada do .NET. Para UNC, conceda leitura no compartilhamento e no NTFS à identidade do computador (`DOMINIO\SERVIDOR$`) ou use uma conta de serviço corporativa aprovada em uma instalação customizada. Não use unidade mapeada e não grave credenciais no Pulse.

## Instalar pelo ZIP

```powershell
$package = 'C:\Pacotes\protheus-pulse-0.1.1-win-x64.zip'
(Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
# Compare visualmente com o arquivo .sha256 obtido por canal confiável.

Expand-Archive -LiteralPath $package -DestinationPath 'C:\Pacotes\ProtheusPulse-0.1.1'
Set-Location 'C:\Pacotes\ProtheusPulse-0.1.1\protheus-pulse-0.1.1-win-x64'
.\install.cmd
```

`install.cmd` solicita elevação e aplica bypass do PowerShell somente ao processo de instalação. O script para o serviço anterior em atualizações, repara as ACLs da pasta gerenciada, copia somente o payload com `robocopy`, preserva dados, cria a chave JWT caso ainda não exista, registra recuperação automática, inicia o serviço e valida `/health/ready`.

Para visualizar as ações sem executá-las:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install-service.ps1 -WhatIf
```

## Instalar pelo `.exe`

Execute o instalador como administrador e preserve o diretório padrão. O instalador chama o mesmo script de registro e health check. Em distribuição corporativa, assine Authenticode tanto o instalador quanto os scripts e valide a cadeia de assinatura.

## Primeiro acesso

Abra [http://127.0.0.1:5058](http://127.0.0.1:5058) no próprio servidor e crie o primeiro administrador. Use uma senha exclusiva e guarde-a no cofre corporativo.

Para acesso remoto, coloque um reverse proxy HTTPS autenticado/restrito diante do bind local. Não altere o bind para LAN sem certificado confiável, firewall restrito e revisão dos cabeçalhos de proxy. O instalador não abre firewall.

## Segredos e permissões

O instalador cria `C:\ProgramData\ProtheusPulse\secrets\jwt.key` com aleatoriedade criptográfica. O serviço lê o arquivo por `PULSE_JWT_SIGNING_KEY_FILE`; o valor não entra em `appsettings.json`, logs ou registro. Não exiba nem copie esse arquivo.

As chaves do ASP.NET Core Data Protection são protegidas por DPAPI da máquina e por ACL. Banco, chaves, logs e backup devem continuar restritos a administradores, `SYSTEM` e à conta do serviço.

## Desinstalação

A desinstalação normal remove serviço e binários, mas preserva `C:\ProgramData\ProtheusPulse`. Para apagar permanentemente banco, logs, chave JWT e chaves DPAPI, faça backup e execute explicitamente:

```powershell
& 'C:\Program Files\Protheus Pulse\scripts\uninstall-service.ps1' -RemoveData
```

Essa operação é irreversível após a confirmação.
