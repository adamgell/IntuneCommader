using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class SettingsCatalogService : ISettingsCatalogService
{
    private readonly GraphServiceClient _graphClient;

    // The configurationPolicies endpoint can return HTTP 500 on certain Cosmos DB
    // skip-token page boundaries. Use smaller pages to reduce the chance of hitting
    // a broken cursor, and retry on transient 500s before returning partial results.
    private const int PageSize = 100;
    private const int MaxRetries = 3;

    public SettingsCatalogService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<DeviceManagementConfigurationPolicy>> ListSettingsCatalogPoliciesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<DeviceManagementConfigurationPolicy>();

        var response = await _graphClient.DeviceManagement.ConfigurationPolicies
            .GetAsync(req =>
            {
                req.QueryParameters.Top = PageSize;
                req.QueryParameters.Select = new[]
                {
                    "id", "name", "description", "platforms", "technologies",
                    "createdDateTime", "lastModifiedDateTime", "settingCount",
                    "roleScopeTagIds", "isAssigned", "templateReference"
                };
            }, cancellationToken);

        while (response != null)
        {
            if (response.Value != null)
                result.AddRange(response.Value);

            if (string.IsNullOrEmpty(response.OdataNextLink))
                break;

            // Retry on transient 500s — the Cosmos DB backend can fail on specific
            // skip-token cursors; retrying with backoff often succeeds on the same URL.
            var nextLink = response.OdataNextLink;
            response = null;
            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    response = await _graphClient.DeviceManagement.ConfigurationPolicies
                        .WithUrl(nextLink)
                        .GetAsync(cancellationToken: cancellationToken);
                    break; // success — exit retry loop
                }
                catch (ApiException apiEx) when (apiEx.ResponseStatusCode == 500 && attempt < MaxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
                }
                catch (ApiException apiEx) when (apiEx.ResponseStatusCode == 500)
                {
                    // All retries exhausted — return the items collected so far rather
                    // than throwing and losing all previously fetched pages.
                    return result;
                }
            }
        }

        return result;
    }

    public async Task<DeviceManagementConfigurationPolicy?> GetSettingsCatalogPolicyAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _graphClient.DeviceManagement.ConfigurationPolicies[id]
            .GetAsync(cancellationToken: cancellationToken);
    }

    public async Task<List<DeviceManagementConfigurationPolicyAssignment>> GetAssignmentsAsync(string policyId, CancellationToken cancellationToken = default)
    {
        var response = await _graphClient.DeviceManagement.ConfigurationPolicies[policyId]
            .Assignments.GetAsync(cancellationToken: cancellationToken);

        return response?.Value ?? [];
    }

    public async Task<List<DeviceManagementConfigurationSetting>> GetPolicySettingsAsync(string policyId, CancellationToken cancellationToken = default)
    {
        var result = new List<DeviceManagementConfigurationSetting>();

        var response = await _graphClient.DeviceManagement.ConfigurationPolicies[policyId]
            .Settings.GetAsync(req =>
            {
                req.QueryParameters.Top = 999;
            }, cancellationToken);

        while (response != null)
        {
            if (response.Value != null)
                result.AddRange(response.Value);

            if (!string.IsNullOrEmpty(response.OdataNextLink))
            {
                response = await _graphClient.DeviceManagement.ConfigurationPolicies[policyId]
                    .Settings.WithUrl(response.OdataNextLink)
                    .GetAsync(cancellationToken: cancellationToken);
            }
            else
            {
                break;
            }
        }

        return result;
    }

    public async Task<DeviceManagementConfigurationPolicy> CreateSettingsCatalogPolicyAsync(DeviceManagementConfigurationPolicy policy, CancellationToken cancellationToken = default)
    {
        var result = await _graphClient.DeviceManagement.ConfigurationPolicies
            .PostAsync(policy, cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("Failed to create settings catalog policy");
    }

    public async Task AssignSettingsCatalogPolicyAsync(string policyId, List<DeviceManagementConfigurationPolicyAssignment> assignments, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceManagement.ConfigurationPolicies[policyId]
            .Assign.PostAsAssignPostResponseAsync(
                new Microsoft.Graph.Beta.DeviceManagement.ConfigurationPolicies.Item.Assign.AssignPostRequestBody
                {
                    Assignments = assignments
                },
                cancellationToken: cancellationToken);
    }

    public async Task<DeviceManagementConfigurationPolicy> UpdateSettingsCatalogPolicyMetadataAsync(string id, DeviceManagementConfigurationPolicy policy, CancellationToken cancellationToken = default)
    {
        var metadataOnly = new DeviceManagementConfigurationPolicy
        {
            Name = policy.Name,
            Description = policy.Description,
            RoleScopeTagIds = policy.RoleScopeTagIds
        };

        var result = await _graphClient.DeviceManagement.ConfigurationPolicies[id]
            .PatchAsync(metadataOnly, cancellationToken: cancellationToken);

        return await GraphPatchHelper.PatchWithGetFallbackAsync(
            result, () => GetSettingsCatalogPolicyAsync(id, cancellationToken), "settings catalog policy");
    }

    public async Task DeleteSettingsCatalogPolicyAsync(string id, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceManagement.ConfigurationPolicies[id]
            .DeleteAsync(cancellationToken: cancellationToken);
    }

    // TODO: Consider $batch optimization (20 requests/batch)
    public async Task UpdatePolicySettingsAsync(string policyId, List<DeviceManagementConfigurationSetting> settings, CancellationToken cancellationToken = default)
    {
        // Step 1: Capture original settings for rollback
        var originalSettings = await GetPolicySettingsAsync(policyId, cancellationToken);

        // Steps 2+3 are wrapped in try so that any failure (including cancellation)
        // triggers a best-effort rollback to the original settings.
        try
        {
            // Step 2: Delete all existing settings
            foreach (var existing in originalSettings)
            {
                if (existing.Id is not null)
                {
                    await _graphClient.DeviceManagement.ConfigurationPolicies[policyId]
                        .Settings[existing.Id]
                        .DeleteAsync(cancellationToken: cancellationToken);
                }
            }

            // Step 3: POST each new setting (create copies to avoid mutating the caller's list)
            foreach (var setting in settings)
            {
                var toPost = new DeviceManagementConfigurationSetting
                {
                    SettingInstance = setting.SettingInstance
                };
                await _graphClient.DeviceManagement.ConfigurationPolicies[policyId]
                    .Settings.PostAsync(toPost, cancellationToken: cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // User cancelled — don't attempt rollback, just propagate
            throw;
        }
        catch (Exception ex)
        {
            // Best-effort rollback: clear whatever partial state exists, re-POST originals.
            // Use CancellationToken.None — rollback must complete regardless.
            try
            {
                var current = await GetPolicySettingsAsync(policyId);
                foreach (var c in current)
                {
                    if (c.Id is not null)
                    {
                        await _graphClient.DeviceManagement.ConfigurationPolicies[policyId]
                            .Settings[c.Id].DeleteAsync();
                    }
                }

                foreach (var original in originalSettings)
                {
                    var toPost = new DeviceManagementConfigurationSetting
                    {
                        SettingInstance = original.SettingInstance
                    };
                    await _graphClient.DeviceManagement.ConfigurationPolicies[policyId]
                        .Settings.PostAsync(toPost);
                }
            }
            catch (Exception rollbackEx)
            {
                throw new AggregateException(
                    "Failed to update policy settings and rollback also failed", ex, rollbackEx);
            }

            throw;
        }
    }
}
