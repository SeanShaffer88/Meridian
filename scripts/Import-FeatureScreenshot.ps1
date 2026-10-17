param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('dual-pane-workspace','folder-dna','drop-shelf','operation-simulator','universal-viewer-text','universal-viewer-image','universal-viewer-video','timevault-snapshot-picker')]
    [string] $Feature,
    [Parameter(Mandatory = $true)]
    [string] $Source,
    [Parameter(Mandatory = $true)]
    [string] $VerifiedCommit,
    [string] $WindowsVersion = 'Windows 11',
    [switch] $ConfirmedRealCapture
)

$ErrorActionPreference = 'Stop'
if (-not $ConfirmedRealCapture) {
    throw 'Only authentic captures of the running application are accepted. Specify -ConfirmedRealCapture after visually verifying the screenshot.'
}
if ($VerifiedCommit -notmatch '^[a-fA-F0-9]{40}$') {
    throw 'VerifiedCommit must be a full 40-character Git commit SHA.'
}
$resolved = (Resolve-Path -LiteralPath $Source).Path
$bytes = [System.IO.File]::ReadAllBytes($resolved)
if ($bytes.Length -lt 24) { throw 'Image is too small to be a valid PNG.' }
$pngSignature = [byte[]](137,80,78,71,13,10,26,10)
for ($i=0; $i -lt 8; $i++) {
    if ($bytes[$i] -ne $pngSignature[$i]) { throw 'Source must be a PNG screenshot.' }
}
$width = [System.Buffers.Binary.BinaryPrimitives]::ReadInt32BigEndian([ReadOnlySpan[byte]]$bytes[16..19])
$height = [System.Buffers.Binary.BinaryPrimitives]::ReadInt32BigEndian([ReadOnlySpan[byte]]$bytes[20..23])
if ($width -lt 640 -or $height -lt 360 -or $width -gt 12000 -or $height -gt 12000) {
    throw "Unexpected PNG dimensions: $width x $height"
}
$repo = Split-Path -Parent $PSScriptRoot
$destinationDirectory = Join-Path $repo 'docs/images/screenshots'
New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
$destination = Join-Path $destinationDirectory "$Feature.png"
if (Test-Path -LiteralPath $destination) { throw "Refusing to overwrite an existing screenshot: $destination" }
Copy-Item -LiteralPath $resolved -Destination $destination
$sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Verified image staged: $destination"
Write-Host "Dimensions: $width x $height; SHA-256: $sha256"
Write-Host "Commit: $VerifiedCommit; Platform: $WindowsVersion"
Write-Host "Next: review for personal data, add an accessible README image/caption, and commit the screenshot with verification details."
