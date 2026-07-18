[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$SourceDirectory,
    [string]$InstallDirectory,
    [string]$DataDirectory,
    [string]$ServiceName = 'ProtheusPulse',
    [ValidateSet('NT AUTHORITY\LocalService')]
    [string]$ServiceAccount = 'NT AUTHORITY\LocalService',
    [switch]$SkipHealthCheck
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptDirectory = if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) {
    [IO.Path]::GetDirectoryName($MyInvocation.MyCommand.Path)
}
else {
    $PSScriptRoot
}
$programFilesDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$commonDataDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
$windowsDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)
if ([string]::IsNullOrWhiteSpace($scriptDirectory) -or
    [string]::IsNullOrWhiteSpace($programFilesDirectory) -or
    [string]::IsNullOrWhiteSpace($commonDataDirectory) -or
    [string]::IsNullOrWhiteSpace($windowsDirectory)) {
    throw 'O Windows não informou uma ou mais pastas especiais necessárias para a instalação.'
}

if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
    $SourceDirectory = [IO.Path]::Combine($scriptDirectory, 'app')
}
if ([string]::IsNullOrWhiteSpace($InstallDirectory)) {
    $InstallDirectory = [IO.Path]::Combine($programFilesDirectory, 'Protheus Pulse')
}
if ([string]::IsNullOrWhiteSpace($DataDirectory)) {
    $DataDirectory = [IO.Path]::Combine($commonDataDirectory, 'ProtheusPulse')
}
$systemDirectory = [IO.Path]::Combine($windowsDirectory, 'System32')

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Execute este script em um PowerShell elevado (Executar como administrador).'
    }
}

function Resolve-ManagedDirectory([string]$Path, [string]$Label) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "$Label não pode ficar vazio."
    }

    $resolved = [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $root = [IO.Path]::GetPathRoot($resolved).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if ($resolved -eq $root -or $resolved.Length -lt ($root.Length + 4)) {
        throw "$Label aponta para um diretório amplo demais."
    }

    return $resolved
}

function Invoke-Icacls([string[]]$Arguments) {
    & ([IO.Path]::Combine($systemDirectory, 'icacls.exe')) @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao aplicar ACL com icacls (código $LASTEXITCODE)."
    }
}

function Test-ServiceExists([string]$Name) {
    return $null -ne (Get-Service -Name $Name -ErrorAction SilentlyContinue)
}

function Initialize-InstallDirectoryAcl([string]$Path) {
    & ([IO.Path]::Combine($systemDirectory, 'takeown.exe')) /F $Path /A /R /D Y | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao assumir a propriedade da pasta de instalação (código $LASTEXITCODE)."
    }

    Invoke-Icacls @($Path, '/reset', '/T', '/C', '/Q')
    Invoke-Icacls @($Path, '/inheritance:r', '/grant:r', '*S-1-5-18:(OI)(CI)F', '*S-1-5-32-544:(OI)(CI)F', '*S-1-5-32-545:(OI)(CI)RX', '*S-1-5-19:(OI)(CI)RX', '/T', '/C', '/Q')
}

Assert-Administrator
$installPath = Resolve-ManagedDirectory $InstallDirectory 'InstallDirectory'
$dataPath = Resolve-ManagedDirectory $DataDirectory 'DataDirectory'
$sourcePath = [IO.Path]::GetFullPath($SourceDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
$secretDirectory = Join-Path $dataPath 'secrets'
$keyDirectory = Join-Path $dataPath 'keys'
$logDirectory = Join-Path $dataPath 'logs'
$jwtKeyPath = Join-Path $secretDirectory 'jwt.key'
$sourceExecutable = Join-Path $sourcePath 'ProtheusPulse.Service.exe'

if (-not (Test-Path -LiteralPath $sourceExecutable -PathType Leaf)) {
    throw 'O pacote não contém ProtheusPulse.Service.exe no diretório app.'
}

if (-not $PSCmdlet.ShouldProcess($installPath, 'Instalar o Protheus Pulse e registrar o serviço')) {
    return
}

if (Test-ServiceExists $ServiceName) {
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    $service = Get-Service -Name $ServiceName
    $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(30))
}

New-Item -ItemType Directory -Path $installPath, $dataPath, $secretDirectory, $keyDirectory, $logDirectory -Force | Out-Null
Initialize-InstallDirectoryAcl $installPath
Invoke-Icacls @($dataPath, '/inheritance:r', '/grant:r', '*S-1-5-18:(OI)(CI)F', '*S-1-5-32-544:(OI)(CI)F', '*S-1-5-19:(OI)(CI)M', '/T', '/C')
Invoke-Icacls @($secretDirectory, '/inheritance:r', '/grant:r', '*S-1-5-18:(OI)(CI)F', '*S-1-5-32-544:(OI)(CI)F', '*S-1-5-19:(OI)(CI)R', '/T', '/C')
Invoke-Icacls @($keyDirectory, '/inheritance:r', '/grant:r', '*S-1-5-18:(OI)(CI)F', '*S-1-5-32-544:(OI)(CI)F', '*S-1-5-19:(OI)(CI)M', '/T', '/C')

