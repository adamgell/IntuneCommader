<#
.SYNOPSIS
  DEPRECATED — superseded by Master Packager Dev (mpdev): packaging/installer.mpdev.json builds the
  signed MSI + MSIX in .github/workflows/cmprojectx-codesign.yml. This MakeAppx-based script is kept
  only as a manual/offline MSIX fallback. See packaging/README.md.

  M12 packaging — build a signed MSIX from a staged bundle (WinUI client app.exe + sidecar).

.DESCRIPTION
  Kept SEPARATE from release.ps1 (which ships the signed self-contained ZIPs) so the working
  ZIP release is never at risk. This produces the optional MSIX/App-Installer distribution.

  It takes a directory already staged by release.ps1 (app.exe, Api.exe, and their runtime
  files), lays the AppxManifest.xml (packaging/AppxManifest.xml, placeholders substituted)
  and the tile assets next to it, then runs MakeAppx + SignTool from the Windows SDK.

  NOT EXERCISED IN CI / this repo: it requires the Windows 10/11 SDK (MakeAppx.exe,
  SignTool.exe) on PATH, a code-signing certificate (PFX or a cert in the store whose
  subject matches -Publisher), and — for the primary arm64 target — the arm64 toolchain.
  Run it in the signing environment. Without those it fails fast with a clear message.

.PARAMETER StageDir
  The staged bundle directory (the per-arch $stage release.ps1 built). Required.

.PARAMETER Version
  4-part MSIX version "Major.Minor.Build.Revision" (MSIX requires 4 parts). Required.

.PARAMETER Arch
  x64 | arm64. Maps to the MSIX ProcessorArchitecture. Default arm64 (primary target).

.PARAMETER Publisher
  The package identity Publisher — MUST equal the signing certificate subject,
  e.g. "CN=gellorg, O=gellorg, C=US". Required.

.PARAMETER Name
  Package Identity Name (Store/enterprise identity). Default "gellorg.cmProjectX".

.PARAMETER CertPath
  Path to a code-signing PFX. If omitted, signing is skipped (an UNSIGNED .msix is produced
  for local inspection only — Windows will not install it without a trusted signature).

.PARAMETER CertPassword
  PFX password (SecureString-friendly; passed to SignTool /p).

.EXAMPLE
  .\scripts\package-msix.ps1 -StageDir .\dist\stage-arm64 -Version 0.2.0.0 -Arch arm64 `
      -Publisher "CN=gellorg" -CertPath .\signing\cmpx.pfx
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$StageDir,
    [Parameter(Mandatory)] [string]$Version,
    [ValidateSet('x64', 'arm64')] [string]$Arch = 'arm64',
    [Parameter(Mandatory)] [string]$Publisher,
    [string]$Name = 'gellorg.IntuneCommander',
    [string]$CertPath,
    [string]$CertPassword
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path $PSScriptRoot -Parent

function Info($m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Die($m) { Write-Host "ERR  $m" -ForegroundColor Red; exit 1 }

# ── preconditions ─────────────────────────────────────────────────────────────
if (-not (Test-Path $StageDir)) { Die "StageDir '$StageDir' does not exist (run release.ps1 first)." }
if (-not (Test-Path (Join-Path $StageDir 'app.exe'))) { Die "StageDir has no app.exe — is it a client bundle?" }
if ($Version.Split('.').Count -ne 4) { Die "Version must be 4-part 'A.B.C.D' for MSIX (got '$Version')." }

$makeappx = (Get-Command MakeAppx.exe -ErrorAction SilentlyContinue)?.Source
if (-not $makeappx) { Die "MakeAppx.exe not on PATH — install the Windows 10/11 SDK, or add its bin to PATH." }

# ── stage the manifest + assets alongside the bundle ────────────────────────────
$manifestSrc = Join-Path $RepoRoot 'packaging/AppxManifest.xml'
if (-not (Test-Path $manifestSrc)) { Die "packaging/AppxManifest.xml missing." }

Info "Substituting manifest placeholders (Name/Publisher/Version/Arch)"
$manifest = (Get-Content $manifestSrc -Raw).
    Replace('__NAME__', $Name).
    Replace('__PUBLISHER__', $Publisher).
    Replace('__VERSION__', $Version).
    Replace('__ARCH__', $Arch)
Set-Content -Path (Join-Path $StageDir 'AppxManifest.xml') -Value $manifest -Encoding UTF8

# Tile assets: MakeAppx requires the Square/Wide/Store logos referenced by the manifest.
$assetsSrc = Join-Path $RepoRoot 'packaging/Assets'
$assetsDst = Join-Path $StageDir 'Assets'
if (Test-Path $assetsSrc) {
    New-Item -ItemType Directory -Force -Path $assetsDst | Out-Null
    Copy-Item -Path (Join-Path $assetsSrc '*') -Destination $assetsDst -Recurse -Force
}
else {
    Write-Host "WARN packaging/Assets not found — add tile PNGs (Square44x44/150x150, Wide310x150, StoreLogo) before packing." -ForegroundColor Yellow
}

# ── pack ────────────────────────────────────────────────────────────────────────
$outDir = Join-Path $RepoRoot 'dist'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$msix = Join-Path $outDir "IntuneCommander-$Version-$Arch.msix"

Info "MakeAppx pack -> $(Split-Path $msix -Leaf)"
& $makeappx pack /d $StageDir /p $msix /overwrite
if ($LASTEXITCODE -ne 0) { Die "MakeAppx pack failed." }

# ── sign (optional) ───────────────────────────────────────────────────────────
if ($CertPath) {
    $signtool = (Get-Command SignTool.exe -ErrorAction SilentlyContinue)?.Source
    if (-not $signtool) { Die "SignTool.exe not on PATH — install the Windows SDK." }
    Info "SignTool sign (SHA256)"
    $args = @('sign', '/fd', 'SHA256', '/a', '/f', $CertPath)
    if ($CertPassword) { $args += @('/p', $CertPassword) }
    $args += $msix
    & $signtool @args
    if ($LASTEXITCODE -ne 0) { Die "SignTool sign failed." }
    Write-Host "OK   signed $msix" -ForegroundColor Green
}
else {
    Write-Host "WARN produced UNSIGNED $msix — pass -CertPath to sign (Windows won't install unsigned)." -ForegroundColor Yellow
}
