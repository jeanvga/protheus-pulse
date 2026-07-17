[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.1.3',
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',
    [switch]$SkipTests,
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$releaseRoot = Join-Path $repositoryRoot 'artifacts\release'
$packageRoot = Join-Path $releaseRoot "protheus-pulse-$Version-$Runtime"
$applicationRoot = Join-Path $packageRoot 'app'
$zipPath = "$packageRoot.zip"

Push-Location $repositoryRoot
try {
    if (Test-Path -LiteralPath $releaseRoot) {
        Remove-Item -LiteralPath $releaseRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Path $applicationRoot -Force | Out-Null
    npm ci
    npm audit --audit-level=moderate
    npm run ui:test
    npm run ui:build
    dotnet restore .\ProtheusPulse.sln --runtime $Runtime
    dotnet build .\ProtheusPulse.sln --configuration Release --no-restore
    if (-not $SkipTests) {
        dotnet test .\ProtheusPulse.sln --configuration Release --no-build
    }

    dotnet clean .\src\ProtheusPulse.Service --configuration Release --runtime $Runtime
    dotnet publish .\src\ProtheusPulse.Service `
        --configuration Release `
        --runtime $Runtime `
        --self-contained true `
        --no-restore `
        --output $applicationRoot `
        /p:SkipFrontendBuild=true `
        /p:Version=$Version `
        /p:ContinuousIntegrationBuild=true `
        /p:DebugType=None `
        /p:DebugSymbols=false

    Copy-Item -LiteralPath .\scripts\install-service.ps1 -Destination $packageRoot
    Copy-Item -LiteralPath .\scripts\install.cmd -Destination $packageRoot
    Copy-Item -LiteralPath .\scripts\uninstall-service.ps1 -Destination $packageRoot
    Copy-Item -LiteralPath .\docs\PILOT-CHECKLIST.md -Destination $packageRoot
    Copy-Item -LiteralPath .\LICENSE -Destination $packageRoot
    Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -CompressionLevel Optimal
    $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    [IO.File]::WriteAllText("$zipPath.sha256", "$zipHash  $([IO.Path]::GetFileName($zipPath))`r`n")

    if (-not $SkipInstaller) {
        $programDirectories = @(
            [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86),
            [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
        ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        $compilerCandidates = $programDirectories |
            ForEach-Object { [IO.Path]::Combine($_, 'Inno Setup 6\ISCC.exe') } |
            Where-Object { Test-Path -LiteralPath $_ }
        $compiler = $compilerCandidates | Select-Object -First 1
        if ($null -ne $compiler) {
            & $compiler "/DMyAppVersion=$Version" "/DSourceDirectory=$applicationRoot" "/DOutputDirectory=$releaseRoot" .\installer\ProtheusPulse.iss
            if ($LASTEXITCODE -ne 0) {
                throw "O Inno Setup falhou com código $LASTEXITCODE."
            }

            Get-ChildItem -LiteralPath $releaseRoot -Filter '*.exe' | ForEach-Object {
                $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
                [IO.File]::WriteAllText("$($_.FullName).sha256", "$hash  $($_.Name)`r`n")
            }
        }
        else {
            Write-Warning 'Inno Setup 6 não encontrado; o ZIP instalável foi gerado e o .exe foi ignorado.'
        }
    }

    Write-Host "Pacote gerado em $zipPath"
    Write-Host "SHA-256: $zipHash"
}
finally {
    Pop-Location
}
