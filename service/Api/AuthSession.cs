using Azure.Core;
using Azure.Identity;
using Intune.Commander.Core.Auth;
using Intune.Commander.Core.Models;
using Intune.Commander.Core.Services;
using Microsoft.Graph.Beta;

namespace CmProjectX.Api;

// Live sign-in session for the active tenant profile. Sign-in runs on a background
// task because device-code blocks until the user completes it in a browser; the
// prompt is surfaced through /health for the client to poll. The resulting
// GraphServiceClient is consumed by the sync engine once SignedIn.
public sealed class AuthSession
{
    private readonly ProfileService _profiles;
    private readonly IntuneGraphClientFactory _graphFactory;
    private readonly object _gate = new();

    private AuthState _state = AuthState.SignedOut;
    private DeviceCodePromptDto? _deviceCode;
    private string? _error;
    private Task? _signInTask;
    private CancellationTokenSource? _signInCts;
    // Monotonic sign-in generation. Bumped on every BeginSignIn and on SignOut, and
    // captured by the background RunSignInAsync. Every state write the task makes is
    // gated on the captured generation still being current, so a sign-in that finishes
    // (or is abandoned) AFTER the user signed out / switched tenant cannot clobber the
    // newer state or revive a dead — or wrong-tenant — Graph session.
    private int _generation;

    public AuthSession(ProfileService profiles, IntuneGraphClientFactory graphFactory)
    {
        _profiles = profiles;
        _graphFactory = graphFactory;
    }

    // Populated once sign-in succeeds.
    public GraphServiceClient? Graph { get; private set; }
    public string[]? Scopes { get; private set; }
    // The live TokenCredential from sign-in. Reused by read-only diagnostics (e.g.
    // /permission-check) so they hit the same token cache instead of building a fresh
    // credential — the latter would re-trigger an interactive browser prompt for
    // delegated (Interactive/DeviceCode) profiles and fail to bind the loopback the
    // live session still owns.
    public TokenCredential? Credential { get; private set; }
    public DateTime? LastSyncUtc { get; set; }

    // M12.1 — blob read-through cache warming (docs/CACHE-M12.1.md).
    // LastWarmedUtc is stamped only on a fully-successful warm (see MarkWarmed); the
    // host wires CacheWarm to a fire-and-forget PrefetchAllToCacheAsync at startup.
    public DateTime? LastWarmedUtc { get; private set; }
    public Func<GraphServiceClient, string, Task>? CacheWarm { get; set; }

