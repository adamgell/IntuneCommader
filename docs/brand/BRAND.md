# cmProjectX — Brand & Design Reference

> **Status: LOCKED** · v1.0 · 2026-06-10
> Canonical tokens: [`../design-system/tokens.css`](../design-system/tokens.css) ·
> Visual styleguide: [`cmprojectx-brand.html`](cmprojectx-brand.html) ·
> Assets: [`assets/`](assets/)
>
> *"cmProjectX" is a working codename.* The system is built to flex if the name changes — only the wordmark and a few strings are name-bound.

---

## 0. At a glance

cmProjectX is **50/50 cmtrace + IntuneCommander**: a native Windows (WinUI/Reactor) tool where the
local **log/diagnostics** viewer (cmtrace heritage) extends feelers through the core into
**Entra & Intune** (IntuneCommander heritage) and pulls cloud context *down onto the endpoint* —
so every log line carries its tenant reality.

- **One-liner:** *Logs that know the cloud.*
- **Spine:** "the join" / **enrichment** — local signal meeting cloud context.
- **Color strategy:** **teal leads** everywhere (cmtraceopen continuity); **blue is reserved for "cloud."**
- **The 50/50 is carried by FORM, not color:** `≣` line = diagnostics, `▣`/node = management.

---

## 1. Positioning

**Thesis.** cmtrace reads what happened *on the endpoint*. cmProjectX reaches into *Entra/Intune* and
correlates it, turning a raw log line into an **enriched** one. The user stops alt-tabbing between a
log viewer and the portal — the answer is on the line.

**Two equal heritages.** Neither half is a sidekick. The brand *is* the seam where they meet.

| Half | Heritage | Owns | Motif | Hue |
|---|---|---|---|---|
| Diagnostics | cmtrace / cmtraceopen | logs, live-tail, severity, dsregcmd, Sysmon, timeline | `≣` line | teal |
| Management | IntuneCommander | tenant CRUD, assignments, compliance, drift, snapshot/restore | `▣` node | blue (cloud) |

**Attributes:** diagnostic · precise · trustworthy · cloud-aware · fast · reversible.

---

## 2. Logo

### The mark — "the join"
Teal **log-lines** (left) extend **feelers** into a **tenant node-cluster** (right). The right-most
node is **cloud blue** — the whole product in one glyph: *cmtrace reaching into Intune*.

- **Anatomy:** 3 stacked log bars (the bottom bar uses `teal-110` for life) → 3 curved feelers →
  a 3-node cluster (the largest node `cloud-bright`, satellites `cloud`).
- **Assets:** [`assets/mark.svg`](assets/mark.svg) (full color, on-dark),
  [`assets/mark-mono.svg`](assets/mark-mono.svg) (single-color, `currentColor`).

### Wordmark
`cmProjectX` set in **Bahnschrift Semibold**. `cm` is **teal** (`--cmx-brand`); `ProjectX` is
primary text (`--cmx-text` on dark, `--cmx-text` on light).

### Lockups
- **Horizontal** (default): mark + wordmark. For distribution use the outlined asset
  [`assets/lockup-horizontal-outlined.svg`](assets/lockup-horizontal-outlined.svg) — the wordmark is real
  vector `<path>` outlines (Bahnschrift SemiBold), so it renders correctly without the font installed.
  Keep [`assets/lockup-horizontal.svg`](assets/lockup-horizontal.svg) (live `<text>`) for **editing**; re-outline after changes.
- **Stacked**: mark over wordmark, for square-ish spaces.
- **Inverse**: on light backgrounds, use `teal-70`/`teal-80` + `cloud` for the mark.

### App icon
Fluent rounded-square tile (corner radius ≈ 22%) with a **teal radial gradient**
(`teal-100 → teal-70 → teal-40`); mark drawn in near-white with the cloud node in blue.
At **16px** the feelers drop — just bold log-lines reaching a single blue cloud node.
Asset: [`assets/app-icon.svg`](assets/app-icon.svg).

### Clear space & minimum size
- **Clear space:** the height of one log bar on all sides.
- **Minimum:** mark 16px; horizontal lockup 120px wide.

### Misuse — don't
Recolor the wordmark · italicize/letterspace it · rotate or distort the mark · put the full-color
mark on a busy photo (use mono) · make blue the dominant color.

---

## 3. Color

> Tokens live in [`tokens.css`](../design-system/tokens.css). Hex below is the dark theme.

### Brand — teal ramp (anchor `#007768` light / `#009688` dark)

| Stop | Hex | Stop | Hex | Stop | Hex | Stop | Hex |
|---|---|---|---|---|---|---|---|
| 10 | `#001F17` | 50 | `#005A4B` | 90  | `#00A698` | 130 | `#00E7E2` |
| 20 | `#00312A` | 60 | `#006959` | 100 | `#00B6AA` | 140 | `#00F8F6` |
| 30 | `#003E30` | 70 | `#007768` | 110 | `#00C6BC` | 150 | `#A0F5F0` |
| 40 | `#004C3D` | 80 | `#009688` | 120 | `#00D6CF` | 160 | `#E8F5F3` |

