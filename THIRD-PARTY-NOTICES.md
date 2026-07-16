# Third-Party Notices — IntuneCommander

IntuneCommander incorporates and/or redistributes third-party and open-source software. This file
lists those components and their licenses. Each component is the property of its respective owners and
is governed by its own license, which controls as to that component.

> **Scope & status:** this covers what the **release bundle ships** — the client executable, the
> self-contained .NET runtime and libraries under `sidecar/`, and the PowerShell modules under
> `sidecar/psmodules/`. It was compiled from the project manifests and is **substantially complete for
> the beta**, but the definitive per-component license text is the `LICENSE` file inside each package
> in the built bundle. **Before GA, regenerate a complete transitive list** with tooling
> (e.g. `dotnet-project-licenses` for .NET, `cargo about` for Rust) and include full license texts.

---

## ⚠️ Commercial / proprietary components — action required before shipping

These are **not** open-source and have redistribution or licensing obligations that must be resolved:

| Component | Version | License | Action needed |
|---|---|---|---|
| **Syncfusion.Presentation.Net.Core** (+ `Syncfusion.Licensing`, `Syncfusion.Compression.Net.Core`, `Syncfusion.OfficeChart.Net.Core`) | 32.2.5 | **Commercial — Syncfusion License / EULA** | Used only for **Conditional Access → PowerPoint** export and the M19 evidence pack (`service/Core/Services/ConditionalAccessPptExportService.cs`). Redistributing the DLLs **requires a valid Syncfusion license**. The sidecar now **registers a license at startup from the `SYNCFUSION_LICENSE_KEY` environment variable** (`service/Api/Program.cs`) — provide a version-matching key via that env (a repo variable in CI; see `packaging/README.md`). Without a key, the PPTX export runs unlicensed (trial watermark); the rest of the app is unaffected. **Confirm your Syncfusion license tier permits redistribution in a downloadable app** (their Community License is free under revenue/size thresholds). |
| **ExchangeOnlineManagement** (PowerShell) | 3.10.0 | **Proprietary — Microsoft Software License Terms** (requires license acceptance; © Microsoft) | Bundled offline for Maester's Exchange checks. **Confirm Microsoft's redistribution terms** permit shipping it inside the bundle, or fetch it at first run instead of redistributing. |
| **MicrosoftTeams** (PowerShell) | 7.8.0 | **Proprietary — Microsoft Software License Terms** (requires license acceptance; © Microsoft) | Same as above (Teams checks). Confirm redistribution or fetch-on-demand. |

The **Windows App SDK / WinUI 3** runtime is a Microsoft redistributable that the client links
against but **does not bundle** — it is a runtime prerequisite the user installs, not redistributed
here.

---

## .NET sidecar — libraries (MIT unless noted)

| Component | Version | License |
|---|---|---|
| Microsoft.Graph.Beta (+ Microsoft.Graph.Core, Microsoft.Kiota.*) | 5.130.0-preview | MIT |
| Azure.Identity (+ Microsoft.Identity.Client / MSAL, Azure.Core) | 1.17.1 | MIT |
| LiteDB | 5.0.21 | MIT |
| Microsoft.Data.Sqlite (+ SQLitePCLRaw) | 9.0.0 | MIT / Apache-2.0 (SQLitePCLRaw) |
| bundled native SQLite engine (`e_sqlite3`) | — | Public Domain (sqlite.org) |
| Microsoft.AspNetCore.DataProtection | 8.0.12 | MIT |
| Microsoft.Extensions.* / System.* | 10.0.x | MIT (.NET Foundation) |
| System.Security.Cryptography.Xml | 8.0.3 | MIT |
| **Lucene.Net** (+ Analysis.Common, QueryParser) | 4.8.0-beta00016 | **Apache-2.0** |
| ModelContextProtocol.AspNetCore | 1.4.0 | MIT |
| **OpenTelemetry.*** (Hosting, AspNetCore, OTLP + Console exporters) | 1.16.0 | **Apache-2.0** |
| **.NET runtime** (self-contained, shipped in the bundle) | net10.0 | MIT (© .NET Foundation / Microsoft) |

## Bundled PowerShell modules (`sidecar/psmodules/`)

| Module | Version | License |
|---|---|---|
| Maester | 2.1.0 | MIT |
| Microsoft.Graph.Authentication | 2.38.0 | MIT |
| Pester | 5.7.1 | **Apache-2.0** |
| PnP.PowerShell | 3.3.0 | MIT |
| ExchangeOnlineManagement | 3.10.0 | **Proprietary Microsoft** (see ⚠️ above) |
| MicrosoftTeams | 7.8.0 | **Proprietary Microsoft** (see ⚠️ above) |

## Rust client — crates (compiled into the executable; all MIT OR Apache-2.0 unless noted)

| Crate | License |
|---|---|
| reqwest | MIT OR Apache-2.0 |
| serde, serde_json | MIT OR Apache-2.0 |
| regex | MIT OR Apache-2.0 |
| rfd | MIT |
| windows-reactor, windows-reactor-setup (windows-rs) | MIT OR Apache-2.0 |
| **cmtraceopen-parser** (external upstream, `adamgell/cmtraceopen`) | **MIT** (© 2026 Adam) |
| chrono | MIT OR Apache-2.0 |
| encoding_rs | (Apache-2.0 OR MIT) AND BSD-3-Clause |
| log | MIT OR Apache-2.0 |
| thiserror | MIT OR Apache-2.0 |
| base64 | MIT OR Apache-2.0 |

## Forked component

`service/Core/` (the Graph engine) is a hard fork of **[adamgell/IntuneCommander](https://github.com/adamgell/IntuneCommander)**,
which is licensed under the **MIT License** (© Adam Gell). The upstream MIT notice is retained below.

---

## License texts

### MIT License

Applies to the MIT-licensed components above, each © its respective authors/contributors (including
the upstream `adamgell/IntuneCommander` project, © Adam Gell). The per-component copyright lines ship
in each package's own `LICENSE` file in the bundle.

```
Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

### Apache License 2.0

Applies to Lucene.NET, OpenTelemetry, Pester, and SQLitePCLRaw. Full text:
<https://www.apache.org/licenses/LICENSE-2.0>. Any `NOTICE` files shipped with those components are
reproduced with them in the bundle.

### Proprietary Microsoft modules

ExchangeOnlineManagement and MicrosoftTeams are governed by the Microsoft Software License Terms
distributed with each module (see each module's folder in `sidecar/psmodules/`).

### Syncfusion

Syncfusion components are governed by the Syncfusion license/EULA:
<https://www.syncfusion.com/license/studio>.
