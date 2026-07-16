---
title: Bulk & lifecycle
description: Backup and restore, cross-tenant migration, bulk assignment, baseline comparison, and drift remediation — the Drift & Compare toolkit.
---

The **Drift & Compare** section is for working across many objects — or many tenants — at once:
backup, restore, compare, and remediate.

## Backup & restore

- **Backup / Export** — serialise tenant objects to files (Conditional Access exports resolve GUIDs
  to names so the backup is readable).
- **Restore / Import** — recreate objects from a backup, including **cross-tenant** restores with an
  **ID remap** (a migration table rewrites group and reference ids for the target tenant).

## Compare & remediate

- **Drift detection** — compare the current tenant to a baseline or an earlier capture; differences
  are classified by severity.
- **Security baselines** — compare against embedded **OIB / CIS** baselines.
- **Policy comparison** — diff two objects or two snapshots directly.
- **Detection & remediation** — push drifted objects back toward the baseline.

## Across groups and tenants

- **Assignment Explorer** — report what a user, group, or device actually receives across the whole
  tenant (read-only — it changes nothing).
- **Bulk assign** — apply assignments to many objects at once.
- **CA → PowerPoint** — export Conditional Access as a presentable document.

:::caution[Dry-run first]
Bulk and import operations default to a **dry-run** so you can review the full plan before anything
is written. Keep it on until the preview looks right.
:::

You've reached the end of the tour. For the under-the-hood details, see the
[Reference](/reference/architecture/).
