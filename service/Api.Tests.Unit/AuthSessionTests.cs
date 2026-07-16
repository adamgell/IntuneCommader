using CmProjectX.Api;
using Intune.Commander.Core.Models;
using Intune.Commander.Core.Services;
using Xunit;

namespace CmProjectX.Tests.Unit;

// Tier-1 tests for the auth/profile state machine (service/Api/AuthSession.cs).
// We exercise only the transitions that DON'T touch MSAL/Graph/network: sign-out,
// the warm-stamp lifecycle, tenant-switch session invalidation, and the cloud/
// auth enum mapping — all against a ProfileService rooted in a throwaway temp dir.
// The graph factory is never used on these paths, so it is passed as null.
public sealed class AuthSessionTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "cmpx-auth-" + Guid.NewGuid().ToString("N"));

    private AuthSession NewSession()
    {
        Directory.CreateDirectory(_dir);
        var profiles = new ProfileService(Path.Combine(_dir, "profiles.json"));
        return new AuthSession(profiles, graphFactory: null!); // unused on the tested paths
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void BeginSignIn_WithNoActiveProfile_ReturnsFalse()
    {
        var s = NewSession();
        Assert.False(s.BeginSignIn());
        Assert.Equal(AuthState.SignedOut, s.Snapshot().State);
    }

    [Fact]
    public void SignOut_ClearsWarmStampAndState()
    {
        var s = NewSession();
        s.MarkWarmed();
        Assert.NotNull(s.LastWarmedUtc);

        s.SignOut();
        Assert.Null(s.LastWarmedUtc);
        Assert.Equal(AuthState.SignedOut, s.Snapshot().State);
    }

    [Fact]
    public async Task Activate_DifferentProfile_InvalidatesSession()
    {
        var s = NewSession();
        var p1 = await s.AddProfileAsync("P1", "tid-1", "cid-1", null, null, null);
        var p2 = await s.AddProfileAsync("P2", "tid-2", "cid-2", null, null, null);

        Assert.True(await s.ActivateAsync(p1.Id));
        s.MarkWarmed();
        Assert.NotNull(s.LastWarmedUtc);

        Assert.True(await s.ActivateAsync(p2.Id));  // switching tenants must force a fresh sign-in
        Assert.Null(s.LastWarmedUtc);    // observable proof SignOut ran (warm no longer valid)
    }

    [Fact]
    public async Task Activate_SameProfile_PreservesSession()
    {
        var s = NewSession();
        var p1 = await s.AddProfileAsync("P1", "tid-1", "cid-1", null, null, null);

        Assert.True(await s.ActivateAsync(p1.Id));
        s.MarkWarmed();
        Assert.True(await s.ActivateAsync(p1.Id));  // re-activating the same profile is a no-op
        Assert.NotNull(s.LastWarmedUtc); // session NOT invalidated
    }

    [Fact]
    public async Task Activate_UnknownProfile_ReturnsFalse()
    {
        var s = NewSession();
        Assert.False(await s.ActivateAsync("does-not-exist"));
    }

    [Fact]
    public async Task UpdateActiveProfile_InvalidatesSession()
    {
        var s = NewSession();
        var p1 = await s.AddProfileAsync("P1", "tid-1", "cid-1", null, null, null);

        Assert.True(await s.ActivateAsync(p1.Id));
        s.MarkWarmed();
        Assert.NotNull(s.LastWarmedUtc);

        Assert.True(await s.UpdateProfileAsync(p1.Id, null, "tid-1b", null, null, null, null));
        Assert.Null(s.LastWarmedUtc);
        Assert.Equal(AuthState.SignedOut, s.Snapshot().State);
    }

    [Fact]
    public async Task DeleteActiveProfile_InvalidatesSession()
    {
        var s = NewSession();
        var p1 = await s.AddProfileAsync("P1", "tid-1", "cid-1", null, null, null);

        Assert.True(await s.ActivateAsync(p1.Id));
        s.MarkWarmed();
        Assert.NotNull(s.LastWarmedUtc);

        Assert.True(await s.DeleteProfileAsync(p1.Id));
        Assert.Null(s.LastWarmedUtc);
        Assert.Equal(AuthState.SignedOut, s.Snapshot().State);
    }

    [Theory]
    [InlineData("GCC", CloudEnvironment.GCC)]
    [InlineData("GCCHigh", CloudEnvironment.GCCHigh)]
    [InlineData("DoD", CloudEnvironment.DoD)]
    [InlineData("Commercial", CloudEnvironment.Commercial)]
    [InlineData(null, CloudEnvironment.Commercial)]    // unset → default
    [InlineData("nonsense", CloudEnvironment.Commercial)]
    public async Task AddProfile_MapsCloudSpelling(string? cloud, CloudEnvironment expected)
    {
        var s = NewSession();
        var p = await s.AddProfileAsync("P", "tid", "cid", cloud, null, null);
        Assert.Equal(expected, p.Cloud);
    }

    [Theory]
    [InlineData("ClientSecret", AuthMethod.ClientSecret)]
    [InlineData("DeviceCode", AuthMethod.DeviceCode)]
    [InlineData("Interactive", AuthMethod.Interactive)]
    [InlineData(null, AuthMethod.Interactive)]         // unset → default
    public async Task AddProfile_MapsAuthMethodSpelling(string? method, AuthMethod expected)
    {
        var s = NewSession();
        var p = await s.AddProfileAsync("P", "tid", "cid", null, method, null);
        Assert.Equal(expected, p.AuthMethod);
    }
}
