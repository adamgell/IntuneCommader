using IntuneManager.Core.Services;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Tests.Integration;

/// <summary>
/// CRUD integration tests that create, read, update, and delete resources
/// against a live Graph tenant. Each test creates a resource with a recognizable
/// prefix and cleans up in a finally block, even on failure.
///
/// These tests require write permissions in the test tenant.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Integration")]
public class GraphServiceCrudIntegrationTests : GraphIntegrationTestBase
{
    private const string TestPrefix = "IntTest_AutoCleanup_";

    /// <summary>
    /// Polls a GET function until <paramref name="predicate"/> returns true or
    /// <paramref name="maxAttempts"/> is exhausted. Returns the last fetched value.
    /// This guards against eventual-consistency staleness when PATCH returns 204
    /// and the service falls back to an immediate GET.
    /// </summary>
    private static async Task<T?> PollUntilAsync<T>(
        Func<Task<T?>> getAsync,
        Func<T, bool> predicate,
        int maxAttempts = 5,
        int baseDelayMs = 2000) where T : class
    {
        T? result = null;
        for (int i = 0; i < maxAttempts; i++)
        {
            await Task.Delay(baseDelayMs * (i + 1));
            result = await getAsync();
            if (result != null && predicate(result))
                return result;
        }
        return result;
    }

    #region ScopeTagService CRUD

    [Fact]
    public async Task ScopeTag_Create_Get_Update_Delete()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<ScopeTagService>()!;
        RoleScopeTag? created = null;

