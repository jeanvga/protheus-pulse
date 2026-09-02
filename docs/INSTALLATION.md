# Instalação no Windows Server

## Pacote recomendado

Use preferencialmente `protheus-pulse-<versão>-win-x64-setup.exe`. Ele é um instalador Inno Setup self-contained e não exige abrir o PowerShell. Valide o SHA-256 recebido por um canal confiável antes de executar o arquivo.

O padrão seguro instala:

- binários em `C:\Program Files\Protheus Pulse`;
- banco, logs e chaves em `C:\ProgramData\ProtheusPulse`;
- serviço `ProtheusPulse`, automático atrasado, sob `NT AUTHORITY\SYSTEM` (`LocalSystem`);
- endpoint somente em `http://127.0.0.1:5058`.

O aplicativo é independente do Protheus. Coletores não alteram INI, RPO, banco ou arquivos monitorados. Somente ações explícitas de Administrator, modo manutenção e auto-start alteram o estado dos serviços Windows cadastrados.

## Pré-requisitos

- Windows Server 2016 ou mais recente, x64;
- sessão administrativa apenas durante instalação/atualização;
- porta local 5058 livre;
- revisão dos acessos do `LocalSystem` aos recursos monitorados e do acesso administrativo ao Pulse;
- espaço e política de backup para `C:\ProgramData\ProtheusPulse`.

