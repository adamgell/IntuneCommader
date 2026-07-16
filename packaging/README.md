# Packaging

IntuneCommander ships as a **signed MSI + MSIX**, built with **[Master Packager Dev
(`mpdev`)](https://www.masterpackager.com/)** — the same tool the original IntuneCommander used.
`mpdev` produces both installer formats from one project file and signs them via **Azure Trusted
Signing** in the same step.

## Files

| File | Role |
|---|---|
| **`installer.mpdev.json`** | The Master Packager project — **the packaging source of truth.** Defines the package identity, the file set (the staged client+sidecar bundle), the Start-Menu shortcut, MSI upgrade code, the icon, and the Azure Trusted Signing block. |
| **`icon.ico`** | Installer + app icon (16/24/32/48/64/128/256 px), generated from `../docs/brand/assets/app-icon.svg`. Wired via the `icon` field. |
| `AppxManifest.xml` | **Superseded.** The old hand-rolled MSIX manifest for the `MakeAppx` path. `mpdev` generates its own manifest — this is kept only as a reference / manual-`MakeAppx` fallback (see `../scripts/package-msix.ps1`, also deprecated). |

## How it's built

The signed pipeline is `.github/workflows/cmprojectx-codesign.yml` (job: **build**, per arch). Per
architecture it:

1. Builds the Rust client (`app.exe` → `IntuneCommander.exe`) and publishes the **self-contained**
   .NET sidecar.
2. Bundles the pinned **Maester** PowerShell modules into `sidecar/psmodules` (offline runtime).
3. Stages the runnable bundle (`IntuneCommander.exe`, `sidecar/`, `docs/RUNBOOK.md`,
   `Start-IntuneCommander.cmd`, `VERSION`) and points `IC_BUNDLE_DIR` at it.
4. Runs `mpdev build packaging/installer.mpdev.json` with per-arch `--properties` overrides
   (version, msix.version, outputFileName, platform) → **signed** `IntuneCommander-<version>-<arch>.msi`
   and `.msix`.
5. Publishes them to `gellorg/intunecommander-release`.

Build one locally (from a machine with `mpdev` installed and a staged bundle):

```powershell
$env:IC_BUNDLE_DIR = "C:\path\to\staging\IntuneCommander-1.0.0-beta.1-x64"
mpdev build packaging/installer.mpdev.json --working-dir . `
  --properties "$.version=1.0.0.1" "$.msix.version=1.0.0.1" `
               "$.outputFileName=IntuneCommander-1.0.0-beta.1-x64" "$.platform=x64"
```

## ⚠️ Three things a release engineer MUST verify before the first signed build

Master Packager project files are plain JSON (no comments), so these caveats live here:

1. **`msix.publisher` must EXACTLY equal the Azure Trusted Signing certificate subject.** Set to
   **`CN=Adam Gell`** — the subject for the `adamgell` Trusted Signing account this repo signs with
   (`SIGNING_ACCOUNT_NAME=adamgell`, `SIGNING_PROFILE_NAME=adamgell-github`), which is the same
   account/subject the original app shipped signed MSIX with. If you switch signing accounts, update
   this to the new cert subject — a mismatch builds fine but **Windows refuses to install**.
2. **`msi.upgradeCode` is a fixed GUID — never change it.** `{7F3A9C21-4B8E-4D6A-9E1F-2C5B8A0D3E64}`
   is *our* product's upgrade code (distinct from the old app's). It must stay constant across every
   release so the MSI recognises upgrades. It is intentionally different from
   `adamgell/IntuneCommander`'s code so the two products don't collide.
3. **Icon:** ✅ wired — `packaging/icon.ico` (generated from `docs/brand/assets/app-icon.svg`).
   Regenerate it if the brand mark changes (see below).

## Syncfusion license (CA → PowerPoint export)

The Conditional Access → PowerPoint export uses **Syncfusion.Presentation** (a commercial component).
The sidecar registers a Syncfusion license **at startup from the `SYNCFUSION_LICENSE_KEY` environment
variable** (`service/Api/Program.cs`). The key is a *redistributable license identifier* (not an auth
secret), but it is kept **out of source** and injected at build / install time:

- **CI / releases:** set a GitHub **repo variable** named `SYNCFUSION_LICENSE_KEY`
  (repo → Settings → Secrets and variables → Actions → **Variables**). The codesign workflow passes it
  to `mpdev`, which (a) sets it as the installer's `SYNCFUSION_LICENSE_KEY` environment variable on the
  user's machine and (b) bakes it into the portable zip's launcher — so installed apps are licensed
  with no user action.
- **Local dev:** `$env:SYNCFUSION_LICENSE_KEY = '<your key>'` before running the sidecar.
- **Absent/blank key:** the app runs normally; only the PPTX export is unlicensed (trial watermark).
  The code ignores an unsubstituted `%SYNCFUSION_LICENSE_KEY%` placeholder.

Use a key whose **version matches** `Syncfusion.Presentation.Net.Core` (currently 32.x) and whose
licensed platform **permits redistribution in a downloadable app** — confirm your Syncfusion license
tier allows this before the public beta.

## Versioning note (MSIX/MSI 4-part vs. SemVer)

Installer versions are 4-part numeric (`A.B.C.D`) with **no prerelease suffix**. The workflow derives
the package version from the SemVer tag: `1.0.0-beta.N` → `1.0.0.N` (the beta number becomes the
revision, so each beta is a valid upgrade of the last). **GA caveat:** because 4-part versions have no
prerelease ordering, `1.0.0.0` would sort *below* `1.0.0.1` (beta.1). The GA release must therefore be
`1.0.1.0` (or higher) to supersede the betas — plan the GA version bump accordingly.

## Regenerating the icon

`icon.ico` is rendered from the brand SVG with `sharp` (SVG rasteriser) + `png-to-ico`:

```bash
npm install sharp png-to-ico
node -e "
import('sharp').then(async ({default: sharp}) => {
  const pngToIco = (await import('png-to-ico')).default;
  const fs = require('fs');
  const svg = fs.readFileSync('docs/brand/assets/app-icon.svg');
  const pngs = await Promise.all([16,24,32,48,64,128,256].map(s =>
    sharp(svg, {density: 900}).resize(s, s, {fit: 'contain', background: {r:0,g:0,b:0,alpha:0}}).png().toBuffer()));
  fs.writeFileSync('packaging/icon.ico', await pngToIco(pngs));
});
"
```

Sizes embedded: 16, 24, 32, 48, 64, 128, 256.
