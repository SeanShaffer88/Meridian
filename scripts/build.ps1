[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release', [ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$platform = if ($Runtime -eq 'win-arm64') { 'ARM64' } else { 'x64' }
Push-Location $root
try {
    dotnet restore .\Meridian.sln
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }
    dotnet publish .\src\Meridian.App\Meridian.App.csproj -c $Configuration -r $Runtime --self-contained true -p:Platform=$platform
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
    Write-Host "Publish output is under src\Meridian.App\bin\$platform\$Configuration\net10.0-windows10.0.22000.0\$Runtime\publish."
}
finally { Pop-Location }
