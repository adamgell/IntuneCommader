---
title: Troubleshooting
description: Common problems across building, running, and using IntuneCommander — and how to fix them.
---

For sign-in problems specifically, see [Signing in › Troubleshooting](/sign-in/troubleshooting/).
This page covers the rest.

## Running

| Symptom | Likely cause | Fix |
| --- | --- | --- |
| **"Can't reach the service"** | Sidecar still starting, or not running | It cold-starts in ~6s — wait and retry, or start it with `dotnet run --project service/Api`. |
| Sidecar **fails to bind `:5099`** | A previous instance still holds the port | Close the old process, then start the sidecar again. |
| Client opens but stays empty | Not signed in yet | Sign in — protected surfaces return `409` until you do. |
| A surface is empty after sign-in | App registration lacks that Graph permission | Consent the required **application** permission in Entra. |

## Building

| Symptom | Likely cause | Fix |
| --- | --- | --- |
| `cargo build` can't find the parser | The sibling `cmtraceopen` checkout is missing | Clone `cmtraceopen` **next to** `intunecommander-src` (see [Install & build](/get-started/install/)). |
| First `cargo build` seems to hang | It's compiling many crates the first time | Give it a few minutes; subsequent builds are fast. |
| `dotnet build` can't find the SDK | .NET 10 SDK not installed | Install it (see [Prerequisites](/get-started/prerequisites/)). |

## Using

| Symptom | Likely cause | Fix |
| --- | --- | --- |
| An edit was rejected with a conflict | Someone changed the object first (`412`) | IntuneCommander re-fetches so you can re-diff against the new state, then save again. |
| A write is greyed out | The signed-in app lacks permission for it | The action is gated on your consented permissions; grant it in Entra. |
| Object JSON looks partial | Deeply nested Graph types can serialise partially in the editor | Known limitation for some complex types; the underlying object is intact. |

:::tip[Read the sidecar console]
The `dotnet run` terminal prints Graph and sign-in errors verbatim. When something fails, that
message is usually the real story — keep it visible while you debug.
:::
