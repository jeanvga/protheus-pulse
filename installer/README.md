# Instalador

`ProtheusPulse.iss` gera um único instalador EXE x64. O fluxo normal não chama PowerShell: o executável publicado registra `ProtheusPulse` como `LocalSystem`, grava o `ImagePath` com aspas, cria o segredo JWT com ACL restrita, configura recuperação automática e valida `/health/ready`. A conta elevada é necessária para as ações administrativas explícitas de iniciar e parar serviços monitorados.

Gere os artefatos em um Windows com .NET 8, Node.js 24 e Inno Setup 6.6 ou mais recente:

```powershell
.\scripts\build-release.ps1 -Version 1.1.0
```

O script produz `protheus-pulse-1.1.0-win-x64-setup.exe`, o ZIP técnico alternativo e um `.sha256` para cada pacote. Se o compilador não estiver instalado, use `winget install --id JRSoftware.InnoSetup -e`. O workflow valida os artefatos em Windows e os publica em GitHub Releases quando a tag `v1.1.0` é criada.

O artefato só recebe assinatura Authenticode quando um certificado de assinatura de código confiável é configurado no ambiente de release. Não armazene certificado ou senha no repositório.

Os dados ficam fora da pasta do programa em `C:\ProgramData\ProtheusPulse` e são preservados por atualização e desinstalação normal. Em caso de falha, consulte `C:\ProgramData\ProtheusPulse\logs\install-diagnostics.txt`.
