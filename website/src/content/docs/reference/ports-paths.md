---
title: Ports & paths
description: The endpoints, files, and build outputs IntuneCommander uses on your machine.
---

A quick reference for where things live.

## Network

| What | Value |
| --- | --- |
| Sidecar base URL | `http://127.0.0.1:5099` |
| Health / auth state | `GET /health` |
| Sign in / out | `POST /auth/signin` · `POST /auth/signout` |
| Refresh snapshots | `POST /sync` |

The sidecar listens on **loopback only** (`127.0.0.1`) — it isn't exposed to the network.

## Files on disk

| What | Path |
| --- | --- |
| Tenant profiles | `%LocalAppData%\Intune.Commander\profiles.json` |
| Profile-secret protection keys | `%LocalAppData%\Intune.Commander\keys\` |
| Client binary (debug build) | `.\target\debug\app.exe` |
| Sidecar project | `service\Api\Api.csproj` |
| API contract | `contract\openapi.yaml` |

:::note[Profile store]
`%LocalAppData%\Intune.Commander\` is IntuneCommander's profile store, kept outside the app so your
tenants persist across versions. See [How sign-in works](/sign-in/overview/).
:::

## Build & run

```powershell
# Sidecar (starts on :5099, cold-start ~6s)
dotnet run --project service/Api

# Client (separate terminal)
./target/debug/app.exe
```

See [Install & build](/get-started/install/) for the full setup.
