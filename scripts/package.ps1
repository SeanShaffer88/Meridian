[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][ValidateSet('x64', 'arm64')][string]$Architecture
)
$ErrorActionPreference = 'Stop'
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$output = (Resolve-Path -LiteralPath $OutputDirectory).Path
$relativeOutput = [IO.Path]::GetRelativePath($publish, $output)
if ($relativeOutput -eq '.' -or (![IO.Path]::IsPathRooted($relativeOutput) -and
    $relativeOutput -ne '..' -and !$relativeOutput.StartsWith('..' + [IO.Path]::DirectorySeparatorChar))) {
    throw 'Artifact output must be outside the publish directory.'
}
foreach ($required in @('Meridian.exe', 'Meridian.pri')) {
    if (!(Test-Path -LiteralPath (Join-Path $publish $required) -PathType Leaf)) { throw "Missing published file: $required" }
}
$installerName = "Meridian-Setup-win-$Architecture.exe"
$zipName = "Meridian-Portable-win-$Architecture.zip"
$manifestName = "SHA256SUMS-win-$Architecture.txt"
$installer = Join-Path $output $installerName
if (!(Test-Path -LiteralPath $installer -PathType Leaf)) { throw "Missing installer: $installerName" }
$zip = Join-Path $output $zipName
$manifest = Join-Path $output $manifestName
foreach ($target in @($zip, $manifest)) {
    if (Test-Path -LiteralPath $target) { throw "Artifact already exists: $target" }
}
$stagedZip = Join-Path $output ('.meridian-package-' + [guid]::NewGuid().ToString('N') + '.tmp')
$stagedManifest = Join-Path $output ('.meridian-checksums-' + [guid]::NewGuid().ToString('N') + '.tmp')
try {
    [IO.Compression.ZipFile]::CreateFromDirectory($publish, $stagedZip)
    $lines = @(
        (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $installerName
        (Get-FileHash -LiteralPath $stagedZip -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $zipName
    )
    [IO.File]::WriteAllLines($stagedManifest, [string[]]$lines, [Text.Encoding]::ASCII)
    [IO.File]::Move($stagedZip, $zip)
    [IO.File]::Move($stagedManifest, $manifest)
    & (Join-Path $PSScriptRoot 'verify-checksums.ps1') -ManifestPath $manifest
} finally {
    foreach ($staged in @($stagedZip, $stagedManifest)) {
        if (Test-Path -LiteralPath $staged) { Remove-Item -LiteralPath $staged -ErrorAction Stop }
    }
}