$deploymentPath = $installPath
if (-not [string]::Equals($sourcePath, $installPath, [StringComparison]::OrdinalIgnoreCase)) {
    $sourceVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($sourceExecutable).FileVersion
    if ([string]::IsNullOrWhiteSpace($sourceVersion)) {
        $sourceVersion = 'unknown'
    }
    $safeVersion = [Text.RegularExpressions.Regex]::Replace($sourceVersion, '[^0-9A-Za-z._-]', '_')
    $deploymentName = '{0}-{1}-{2}' -f $safeVersion, [DateTime]::UtcNow.ToString('yyyyMMddHHmmss'), [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $deploymentPath = [IO.Path]::Combine($installPath, 'releases', $deploymentName)
    New-Item -ItemType Directory -Path $deploymentPath -Force | Out-Null

    $writeProbePath = [IO.Path]::Combine($deploymentPath, '.write-test')
    [IO.File]::WriteAllText($writeProbePath, 'ok')
    Remove-Item -LiteralPath $writeProbePath -Force

    $copyLogPath = [IO.Path]::Combine($logDirectory, 'install-copy.log')
    & ([IO.Path]::Combine($systemDirectory, 'robocopy.exe')) $sourcePath $deploymentPath '*.*' /E /COPY:DAT /DCOPY:DAT /R:3 /W:2 /NP /NFL /NDL /TEE "/LOG:$copyLogPath" | Out-Null
    $robocopyExitCode = $LASTEXITCODE
    if ($robocopyExitCode -gt 7) {
        throw "Falha ao copiar o payload com robocopy (código $robocopyExitCode). Consulte $copyLogPath."
    }
}

Get-ChildItem -LiteralPath $deploymentPath -Recurse -File -Force | Unblock-File -ErrorAction SilentlyContinue

$executablePath = Join-Path $deploymentPath 'ProtheusPulse.Service.exe'
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "O executável do serviço não foi encontrado em $installPath."
}

Invoke-Icacls @($installPath, '/inheritance:r', '/grant:r', '*S-1-5-18:(OI)(CI)F', '*S-1-5-32-544:(OI)(CI)F', '*S-1-5-32-545:(OI)(CI)RX', '*S-1-5-19:(OI)(CI)RX', '/T', '/C', '/Q')

if (-not (Test-Path -LiteralPath $jwtKeyPath -PathType Leaf)) {
    $buffer = New-Object byte[] 64
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($buffer)
        $key = [Convert]::ToBase64String($buffer)
        $utf8WithoutBom = [Text.UTF8Encoding]::new($false)
        [IO.File]::WriteAllText($jwtKeyPath, $key, $utf8WithoutBom)
    }
    finally {
        $generator.Dispose()
        [Array]::Clear($buffer, 0, $buffer.Length)
    }
}
Invoke-Icacls @($jwtKeyPath, '/inheritance:r', '/grant:r', '*S-1-5-18:F', '*S-1-5-32-544:F', '*S-1-5-19:R')

$quotedExecutable = '"{0}"' -f $executablePath
if (Test-ServiceExists $ServiceName) {
    & ([IO.Path]::Combine($systemDirectory, 'sc.exe')) config $ServiceName 'binPath=' $quotedExecutable 'start=' 'delayed-auto' 'obj=' $ServiceAccount | Out-Null
}
else {
    & ([IO.Path]::Combine($systemDirectory, 'sc.exe')) create $ServiceName 'binPath=' $quotedExecutable 'start=' 'delayed-auto' 'obj=' $ServiceAccount | Out-Null
}
if ($LASTEXITCODE -ne 0) {
    throw "Falha ao criar ou atualizar o serviço (código $LASTEXITCODE)."
}

& ([IO.Path]::Combine($systemDirectory, 'sc.exe')) description $ServiceName 'Monitoramento técnico local e somente leitura do ambiente Protheus' | Out-Null
& ([IO.Path]::Combine($systemDirectory, 'sc.exe')) failure $ServiceName 'reset=' '86400' 'actions=' 'restart/5000/restart/15000/restart/60000' | Out-Null
& ([IO.Path]::Combine($systemDirectory, 'sc.exe')) failureflag $ServiceName '1' | Out-Null

$serviceRegistryPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
$environment = @(
    'DOTNET_ENVIRONMENT=Production',
    "Pulse__DataDirectory=$dataPath",
    "PULSE_JWT_SIGNING_KEY_FILE=$jwtKeyPath"
)
New-ItemProperty -Path $serviceRegistryPath -Name Environment -PropertyType MultiString -Value $environment -Force | Out-Null
New-ItemProperty -Path $serviceRegistryPath -Name DelayedAutostart -PropertyType DWord -Value 1 -Force | Out-Null

Start-Service -Name $ServiceName
$runningService = Get-Service -Name $ServiceName
$runningService.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running, [TimeSpan]::FromSeconds(30))

if (-not $SkipHealthCheck) {
    $healthy = $false
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri 'http://127.0.0.1:5058/health/ready' -UseBasicParsing -TimeoutSec 3
            if ($response.StatusCode -eq 200) {
                $healthy = $true
                break
            }
        }
        catch {
            Start-Sleep -Seconds 1
        }
    }

    if (-not $healthy) {
        throw 'O serviço foi iniciado, mas o health check /health/ready não respondeu com sucesso.'
    }
}

Write-Host 'Protheus Pulse instalado e saudável em http://127.0.0.1:5058/'
Write-Host "Dados preservados em: $dataPath"