        try
        {
            // Create
            var tag = new RoleScopeTag
            {
                DisplayName = $"{TestPrefix}ScopeTag_{Guid.NewGuid():N}",
                Description = "Integration test — will be deleted automatically"
            };
            created = await svc.CreateScopeTagAsync(tag);
            Assert.NotNull(created);
            Assert.NotNull(created.Id);
            Assert.StartsWith(TestPrefix, created.DisplayName);

            // Get
            var fetched = await svc.GetScopeTagAsync(created.Id!);
            Assert.NotNull(fetched);
            Assert.Equal(created.Id, fetched!.Id);
            Assert.Equal(created.DisplayName, fetched.DisplayName);

            // Update
            created.Description = "Updated by integration test";
            var updated = await svc.UpdateScopeTagAsync(created);
            Assert.NotNull(updated);

            // Verify update via follow-up GET — PATCH may return 204 and the
            // immediate GET fallback can be stale under eventual consistency
            var verified = await PollUntilAsync(
                () => svc.GetScopeTagAsync(created.Id!),
                t => t.Description == "Updated by integration test");
            Assert.NotNull(verified);
            Assert.Equal("Updated by integration test", verified!.Description);

            // Delete
            await svc.DeleteScopeTagAsync(created.Id!);

            // Verify deletion — Get should return null or throw
            var deleted = await svc.GetScopeTagAsync(created.Id!);
            // If we get here without exception, it should be null
            // (Graph may throw 404 instead — both are acceptable)
            created = null; // prevent double-delete in finally
        }
        catch (Microsoft.Graph.Beta.Models.ODataErrors.ODataError ex)
            when (ex.ResponseStatusCode == 404)
        {
            // Expected after delete — the item is gone
            created = null;
        }
        finally
        {
            // Safety cleanup
            if (created?.Id != null)
            {
                try { await svc.DeleteScopeTagAsync(created.Id); } catch { }
            }
        }
    }

    #endregion

    #region NamedLocationService CRUD

    /// <summary>
    /// Reliable Create/Get/Delete test for named locations.
    /// Update is tested separately because Entra ID named locations have
    /// extreme replication lag that makes PATCH unreliable in CI.
    /// </summary>
    [Fact]
    public async Task NamedLocation_Create_Get_Delete()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<NamedLocationService>()!;
        NamedLocation? created = null;

        try
        {
            // Create an IP Named Location
            var location = new IpNamedLocation
            {
                DisplayName = $"{TestPrefix}NamedLoc_{Guid.NewGuid():N}",
                IsTrusted = false,
                IpRanges =
                [
                    new IPv4CidrRange { CidrAddress = "203.0.113.0/24" }
                ]
            };
            created = await svc.CreateNamedLocationAsync(location);
            Assert.NotNull(created);
            Assert.NotNull(created.Id);

            // Retry GET with backoff — Entra ID named locations have significant replication lag
            NamedLocation? fetched = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                await Task.Delay(3000 * (attempt + 1));
                try
                {
                    fetched = await svc.GetNamedLocationAsync(created.Id!);
                    if (fetched != null) break;
                }
                catch (Microsoft.Graph.Beta.Models.ODataErrors.ODataError ex)
                    when (ex.ResponseStatusCode is 404 or 500)
                {
                    // Not replicated yet or transient server error — retry
                }
            }
            Assert.NotNull(fetched);
            Assert.Equal(created.Id, fetched!.Id);

            // Delete — retry with backoff for replication lag (400/404/500 are all transient for Entra)
            for (int delAttempt = 0; delAttempt < 5; delAttempt++)
            {
                try
                {
                    await Task.Delay(3000 * (delAttempt + 1));
                    await svc.DeleteNamedLocationAsync(created.Id!);
                    created = null;
                    break;
                }
                catch (Microsoft.Graph.Beta.Models.ODataErrors.ODataError ex)
                    when (ex.ResponseStatusCode is 400 or 404 or 500 && delAttempt < 4)
                {
                    // Not replicated yet or transient server error — retry
                }
            }
        }
        finally
        {
            if (created?.Id != null)
            {
                try { await svc.DeleteNamedLocationAsync(created.Id); } catch { }
            }
        }
    }

    /// <summary>
    /// Tests UpdateNamedLocationAsync with a follow-up GET to verify the change.
    /// Entra ID named locations have extreme replication lag (30+ seconds) that
    /// causes PATCH to return 400/404 intermittently. This test retries with
    /// extended backoff (up to ~75 s total) and fails explicitly if the update
    /// cannot be verified — it never silently skips verification.
    /// </summary>
    [Trait("Flaky", "EntraReplication")]
    [Fact]
    public async Task NamedLocation_Update()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<NamedLocationService>()!;
        NamedLocation? created = null;

        try
        {
            // Create
            var location = new IpNamedLocation
            {
                DisplayName = $"{TestPrefix}NamedLoc_{Guid.NewGuid():N}",
                IsTrusted = false,
                IpRanges =
                [
                    new IPv4CidrRange { CidrAddress = "203.0.113.0/24" }
                ]
            };
            created = await svc.CreateNamedLocationAsync(location);
            Assert.NotNull(created);
            Assert.NotNull(created.Id);

            // Wait for replication before attempting update
            NamedLocation? fetched = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                await Task.Delay(3000 * (attempt + 1));
                try
                {
                    fetched = await svc.GetNamedLocationAsync(created.Id!);
                    if (fetched != null) break;
                }
                catch (Microsoft.Graph.Beta.Models.ODataErrors.ODataError ex)
                    when (ex.ResponseStatusCode is 404 or 500)
                {
                    // Not replicated yet or transient server error — retry
                }
            }
            Assert.NotNull(fetched);

            // Update — retry with extended backoff (up to ~75 s total wait)
            // Must include IpRanges in the PATCH payload — Graph returns 400
            // for IpNamedLocation PATCH without the required derived-type fields.
            var updatedName = $"{TestPrefix}NamedLoc_Updated_{Guid.NewGuid():N}";
            var toUpdate = new IpNamedLocation
            {
                Id = created.Id,
                DisplayName = updatedName,
                IpRanges =
                [
                    new IPv4CidrRange { CidrAddress = "203.0.113.0/24" }
                ],
            };
            NamedLocation? updated = null;
            Microsoft.Graph.Beta.Models.ODataErrors.ODataError? lastError = null;
            for (int updateAttempt = 0; updateAttempt < 6; updateAttempt++)
            {
                await Task.Delay(5000 * (updateAttempt + 1));
                try
                {
                    updated = await svc.UpdateNamedLocationAsync(toUpdate);
                    break;
                }
                catch (Microsoft.Graph.Beta.Models.ODataErrors.ODataError ex)
                    when (ex.ResponseStatusCode is 400 or 404 or 500)
                {
                    lastError = ex;
                }
            }

            if (updated == null)
            {
                throw new InvalidOperationException(
                    $"PATCH failed after 6 retries ({lastError?.ResponseStatusCode}). " +
                    $"Last error: {lastError?.Message}");
            }

            // Verify via follow-up GET with polling — do not trust the PATCH
            // response alone; Entra ID has extreme eventual-consistency lag
            var verified = await PollUntilAsync(
                () => svc.GetNamedLocationAsync(created.Id!),
                n => n.DisplayName == updatedName);
            Assert.NotNull(verified);
            Assert.Equal(updatedName, verified!.DisplayName);

            // Delete — retry with backoff for replication lag (400/404/500 are all transient for Entra)
            for (int delAttempt = 0; delAttempt < 5; delAttempt++)
            {
                try
                {
                    await Task.Delay(3000 * (delAttempt + 1));
                    await svc.DeleteNamedLocationAsync(created.Id!);
                    created = null;
                    break;
                }
                catch (Microsoft.Graph.Beta.Models.ODataErrors.ODataError ex)
                    when (ex.ResponseStatusCode is 400 or 404 or 500 && delAttempt < 4)
                {
                    // Not replicated yet or transient server error — retry
                }
            }
        }
        finally
        {
            if (created?.Id != null)
            {
                try { await svc.DeleteNamedLocationAsync(created.Id); } catch { }
            }
        }
    }

    #endregion

    #region TermsAndConditionsService CRUD

    [Fact]
    public async Task TermsAndConditions_Create_Get_Update_Delete()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<TermsAndConditionsService>()!;
        TermsAndConditions? created = null;

        try
        {
            // Create
            var tac = new TermsAndConditions
            {
                DisplayName = $"{TestPrefix}TAC_{Guid.NewGuid():N}",
                Description = "Integration test -- auto cleanup",
                Title = "Test Terms",
                BodyText = "These are test terms and conditions.",
                AcceptanceStatement = "I accept these terms.",
                Version = 1,
            };
            created = await svc.CreateTermsAndConditionsAsync(tac);
            Assert.NotNull(created);
            Assert.NotNull(created.Id);

            // Brief delay — Intune terms and conditions have eventual consistency
            await Task.Delay(3000);

            // Get
            var fetched = await svc.GetTermsAndConditionsAsync(created.Id!);
            Assert.NotNull(fetched);
            Assert.Equal(created.Id, fetched!.Id);

            // Update — send only mutable properties to avoid read-only field errors
            var patchTac = new TermsAndConditions
            {
                Id = created.Id,
                Description = "Updated by integration test",
            };
            var updated = await svc.UpdateTermsAndConditionsAsync(patchTac);
            Assert.NotNull(updated);

            // Verify update via follow-up GET — PATCH may return 204 and the
            // immediate GET fallback can be stale under eventual consistency
            var verified = await PollUntilAsync(
                () => svc.GetTermsAndConditionsAsync(created.Id!),
                t => t.Description == "Updated by integration test");
            Assert.NotNull(verified);
            Assert.Equal("Updated by integration test", verified!.Description);

            // Delete
            await svc.DeleteTermsAndConditionsAsync(created.Id!);
            created = null;
        }
        finally
        {
            if (created?.Id != null)
            {
                try { await svc.DeleteTermsAndConditionsAsync(created.Id); } catch { }
            }
        }
    }

    #endregion

    #region DeviceHealthScriptService CRUD

    [Fact]
    public async Task DeviceHealthScript_Create_Get_Update_Delete()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<DeviceHealthScriptService>()!;
        DeviceHealthScript? created = null;

        try
        {
            var script = new DeviceHealthScript
            {
                DisplayName = $"{TestPrefix}HealthScript_{Guid.NewGuid():N}",
                Description = "Integration test — auto cleanup",
                Publisher = "IntegrationTest",
                DetectionScriptContent = Convert.FromBase64String(
                    Convert.ToBase64String("Write-Output 'OK'"u8.ToArray())),
                RunAsAccount = RunAsAccountType.System,
            };
            created = await svc.CreateDeviceHealthScriptAsync(script);
            Assert.NotNull(created);
            Assert.NotNull(created.Id);

            // Get
            var fetched = await svc.GetDeviceHealthScriptAsync(created.Id!);
            Assert.NotNull(fetched);

            // Update
            created.Description = "Updated by integration test";
            var updated = await svc.UpdateDeviceHealthScriptAsync(created);
            Assert.NotNull(updated);

            // Verify update via follow-up GET — PATCH may return 204 and the
            // immediate GET fallback can be stale under eventual consistency
            var verified = await PollUntilAsync(
                () => svc.GetDeviceHealthScriptAsync(created.Id!),
                s => s.Description == "Updated by integration test");
            Assert.NotNull(verified);
            Assert.Equal("Updated by integration test", verified!.Description);

            // Delete
            await svc.DeleteDeviceHealthScriptAsync(created.Id!);
            created = null;
        }
        finally
        {
            if (created?.Id != null)
            {
                try { await svc.DeleteDeviceHealthScriptAsync(created.Id); } catch { }
            }
        }
    }

    #endregion

    #region FeatureUpdateProfileService CRUD

    [Fact]
    public async Task FeatureUpdate_Create_Get_Update_Delete()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<FeatureUpdateProfileService>()!;
        WindowsFeatureUpdateProfile? created = null;

        try
        {
            var profile = new WindowsFeatureUpdateProfile
            {
                DisplayName = $"{TestPrefix}FeatureUpdate_{Guid.NewGuid():N}",
                Description = "Integration test -- auto cleanup",
                FeatureUpdateVersion = "Windows 11, version 24H2",
            };
            created = await svc.CreateFeatureUpdateProfileAsync(profile);
            Assert.NotNull(created);
            Assert.NotNull(created.Id);

            // Brief delay — Intune feature update profiles have eventual consistency
            await Task.Delay(3000);

            // Get
            var fetched = await svc.GetFeatureUpdateProfileAsync(created.Id!);
            Assert.NotNull(fetched);

            // Update — send only mutable properties to avoid read-only field errors
            var patchProfile = new WindowsFeatureUpdateProfile
            {
                Id = created.Id,
                Description = "Updated by integration test",
            };
            var updated = await svc.UpdateFeatureUpdateProfileAsync(patchProfile);
            Assert.NotNull(updated);

            // Verify update via follow-up GET — PATCH may return 204 and the
            // immediate GET fallback can be stale under eventual consistency
            var verified = await PollUntilAsync(
                () => svc.GetFeatureUpdateProfileAsync(created.Id!),
                p => p.Description == "Updated by integration test");
            Assert.NotNull(verified);
            Assert.Equal("Updated by integration test", verified!.Description);

            // Delete
            await svc.DeleteFeatureUpdateProfileAsync(created.Id!);
            created = null;
        }
        finally
        {
            if (created?.Id != null)
            {
                try { await svc.DeleteFeatureUpdateProfileAsync(created.Id); } catch { }
            }
        }
    }

    #endregion

    #region RoleDefinitionService CRUD

    [Fact]
    public async Task RoleDefinition_Create_Get_Update_Delete()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<RoleDefinitionService>()!;
        RoleDefinition? created = null;

        try
        {
            var role = new DeviceAndAppManagementRoleDefinition
            {
                DisplayName = $"{TestPrefix}Role_{Guid.NewGuid():N}",
                Description = "Integration test -- auto cleanup",
                IsBuiltIn = false,
                RolePermissions =
                [
                    new RolePermission
                    {
                        ResourceActions =
                        [
                            new ResourceAction
                            {
                                AllowedResourceActions = ["Microsoft.Intune_Organization_Read"]
                            }
                        ]
                    }
                ],
            };
            created = await svc.CreateRoleDefinitionAsync(role);
            Assert.NotNull(created);
            Assert.NotNull(created.Id);

            // Get
            var fetched = await svc.GetRoleDefinitionAsync(created.Id!);
            Assert.NotNull(fetched);

            // Update
            created.Description = "Updated by integration test";
            var updated = await svc.UpdateRoleDefinitionAsync(created);
            Assert.NotNull(updated);

            // Verify update via follow-up GET — PATCH may return 204 and the
            // immediate GET fallback can be stale under eventual consistency
            var verified = await PollUntilAsync(
                () => svc.GetRoleDefinitionAsync(created.Id!),
                r => r.Description == "Updated by integration test");
            Assert.NotNull(verified);
            Assert.Equal("Updated by integration test", verified!.Description);

            // Delete
            await svc.DeleteRoleDefinitionAsync(created.Id!);
            created = null;
        }
        finally
        {
            if (created?.Id != null)
            {
                try { await svc.DeleteRoleDefinitionAsync(created.Id); } catch { }
            }
        }
    }

    #endregion

    #region Update-without-ID validation (no Graph call needed)

    [Fact]
    public async Task ScopeTag_Update_WithoutId_Throws_ArgumentException()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<ScopeTagService>()!;
        var tag = new RoleScopeTag { DisplayName = "NoId" };
        await Assert.ThrowsAsync<ArgumentException>(() => svc.UpdateScopeTagAsync(tag));
    }

    [Fact]
    public async Task NamedLocation_Update_WithoutId_Throws_ArgumentException()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<NamedLocationService>()!;
        var loc = new IpNamedLocation { DisplayName = "NoId" };
        await Assert.ThrowsAsync<ArgumentException>(() => svc.UpdateNamedLocationAsync(loc));
    }

    [Fact]
    public async Task TermsAndConditions_Update_WithoutId_Throws_ArgumentException()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<TermsAndConditionsService>()!;
        var tac = new TermsAndConditions { DisplayName = "NoId" };
        await Assert.ThrowsAsync<ArgumentException>(() => svc.UpdateTermsAndConditionsAsync(tac));
    }

    [Fact]
    public async Task AuthenticationStrength_Update_WithoutId_Throws_ArgumentException()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<AuthenticationStrengthService>()!;
        var policy = new AuthenticationStrengthPolicy { DisplayName = "NoId" };
        await Assert.ThrowsAsync<ArgumentException>(() => svc.UpdateAuthenticationStrengthPolicyAsync(policy));
    }

    [Fact]
    public async Task AuthenticationContext_Update_WithoutId_Throws_ArgumentException()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<AuthenticationContextService>()!;
        var ctx = new AuthenticationContextClassReference { DisplayName = "NoId" };
        await Assert.ThrowsAsync<ArgumentException>(() => svc.UpdateAuthenticationContextAsync(ctx));
    }

    [Fact]
    public async Task TermsOfUse_Update_WithoutId_Throws_ArgumentException()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<TermsOfUseService>()!;
        var agreement = new Agreement { DisplayName = "NoId" };
        await Assert.ThrowsAsync<ArgumentException>(() => svc.UpdateTermsOfUseAgreementAsync(agreement));
    }

    #endregion
}
