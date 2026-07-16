# M22 spike — does the `IMdmProvider` abstraction hold?

> Feasibility spike for [M22 — cross-MDM](./M22-cross-mdm.md). The milestone doc is
> the north star; this is the *empirical* check. Per M22's own instruction this is a
> **spike, not an implementation**: a feasibility report + a minimal seam + ONE
> read-only stub provider that renders through the uniform contract unchanged.

## Verdict

**The abstraction holds for the milestone's Definition of Done — uniform LIST of a
read-only surface from a non-Intune provider — and the seam is genuinely one layer
deep. But the doc's pseudocode gets two structural details wrong, both of which the
spike corrects and neither of which is fatal.** Recommendation: **the read path is
green; defer writes/assignments and DETAIL parity, and resolve the five open
questions before committing the full provider.** Detail below.

What was built and verified (hermetic, no tenant):

- `service/Api/Providers/IMdmProvider.cs` — the seam: `list/get/create/update/delete`
  + `getAssignments/setAssignments`, projecting into the shared `ListItemDto` /
  `AssignmentDto`. Unsupported verbs throw `ProviderUnsupportedException`.
- `service/Api/Providers/StubMdmProvider.cs` — a read-only `"jamf"` provider serving
  4 fake Jamf computers from memory, declaring exactly one supported surface
  (`managed-devices`).
- `service/Api/Providers/ProviderRegistry.cs` — resolves the active provider by id.
- `service/Api/Endpoints/ProvidersEndpoints.cs` — dispatch over the uniform contract.
- `service/Api.Tests.Unit/MdmProviderTests.cs` — 6 hermetic tests.
- Wired into DI + endpoint registration in `Program.cs`.

`dotnet build service/CmProjectX.slnx -c Release` → **0 warnings, 0 errors**.
`dotnet test service/Api.Tests.Unit` → **all green** (existing + 6 new).

---

## What the doc got right

The three load-bearing claims in M22 §"Why it's even plausible" check out against the
code as written:

1. **The client speaks paths, not Graph.** `app/src/api_client.rs::get_list(path)`
   hits `{base}{path}` verbatim; there is no `Microsoft.Graph` symbol in `app/`. A
   provider that returns `ListItem` rows is rendered identically. Confirmed — the stub
   needs **zero** client changes.
2. **The surface set is a declarative catalog.** `service/Api/Surfaces.cs` is
   provider-neutral by shape (it names `/managed-devices`, not `ManagedDevice`).
   `SupportedSurfaces` slices it cleanly — the stub declares one row and the rest
   resolve to 404, exactly as the doc predicts the client already tolerates.
3. **The DTOs are normalized and Graph-free.** `ListItemDto` / `AssignmentDto` carry
   no Graph type. The stub projects fake Jamf inventory into a byte-identical
   `ListItem` schema; the contract-parity test corpus is untouched and still passes.

The seam **is** one projection step deep, and that step is the same *kind* of code
`ApplicationMapper` already runs for Graph — just starting from a different payload.

## What the doc got wrong (and the spike corrects)

### 1. The provider seam belongs in `Api/`, not `Core/`

The doc places `IMdmProvider` under `service/Core/Providers/`. **It can't live there.**
The interface's whole job is to return `ListItemDto` / `AssignmentDto`, and those DTOs
are defined in the **Api** project (`Contracts.cs`, `Endpoints/Assignments.cs`), which
*references* Core, not the other way round. Putting `IMdmProvider` in Core would invert
that dependency (Core → Api), and Core is a hard-forked engine we don't want to grow an
upward dependency in.

The honest seam is: **the projection-to-DTO step is an Api-layer concern, so the
provider interface is too.** The spike puts the whole seam under `service/Api/Providers/`.
A real `IntuneProvider` would be a thin Api-layer adapter that calls the *same* Core
services the endpoint modules call today and runs the *same* projection — Core stays a
pure Graph engine with no knowledge of the contract. This is a small correction to the
doc, not a blocker, but it changes where the code lives.

### 2. `IntuneProvider` is not "literally the endpoint body" — close, but not free

The doc says `IntuneProvider.ListAsync("device-configs", ct)` is *"literally the body
that lives in DevicesEndpoints.cs today."* Almost. Two frictions the spike surfaces:

- **The cache read-through is interleaved with the fetch.** Today every LIST goes
  through `CachedReader.ListAsync(cache, auth, "DeviceConfigurations", fetch, ct)`
  (DevicesEndpoints.cs:26) — the cache key, the tenant scoping, and the 409-when-signed-
  out check are *in the handler*, not in Core. An `IntuneProvider` has to either pull
  that plumbing in or the dispatch layer keeps it. The cleanest split is: **dispatch
  owns auth+cache, the provider owns fetch+project.** That's a refactor of every
  endpoint module, ~40 handlers — bounded, mechanical, but not "move the body once."
