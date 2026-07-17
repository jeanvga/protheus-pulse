[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
try {
    npm ci
    npm run ui:test
    npm run ui:build
    dotnet restore .\ProtheusPulse.sln
    dotnet build .\ProtheusPulse.sln --configuration $Configuration --no-restore
    dotnet test .\ProtheusPulse.sln --configuration $Configuration --no-build
}
finally {
    Pop-Location
}
