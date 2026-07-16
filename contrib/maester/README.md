# Maester give-back drafts (Direction B1)

Draft Intune `Test-Mt*` checks derived from this repo's embedded OIB / CIS baseline
knowledge, staged here as a **give-back to [maester365/maester](https://github.com/maester365/maester)**
(Direction B1 in [`../../docs/MAESTER-INTEGRATION.md`](../../docs/MAESTER-INTEGRATION.md)).

> **Status: draft, NOT submitted upstream.** These are a starting point authored against
> Maester's Intune test conventions (comment-based help → `[OutputType([bool])]` → license
> gate via `Get-MtLicenseInformation` → `Invoke-MtGraphRequest` → `Add-MtTestResultDetail`
> → return `$true`/`$false`/`$null`). Opening a PR to the external Maester repo is a separate,
> outward-facing step that needs a maintainer-assigned `MT.*` id, their contribution process,
> and explicit sign-off — it has intentionally **not** been done automatically.

## Contents

| File | Check |
|---|---|
| `Test-MtIntuneWindowsComplianceRequireEncryption.ps1` | A Windows 10/11 device compliance policy requires storage (BitLocker) encryption (`storageRequireEncryption`). Not covered by an existing `MT.*` test. |
| `Test-MtIntuneWindowsComplianceRequireEncryption.Tests.ps1` | Pester wrapper (placeholder id `MT.CONTRIB.1001`; a real submission gets a maintainer-assigned id). |

## To contribute upstream (when ready)

1. Fork `maester365/maester`; place the function under `powershell/public/maester/intune/`
   and the wrapper under `powershell/tests/intune/` (match the repo's current layout).
2. Replace the placeholder `MT.CONTRIB.1001` id with one coordinated with the maintainers.
3. Add the doc page under `website/docs/commands/` and follow `CONTRIBUTING.md`.
4. Verify locally: `Invoke-Maester -Path <tests dir> -Tag Intune`.

These run today inside cmProjectX too: the sidecar bundles the Maester module and runs the
full suite — dropping a validated check here (or upstream, once merged) makes it show up in
the app's Security & Compliance screen with everything else.
