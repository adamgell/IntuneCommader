# cmProjectX — Design tokens

[`tokens.css`](tokens.css) is the **canonical** source of truth for cmProjectX's visual tokens
(color, type, spacing, radius, elevation, motion). Everything else — the visual styleguide, and the
forthcoming Reactor client theme — derives from it. **If a consumer disagrees with this file, this file wins.**

- **Namespace:** `--cmx-*`
- **Themes:** dark (default) and a softened light, via `[data-theme="..."]`.
- **Brand:** teal ramp (inherited from sibling project cmtraceopen) + an Intune-blue *cloud* accent used sparingly.

See [`../brand/BRAND.md`](../brand/BRAND.md) for usage rules and the design rationale.

> When porting to the Rust/Reactor client, treat the **semantic** tokens (`--cmx-bg`, `--cmx-brand`,
> `--cmx-cloud-fg`, `--cmx-text*`, status) as the API — not the raw ramp stops — so light/dark swaps stay free.
