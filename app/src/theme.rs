//! cmProjectX — Reactor theme module (the brand, made real in Rust).
//!
//! Every value here is a verbatim port of the **dark theme** semantic tokens in
//! `docs/design-system/tokens.css` — the single source of truth (CANONICAL). If
//! a value here ever disagrees with that file, tokens.css wins; re-sync this.
//!
//! Strategy (see `docs/brand/BRAND.md`): **teal leads everywhere; blue (`cloud`)
//! is reserved for cloud/Entra/Intune wayfinding ONLY** — the connect/sign-in
//! moment, cloud-sourced facts, the device-login link. Aim ~70 neutral / 25 teal
//! / 5 blue. Never let blue become a generic accent.
//!
//! Values are `windows_reactor::Color` (RGB, alpha 255). They flow into widget
//! builders via `.background(..)` / `.foreground(..)` / `.border_brush(..)`,
//! which accept `Color` (there's a `From<Color> for BrushBinding`).
//!
//! NOTE: dark theme only for now — the light set in tokens.css is a deliberate
//! follow-up (the visual styleguide is dark-only so far).

#![allow(dead_code)] // a full palette; not every stop is wired up yet.

use windows_reactor::Color;

// ── Surfaces (dark) ──────────────────────────────────────────────────────────
/// `--cmx-bg` #111111 — app background / deepest neutral.
pub const BG: Color = Color::rgb(0x11, 0x11, 0x11);
/// `--cmx-surface` #1a1a1a — base card/surface fill.
pub const SURFACE: Color = Color::rgb(0x1a, 0x1a, 0x1a);
/// `--cmx-surface-2` #222222 — raised surface (inputs, sub-panels).
pub const SURFACE_2: Color = Color::rgb(0x22, 0x22, 0x22);
/// `--cmx-surface-3` #2a2a2a — highest neutral surface.
pub const SURFACE_3: Color = Color::rgb(0x2a, 0x2a, 0x2a);
/// `--cmx-line` #333333 — hairline divider/border.
pub const LINE: Color = Color::rgb(0x33, 0x33, 0x33);
/// `--cmx-line-2` #484848 — stronger border (control strokes).
pub const LINE_2: Color = Color::rgb(0x48, 0x48, 0x48);

// ── Foreground (dark) ────────────────────────────────────────────────────────
/// `--cmx-text` #ffffff — primary text.
pub const TEXT: Color = Color::rgb(0xff, 0xff, 0xff);
/// `--cmx-text-2` #d6d6d6 — secondary text.
pub const TEXT_2: Color = Color::rgb(0xd6, 0xd6, 0xd6);
/// `--cmx-text-3` #a6a6a6 — tertiary / muted text.
pub const TEXT_3: Color = Color::rgb(0xa6, 0xa6, 0xa6);
/// `--cmx-text-4` #7a7a7a — disabled / faintest text.
pub const TEXT_4: Color = Color::rgb(0x7a, 0x7a, 0x7a);

// ── Brand — teal (dark anchor) ───────────────────────────────────────────────
/// `--cmx-brand` = teal-80 #009688 — primary brand teal (fills, accents).
pub const BRAND: Color = Color::rgb(0x00, 0x96, 0x88);
/// `--cmx-brand-strong` = teal-70 #007768 — pressed / heavier brand.
pub const BRAND_STRONG: Color = Color::rgb(0x00, 0x77, 0x68);
/// `--cmx-brand-bright` = teal-110 #00C6BC — brand text/icon ON dark.
pub const BRAND_BRIGHT: Color = Color::rgb(0x00, 0xC6, 0xBC);
/// `--cmx-on-brand` #00201b — text/icon placed on a brand-teal fill.
pub const ON_BRAND: Color = Color::rgb(0x00, 0x20, 0x1b);
/// teal-20 #00312A — deep teal (the Split Hero brand panel background).
pub const TEAL_DEEP: Color = Color::rgb(0x00, 0x31, 0x2A);
/// teal-10 #001F17 — deepest teal (alt hero shade / shadow).
pub const TEAL_DEEPEST: Color = Color::rgb(0x00, 0x1F, 0x17);

// ── Cloud accent — Intune blue (USE SPARINGLY: cloud/Entra/Intune cues only) ──
/// `--cmx-cloud` #0078D4 — official Intune blue.
pub const CLOUD: Color = Color::rgb(0x00, 0x78, 0xD4);
/// `--cmx-cloud-bright` #4DA3FF — cloud text/icon on dark (`--cmx-cloud-fg`).
pub const CLOUD_BRIGHT: Color = Color::rgb(0x4D, 0xA3, 0xFF);
/// `--cmx-cloud-deep` #0B2A52 — deep cloud tint.
pub const CLOUD_DEEP: Color = Color::rgb(0x0B, 0x2A, 0x52);

// ── Status (dark) ────────────────────────────────────────────────────────────
/// `--cmx-ok` #51cf66 — success.
pub const OK: Color = Color::rgb(0x51, 0xcf, 0x66);
/// `--cmx-warn` #ffc83d — caution.
pub const WARN: Color = Color::rgb(0xff, 0xc8, 0x3d);
/// `--cmx-error` #ff5a52 — error.
pub const ERROR: Color = Color::rgb(0xff, 0x5a, 0x52);

// ── cmtrace severity (heritage; identical in both themes) ─────────────────────
/// `--cmx-sev-error` #ff5a52 — log gutter / badge error.
pub const SEV_ERROR: Color = Color::rgb(0xff, 0x5a, 0x52);
/// `--cmx-sev-warn` #ffc83d — log gutter / badge warning.
pub const SEV_WARN: Color = Color::rgb(0xff, 0xc8, 0x3d);
/// `--cmx-sev-info` #d4d4d4 — log gutter / badge info.
pub const SEV_INFO: Color = Color::rgb(0xd4, 0xd4, 0xd4);

// ── posture severity (Maester controls) — warm ramp, distinct per level ───────
/// `--cmx-sev-critical` #ff5a52 — red (highest urgency).
pub const SEV_CRITICAL: Color = Color::rgb(0xff, 0x5a, 0x52);
/// `--cmx-sev-high` #ff8c42 — orange.
pub const SEV_HIGH: Color = Color::rgb(0xff, 0x8c, 0x42);
/// `--cmx-sev-medium` #ffc83d — amber.
pub const SEV_MEDIUM: Color = Color::rgb(0xff, 0xc8, 0x3d);
/// `--cmx-sev-low` #a6a6a6 — muted (low urgency; informational/none fall to TEXT_4).
pub const SEV_LOW: Color = Color::rgb(0xa6, 0xa6, 0xa6);

// ── Type families (CANONICAL names; consumed via `.font_family(..)`) ──────────
/// Display face — `--cmx-font-display`. Headlines, wordmark, big numerals.
pub const FONT_DISPLAY: &str = "Bahnschrift";
/// Body / UI face — `--cmx-font-ui`. Every interface string (Fluent-native).
pub const FONT_UI: &str = "Segoe UI Variable Text";
/// Data / mono face — `--cmx-font-mono`. Logs, timestamps, device codes.
pub const FONT_MONO: &str = "Consolas";
