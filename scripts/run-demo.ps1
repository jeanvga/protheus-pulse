[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
try {
    npm ci
    npm run ui:build
    dotnet run --project .\src\ProtheusPulse.Service -- --demo
}
finally {
    Pop-Location
}
