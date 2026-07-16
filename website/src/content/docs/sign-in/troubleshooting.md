---
title: Troubleshooting sign-in
description: Common sign-in problems in IntuneCommander and how to fix them.
---

## Quick reference

| Symptom | Likely cause | Fix |
| --- | --- | --- |
| Status shows **`authState: Failed`** | The profile's **client secret has expired**. | Create a new secret in Entra, then update the profile under [Manage tenant profiles](/sign-in/manage-profiles/). |
| `GET /apps` (or a list) returns **`409`** | You're **not signed in** yet. | Sign in first; protected surfaces return `409` until then. |
| **"Can't reach the service"** | The **sidecar is still starting** (or isn't running). | Wait a few seconds (it cold-starts in ~6s) and retry, or start it with `dotnet run --project service/Api`. |
| A surface is **empty** or a write is **greyed out** | The app registration **lacks that Graph permission**. | Consent the required **application permission** in Entra for that app registration. |
| Signed in, but to the **wrong tenant/cloud** | The selected profile targets a different **cloud**. | Pick the right profile, or fix its **Cloud** under Manage tenant profiles. |

## Still stuck?

Re-run the [smoke test](/get-started/smoke-test/) to find the first step that fails — that usually
points straight at the cause:

- **Health fails** → the sidecar isn't up. Start it and check `http://127.0.0.1:5099/health`.
- **Sign-in fails** → check the profile's secret and cloud (the two most common culprits).
- **Lists are empty after sign-in** → check the app registration's application permissions in Entra.

:::note[Where to look]
The sidecar prints sign-in and Graph errors to its console. Keep the `dotnet run` terminal visible
while you debug — the message there is usually the real story.
:::