A publicação é self-contained e não exige instalação separada do .NET. O instalador padrão usa `LocalSystem` porque iniciar/parar serviços e observar processos de outros usuários exige privilégio local elevado. Para UNC, essa conta acessa a rede como a identidade do computador: conceda somente leitura no compartilhamento e no NTFS a `DOMINIO\SERVIDOR$`, ou use uma conta de serviço corporativa aprovada em uma instalação customizada. Não use unidade mapeada e não grave credenciais no Pulse.

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
$package = 'C:\Pacotes\protheus-pulse-1.2.0-win-x64.zip'
(Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
# Compare visualmente com o arquivo .sha256 obtido por canal confiável.

Expand-Archive -LiteralPath $package -DestinationPath 'C:\Pacotes\ProtheusPulse-1.2.0'
Set-Location 'C:\Pacotes\ProtheusPulse-1.2.0\protheus-pulse-1.2.0-win-x64'
.\install.cmd
```

`install.cmd` solicita elevação e aplica bypass do PowerShell somente ao processo de instalação. O script para o serviço anterior em atualizações, repara as ACLs da pasta gerenciada, copia o payload para uma nova pasta versionada, preserva dados, cria a chave JWT caso ainda não exista, registra recuperação automática, inicia o serviço e valida `/health/ready`. O diagnóstico da cópia fica em `C:\ProgramData\ProtheusPulse\logs\install-copy.log`.

Para visualizar as ações sem executá-las:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install-service.ps1 -WhatIf
```

## Acesso de outro computador

O padrão escuta em `127.0.0.1:5058` e só abre no próprio servidor. Em **Configurações → Acesso pela rede** um administrador
libera o painel para a rede: o serviço passa a escutar em todas as interfaces e a tela mostra os endereços `http://ip:porta`
que podem ser digitados de outra máquina. A opção é gravada em `C:\ProgramData\ProtheusPulse\network.json` e **vale a partir
do próximo start do serviço** — reinicie `ProtheusPulse` depois de salvar. Ao ligar, o serviço passa a escutar em
`0.0.0.0` e o filtro de host deixa de exigir `localhost`; com a opção desligada, ambos voltam ao padrão restrito.

O tráfego é HTTP puro: senha e token trafegam legíveis e por essa tela se controla serviço do Windows. Libere apenas em rede
interna confiável. Para acesso amplo, mantenha o bind em loopback e publique por um proxy HTTPS, como descrito acima.

O instalador não cria regra de firewall. Para liberar a porta, em sessão administrativa:

```powershell
netsh advfirewall firewall add rule name="Protheus Pulse" dir=in action=allow protocol=TCP localport=5058
```

## Contas de acesso

Depois do administrador inicial, as demais contas são criadas em **Configurações → Usuários e perfis**: criar, trocar perfil,
desativar, trocar senha e remover. `Administrator` controla serviços e configuração, `Operator` reconhece alertas e opera a
manutenção, `Viewer` só enxerga. A última conta de administrador ativa não pode ser rebaixada, desativada nem removida — sem
ela ninguém conseguiria voltar a abrir essa tela.

## Primeiro acesso

Abra [http://127.0.0.1:5058](http://127.0.0.1:5058) no próprio servidor e crie o primeiro administrador. Use uma senha exclusiva e guarde-a no cofre corporativo.

Toda a configuração do ambiente monitorado é feita nesse endereço: abra **Instalações**, escolha **Adicionar instalação**, informe os alvos de leitura e use **Salvar e monitorar**. A própria tela pesquisa serviços Windows e arquivos dentro da pasta indicada, permite cadastrar portas TCP e endpoints HTTP/HTTPS e oferece **Coletar agora**. Não é necessário executar PowerShell, editar JSON ou alterar `appsettings.json` para cadastrar o Protheus.

Para acesso remoto, coloque um reverse proxy HTTPS autenticado/restrito diante do bind local. Não altere o bind para LAN sem certificado confiável, firewall restrito e revisão dos cabeçalhos de proxy. O instalador não abre firewall.

## Segredos e permissões

O instalador cria `C:\ProgramData\ProtheusPulse\secrets\jwt.key` com aleatoriedade criptográfica. O serviço lê o arquivo por `PULSE_JWT_SIGNING_KEY_FILE`; o valor não entra em `appsettings.json`, logs ou registro. Não exiba nem copie esse arquivo.

As chaves do ASP.NET Core Data Protection são protegidas por DPAPI da máquina e por ACL. Banco, chaves, logs e backup devem continuar restritos a administradores, `SYSTEM` e à conta do serviço.

Como o processo roda como `LocalSystem`, uma conta Administrator do Pulse controla serviços no host. Preserve o bind em loopback, publique acesso remoto somente por reverse proxy HTTPS restrito, mantenha poucos administradores e monitore a auditoria. Se a organização substituir a conta em uma instalação customizada, valide antes as permissões de SCM, processos, arquivos, DPAPI e UNC; a troca não faz parte do instalador padrão.

## Desinstalação

A desinstalação pelo menu Aplicativos do Windows remove o serviço e os binários, mas preserva `C:\ProgramData\ProtheusPulse`. Para apagar permanentemente banco, logs, chave JWT e chaves DPAPI, primeiro desinstale o produto, faça o backup necessário e remova explicitamente essa pasta com uma sessão administrativa.

Essa operação é irreversível após a confirmação.

## HTTPS no painel

Por padrão o painel escuta em `http://127.0.0.1:5058`, acessível só do próprio servidor. Ao ligar **Acesso pela rede** em Configurações, ligue também o **HTTPS**: sem ele, a senha e o token de sessão trafegam em texto claro na rede do cliente.

Em **Configurações › Acesso pela rede** há dois caminhos:

- **Certificado da sua rede** — informe o caminho de um `.pfx` que traga a chave privada. Se ele tiver senha, informe-a no campo próprio; ela é cifrada com Data Protection e guardada fora do `network.json`.
- **Certificado para esta máquina** — o botão gera um certificado autoassinado em `C:\ProgramData\ProtheusPulse\certs`, válido por dois anos, com o nome da máquina, `localhost` e os IPs locais. O navegador avisa que não conhece quem assinou, mas o tráfego deixa de ir em texto claro.

O certificado é conferido ao salvar e a gravação é recusada se ele não servir. Se mesmo assim ele falhar na inicialização — arquivo movido, permissão negada — o serviço sobe em HTTP e registra o motivo em `logs\startup-trace.log`, em vez de ficar inacessível sem caminho de volta pela tela.

Depois de salvar, reinicie o serviço para o novo endereço valer:

```powershell
Restart-Service ProtheusPulse
```

## Backup

**Configurações › Backup** gera um pacote com o banco, a chave de Data Protection e a configuração de rede, e o disponibiliza para download. A chave é indispensável: sem ela, um banco restaurado perde as URLs dos pontos de contato e a senha do SMTP, que ficam cifradas por ela.

Os dez pacotes mais recentes ficam em `C:\ProgramData\ProtheusPulse\backups`. Leve uma cópia para fora da máquina — um backup que mora no disco que falhou não serve.

A restauração é manual de propósito: trocar o banco embaixo de um serviço em execução corrompe o que estiver sendo escrito. O passo a passo vai dentro do pacote, no `LEIAME.txt`.
