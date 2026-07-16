#requires -Version 7.0
<#
.SYNOPSIS
  Local release builder for IntuneCommander.

.DESCRIPTION
  The "go script". Builds the Rust/WinUI client + the .NET sidecar for each
  target architecture, stages a runnable bundle, zips it per-arch, and (unless
  -DryRun) publishes a GitHub Release tagged v<Version> with the zips attached
  to the release repo (default: gellorg/intunecommander-release).

  Run it from anywhere -- it resolves the repo root from its own location, so it
  must live under <repo-root>/scripts/. Build the source repo, publish to the
  release repo; the two are kept separate on purpose.

  The Rust client depends on sibling checkouts (../windows-rs, ../cmtraceopen)
  via path deps in app/Cargo.toml -- they must sit next to the repo root or the
  cargo build fails. The script preflights for them.

.PARAMETER Version
  Semver for this release, e.g. 0.2.0 (no leading "v"). The git tag is v<Version>.

.PARAMETER Arch
  Architectures to build. Default: both. arm64 is the primary target; pass
  -Arch arm64 to build only it (cross-building x64 needs the x64 MSVC tools).

.PARAMETER SelfContained
  Bundle the .NET runtime into the sidecar so end users don't need .NET 10
  installed. Default $true. (The WinUI client still needs the Windows App SDK
  runtime regardless -- it links framework-dependent.)

.PARAMETER Stable
  Publish as a normal release. Default (omitted) marks it a prerelease.

.PARAMETER DryRun
  Build + package only; skip the GitHub Release. Artifacts land in ./dist.

.PARAMETER Force
  Replace an existing release/tag of the same version.

.EXAMPLE
  .\scripts\release.ps1 -Version 0.2.0
  Build both arches and publish prerelease v0.2.0.

.EXAMPLE
  .\scripts\release.ps1 -Version 0.2.0 -Arch arm64 -DryRun
  Build just the arm64 bundle into ./dist, don't publish.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$')]
    [string]$Version,

    [ValidateSet('arm64', 'x64')]
    [string[]]$Arch = @('arm64', 'x64'),

    [string]$ReleaseRepo = 'gellorg/intunecommander-release',

    [bool]$SelfContained = $true,

    # Inline release-notes text, or a path to a markdown file. Defaults to a stub.
    [string]$Notes,

    # Working copy of the release repo for doc sync. Default: sibling
    # 'intunecommander-release' next to the repo root.
    [string]$ReleaseRepoPath,

    # Don't copy curated docs into the release repo after publishing.
    [switch]$SkipDocsSync,

    [switch]$Stable,
    [switch]$DryRun,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---- arch maps ------------------------------------------------------------
$RustTriple = @{ arm64 = 'aarch64-pc-windows-msvc'; x64 = 'x86_64-pc-windows-msvc' }
$DotnetRid  = @{ arm64 = 'win-arm64'; x64 = 'win-x64' }

# Docs copied from this repo into the release repo. Curated on purpose -- keep
# internal planning docs (MVP-PLAN, ROADMAP, CACHE-*, PLUGINS-MCP) OUT of the
# release channel.
$ReleaseDocs = @('RUNBOOK.md')

# ---- paths ----------------------------------------------------------------
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$DistRoot = Join-Path $RepoRoot 'dist'
$Tag      = "v$Version"

