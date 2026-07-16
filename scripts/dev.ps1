#Requires -Version 7
<#
.SYNOPSIS
  cmProjectX dev loop — sidecar + client with file-watch rebuild/reload.

.DESCRIPTION
  Runs the two-process stack for active development:

    * .NET sidecar  -> `dotnet watch` (genuine Hot Reload; auto-restart on edit)
    * Rust/WinUI    -> `cargo run -p app` under a FileSystemWatcher that rebuilds
                       and relaunches the client whenever an app/ or crates/ source
                       (*.rs, Cargo.toml) is saved.

  Rust has no true hot reload — the client is recompiled and relaunched on each
  change. (cargo-watch is deliberately NOT used: its pinned watchexec 1.17
  corrupts the console on this box and exits immediately, so a native .NET
  FileSystemWatcher drives the loop instead.)

  The Rust build loads an MSVC x64 dev shell (vcvars) into the session — same
  vcvars discovery as scripts/test.ps1 — so it works from a plain PowerShell.

  Default (-Only both): the sidecar starts with `dotnet watch` in its OWN window
  so you can watch its logs (CLAUDE.md: run them separately for service work),
  then the client runs in THIS window and borrows the live sidecar on :5099 via
  ensure_running(). Ctrl+C stops the client; close the sidecar window to stop it.

.PARAMETER Only
  Which process to run: 'both' (default), 'client', or 'sidecar'.

.PARAMETER NoWatch
  Plain run instead of watch — `dotnet run` / `cargo run -p app`, one shot, no
  file-watching. Useful for a quick launch.

.EXAMPLE
  ./scripts/dev.ps1                # sidecar (dotnet watch, new window) + client (watch loop)
  ./scripts/dev.ps1 -Only sidecar  # just the sidecar with hot reload, foreground
  ./scripts/dev.ps1 -Only client   # just the client (expects/spawns a sidecar)
  ./scripts/dev.ps1 -NoWatch        # one-shot run of both, no watching
#>
[CmdletBinding()]
param(
  [ValidateSet('both', 'client', 'sidecar')] [string]$Only = 'both',
  [switch]$NoWatch
)

$PSNativeCommandUseErrorActionPreference = $false  # cargo/dotnet write progress to stderr
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ApiProj  = Join-Path $RepoRoot 'service/Api/Api.csproj'

function Info($m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Ok($m)   { Write-Host "OK   $m" -ForegroundColor Green }
function Warn($m) { Write-Host "WARN $m" -ForegroundColor Yellow }
function Die($m)  { Write-Host "ERR  $m" -ForegroundColor Red; exit 1 }

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
  throw "vcvars64.bat not found (need the VC Tools x64 component)."
}

