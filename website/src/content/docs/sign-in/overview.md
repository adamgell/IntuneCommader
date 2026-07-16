---
title: How sign-in works
description: Tenant profiles, where they live, the two authentication methods, and what signing in unlocks in IntuneCommander.
---

IntuneCommander connects to **Entra & Intune** through a **tenant profile** — a saved Entra app
registration. You pick a profile, sign in, and the live screens populate.

## Tenant profiles

A profile holds everything needed to reach one tenant: the tenant, the app registration (client)
ID, its secret or auth method, and which cloud it lives in. Profiles are stored on your machine at:

```text
%LocalAppData%\Intune.Commander\
```

- `profiles.json` — your saved tenant profiles.
- `keys\` — Windows DataProtection keys. IntuneCommander decrypts each profile's secret transparently,
  so secrets never sit in plain text.

:::note[Profiles persist across versions]
The profile store lives outside the app, so upgrading or reinstalling IntuneCommander keeps your
saved tenants — they're picked up automatically.
:::

## Two ways to authenticate

| Method | What happens | When it's used |
| --- | --- | --- |
| **Client secret** (app-only) | **Silent** — no browser, no prompt. Signs in within a few seconds. | The profile has a stored client secret. |
| **Device code** (interactive) | The app shows a short code + a link; you finish in a browser. | The profile is set to device-code. |

What you can *do* once signed in is governed by the **application permissions** consented to that
app registration in Entra. If a surface is empty or a write is greyed out, the app registration
probably lacks that permission.

## Clouds

Each profile targets one sovereign cloud: **Commercial**, **GCC**, **GCC High**, or **DoD**. The
status bar shows the active cloud alongside the sign-in state.

## What signing in unlocks

Signed out, the app shows only the **Sign in** card. Once you're signed in, the **live screens
populate** — lists and detail views across the whole Intune management surface, plus sync of the
audit/drift snapshots.

Next: [Sign in step by step](/sign-in/sign-in/) →
