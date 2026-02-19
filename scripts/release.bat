@echo off
setlocal enabledelayedexpansion

:: ── IntuneManager Release Script ──────────────────────────────────
:: Builds a self-contained single-file exe and creates a GitHub release.
:: Usage:  release.bat 0.2.0
::         release.bat 0.2.0-beta1
:: ───────────────────────────────────────────────────────────────────

if "%~1"=="" (
    echo Usage: release.bat ^<version^>
    echo   e.g. release.bat 0.2.0
    echo        release.bat 0.2.0-beta1
    exit /b 1
)

set VERSION=%~1
set TAG=v%VERSION%
set PROJECT=src\IntuneManager.Desktop\IntuneManager.Desktop.csproj
set PUBLISH_DIR=src\IntuneManager.Desktop\bin\Release\net8.0\win-x64\publish
set EXE=%PUBLISH_DIR%\IntuneManager.Desktop.exe

:: Determine if this is a prerelease (contains hyphen)
set "PRERELEASE="
echo %VERSION% | findstr /C:"-" >nul && set "PRERELEASE=--prerelease"

echo.
echo ============================================
echo  IntuneManager Release %TAG%
echo ============================================
echo.

:: ── Step 1: Clean previous publish output ─────────────────────────
echo [1/5] Cleaning previous publish output...
if exist "%PUBLISH_DIR%" rd /s /q "%PUBLISH_DIR%"

:: ── Step 2: Build ─────────────────────────────────────────────────
echo [2/5] Publishing Release build...
dotnet publish "%PROJECT%" ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:Version=%VERSION%

if errorlevel 1 (
    echo.
    echo ERROR: Build failed. Aborting release.
    exit /b 1
)

if not exist "%EXE%" (
    echo.
    echo ERROR: Expected output not found at %EXE%
    exit /b 1
)

:: ── Step 3: Show artifact info ────────────────────────────────────
echo.
echo [3/5] Build succeeded.
for %%A in ("%EXE%") do echo   Artifact: %%~nxA  (%%~zA bytes)

:: ── Step 4: Git tag ───────────────────────────────────────────────
echo [4/5] Tagging %TAG%...
git tag -a %TAG% -m "Release %TAG%"
if errorlevel 1 (
    echo WARNING: Tag may already exist. Continuing...
)
git push origin %TAG%
if errorlevel 1 (
    echo ERROR: Failed to push tag. Aborting release.
    exit /b 1
)

:: ── Step 5: GitHub Release ────────────────────────────────────────
echo [5/5] Creating GitHub release...
gh release create %TAG% ^
    %PRERELEASE% ^
    --title "%TAG%" ^
    --generate-notes ^
    "%EXE%"

if errorlevel 1 (
    echo.
    echo ERROR: gh release create failed.
    echo   Make sure you're authenticated: gh auth status
    exit /b 1
)

echo.
echo ============================================
echo  Release %TAG% published successfully!
echo ============================================
