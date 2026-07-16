---
title: Assignments
description: Target policies and apps to groups with include/exclude rules, assignment filters, and intent — from one shared editor.
---

Assignments are how a policy or app reaches devices and users. In Intune they're a **uniform
concept**, so IntuneCommander gives every assignable surface the **same editor** — open it from the
**Assignments** tab in the detail pane.

## What an assignment is

- **Targets** — the groups a policy applies to, as **include** or **exclude** rules.
- **Filters** — an optional [assignment filter](https://learn.microsoft.com/mem/intune/fundamentals/filters)
  that narrows a target (e.g. only Windows 11, only corporate-owned).
- **Intent** — for apps, whether the assignment is _required_, _available_, or _uninstall_.

## Editing assignments

From the **Assignments** tab you can:

1. **See current assignments** for the selected object.
2. **Add or remove group targets** with the group picker, as include or exclude.
3. **Pick a filter** and set its mode.
4. **Set the intent** (apps).
5. **Save** — the change is written through that surface's assign operation.

:::note[Coverage varies by surface]
Most policy types support setting assignments; a few expose them **read-only** (you can see where
they apply but not change it here), and Conditional Access is read-only throughout. The Assignments
tab reflects what each surface supports.
:::

:::tip[See where everything lands]
To answer "what does this group actually get?" across the whole tenant, use the **Assignment
Explorer** under [Drift & Compare](/using/bulk/) — it reports assignments by user, group, or device
without changing anything.
:::

Next: [The time-machine](/using/time-machine/) →
