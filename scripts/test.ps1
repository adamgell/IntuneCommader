#Requires -Version 7
<#
.SYNOPSIS
  cmProjectX regression test orchestrator — the single source of truth the
  self-hosted runners call (see docs/REGRESSION-TESTING.md).

.DESCRIPTION
  Tier 1 (default): HERMETIC, offline. .NET unit/parity tests + Rust api-types
  parity + lint gates. No sidecar, no tenant, no network, no Entra secret — safe
  on every change and on cheap hosted PR checks.

  Tier 2 (recorded-Graph integration) and Tier 3 (live E2E + WinUI smoke) are
  placeholders until RT2/RT4 land.

  Rust steps bootstrap an MSVC x64 dev shell (vcvars) themselves, so the script
  works from a bare runner with no dev shell pre-loaded. .NET and Rust steps run
  sequentially (one sidecar/build at a time, per CLAUDE.md).

.EXAMPLE
  ./scripts/test.ps1                 # Tier 1, everything
  ./scripts/test.ps1 -NoLint         # tests only, skip clippy + dotnet format
  ./scripts/test.ps1 -SkipRust       # .NET hermetic only (e.g. before vcvars is set up)
#>
[CmdletBinding()]
param(
  [ValidateSet('1', '2', '3', 'all')] [string]$Tier = '1',
  [switch]$SkipRust,
  [switch]$SkipDotnet,
  [switch]$NoLint
)

$PSNativeCommandUseErrorActionPreference = $false  # cargo/dotnet write progress to stderr
$RepoRoot = Split-Path -Parent $PSScriptRoot
$results = [System.Collections.Generic.List[object]]::new()

function Record([string]$name, [int]$code) {
  $ok = ($code -eq 0)
  $results.Add([pscustomobject]@{ Step = $name; Ok = $ok })
  Write-Host ("  [{0}] {1}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name) `
    -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
}

function Find-VcVars {
  $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
  if (Test-Path $vswhere) {
    $install = & $vswhere -latest -products * `
      -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
      -property installationPath
    if ($install) {
      $p = Join-Path $install 'VC\Auxiliary\Build\vcvars64.bat'
      if (Test-Path $p) { return $p }
    }
  }
  $fallback = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat"
  if (Test-Path $fallback) { return $fallback }
  throw "vcvars64.bat not found (need the VC Tools x64 component). Run with -SkipRust to skip Rust steps."
}

# Run a cargo subcommand inside an MSVC x64 dev shell. We shell out to a generated
# .cmd (vcvars + cargo with the redirect done *inside* cmd) because PowerShell
# can't reliably capture a nested `cmd /c "vcvars && cargo"` with quoted paths.
function Invoke-Cargo([string]$cargoArgs, [string]$name) {
  $vcvars = Find-VcVars
  $stamp = [Guid]::NewGuid().ToString('N')
  $bat = Join-Path $env:TEMP "cmpx-$stamp.cmd"
  $log = Join-Path $env:TEMP "cmpx-$stamp.log"
  @"
@echo off
call "$vcvars" >nul
cd /d "$RepoRoot"
cargo $cargoArgs > "$log" 2>&1
exit /b %ERRORLEVEL%
"@ | Set-Content -LiteralPath $bat -Encoding ascii
  cmd /c $bat
  $code = $LASTEXITCODE
  if (Test-Path $log) { Get-Content $log | Write-Host }
  Remove-Item -LiteralPath $bat, $log -ErrorAction SilentlyContinue
  Record $name $code
}

function Invoke-DotnetTest([string]$projRel, [string]$name) {
  dotnet test (Join-Path $RepoRoot $projRel) -c Debug --nologo
  Record $name $LASTEXITCODE
}

Write-Host "cmProjectX regression — Tier $Tier — $RepoRoot" -ForegroundColor Cyan

if ($Tier -in '1', 'all') {
  Write-Host "`n── Tier 1: hermetic ──" -ForegroundColor Cyan

  if (-not $SkipDotnet) {
    Invoke-DotnetTest 'service/Api.Tests.Unit/Api.Tests.Unit.csproj' '.NET hermetic (Store + JsonDrift + contract parity)'
    if (-not $NoLint) {
      dotnet format (Join-Path $RepoRoot 'service/Api.Tests.Unit/Api.Tests.Unit.csproj') --verify-no-changes
      Record '.NET format (--verify-no-changes)' $LASTEXITCODE
    }
  }

  if (-not $SkipRust) {
    Invoke-Cargo 'test -p api-types' 'Rust api-types contract parity'
    if (-not $NoLint) {
      Invoke-Cargo 'clippy -p api-types --all-targets -- -D warnings' 'Rust clippy (api-types)'
    }
  }
}

if ($Tier -in '2', 'all') {
  Write-Host "`n── Tier 2: recorded-Graph integration ──" -ForegroundColor Cyan
  # Replay-based integration tests (offline). Record mode against the live tenant
  # is a separate, opt-in step (RT2 #12) — not run here.
  Invoke-DotnetTest 'service/Api.Tests.Integration/Api.Tests.Integration.csproj' 'Recorded-Graph integration (replay)'
}
if ($Tier -in '3', 'all') {
  Write-Host "`n── Tier 3: live E2E + WinUI smoke — not implemented yet (RT4) ──" -ForegroundColor Yellow
}

Write-Host "`n===== Summary =====" -ForegroundColor Cyan
$results | ForEach-Object {
  Write-Host ("  {0}  {1}" -f $(if ($_.Ok) { 'PASS' } else { 'FAIL' }), $_.Step) `
    -ForegroundColor $(if ($_.Ok) { 'Green' } else { 'Red' })
}
$failed = @($results | Where-Object { -not $_.Ok })
if ($failed.Count -gt 0) {
  Write-Host "`n$($failed.Count) of $($results.Count) step(s) FAILED." -ForegroundColor Red
  exit 1
}
Write-Host "`nAll $($results.Count) step(s) passed." -ForegroundColor Green
exit 0
