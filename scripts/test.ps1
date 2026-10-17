[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet restore .\tests\Meridian.Core.Tests\Meridian.Core.Tests.csproj --locked-mode
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }
    dotnet test .\tests\Meridian.Core.Tests\Meridian.Core.Tests.csproj -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE" }
}
finally { Pop-Location }