- **Primary** `teal-80 #009688` (dark) / `teal-70 #007768` (light)
- **Bright (text/icon on dark)** `teal-110 #00C6BC`

### Cloud accent — Intune blue (use **sparingly**)

| Role | Hex |
|---|---|
| Intune blue | `#0078D4` |
| On-dark bright | `#4DA3FF` |
| Deep / tint | `#0B2A52` |

**Rule:** blue *only ever means cloud* — Entra/Intune surfaces, cloud-sourced facts, the "enriched"
lane, the sign-in/connect moment. Never a generic accent.

### Neutrals (dark)
`bg #111111` · `surface #1a1a1a` · `surface-2 #222222` · `surface-3 #2a2a2a` ·
`line #333333` · `line-2 #484848` · text `#ffffff / #d6d6d6 / #a6a6a6 / #7a7a7a`.

### Semantic status
`ok #51cf66` · `warn #ffc83d` · `error #ff5a52` (dark). cmtrace **severity** uses the same
`error #ff5a52` / `warn #ffc83d` / `info #d4d4d4` for the log gutter & badges.

### Balance
Aim for **~70 neutral / 25 teal / 5 blue**. If blue is doing more than wayfinding, pull it back.

### Accessibility
Body text on `--cmx-bg` meets WCAG AA. Never put `teal-80`/`cloud` text on a mid-gray; use the
`-bright` variant on dark and the base on light.

---

## 4. Typography

| Role | Family | Use |
|---|---|---|
| Display | **Bahnschrift** Semibold | headlines, big numerals, kickers — the instrument-panel voice |
| Body / UI | **Segoe UI** | every interface string (Fluent-native; matches the WinUI app) |
| Data / Mono | **Consolas** | logs, code, timestamps, device codes, metadata labels |

**Scale** (Fluent ramp, see `--cmx-size-*`): Hero 68 · Title 40 · Subtitle 28 · H 20 · Body 14 · Caption 12 · Mono 12.5.

---

## 5. Iconography & motion

- **Line icons:** 1.6px stroke, round caps/joins, 24px grid. Teal-bright for diagnostics; blue for cloud.
- **Radius:** 4 / 6 / 8 / 12 / 16. **Elevation:** Fluent `shadow-2 → shadow-64`.
- **Motion:** 100/150/200/250ms; standard ease `cubic-bezier(.1,.9,.2,1)`.
- **Signature motion:** the "feeler draws in" — when a log line gains context, the elbow + chips
  reveal over 200ms (decelerate). Used once, with intent — not scattered micro-interactions.

---

## 6. Components (canonical patterns)

- **Buttons** — accent = teal (primary); standard = neutral; subtle = teal text; **cloud button**
  (blue) reserved for the Entra/sign-in moment.
- **Inputs / combo** — `surface-2` fill, `line-2` border, 4px radius; tenant avatar chip in teal.
- **Status pills** — semantic (ok/warn/error), plus `teal` (signed-in/active) and `cloud` (synced-from-Intune).
- **Metric tiles** — big Bahnschrift numeral; 3px left edge: **teal = diagnostics**, **blue = cloud-sourced**.
- **Device-code panel** — big Consolas code + copy + `microsoft.com/devicelogin` link (blue).
- **★ Enriched log row (signature)** — cmtrace line (severity gutter + ts + sev + source + message)
  with an indented **enrich lane**: a blue elbow ("feeler") + cloud-context chips. This is the product
  in one component — lead with it in any demo/marketing.

---

## 7. Voice & tone

**Principles:** Plain & precise (the code *and* what it means) · Actionable (every dead end offers the
next move) · Calm under failure (diagnostics, not drama) · Reversible by default (name the undo first).

**Examples**
- *Device code:* "Finish signing in from a browser — enter **FQXJ-7K2D** at microsoft.com/devicelogin."
- *Error:* "Can't reach the service — the sidecar may still be starting. Retry."
- *Write confirm:* "This changes 3 settings on 1,284 devices. We'll snapshot first so you can roll back."

**Words:** prefer *tenant, endpoint, enrich, correlate, snapshot, restore, drift.* Avoid *synergy,
leverage, seamless,* exclamation marks, and alarm language.

---

## 8. Open decisions / changelog

- **Name** — `cmProjectX` is a working codename; revisit before any public-facing release.
- **Cloud blue** — locked to Intune `#0078D4`; vivid azure `#0A84FF` is the standby alt.
- **Light theme** — softened set defined in `tokens.css`; the visual styleguide is dark-only so far.
- **Next** — port `tokens.css` → Reactor theme module in `app/src`, then re-skin `signin.rs` to the Split Hero.

**Changelog**
- *2026-06-10* — v1.0 locked: strategy 03 (teal lead + blue accent), "the join" mark, type system, tokens, this reference.
