# Instalação no Windows Server

## Pacote recomendado

Use preferencialmente `protheus-pulse-<versão>-win-x64-setup.exe`. Ele é um instalador Inno Setup self-contained e não exige abrir o PowerShell. Valide o SHA-256 recebido por um canal confiável antes de executar o arquivo.

O padrão seguro instala:

- binários em `C:\Program Files\Protheus Pulse`;
- banco, logs e chaves em `C:\ProgramData\ProtheusPulse`;
- serviço `ProtheusPulse`, automático atrasado, sob `LocalSystem`, necessário para as ações operacionais administrativas;
- endpoint somente em `http://127.0.0.1:5058`.

O aplicativo não altera INI, RPO, banco ou arquivos monitorados. Iniciar ou parar um serviço exige uma ação administrativa explícita e confirmada na interface.

## Pré-requisitos

- Windows Server 2016 ou mais recente, x64;
- sessão administrativa apenas durante instalação/atualização;
- porta local 5058 livre;
- acesso de leitura da conta do serviço aos recursos monitorados;
- espaço e política de backup para `C:\ProgramData\ProtheusPulse`.

A publicação é self-contained e não exige instalação separada do .NET. Para UNC, conceda leitura no compartilhamento e no NTFS à identidade do computador (`DOMINIO\SERVIDOR$`) ou use uma conta de serviço corporativa aprovada em uma instalação customizada. Não use unidade mapeada e não grave credenciais no Pulse.

## Instalar pelo `.exe` (recomendado)

1. coloque o `.exe` e o `.sha256` correspondente na mesma pasta;
2. confira o SHA-256;
3. abra o `.exe` e aprove a elevação do Windows;
4. mantenha o diretório padrão e conclua o assistente.

O instalador interrompe o serviço da versão anterior, repara somente as ACLs administradas pelo Pulse, copia os binários, preserva banco e chaves, registra o serviço com o caminho corretamente delimitado, inicia o serviço e valida `/health/ready`. Nenhum bypass de política do PowerShell é necessário.

Se o serviço não iniciar, a mensagem do próprio assistente mostra a causa resumida. O diagnóstico completo fica em `C:\ProgramData\ProtheusPulse\logs\install-diagnostics.txt`.

O build reproduzível gera SHA-256, mas a identidade do publicador depende de um certificado Authenticode. Builds locais sem certificado podem exibir “Publicador desconhecido”; isso é diferente do bloqueio de script não assinado e não exige alterar a política do PowerShell. Pacotes distribuídos por uma organização devem ser assinados e ter a cadeia do certificado validada.

## Instalar pelo ZIP (alternativo)

```powershell
$package = 'C:\Pacotes\protheus-pulse-1.1.0-win-x64.zip'
(Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
# Compare visualmente com o arquivo .sha256 obtido por canal confiável.

Expand-Archive -LiteralPath $package -DestinationPath 'C:\Pacotes\ProtheusPulse-1.1.0'
Set-Location 'C:\Pacotes\ProtheusPulse-1.1.0\protheus-pulse-1.1.0-win-x64'
.\install.cmd
```

`install.cmd` solicita elevação e aplica bypass do PowerShell somente ao processo de instalação. O script para o serviço anterior em atualizações, repara as ACLs da pasta gerenciada, copia o payload para uma nova pasta versionada, preserva dados, cria a chave JWT caso ainda não exista, registra recuperação automática, inicia o serviço e valida `/health/ready`. O diagnóstico da cópia fica em `C:\ProgramData\ProtheusPulse\logs\install-copy.log`.

Para visualizar as ações sem executá-las:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install-service.ps1 -WhatIf
```

## Primeiro acesso

Abra [http://127.0.0.1:5058](http://127.0.0.1:5058) no próprio servidor e crie o primeiro administrador. Use uma senha exclusiva e guarde-a no cofre corporativo.

Toda a configuração do ambiente monitorado é feita nesse endereço: abra **Instalações**, escolha **Adicionar instalação**, informe os alvos de leitura e use **Salvar e monitorar**. A própria tela pesquisa serviços Windows e arquivos dentro da pasta indicada, permite cadastrar portas TCP e endpoints HTTP/HTTPS e oferece **Coletar agora**. Não é necessário executar PowerShell, editar JSON ou alterar `appsettings.json` para cadastrar o Protheus.

Para acesso remoto, coloque um reverse proxy HTTPS autenticado/restrito diante do bind local. Não altere o bind para LAN sem certificado confiável, firewall restrito e revisão dos cabeçalhos de proxy. O instalador não abre firewall.

## Segredos e permissões

O instalador cria `C:\ProgramData\ProtheusPulse\secrets\jwt.key` com aleatoriedade criptográfica. O serviço lê o arquivo por `PULSE_JWT_SIGNING_KEY_FILE`; o valor não entra em `appsettings.json`, logs ou registro. Não exiba nem copie esse arquivo.

As chaves do ASP.NET Core Data Protection são protegidas por DPAPI da máquina e por ACL. Banco, chaves, logs e backup devem continuar restritos a administradores, `SYSTEM` e à conta do serviço.

## Desinstalação

A desinstalação pelo menu Aplicativos do Windows remove o serviço e os binários, mas preserva `C:\ProgramData\ProtheusPulse`. Para apagar permanentemente banco, logs, chave JWT e chaves DPAPI, primeiro desinstale o produto, faça o backup necessário e remova explicitamente essa pasta com uma sessão administrativa.

Essa operação é irreversível após a confirmação.