- **Surface keys don't map 1:1 to cache `dataType`s.** The route is `device-configs`;
  the cache key is `DeviceConfigurations`; the Core method is
  `ListDeviceConfigurationsAsync`. Today that mapping is hand-written per handler. A
  generic `IntuneProvider.ListAsync(surfaceKey)` needs a surface-key → (cacheKey,
  fetch-delegate, projection) table. `Surfaces.cs` already carries `CacheKey`, so the
  table is half-built — but the *projection lambda* and the *Core call* per surface are
  not yet data, they're code. Extracting ~38 of those is the real work of an
  `IntuneProvider`, and it's where Intune assumptions could leak if done carelessly.

Neither breaks the abstraction; both mean "lift Intune behind the interface" is a
**multi-day refactor of the Api layer**, not a move. That's the single biggest cost the
doc under-states.

## What's confirmed out of scope (the doc is honest here)

- **DETAIL parity.** `GetAsync` returns raw provider JSON; the stub emits a Jamf-shaped
  body. The client's detail pane + `JsonDrift.Diff` compare whatever shape comes back.
  Uniform LIST, provider-native DETAIL — as the doc's open question #2 leans.
- **Writes / assignments / round-trip fidelity.** The stub `501`s (→ 404) every write.
  Authoring a valid cross-provider body (`.mobileconfig` signing, WS1 versioning) is
  genuinely hard; M22 is read-first and this spike doesn't pretend otherwise.
- **Auth.** No second auth adapter was built. `AuthSession` is Entra-shaped
  (`CreateClientWithCredentialAsync`, `Cloud` enum, `GraphServiceClient`). A real Jamf
  provider needs a provider-keyed credential path; that's net-new and untouched here.
- **`providerId` on the profile.** The forked Core `TenantProfile` has no `providerId`
  field. Adding it is a Core-model change (with a `"intune"` default so existing
  profiles keep working). The spike sidesteps this with an explicit
  `/providers/{providerId}/{surface}` route so the active-tenant selection and the
  persisted profile model are untouched — the demo runs *next to* the live Intune path,
  not through it.

## Where it plugs into DI (as built)

`Program.cs` today registers the implicit Intune engine via
`builder.Services.AddIntuneCommanderCore()`. The spike adds, right after the existing
singletons:

```csharp
builder.Services.AddSingleton<IMdmProvider, StubMdmProvider>();   // read-only "jamf" stub
builder.Services.AddSingleton<IProviderRegistry, ProviderRegistry>();
...
app.MapProviders();   // /providers, /providers/{providerId}/{surface}[/{id}]
```

The full M22 grows this to the doc's shape — `AddSingleton<IMdmProvider, IntuneProvider>()`
alongside one registration per backend, with the registry resolving by
`activeProfile.ProviderId` and the plain `/managed-devices` route dispatching to
`registry.Get(profile.ProviderId).ListAsync(...)` instead of newing a Core service
inline. The spike deliberately stops short of touching the live route so nothing
regresses.

## Try it (hermetic — no sign-in)

```
GET /providers                                  → [{ "id":"jamf", "supportedSurfaces":["managed-devices"] }]
GET /providers/jamf/managed-devices             → ListItem[] (4 fake Jamf computers, source:jamf in subtitle)
GET /providers/jamf/managed-devices/312         → raw provider-native JSON
GET /providers/jamf/conditional-access          → 404 (unsupported on this provider)
```

The `managed-devices` response is the same `ListItem` schema the Intune endpoint emits
— the unchanged WinUI list workspace would render these rows next to Intune devices.
(One honest gap: `ListItem` has **no `source`/provider field** yet, so the stub tags
`source: jamf` into the `subtitle`. A first-class `source` field is the recommended
next step — it's a one-line addition to `ListItemDto` + the Rust `ListItem` + the
contract, and would let the client render a real provider chip instead of a subtitle
hack.)

## Recommendation

**Go further only after M18, and only for the read path first.** Concretely:

1. **Cheap, do-now-if-desired:** add a first-class `source` field to `ListItem`
   (contract + both DTO sets + parity corpus). It's the one piece of "uniform LIST"
   that's currently faked, and it unblocks a real provider chip in the client.
2. **The actual M22 work, gated on M18:** the `IntuneProvider` extraction (§"What the
   doc got wrong" #2) is the long pole — a bounded but multi-day Api-layer refactor that
   moves ~38 surface projections behind the interface and splits dispatch (auth+cache)
   from provider (fetch+project). Do this *first*, against Intune only, as a no-behavior-
   change refactor. If it lands clean, the abstraction has earned its keep and a
   `JamfProvider` is additive.
3. **Resolve the open questions before any of that** — especially #1 (one provider per
   profile: yes, keep it simple) and #5 (is the juice worth the squeeze: validate
   demand from dual-MDM orgs, since half the product — the Entra/identity plane — has no
   cross-MDM analog).

**Bottom line:** the architecture *permits* a second provider exactly as M22 claims;
the spike turned "permits" into a running read. The cost the doc under-states is the
`IntuneProvider` refactor, not the new provider. Nothing here is a reason to *not* do
M22 — but it stays a post-M18 stretch, read-first.
