# Instalador

`ProtheusPulse.iss` gera o instalador Inno Setup 6 x64. Ele instala os binários, registra o serviço `ProtheusPulse` como `LocalService`, cria um segredo JWT aleatório com ACL restrita, configura recuperação automática e valida `/health/ready`.

Gere os artefatos em um Windows com .NET 8, Node.js 24 e, opcionalmente, Inno Setup 6:

```powershell
.\scripts\build-release.ps1 -Version 0.1.1
```

O ZIP self-contained é sempre produzido. Quando `ISCC.exe` está instalado, o script também produz `protheus-pulse-0.1.1-win-x64-setup.exe`. Cada pacote recebe um arquivo `.sha256`. No ZIP, execute `install.cmd`; ele solicita elevação e chama o instalador PowerShell com política temporária.

Os dados ficam fora da pasta do programa em `C:\ProgramData\ProtheusPulse` e são preservados por atualização e desinstalação normal. A remoção de dados só ocorre com chamada administrativa explícita a `uninstall-service.ps1 -RemoveData`.
