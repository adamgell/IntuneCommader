---
title: Diagnostics
description: The cmtrace heritage — live-tail Intune logs, dsregcmd, the error-code database, and native Windows diagnostics, correlated on one timeline.
---

The **Diagnostics** section is IntuneCommander's cmtrace heritage: it reads what happened **on the
endpoint**. Most of it works **without signing in** — it's local device data, not tenant data.

## Parser workspaces

Open a workspace for the log or data source you're investigating:

- **Log Explorer** — open any cmtrace-format log with severity colouring and fast filtering.
- **Intune IME** — live-tail the Intune Management Extension logs as they're written.
- **dsregcmd** — parse `dsregcmd /status` to diagnose device registration / Entra join state.
- **Software deployment**, **DNS/DHCP**, **Registry** — focused views over their respective sources.
- **Error-code database** — look up a Windows/Intune error code and get a plain-language meaning.

## Native diagnostics

Beyond the cmtrace parsers, IntuneCommander reads Windows directly:

- **Event Log** and **Sysmon** (EVTX) viewers, with live-tail.
- **Secure Boot** certificate state.
- **Timeline Correlation** — line up events from several sources on one timeline so you can see
  cause and effect across logs.
- **Diagnostics Collector** — gather the relevant logs and state into one bundle to share.

:::note[Severity, the cmtrace way]
Log rows keep the familiar cmtrace severity colours — **error**, **warning**, **info** — in the
gutter and badges, so a problem line stands out at a glance.
:::

:::tip[Logs that know the cloud]
This is where the two heritages meet: a local log line, enriched with the tenant context the
management side already knows. That's the whole idea behind IntuneCommander.
:::

Next: [Bulk & lifecycle](/using/bulk/) →
