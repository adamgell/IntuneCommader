# cmProjectX — Brand & Design system

The canonical brand identity for cmProjectX. **Status: LOCKED (v1.0, 2026-06-10).**
Strategy: *teal leads, blue means cloud*; 50/50 cmtrace ⟷ IntuneCommander carried by form.

## What's here

| File | What it is |
|---|---|
| [`BRAND.md`](BRAND.md) | **The reference** — positioning, logo, color, type, components, voice, do/don'ts. Start here. |
| [`../design-system/tokens.css`](../design-system/tokens.css) | **Canonical tokens** (`--cmx-*`, dark + light). Single source of truth for color/type/space. |
| [`cmprojectx-brand.html`](cmprojectx-brand.html) | **Visual styleguide** — the whole system rendered (open in a browser). |
| [`color-strategy.html`](color-strategy.html) | The 3 color strategies we compared (archive of the decision). |
| [`assets/mark.svg`](assets/mark.svg) | Primary mark, full color (on-dark). |
| [`assets/mark-mono.svg`](assets/mark-mono.svg) | Single-color mark (`currentColor`). |
| [`assets/lockup-horizontal-outlined.svg`](assets/lockup-horizontal-outlined.svg) | Mark + wordmark lockup, **distribution-safe** (wordmark = vector `<path>` outlines; no font needed). Use this everywhere. |
| [`assets/lockup-horizontal.svg`](assets/lockup-horizontal.svg) | Editable mark + wordmark lockup (live `<text>`, needs Bahnschrift). Edit here, then re-outline. |
| [`assets/app-icon.svg`](assets/app-icon.svg) | Fluent rounded-tile app icon (256). |

## Quick rules

- **Teal** is the brand; **blue (`#0078D4`)** appears *only* for cloud/Entra/Intune. Balance ≈ 70 neutral / 25 teal / 5 blue.
- **Type:** Bahnschrift (display) · Segoe UI (body) · Consolas (data).
- **Signature element:** the enriched log row. **Signature mark:** "the join" (logs → feelers → cloud node).
- Name `cmProjectX` is a working codename.

## Next

Port `tokens.css` into a Reactor theme module in `app/src`, then re-skin `signin.rs` to the Split Hero login.
