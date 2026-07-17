[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [string]$DataDirectory = (Join-Path $env:ProgramData 'ProtheusPulse'),
    [string]$ServiceName = 'ProtheusPulse',
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Execute este script em um PowerShell elevado (Executar como administrador).'
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $service -and $PSCmdlet.ShouldProcess($ServiceName, 'Parar e remover o serviço Windows')) {
    if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        Stop-Service -Name $ServiceName -Force
        $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(30))
    }

    & "$env:SystemRoot\System32\sc.exe" delete $ServiceName | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao remover o serviço (código $LASTEXITCODE)."
    }
}

if ($RemoveData) {
    $dataPath = [IO.Path]::GetFullPath($DataDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $root = [IO.Path]::GetPathRoot($dataPath).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if ($dataPath -eq $root -or $dataPath.Length -lt ($root.Length + 4)) {
        throw 'DataDirectory aponta para um diretório amplo demais; os dados não foram removidos.'
    }

    if ((Test-Path -LiteralPath $dataPath) -and $PSCmdlet.ShouldProcess($dataPath, 'Remover permanentemente banco, chaves e logs')) {
        Remove-Item -LiteralPath $dataPath -Recurse -Force
        Write-Host "Dados removidos permanentemente de: $dataPath"
    }
}
else {
    Write-Host 'Serviço removido. Banco, chaves e logs foram preservados.'
}
