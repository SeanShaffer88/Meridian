[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
function Assert-Refused([scriptblock]$Action) {
    $refused = $false
    try { & $Action } catch { $refused = $true }
    if (!$refused) { throw 'Expected operation to be refused.' }
}
$root = Join-Path (Split-Path -Parent $PSScriptRoot) ('TestResults/packaging-' + [guid]::NewGuid().ToString('N'))
$publish = New-Item -ItemType Directory -Path (Join-Path $root 'publish') -Force
$output = New-Item -ItemType Directory -Path (Join-Path $root 'artifacts') -Force
$packageScript = Join-Path $PSScriptRoot 'package.ps1'
$verifyScript = Join-Path $PSScriptRoot 'verify-checksums.ps1'
[IO.File]::WriteAllText((Join-Path $publish 'Meridian.exe'), 'fixture executable')
[IO.File]::WriteAllText((Join-Path $publish 'Meridian.pri'), 'fixture resources')
$hidden = Join-Path $publish '.hidden.txt'
[IO.File]::WriteAllText($hidden, 'hidden payload')
[IO.File]::SetAttributes($hidden, [IO.FileAttributes]::Hidden)
$installer = Join-Path $output 'Meridian-Setup-win-x64.exe'
[IO.File]::WriteAllText($installer, 'fixture installer')
& $packageScript -PublishDirectory $publish -OutputDirectory $output -Architecture x64
$archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $output 'Meridian-Portable-win-x64.zip'))
try {
    if ($archive.Entries.Count -ne 3 -or !$archive.GetEntry('.hidden.txt')) { throw 'ZIP omitted published content.' }
    $reader = [IO.StreamReader]::new($archive.GetEntry('.hidden.txt').Open())
    try { if ($reader.ReadToEnd() -ne 'hidden payload') { throw 'ZIP payload mismatch.' } }
    finally { $reader.Dispose() }
} finally { $archive.Dispose() }
$manifest = Join-Path $output 'SHA256SUMS-win-x64.txt'
$validManifest = [IO.File]::ReadAllText($manifest)
Assert-Refused { & $packageScript -PublishDirectory $publish -OutputDirectory $output -Architecture x64 }
Assert-Refused { & $packageScript -PublishDirectory $publish -OutputDirectory $publish -Architecture x64 }
[IO.File]::WriteAllText($installer, 'changed bytes')
$tamperingDetected = $false
try { & $verifyScript -ManifestPath $manifest }
catch { $tamperingDetected = $_.Exception.Message -like 'SHA-256 mismatch:*' }
if (!$tamperingDetected) { throw 'Tampered artifact was not rejected with a checksum mismatch.' }
[IO.File]::WriteAllText($installer, 'fixture installer')
foreach ($bad in @('', ('0' * 64 + '  ../outside.exe'), ($validManifest + $validManifest), ('0' * 64 + '  missing.exe'))) {
    [IO.File]::WriteAllText($manifest, $bad)
    Assert-Refused { & $verifyScript -ManifestPath $manifest }
}
[IO.File]::WriteAllText($manifest, $validManifest)
& $verifyScript -ManifestPath $manifest
if (@(Get-ChildItem -LiteralPath $output -Force -Filter '.meridian-*.tmp').Count -ne 0) { throw 'Temporary packaging files remain.' }
Write-Host "Packaging checks passed. Disposable fixtures retained under $root"
