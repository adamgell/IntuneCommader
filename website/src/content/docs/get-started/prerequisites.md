---
title: Prerequisites
description: What you need before building and running IntuneCommander — a Windows 11 on ARM64 box with the .NET 10 SDK, Rust, and the Windows App SDK runtime.
---

IntuneCommander has two parts you build and run locally:

- a **.NET sidecar** (`service/`) that talks to Microsoft Graph, and
- a **Rust + WinUI 3 client** (`app/`) that talks to the sidecar over local HTTP.

This is a **Windows-on-ARM64** product (`aarch64-pc-windows-msvc`). Treat ARM64 as a first-class
requirement, not an afterthought.

:::caution[Windows on ARM64]
The client is built and tested for **Windows 11 on ARM64**. Other hosts and targets aren't
supported today.
:::

## What you need

| Tool | Why | Check it |
| --- | --- | --- |
| **Windows 11 on ARM64** | The client's only supported host/target. | `winver` |
| **.NET SDK 10** | The sidecar targets `net10.0`. | `dotnet --version` |
| **Rust (stable, 1.95+)** | Builds the `app` client crate. | `rustc --version` |
| **Windows App SDK runtime 2.0.1** | Required by the WinUI 3 client. | `winget list "Windows App Runtime"` |
| **Git** | Clone this repo and its parser dependency. | `git --version` |

## Installing the toolchain

```powershell
# .NET 10 SDK
winget install Microsoft.DotNet.SDK.10

# Rust via rustup (defaults to the stable toolchain)
winget install Rustlang.Rustup
rustup default stable
```

For the **Windows App SDK runtime (2.0.1)**, install the runtime package from Microsoft's
[Windows App SDK downloads](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads).
Choose the **2.0.1** runtime for **ARM64**.

:::tip
You don't need Visual Studio — the command-line SDKs above are enough to build and run IntuneCommander.
If a `winget` package ID has changed, grab the installer from the linked official download page.
:::

Next: [Install & build](/get-started/install/) →
