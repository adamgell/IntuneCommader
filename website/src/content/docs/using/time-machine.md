---
title: The time-machine
description: IntuneCommander keeps an append-only history of your tenant's configuration — audit timeline, drift, full-text search, and point-in-time restore.
---

The time-machine is IntuneCommander's differentiator. Every sync appends a **snapshot** of your tenant's
configuration to an **append-only** store, and every change _you_ make is snapshotted too. Nothing is
overwritten — so you can always look back, compare, and roll forward or back.

## What it gives you

- **Audit Timeline** (Overview) — a chronological view of what changed, when.
- **Config snapshots** — point-in-time copies of each object's full state.
- **Drift detection** — what has diverged from a baseline or an earlier snapshot, severity-classified.
- **Full-text search** — find a setting, value, or object across the whole captured history.
- **Point-in-time restore** — re-apply any past snapshot's body to bring an object back to how it was.

## Keeping it fresh

Run a sync to capture the current tenant state:

```powershell
curl -X POST http://127.0.0.1:5099/sync
```

The status bar's **Sync now** does the same. Because the store is append-only, syncing never loses
history — it just adds the latest point in time.

## Restore = undo that outlives the session

Because every write is snapshotted (see [Edit safely](/using/edit-safely/)), **History → restore**
re-applies a chosen earlier snapshot. Combined with the write path, that's **point-in-time rollback**:
if a change causes trouble next week, restore the object to last week's known-good state.

:::tip[Drift, then remediate]
Pair the time-machine with [Drift & Compare](/using/bulk/) to spot divergence from a baseline and
push objects back into line.
:::

Next: [Diagnostics](/using/diagnostics/) →
