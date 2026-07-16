# M22 — Moonshot: beyond Intune (cross-MDM)

> Push the M6 uniform contract one level up: the same `(verb, path, body)` shape — plus plan/apply (M15), twin (M17), and the autonomy loop (M18) — served over a **second MDM provider** (Jamf Pro, Omnissa/VMware Workspace ONE). Turn "an Intune tool" into **the cross-platform device-management control plane.**

**Status: stretch / vision — sets the ceiling, not committed.** This doc is the north star, not a plan of record. Revisit only after M18 ships and the contract has *empirically* proven provider-agnostic (see "Why it's last"). Every honest gap is called out; nothing here is scheduled.

---

## Why it's even plausible

The whole upper stack already doesn't know it's talking to Intune. Three properties make a second provider a *Core-only* change rather than a rewrite:

1. **The client speaks paths, never Graph.** `app/src/api_client.rs` is a blocking `reqwest` wrapper whose generic methods take a *string path*: `get_list(&self, path: &str)` (api_client.rs:144) and `get_detail(&self, path, id)` (api_client.rs:153) hit `{base}{path}` / `{base}{path}/{id}` verbatim. The path comes from the `features.rs` registry row. There is no `Microsoft.Graph` symbol anywhere in `app/`. Swap the bytes behind `/device-configs` and the client is none the wiser.

2. **The surface set is a declarative catalog.** `service/Api/Surfaces.cs` enumerates every management surface as a `Surface(Path, DisplayName, Section, Writable, Assignable, CacheKey)` row. It already encodes the *capability contract* a backend must satisfy: which surfaces are read-only, which are full CRUD, which expose `/{id}/assignments`. That catalog is provider-neutral by shape — it names `/compliance-policies`, not `DeviceCompliancePolicy`.

3. **The DTOs are normalized and Graph-free.** `crates/api-types/src/lib.rs` defines `ListItem { id, title, subtitle, badge, platform?, modified? }` (lib.rs:177) and `Assignment { kind, group_id, group_name, filter_id, filter_mode, intent? }` (lib.rs:377). These are *already* a projection target — the endpoint modules flatten Graph union types down into them server-side. The time-machine (`SnapshotStore`), the M15 GitOps plan/apply, the M16 simulator, the M17 twin, the M18 loop, and the M13 HITL inbox all consume these DTOs and the uniform contract. None imports a Graph type.

The seam is therefore exactly one layer deep: **per-surface endpoint modules project a provider's native types into the shared DTOs.** That projection is already an explicit, isolated step — see `DevicesEndpoints.MapDevices` projecting `ConfigurationProfileService` results into `ListItemDto` (DevicesEndpoints.cs:27-33). Today that step starts from `Microsoft.Graph.Beta.Models`. M22's bet is that the *same step* can start from a Jamf payload or a Workspace ONE profile instead.

---

## The provider seam

Today the endpoint modules construct Core services inline against the active Graph client (`new ConfigurationProfileService(g)` — DevicesEndpoints.cs:26). To admit a second provider we lift that into an interface keyed by the catalog `Surface.Key`, so an endpoint asks *the active provider* for a surface's data rather than newing a Graph-specific service:

```csharp
// service/Core/Providers/IMdmProvider.cs  (net-new)
public interface IMdmProvider
{
    string Id { get; }                       // "intune" | "jamf" | "workspaceone"
    IReadOnlyList<Surface> SupportedSurfaces { get; }   // ⊆ Surfaces.All

    // Reads — already the shape every endpoint produces today.
    Task<IReadOnlyList<ListItemDto>> ListAsync(string surfaceKey, CancellationToken ct);
    Task<string?> GetAsync(string surfaceKey, string id, CancellationToken ct);   // raw JSON body

    // Writes — gated by Surface.Writable; provider 501s an unsupported verb.
    Task<string> CreateAsync(string surfaceKey, string bodyJson, CancellationToken ct);  // returns new id
    Task UpdateAsync(string surfaceKey, string id, string bodyJson, CancellationToken ct);
    Task DeleteAsync(string surfaceKey, string id, CancellationToken ct);

    // Assignments — gated by Surface.Assignable; projects native targeting → Assignment DTO.
    Task<IReadOnlyList<AssignmentDto>> GetAssignmentsAsync(string surfaceKey, string id, CancellationToken ct);
    Task SetAssignmentsAsync(string surfaceKey, string id, IReadOnlyList<AssignmentDto> a, CancellationToken ct);
}
```