# Load the MSVC x64 dev shell (vcvars) into THIS PowerShell session's environment
# once, so `cargo` links correctly without a per-invocation cmd trampoline. We ask
# cmd to run vcvars and dump `set`, then import each var. (cargo-watch is NOT used:
# its pinned watchexec 1.17 corrupts the console on this Windows box and exits.)
function Import-VcVars {
  $vcvars = Find-VcVars
  $dump = Join-Path $env:TEMP "cmpx-vcenv-$([Guid]::NewGuid().ToString('N')).txt"
  cmd /c "call `"$vcvars`" >nul && set" > $dump
  Get-Content $dump | ForEach-Object {
    if ($_ -match '^([^=]+)=(.*)$') { Set-Item -LiteralPath "env:$($matches[1])" -Value $matches[2] }
  }
  Remove-Item -LiteralPath $dump -ErrorAction SilentlyContinue
}

# Force-kill a process and its whole child tree (cargo -> app.exe -> sidecar).
function Stop-Tree([int]$procId) {
  if ($procId) { taskkill /PID $procId /T /F *> $null 2>&1 }
}

# Start `cargo run -p app` as a child, inheriting our console so output streams
# live. Returns the process object (not awaited).
function Start-CargoRun {
  Push-Location $RepoRoot
  try { return Start-Process cargo -ArgumentList 'run', '-p', 'app' -NoNewWindow -PassThru }
  finally { Pop-Location }
}

# Rebuild-and-rerun-on-save loop, backed by a .NET FileSystemWatcher (no external
# watcher dependency). Watches app/ and crates/ for *.rs and Cargo.toml edits;
# on a change it kills the running client and rebuilds.
function Start-Client {
  Import-VcVars

  if ($NoWatch) {
    Warn 'The client is COMPILING now — the window appears only after the first'
    Warn 'build finishes (a clean build can take a few minutes). Not stalled.'
    Info 'client: cargo run -p app  (one shot)'
    $p = Start-CargoRun
    $p.WaitForExit()
    return
  }

  # Set up watchers. Events queue to the session; we drain them in the loop.
  $watchDirs = @('app', 'crates') | ForEach-Object { Join-Path $RepoRoot $_ } | Where-Object { Test-Path $_ }
  $watchers = @()
  $subs = @()
  foreach ($d in $watchDirs) {
    foreach ($filter in @('*.rs', 'Cargo.toml')) {
      $w = New-Object System.IO.FileSystemWatcher $d, $filter
      $w.IncludeSubdirectories = $true
      $w.EnableRaisingEvents = $true
      $watchers += $w
      foreach ($evt in @('Changed', 'Created', 'Renamed', 'Deleted')) {
        $subs += Register-ObjectEvent -InputObject $w -EventName $evt -SourceIdentifier "cmpx-$([Guid]::NewGuid().ToString('N'))"
      }
    }
  }
  Info ("watching: {0} (*.rs, Cargo.toml). Save a file to rebuild; Ctrl+C to quit." -f ($watchDirs -join ', '))

  $proc = $null
  try {
    while ($true) {
      Get-Event | Remove-Event -ErrorAction SilentlyContinue   # clear stale events
      Warn 'client: COMPILING — the app window appears after the build (first one takes minutes).'
      Info 'client: cargo run -p app'
      $proc = Start-CargoRun

      # Wait for either a source change or the app to exit.
      $dirty = $false
      while (-not $proc.HasExited) {
        if (Wait-Event -Timeout 1) { $dirty = $true; break }
      }

      if (-not $dirty) {
        Warn "client exited (code $($proc.ExitCode)). Waiting for a file change to rebuild… (Ctrl+C to quit)"
        Get-Event | Remove-Event -ErrorAction SilentlyContinue
        Wait-Event | Out-Null
      }

      Start-Sleep -Milliseconds 400        # debounce editor write-bursts
      Get-Event | Remove-Event -ErrorAction SilentlyContinue
      Info 'change detected — restarting client'
      Stop-Tree $proc.Id
    }
  }
  finally {
    if ($proc -and -not $proc.HasExited) { Stop-Tree $proc.Id }
    $subs | ForEach-Object { Unregister-Event -SourceIdentifier $_.Name -ErrorAction SilentlyContinue }
    $watchers | ForEach-Object { $_.EnableRaisingEvents = $false; $_.Dispose() }
  }
}

# dotnet watch in the CURRENT window (used for -Only sidecar).
function Start-SidecarForeground {
  if (-not (Test-Path $ApiProj)) { Die "sidecar project not found: $ApiProj" }
  if ($NoWatch) {
    Info 'sidecar: dotnet run  (one shot, :5099)'
    dotnet run --project $ApiProj
  }
  else {
    Info 'sidecar: dotnet watch  (Hot Reload, :5099)'
    dotnet watch --project $ApiProj run
  }
}

# Launch the sidecar in its OWN pwsh window so its logs stay visible while the
# client runs in this one. Returns once the window is spawned (not awaited).
function Start-SidecarWindow {
  if (-not (Test-Path $ApiProj)) { Die "sidecar project not found: $ApiProj" }
  $cmd = if ($NoWatch) {
    "dotnet run --project '$ApiProj'"
  } else {
    "dotnet watch --project '$ApiProj' run"
  }
  Info "sidecar: launching in a new window -> $cmd"
  Start-Process pwsh -ArgumentList @(
    '-NoExit', '-Command',
    "`$host.UI.RawUI.WindowTitle = 'cmProjectX sidecar (:5099)'; $cmd"
  ) | Out-Null
  # give it a beat to grab the single-instance mutex + start binding :5099
  Start-Sleep -Seconds 2
}

# Preflight the sibling path-deps the Rust client needs (see app/Cargo.toml).
function Test-RustSiblings {
  $parent = Split-Path $RepoRoot -Parent
  foreach ($sib in @('windows-rs', 'cmtraceopen')) {
    if (-not (Test-Path (Join-Path $parent $sib))) {
      Die "Missing sibling checkout '$sib' next to the repo root (path dep in app/Cargo.toml). Clone it and retry."
    }
  }
}

Write-Host "cmProjectX dev — Only=$Only  Watch=$(-not $NoWatch)  $RepoRoot" -ForegroundColor Cyan

switch ($Only) {
  'sidecar' {
    Start-SidecarForeground
  }
  'client' {
    Test-RustSiblings
    Warn 'No sidecar started here — the client will borrow one on :5099 or spawn a (non-watch) one itself.'
    Start-Client
  }
  'both' {
    Test-RustSiblings
    Start-SidecarWindow
    Start-Client
  }
}
