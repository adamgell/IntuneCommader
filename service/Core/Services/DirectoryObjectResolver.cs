using Intune.Commander.Core.Models;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.DirectoryObjects.GetByIds;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

/// <summary>
/// Resolves directory object GUIDs to display names using the Graph Beta
/// POST /directoryObjects/getByIds endpoint (up to 1000 IDs per call).
/// </summary>
public class DirectoryObjectResolver : IDirectoryObjectResolver
{
    private readonly GraphServiceClient _graphClient;

    // Well-known sentinel values that should never be sent to the Graph API
    private static readonly HashSet<string> SentinelValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "All", "None", "GuestsOrExternalUsers",
        "ServicePrincipalsInMyTenant",
        "Office365", "MicrosoftAdminPortals",
        "AllTrusted",
        "00000000-0000-0000-0000-000000000000" // MFA Trusted IPs placeholder
    };

    public DirectoryObjectResolver(GraphServiceClient graphClient)
    {
        _graphClient = graphClient ?? throw new ArgumentNullException(nameof(graphClient));
    }

    public async Task<IReadOnlyDictionary<string, string>> ResolveAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Deduplicate, filter out sentinels and empty values
        var idsToResolve = new List<string>();
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id) || SentinelValues.Contains(id) || result.ContainsKey(id))
                continue;

            // Check well-known Microsoft apps from MicrosoftApps.json (local, no API call needed)
            if (WellKnownAppRegistry.Apps.TryGetValue(id, out var wellKnownName))
            {
                result[id] = wellKnownName;
                continue;
            }

            // Only resolve GUID-shaped strings
            if (Guid.TryParse(id, out _) && !result.ContainsKey(id))
                idsToResolve.Add(id);
        }

        if (idsToResolve.Count == 0)
            return result;

        // Graph API accepts up to 1000 IDs per call
        const int batchSize = 1000;
        for (var i = 0; i < idsToResolve.Count; i += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = idsToResolve.Skip(i).Take(batchSize).ToList();
            try
            {
                var response = await _graphClient.DirectoryObjects.GetByIds.PostAsGetByIdsPostResponseAsync(
                    new GetByIdsPostRequestBody
                    {
                        Ids = batch,
                        Types = [] // empty = all types
                    },
                    cancellationToken: cancellationToken);

                if (response?.Value != null)
                {
                    foreach (var obj in response.Value)
                    {
                        if (string.IsNullOrEmpty(obj.Id)) continue;

                        var displayName = obj switch
                        {
                            User u => u.DisplayName ?? u.UserPrincipalName ?? obj.Id,
                            Group g => g.DisplayName ?? obj.Id,
                            DirectoryRole r => r.DisplayName ?? obj.Id,
                            DirectoryRoleTemplate rt => rt.DisplayName ?? obj.Id,
                            ServicePrincipal sp => sp.DisplayName ?? sp.AppDisplayName ?? obj.Id,
                            Application app => app.DisplayName ?? obj.Id,
                            _ => ExtractDisplayName(obj) ?? obj.Id
                        };

                        result[obj.Id] = displayName;
                    }
                }
            }
            catch (ApiException)
            {
                // If the batch call fails, leave those IDs unresolved (raw GUID will be shown)
            }

            // Any IDs that weren't returned by the API remain unresolved
        }

        return result;
    }

    /// <summary>
    /// Fallback extractor for directory objects that don't match a known derived type.
    /// Reads the AdditionalData bag for a "displayName" property.
    /// </summary>
    private static string? ExtractDisplayName(DirectoryObject obj)
    {
        if (obj.AdditionalData?.TryGetValue("displayName", out var val) == true && val is string s)
            return s;
        return null;
    }
}