A provider implements only the surfaces it supports; `SupportedSurfaces` is its honest declaration of overlap with `Surfaces.All`. Surfaces it doesn't list resolve to `404 (unsupported on this provider)`, which the client already tolerates the same way it tolerates `409`-when-signed-out (api_client.rs:146).

**Where it plugs into DI.** Today `Program.cs` registers exactly one Graph-backed engine:

```csharp
builder.Services.AddIntuneCommanderCore();          // Program.cs:32 — the Intune provider, implicitly
builder.Services.AddSingleton<AuthSession>();        // Program.cs:34
```

M22 makes the provider explicit and keyed, with the active one selected from the tenant profile:

```csharp
builder.Services.AddIntuneCommanderCore();                       // Intune provider impl
builder.Services.AddSingleton<IMdmProvider, IntuneProvider>();   // wraps the forked Graph engine
builder.Services.AddSingleton<IMdmProvider, JamfProvider>();     // net-new
builder.Services.AddSingleton<IMdmProvider, WorkspaceOneProvider>();
builder.Services.AddSingleton<IProviderRegistry, ProviderRegistry>();  // resolve by profile.ProviderId
```

`IntuneProvider` is a thin adapter: its `ListAsync("device-configs", ct)` is *literally the body that lives in DevicesEndpoints.cs today* — `new ConfigurationProfileService(g).ListDeviceConfigurationsAsync(ct)` then the `ListItemDto` projection. No Graph behavior changes; it just moves behind the interface. The endpoint modules become provider-dispatch: `registry.Active.ListAsync(surfaceKey, ct)`. `NewProfile`/`TenantProfileSummary` gain a `provider_id` field (Intune is the default, preserving every existing profile).

---

## Mapping a second provider

Honest, surface-by-surface. "✅" = direct projection into the existing DTO; "≈" = lossy/partial; "—" = no equivalent.

