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
$width = ([int64]$bytes[16] -shl 24) -bor ([int64]$bytes[17] -shl 16) -bor ([int64]$bytes[18] -shl 8) -bor [int64]$bytes[19]
$height = ([int64]$bytes[20] -shl 24) -bor ([int64]$bytes[21] -shl 16) -bor ([int64]$bytes[22] -shl 8) -bor [int64]$bytes[23]
if ($width -lt 640 -or $height -lt 360 -or $width -gt 12000 -or $height -gt 12000) {
    throw "Unexpected PNG dimensions: $width x $height"
}
$repo = Split-Path -Parent $PSScriptRoot
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw 'Git is required to validate the captured build commit.' }
& git -C $repo cat-file -e "$VerifiedCommit^{commit}" 2>$null
if ($LASTEXITCODE -ne 0) { throw "Commit is not present in this repository: $VerifiedCommit" }
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
