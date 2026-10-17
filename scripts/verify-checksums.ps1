[CmdletBinding()]
param([Parameter(Mandatory)][string]$ManifestPath)
$ErrorActionPreference = 'Stop'
$manifest = (Resolve-Path -LiteralPath $ManifestPath).Path
$directory = Split-Path -Parent $manifest
$entries = @([IO.File]::ReadAllLines($manifest))
if ($entries.Count -eq 0) { throw 'Checksum manifest is empty.' }
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($line in $entries) {
    if ($line -notmatch '^([0-9a-fA-F]{64})  ([A-Za-z0-9][A-Za-z0-9._-]*)$') {
        throw 'Invalid checksum manifest entry; expected SHA-256 and a plain filename.'
    }
    $expectedHash = $Matches[1]
    $name = $Matches[2]
    if ($name.EndsWith('.') -or !$seen.Add($name)) { throw "Invalid or duplicate artifact name: $name" }
    $path = Join-Path $directory $name
    $item = Get-Item -LiteralPath $path -Force -ErrorAction Stop
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Artifact must be a regular file: $name"
    }
    $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if (![string]::Equals($expectedHash, $actualHash, [StringComparison]::OrdinalIgnoreCase)) {
        throw "SHA-256 mismatch: $name"
    }
    Write-Host "Verified SHA-256: $name"
}