    // Stamp the warm timestamp, never regressing it (overlapping warms can finish out
    // of order; a partial/failed warm must not stamp at all — callers invoke this only
    // after PrefetchAllToCacheAsync returns without throwing).
    public void MarkWarmed()
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            if (LastWarmedUtc is null || now > LastWarmedUtc) LastWarmedUtc = now;
        }
    }

    public IReadOnlyList<TenantProfile> Profiles => _profiles.Profiles;
    public string? ActiveProfileId => _profiles.ActiveProfileId;
    public TenantProfile? ActiveProfile => _profiles.GetActiveProfile();

    public Task InitializeAsync(CancellationToken ct = default) => _profiles.LoadAsync(ct);

    public async Task<bool> ActivateAsync(string profileId, CancellationToken ct = default)
    {
        var was = _profiles.ActiveProfileId;
        try { _profiles.SetActiveProfile(profileId); }
        catch (ArgumentException) { return false; }
        // Switching tenants invalidates the live session: a token issued for the
        // previous tenant is not valid for the new one, and the per-tenant blob cache
        // must never be read/written with a Graph client whose tenant differs from
        // ActiveProfile.TenantId (that would store one tenant's data under another's
        // key). Force a fresh sign-in for the newly-activated profile.
        if (!string.Equals(was, profileId, StringComparison.Ordinal))
            SignOut();
        await _profiles.SaveAsync(ct);
        return true;
    }

    // ─── M11 profile lifecycle — add / edit / delete saved tenant profiles ───
    // Persist via the forked Core ProfileService, which writes
    // %LocalAppData%\Intune.Commander\profiles.json and DataProtection-encrypts the
    // client secret (app name `IntuneManager` stays frozen).

    public async Task<TenantProfile> AddProfileAsync(
        string name, string tenantId, string clientId,
        string? cloud, string? authMethod, string? clientSecret,
        CancellationToken ct = default)
    {
        var profile = new TenantProfile
        {
            Name = name,
            TenantId = tenantId,
            ClientId = clientId,
            Cloud = ParseCloud(cloud),
            AuthMethod = ParseAuthMethod(authMethod),
            ClientSecret = string.IsNullOrWhiteSpace(clientSecret) ? null : clientSecret,
        };
        _profiles.AddProfile(profile);
        await _profiles.SaveAsync(ct);
        return profile;
    }

    // null/blank fields leave the existing value; an empty-string secret clears it.
    public async Task<bool> UpdateProfileAsync(
        string id, string? name, string? tenantId, string? clientId,
        string? cloud, string? authMethod, string? clientSecret,
        CancellationToken ct = default)
    {
        var profile = _profiles.Profiles.FirstOrDefault(p => p.Id == id);
        if (profile is null) return false;
        var wasActive = string.Equals(_profiles.ActiveProfileId, id, StringComparison.Ordinal);
        if (wasActive) SignOut();
        if (!string.IsNullOrWhiteSpace(name)) profile.Name = name;
        if (!string.IsNullOrWhiteSpace(tenantId)) profile.TenantId = tenantId;
        if (!string.IsNullOrWhiteSpace(clientId)) profile.ClientId = clientId;
        if (cloud is not null) profile.Cloud = ParseCloud(cloud);
        if (authMethod is not null) profile.AuthMethod = ParseAuthMethod(authMethod);
        if (clientSecret is not null)
            profile.ClientSecret = clientSecret.Length == 0 ? null : clientSecret;
        await _profiles.SaveAsync(ct);
        return true;
    }

    public async Task<bool> DeleteProfileAsync(string id, CancellationToken ct = default)
    {
        if (_profiles.Profiles.All(p => p.Id != id)) return false;
        var wasActive = string.Equals(_profiles.ActiveProfileId, id, StringComparison.Ordinal);
        if (wasActive) SignOut();
        _profiles.RemoveProfile(id);
        await _profiles.SaveAsync(ct);
        return true;
    }

    private static CloudEnvironment ParseCloud(string? s) => s switch
    {
        "GCC" => CloudEnvironment.GCC,
        "GCCHigh" => CloudEnvironment.GCCHigh,
        "DoD" => CloudEnvironment.DoD,
        _ => CloudEnvironment.Commercial,
    };

    private static AuthMethod ParseAuthMethod(string? s) => s switch
    {
        "ClientSecret" => AuthMethod.ClientSecret,
        "DeviceCode" => AuthMethod.DeviceCode,
        _ => AuthMethod.Interactive,
    };

    public (AuthState State, DeviceCodePromptDto? DeviceCode, string? Error) Snapshot()
    {
        lock (_gate) return (_state, _deviceCode, _error);
    }

    // Returns false when there is no active profile to sign in.
    public bool BeginSignIn()
    {
        lock (_gate)
        {
            if (_state is AuthState.SigningIn or AuthState.AwaitingDeviceCode or AuthState.AwaitingInteractive)
                return true; // already in flight

            var profile = _profiles.GetActiveProfile();
            if (profile is null) return false;

            _state = AuthState.SigningIn;
            _deviceCode = null;
            _error = null;
            _signInCts?.Dispose(); // the previous (completed or canceled) source
            _signInCts = new CancellationTokenSource();
            var generation = ++_generation;
            var token = _signInCts.Token;
            _signInTask = Task.Run(() => RunSignInAsync(profile, generation, token));
            return true;
        }
    }

    // Runs on a background task. `generation`/`ct` are captured at BeginSignIn; every
    // state write is gated on the generation still being current so a SignOut or tenant
    // switch that happened while we were awaiting the token wins (and `ct` lets SignOut
    // actually abort an in-flight interactive/device-code prompt).
    private async Task RunSignInAsync(TenantProfile profile, int generation, CancellationToken ct)
    {
        try
        {
            Func<DeviceCodeInfo, CancellationToken, Task> onDeviceCode = (info, _) =>
            {
                lock (_gate)
                {
                    if (generation != _generation) return Task.CompletedTask; // superseded
                    _state = AuthState.AwaitingDeviceCode;
                    _deviceCode = new DeviceCodePromptDto(
                        info.UserCode,
                        info.VerificationUri.ToString(),
                        info.Message,
                        info.ExpiresOn.UtcDateTime.ToString("o"));
                }
                return Task.CompletedTask;
            };

            var (client, credential, scopes) =
                await _graphFactory.CreateClientWithCredentialAsync(profile, onDeviceCode, ct);

            // Interactive browser auth blocks in GetTokenAsync below while the user
            // completes sign-in in the system browser the sidecar opens. Surface a
            // distinct state so the client shows "continue in your browser" rather than
            // a generic spinner. (DeviceCode reaches AwaitingDeviceCode via onDeviceCode;
            // ClientSecret is a silent acquisition and stays SigningIn.)
            if (profile.AuthMethod == AuthMethod.Interactive)
            {
                lock (_gate)
                {
                    if (generation == _generation) _state = AuthState.AwaitingInteractive;
                }
            }

            // Force token acquisition now so the device-code/interactive prompt fires
            // here and any failure is reported as Failed rather than on first /sync.
            await credential.GetTokenAsync(new TokenRequestContext(scopes), ct);

            var signedIn = false;
            lock (_gate)
            {
                if (generation == _generation)
                {
                    Graph = client;
                    Scopes = scopes;
                    Credential = credential;
                    _state = AuthState.SignedIn;
                    _deviceCode = null;
                    _error = null;
                    signedIn = true;
                }
                // else: signed out / switched tenant while we awaited the token — drop
                // this client rather than reviving a dead (or wrong-tenant) session.
            }

            // M12.1 — kick a fire-and-forget blob-cache warm now that Graph + tenant
            // are known. Never blocks sign-in; the client discovers readiness via the
            // lastWarmedUtc it already polls on /health.
            if (signedIn && CacheWarm is not null)
                _ = CacheWarm(client, profile.TenantId);
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                if (generation != _generation) return; // superseded; don't clobber newer state
                Graph = null;
                Scopes = null;
                Credential = null;
                _state = AuthState.Failed;
                _deviceCode = null;
                _error = ex is OperationCanceledException ? "Sign-in canceled." : ex.Message;
            }
        }
    }

    public void SignOut()
    {
        lock (_gate)
        {
            // Abort an in-flight interactive/device-code prompt and invalidate any
            // background RunSignInAsync writes (bumping the generation), so a sign-in the
            // user just abandoned can't later revive the session. The next BeginSignIn
            // disposes this canceled source before creating a fresh one.
            _signInCts?.Cancel();
            _generation++;
            Graph = null;
            Scopes = null;
            Credential = null;
            _state = AuthState.SignedOut;
            _deviceCode = null;
            _error = null;
            LastWarmedUtc = null; // the session's warm no longer reflects the live state
        }
    }
}