# ---- logging --------------------------------------------------------------
function Info($m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Ok($m)   { Write-Host "OK   $m" -ForegroundColor Green }
function Warn($m) { Write-Host "WARN $m" -ForegroundColor Yellow }
function Die($m)  { Write-Host "ERR  $m" -ForegroundColor Red; exit 1 }

function Need($exe, $hint) {
    if (-not (Get-Command $exe -ErrorAction SilentlyContinue)) {
        Die "$exe not found on PATH. $hint"
    }
}

# Copy curated docs into the release repo and commit/push if anything changed.
function Sync-ReleaseDocs {
    param([string]$RepoPath)

    if (-not (Test-Path $RepoPath)) {
        Warn "release repo not found at '$RepoPath' -- skipping doc sync"
        return
    }
    if (-not (Test-Path (Join-Path $RepoPath '.git'))) {
        Warn "'$RepoPath' is not a git checkout -- skipping doc sync"
        return
    }

    $destDocs = Join-Path $RepoPath 'docs'
    New-Item -ItemType Directory -Path $destDocs -Force | Out-Null
    foreach ($d in $ReleaseDocs) {
        $src = Join-Path $RepoRoot "docs/$d"
        if (Test-Path $src) { Copy-Item $src $destDocs -Force }
        else { Warn "doc '$d' not found in source -- skipped" }
    }

    Push-Location $RepoPath
    try {
        git add docs 1>$null 2>$null
        git diff --cached --quiet
        if ($LASTEXITCODE -eq 0) { Info 'release-repo docs already up to date'; return }
        git commit -q -m "docs: sync from source @ $Tag"
        if ($LASTEXITCODE -ne 0) { Warn 'doc-sync commit failed'; return }
        git push -q
        if ($LASTEXITCODE -ne 0) { Warn 'doc-sync push failed'; return }
        Ok "synced docs to release repo ($($ReleaseDocs -join ', '))"
    }
    finally { Pop-Location }
}

# ---- preflight ------------------------------------------------------------
Info "IntuneCommander release $Tag  (arch: $($Arch -join ', '))"

Need cargo  'Install Rust: https://rustup.rs'
Need rustup 'Install Rust: https://rustup.rs'
Need dotnet 'Install the .NET 10 SDK (see service/global.json)'
if (-not $DryRun) { Need gh 'Install the GitHub CLI: https://cli.github.com' }

# sibling path-deps the Rust client needs (see app/Cargo.toml)
$ParentDir = Split-Path $RepoRoot -Parent
foreach ($sib in @('windows-rs', 'cmtraceopen')) {
    $p = Join-Path $ParentDir $sib
    if (-not (Test-Path $p)) {
        Die "Missing sibling checkout '$p' (path dep in app/Cargo.toml). Clone it next to the repo root and retry."
    }
}

# rust targets installed?
$installed = @(rustup target list --installed)
foreach ($a in $Arch) {
    if ($installed -notcontains $RustTriple[$a]) {
        Warn "Rust target $($RustTriple[$a]) not installed -- adding it"
        rustup target add $RustTriple[$a]
        if ($LASTEXITCODE -ne 0) { Die "rustup target add $($RustTriple[$a]) failed" }
    }
}

# gh auth
if (-not $DryRun) {
    gh auth status 1>$null 2>$null
    if ($LASTEXITCODE -ne 0) { Die 'gh is not authenticated. Run: gh auth login' }
}

# clean dist
if (Test-Path $DistRoot) { Remove-Item $DistRoot -Recurse -Force }
New-Item -ItemType Directory -Path $DistRoot | Out-Null

# ---- one-time PowerShell module cache ------------------------------------
# Bundle Maester + everything its tests need for the non-Graph services so a packaged
# install runs FULLY OFFLINE — no PSGallery at runtime (matters in locked-down /
# national-cloud tenants). Downloaded ONCE here and copied into each arch's
# sidecar/psmodules below; the service modules ship all-arch native runtimes
# (win-x64/arm64/x86) in one package, so a single download serves both arches. The BUILD
# machine needs PSGallery access for this one step.
#
# Versions are PINNED so a release is reproducible and can't be silently broken by an
# upstream bump — bump these deliberately after re-testing. Pester is held at 5.x: Maester
# 2.x predates and targets Pester 5 (6.0 is a ground-up rewrite). See
# docs/MAESTER-INTEGRATION.md for the bundle rationale.
$pinnedModules = @(
    @{ Name = 'Maester';                        Version = '2.1.0'  }   # the suite
    @{ Name = 'Microsoft.Graph.Authentication'; Version = '2.38.0' }   # Graph (Connect-MgGraph)
    @{ Name = 'Pester';                         Version = '5.7.1'  }   # test runner (Pester 5.x)
    @{ Name = 'ExchangeOnlineManagement';       Version = '3.10.0' }   # Exchange + Security&Compliance (EOP)
    @{ Name = 'MicrosoftTeams';                 Version = '7.8.0'  }   # Teams
    @{ Name = 'PnP.PowerShell';                 Version = '3.3.0'  }   # SharePoint
)
$psModuleCache = Join-Path $DistRoot '_psmodules-cache'
New-Item -ItemType Directory -Path $psModuleCache -Force | Out-Null
Info 'caching pinned Maester + service modules from PSGallery (once, shared across arches)'
foreach ($mod in $pinnedModules) {
    Info "  Save-Module $($mod.Name) $($mod.Version)"
    Save-Module -Name $mod.Name -RequiredVersion $mod.Version -Path $psModuleCache -Repository PSGallery -Force -ErrorAction Stop
}
# Save-Module also pulls each module's transitive deps at their LATEST, which can drop a
# SECOND version of a pinned module next to ours (e.g. Pester 6.x beside our 5.7.1) — that
# would win at import time and defeat the pin. Keep only the pinned version of each.
foreach ($mod in $pinnedModules) {
    $modDir = Join-Path $psModuleCache $mod.Name
    Get-ChildItem $modDir -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ne $mod.Version } |
        ForEach-Object { Warn "  pruning non-pinned $($mod.Name) $($_.Name)"; Remove-Item $_.FullName -Recurse -Force }
}
# Save-Module also drags in the PSGallery infra modules (PackageManagement, PowerShellGet)
# as its own dependencies — they're only needed to INSTALL modules, not to run Maester or
# the service modules, and bundling them would shadow the target's (newer) system copies.
# Drop them (verified: the bundle still imports + exposes every Connect-* cmdlet without them).
foreach ($infra in 'PackageManagement', 'PowerShellGet') {
    $infraDir = Join-Path $psModuleCache $infra
    if (Test-Path $infraDir) { Info "  dropping bundled infra module $infra"; Remove-Item $infraDir -Recurse -Force }
}
# sanity: every pinned module is present at its pinned version
foreach ($mod in $pinnedModules) {
    if (-not (Test-Path (Join-Path $psModuleCache (Join-Path $mod.Name $mod.Version)))) {
        Die "module cache missing $($mod.Name) $($mod.Version)"
    }
}
$cacheSizeMB = [math]::Round(((Get-ChildItem $psModuleCache -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 0)
Ok ("cached modules ($cacheSizeMB MB): " + ((Get-ChildItem $psModuleCache -Directory).Name -join ', '))

# ---- build + package each arch -------------------------------------------
$assets = @()
$scStr  = $SelfContained.ToString().ToLower()

foreach ($a in $Arch) {
    $triple = $RustTriple[$a]
    $rid    = $DotnetRid[$a]
    Info "==== $a  ($triple / $rid) ===="

    # 1) Rust/WinUI client
    Info 'cargo build -p app --release'
    Push-Location $RepoRoot
    try { cargo build -p app --release --target $triple } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { Die "cargo build failed for $triple" }
    $clientExe = Join-Path $RepoRoot "target/$triple/release/app.exe"
    if (-not (Test-Path $clientExe)) { Die "client binary not found: $clientExe" }

    # 2) .NET sidecar
    $sidecarOut = Join-Path $DistRoot "_sidecar-$a"
    Info "dotnet publish Api  (rid=$rid self-contained=$scStr)"
    # -p:DebugType=none/DebugSymbols=false: no .pdb symbols in the release bundle.
    dotnet publish (Join-Path $RepoRoot 'service/Api/Api.csproj') `
        -c Release -r $rid --self-contained $scStr `
        -p:DebugType=none -p:DebugSymbols=false -o $sidecarOut
    if ($LASTEXITCODE -ne 0) { Die "dotnet publish failed for $rid" }

    # 2b) Bundle Maester + Graph/Pester + the service modules (Exchange, Teams, SharePoint)
    #     into sidecar/psmodules so /maester/run works offline -- no PSGallery at runtime
    #     (matters in locked-down / national-cloud tenants). The sidecar prepends this
    #     folder to PSModulePath (MaesterRunner.BundledModulePath). PowerShell 7 must
    #     be present on the target machine -- the sidecar 501s with an install hint if not.
    #     Copies the whole once-downloaded $psModuleCache (no per-arch PSGallery hit).
    $psmodules = Join-Path $sidecarOut 'psmodules'
    Info 'bundling pinned Maester + service modules into sidecar/psmodules (offline runtime)'
    New-Item -ItemType Directory -Path $psmodules -Force | Out-Null
    Copy-Item (Join-Path $psModuleCache '*') $psmodules -Recurse -Force
    if (-not (Test-Path (Join-Path $psmodules 'Maester'))) { Die "module cache copy produced nothing under $psmodules" }
    Ok ("bundled modules: " + ((Get-ChildItem $psmodules -Directory).Name -join ', '))

    # 3) stage the runnable bundle
    $stage = Join-Path $DistRoot "IntuneCommander-$Version-win-$a"
    New-Item -ItemType Directory -Path $stage | Out-Null
    Copy-Item $clientExe (Join-Path $stage 'IntuneCommander.exe')
    Copy-Item $sidecarOut (Join-Path $stage 'sidecar') -Recurse
    Set-Content (Join-Path $stage 'VERSION') $Version

    # ship a couple of docs alongside the binaries
    $docDst = Join-Path $stage 'docs'
    New-Item -ItemType Directory -Path $docDst | Out-Null
    foreach ($d in @('RUNBOOK.md')) {
        $src = Join-Path $RepoRoot "docs/$d"
        if (Test-Path $src) { Copy-Item $src $docDst }
    }

    # convenience launcher: start sidecar, wait for :5099, then the client
    $launcher = @'
@echo off
REM Start the sidecar first, give it a moment to bind :5099, then the client.
start "IntuneCommander sidecar" /min "%~dp0sidecar\Api.exe"
timeout /t 3 /nobreak >nul
start "" "%~dp0IntuneCommander.exe"
'@
    Set-Content (Join-Path $stage 'Start-IntuneCommander.cmd') $launcher -Encoding ascii

    # 4) zip (keeps the top-level folder)
    $zip = Join-Path $DistRoot "IntuneCommander-$Version-win-$a.zip"
    Compress-Archive -Path $stage -DestinationPath $zip -Force
    Ok "packaged $(Split-Path $zip -Leaf)"
    $assets += $zip

    Remove-Item $sidecarOut -Recurse -Force
}

Ok "built $($assets.Count) artifact(s) in $DistRoot"
$assets | ForEach-Object { Write-Host "       $(Split-Path $_ -Leaf)" }

if ($DryRun) {
    Warn 'DryRun: skipping the GitHub Release.'
    exit 0
}

# ---- release notes --------------------------------------------------------
$notesFile = Join-Path $DistRoot 'NOTES.md'
if ($Notes -and (Test-Path $Notes)) {
    Copy-Item $Notes $notesFile
}
elseif ($Notes) {
    Set-Content $notesFile $Notes
}
else {
    @"
## IntuneCommander $Tag

Windows-forward unified Intune platform -- a Rust/WinUI 3 client + a .NET sidecar
with a searchable audit/drift time-machine.

### Downloads
- ``IntuneCommander-$Version-win-arm64.zip`` -- primary target
- ``IntuneCommander-$Version-win-x64.zip``

### Install
Extract, then run ``Start-IntuneCommander.cmd``. See ``docs/RUNBOOK.md`` in the
bundle for prerequisites (Windows App SDK runtime) and the smoke test.
"@ | Set-Content $notesFile
}

# ---- publish --------------------------------------------------------------
gh release view $Tag -R $ReleaseRepo 1>$null 2>$null
$exists = ($LASTEXITCODE -eq 0)
if ($exists) {
    if (-not $Force) { Die "release $Tag already exists in $ReleaseRepo. Re-run with -Force to replace it." }
    Warn "replacing existing release $Tag (-Force)"
    gh release delete $Tag -R $ReleaseRepo --yes --cleanup-tag
}

$ghArgs = @('release', 'create', $Tag) + $assets + @(
    '-R', $ReleaseRepo,
    '--title', "IntuneCommander $Tag",
    '--notes-file', $notesFile
)
if (-not $Stable) { $ghArgs += '--prerelease' }

Info "creating release $Tag in $ReleaseRepo"
gh @ghArgs
if ($LASTEXITCODE -ne 0) { Die 'gh release create failed' }

Ok "published $Tag -> https://github.com/$ReleaseRepo/releases/tag/$Tag"

# ---- sync docs into the release repo --------------------------------------
if (-not $SkipDocsSync) {
    $relPath = if ($ReleaseRepoPath) { $ReleaseRepoPath } else { Join-Path $ParentDir 'intunecommander-release' }
    Info "syncing docs to release repo: $relPath"
    Sync-ReleaseDocs -RepoPath $relPath
}