| cmProjectX surface (`Surfaces.cs`) | Jamf Pro | Workspace ONE | Notes |
|---|---|---|---|
| `/device-configs` | Configuration Profiles (`/v1/macos-configuration-profiles`, mobiledevice profiles) | Device Profiles (`/mdm/profiles`) | ≈ — payload model differs hard (see hard parts) |
| `/compliance-policies` | Compliance is **Smart Group + policy** composition, not a first-class object | Compliance Policies (`/mdm/groups/compliance`) | ≈ Jamf has no 1:1 "compliance policy" object |
| `/settings-catalog` | — (no settings-catalog graph) | — | Intune-specific authoring model |
| `/admin-templates` / `/admx-files` | macOS has no ADMX; Windows via WS1 only | Custom Settings (Win) | — on Jamf |
| `/apps` | Mac/Mobile Apps, VPP apps (`/v1/mac-applications`) | Applications (`/mam/apps`) | ✅ projects to `AppListItem`/`ListItem` cleanly |
| `/groups` | **Smart Groups / Static Groups** (`/v1/computer-groups`) | **Smart Groups** / OG assignment | ✅ the closest cross-provider concept — dynamic membership |
| `/managed-devices` | Computers / Mobile Devices inventory | Devices (`/mdm/devices`) | ✅ read-only inventory, maps well |
| `/scope-tags` | **Sites** (Jamf's scoping primitive) | Organization Groups (OGs) | ≈ semantics differ but role is the same |
| `/remediation-scripts` / `/platform-scripts` | Scripts + Policies | Scripts (Freestyle/Workflows) | ≈ execution model differs |
| `/conditional-access` / `/named-locations` / `/auth-strengths` | — (Entra/identity-plane, not MDM) | — | identity surfaces stay Intune-only |
| `/autopilot` / `/apple-dep` / `/enrollment-configs` | PreStage Enrollments, Automated Device Enrollment | Enrollment profiles | ≈ Apple ADE overlaps; Autopilot is Windows/Intune-only |
| `/feature-updates` / `/quality-updates` / `/driver-updates` | macOS/iOS update *deferral* via config profile | Update policies | ≈ Windows-update cadence is Intune-shaped |

The strongest, lowest-risk overlap is **groups + apps + device inventory** — exactly the surfaces that are already `ListItem`-shaped and either read-only or simple CRUD. The identity plane (Conditional Access, named locations, auth strengths) is Entra, not MDM, and stays Intune-only — those surfaces simply aren't in a Jamf/WS1 provider's `SupportedSurfaces`.

---

## What's reused for free vs net-new

**Reused, unchanged (provider-agnostic by construction):**

- **The client** (`app/`) — drives everything by `path` (api_client.rs:144,153); renders `ListItem` uniformly. Zero changes for a new provider beyond a provider badge.
- **The time-machine** (`service/Store/SnapshotStore.cs`) — snapshots normalized JSON bodies keyed by object id + content hash; append-only. Provider-blind.
- **M13 HITL inbox** — `PendingChange` stores `(verb, path, body)` and replays it through the same write pipeline (PLUGINS-MCP.md §4). A Jamf write is just a different `path` resolving to a different provider; the propose→approve→replay rails are identical.
- **M15 GitOps plan/apply** — desired state is contract bodies, not Graph types; `plan` diffs DTO bodies, `apply` issues `(verb, path, body)`.
- **M16 simulator / M17 twin / M18 autonomy loop** — all consume `ListItem`/`Assignment` + the uniform contract. They model assignment outcomes from the normalized `Assignment` DTO, not from Graph.
- **The MCP tool surface** (M13) — generated mechanically from `Surfaces.cs` (PLUGINS-MCP.md §2). A provider that declares supported surfaces gets MCP tools for free.

**Net-new (one bounded thing per provider):**

- **One `IMdmProvider` Core implementation** — `JamfProvider` / `WorkspaceOneProvider`, projecting native types → `ListItemDto`/`AssignmentDto` (the only genuinely new code, and it's the same *kind* of projection `ApplicationMapper.cs` already does for Graph).
- **One auth adapter** — Jamf API client + token (or OAuth client-credentials on newer Jamf), or WS1 OAuth/basic. `AuthSession` gains a provider-keyed credential path; `NewProfile` gains `provider_id` + provider-specific fields.

---

## Sample data

The point of the whole architecture: **the wire shape is identical regardless of backend.** Two `GET /device-configs` responses, same `ListItem` schema, distinguished only by a `source` annotation (a thin addition to `ListItem`; today the field is implicit-Intune).

Intune (today, `ConfigurationProfileService` → `ListItemDto`, DevicesEndpoints.cs:27):

```json
{
  "id": "a1f3c2e0-0000-4b2a-9f10-1122334455aa",
  "title": "macOS — FileVault Baseline",
  "subtitle": "macOSGeneralDeviceConfiguration • v3",
  "badge": null,
  "platform": "macOS",
  "modified": "2026-05-18",
  "source": "intune"
}
```

Jamf Pro (M22, `JamfProvider` projecting a `/v1/macos-configuration-profiles` payload → the *same* DTO):

```json
{
  "id": "312",
  "title": "macOS — FileVault Baseline",
  "subtitle": "Configuration Profile • 4 payloads",
  "badge": null,
  "platform": "macOS",
  "modified": "2026-05-18",
  "source": "jamf"
}
```

The client renders both rows identically; only the `source` chip differs. The `Assignment` DTO is the unification point for targeting — a Jamf Smart Group scope projects into the same shape an Entra group assignment does:

```json
{ "kind": "group", "groupId": "57", "groupName": "All Managed Macs (Smart Group)",
  "filterId": null, "filterMode": null, "intent": null }
```

**Provider registration** (tenant profile, `provider_id` net-new on `NewProfile`):

```json
{
  "name": "Acme — Jamf Pro",
  "providerId": "jamf",
  "baseUrl": "https://acme.jamfcloud.com",
  "authMethod": "ClientSecret",
  "clientId": "a3b1...-jamf-api-role",
  "clientSecret": "••••••"
}
```

`providerId` defaults to `"intune"` when absent, so every existing profile keeps working untouched.

---

## The hard parts (honest)

1. **Semantic mismatch in the data model.** Intune's Settings Catalog is a flat `(settingDefinitionId, value)` bag; Jamf configuration profiles are nested **payload** dictionaries (a signed `.mobileconfig`); WS1 has its own profile JSON. `ListItem` survives this trivially (title/subtitle/platform), but **DETAIL and write bodies do not** — `get_detail` returns raw provider JSON (api_client.rs:153), and the M6 field-level diff (`JsonDrift.Diff`) compares whatever shape the provider emits. Cross-provider *DETAIL* parity is out of scope; M22's win is uniform LIST + per-provider DETAIL.

2. **Auth models don't rhyme.** Entra is OAuth with delegated/app-only flows and tenant clouds (`Cloud::{Commercial,Gcc,GccHigh,DoD}`, lib.rs:78). Jamf is a per-instance base URL with an API role token (bearer, short TTL, refreshable) — *no tenant directory*. WS1 is OAuth client-credentials against a region host. `AuthSession` is currently Entra-shaped; it needs a provider-keyed credential strategy, and `Cloud` becomes Intune-only.

3. **Partial surface overlap is real, not cosmetic.** Half the catalog (Conditional Access, Autopilot, ADMX, Settings Catalog, named locations, auth strengths) has **no Jamf/WS1 equivalent** — it's Entra/Windows-specific. A non-Intune provider's `SupportedSurfaces` is a *subset*, so the nav must gray out or hide unsupported surfaces per active provider. That's a `features.rs` filter against the active provider's declared set — small, but new UX.

4. **Simulator and twin need per-provider assignment semantics.** M16/M17 model "who gets this policy" from the `Assignment` DTO. But Intune evaluates assignment-filter expressions + include/exclude group precedence; Jamf evaluates **Smart Group criteria** (inventory-attribute logic); WS1 evaluates OG hierarchy. The DTO unifies the *shape* of targeting, not its *evaluation*. A faithful twin needs a per-provider resolver — the simulator's evaluation engine becomes pluggable, not the contract.

5. **Write fidelity / round-trip.** Jamf `.mobileconfig` signing, WS1 profile versioning, and Intune's Settings-Catalog split-update (which an endpoint module already special-cases) are all backend quirks. The HITL replay rails carry the body opaquely, but **authoring** a valid cross-provider body is hard — M22 leans read-first and treats writes as provider-native, not portable.

---

## Definition of done (stretch)

A single **READ** surface, served by a **non-Intune** provider, behind the **same contract**, rendering in the **existing client unchanged**:

- Add a `jamf` tenant profile (`providerId: "jamf"`, base URL + API token).
- `JamfProvider` implements `ListAsync("managed-devices", …)` (or `/groups`) projecting Jamf inventory → `ListItemDto`.
- `GET /managed-devices` returns the normalized `ListItem` array — byte-identical schema to the Intune response, `source: "jamf"`.
- The unchanged WinUI list workspace renders Jamf computers next to (a different profile's) Intune devices, with a provider chip.
- The snapshot store captures the Jamf bodies; the audit timeline records the reads — both provider-blind.

No writes, one surface, one provider. That single end-to-end read is the proof the abstraction holds one level up.

---

## Why it's last / open questions

This is deliberately the **final** milestone and explicitly a moonshot. It's only worth attempting once the contract has *earned* the claim of being provider-agnostic — i.e. after M18 ships and the loop/twin/simulator have run in anger against Intune. Building a second provider before then risks hard-coding Intune assumptions into "neutral" layers we haven't stress-tested.

Open questions to resolve *before* committing any of this:

1. **One active provider or federated?** Is a profile single-provider (simplest — matches today's one-`AuthSession` model), or does one workspace span Intune + Jamf simultaneously? Federated multiplies the assignment/twin complexity. *Lean: one provider per profile first.*
2. **DETAIL parity — punt or portal?** Accept provider-native DETAIL JSON (cheap, honest), or invest in a normalized detail model (expensive, lossy)? *Lean: provider-native DETAIL; uniform LIST only.*
3. **Where does the provider live?** A new `service/Core/Providers/` tree, or a separate sidecar per provider behind the same `:5099` contract? *Lean: in-process Core, mirroring `AddIntuneCommanderCore()`.*
4. **Simulator evaluation engine.** Make assignment evaluation pluggable per provider (Smart Group criteria vs filter expressions) — design before M16/M17 harden, or retrofit later?
5. **Is the juice worth the squeeze?** The Entra/identity surfaces — half the product's value — have no cross-MDM analog. M22 is most compelling for orgs running *both* Intune and Jamf who want one drift/audit time-machine across both, not for single-MDM shops. Validate that demand before building.

The architecture *permits* this. M22 is the claim that "permits" can become "does." Until M18 proves the seam, it stays vision.
