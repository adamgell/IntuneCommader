# CI drift gate (`Api.exe drift-gate`)

A headless, one-shot drift check for CI/CD pipelines. It reuses the sidecar's own drift
engine (`DriftEvaluator` — the same code behind `GET /objects`) to score the local snapshot
store's per-object drift, prints a report, optionally alerts Teams/Slack/GitHub, and exits
with a CI-friendly code. It runs **without** taking the single-instance mutex or starting the
web server. It does open the local store (SQLite + the single-writer Lucene index), so a
running UI sidecar must be stopped first — a held index lock is reported as a clean **exit 2**,
not a crash. In CI (no UI running) this never applies.

```powershell
Api.exe drift-gate [flags]
```

## Flags

| Flag | Meaning |
|---|---|
| `--fail-on-drift` | exit **1** when drift is detected (otherwise a clean run always exits 0) |
| `--min-changes <n>` | an object counts as *drifted* at ≥ n field changes (default **1**) |
| `--format text\|json\|markdown` | stdout format (default `text`) |
| `--output <path>` | also write the **JSON** report to a file |
| `--teams <url>` | POST a MessageCard to a Teams incoming webhook (or env `CMPX_TEAMS_WEBHOOK`) |
| `--slack <url>` | POST `{text}` to a Slack incoming webhook (or env `CMPX_SLACK_WEBHOOK`) |
| `--github <owner/repo>` | open a GitHub issue (needs env `GITHUB_TOKEN`) |

Alerts fire **only when drift is detected** and are best-effort — a webhook failure logs to
stderr and never changes the drift verdict or exit code. No-drift runs post nothing.

## Exit codes

| Exit | Meaning |
|---|---|
| **0** | Ran OK — no drift, or drift found but `--fail-on-drift` not set. |
| **1** | `--fail-on-drift` set **and** drift detected at/above `--min-changes`. |
| **2** | Operational error — bad flags, unreadable store, or an unimplemented mode. |

Splitting a drift *fail* (1) from an operational *error* (2) lets CI distinguish a policy
failure from a broken run.

## What "drift" means here

`store-only` mode (the default and only mode today) compares the two most recent config
snapshots of each tracked object in the local time-machine store
(`%LocalAppData%\cmProjectX\`). So the gate reflects whatever the sidecar has already synced;
run it on a machine/agent that syncs the tenant, or after a scheduled sync step.

## Report shape (`--format json`)

```json
{
  "generatedUtc": "2026-07-08T12:00:00Z",
  "mode": "store-only",
  "tenantId": null,
  "trackedObjectCount": 42,
  "driftedObjectCount": 5,
  "minChanges": 1,
  "driftDetected": true,
  "objects": [
    { "objectId": "…", "objectType": "DeviceConfiguration", "objectName": "Windows 10 Baseline",
      "snapshotCount": 7, "lastCapturedUtc": "2026-07-08T11:59:00Z", "changeCount": 3 }
  ]
}
```

## GitHub Actions example

```yaml
- name: Intune drift gate
  run: ./Api.exe drift-gate --fail-on-drift --min-changes 1 --format json --output drift.json
  env:
    CMPX_TEAMS_WEBHOOK: ${{ secrets.TEAMS_WEBHOOK }}
    GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

## Deferred

- **`--live`** — app-only (client-secret) sign-in + a delta sync to refresh snapshots before
  gating, so CI can check a tenant that isn't otherwise synced. Needs a live tenant secret to
  verify, so it is not yet shipped.
- **`--baseline <zip>`** — diff a fresh `GET /export` against a committed baseline zip
  (per-file `JsonDrift`). The diff engine already exists; only the zip pairing is new.
- **`--min-severity`** — the store has no per-change severity field yet, so drift is currently
  binary (any change ≥ `--min-changes` = drift). Teams cards are always red when drift is present.
