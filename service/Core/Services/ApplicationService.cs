using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace Intune.Commander.Core.Services;

public class ApplicationService : IApplicationService
{
    private readonly GraphServiceClient _graphClient;
    private static readonly string[] ApplicationListSelectFields =
    [
        // Base MobileApp properties that are safe to query across the mixed mobileApps collection.
        "id", "displayName", "description", "publisher",
        "owner", "developer", "notes",
        "isFeatured", "isAssigned",
        "informationUrl", "privacyInformationUrl",
        "createdDateTime", "lastModifiedDateTime", "publishingState",
        "roleScopeTagIds", "categories",
        "supersededAppCount", "supersedingAppCount", "dependentAppCount"
    ];

    public ApplicationService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    public async Task<List<MobileApp>> ListApplicationsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<MobileApp>();

        // Manual pagination — PageIterator can silently stop on some tenants.
        var response = await _graphClient.DeviceAppManagement.MobileApps
            .GetAsync(req =>
            {
                req.QueryParameters.Top = 999;
                req.QueryParameters.Select = ApplicationListSelectFields;
            }, cancellationToken);

        while (response != null)
        {
            if (response.Value != null)
                result.AddRange(response.Value);

            // Follow @odata.nextLink if present
            if (!string.IsNullOrEmpty(response.OdataNextLink))
            {
                response = await _graphClient.DeviceAppManagement.MobileApps
                    .WithUrl(response.OdataNextLink)
                    .GetAsync(cancellationToken: cancellationToken);
            }
            else
            {
                break;
            }
        }

        // Ensure OdataType is populated — the Graph SDK sometimes deserializes into
        // the correct concrete type but leaves OdataType null.
        foreach (var app in result)
            EnsureOdataType(app);

        return result;
    }

    public async Task<MobileApp?> GetApplicationAsync(string id, CancellationToken cancellationToken = default)
    {
        var app = await _graphClient.DeviceAppManagement.MobileApps[id]
            .GetAsync(cancellationToken: cancellationToken);
        if (app != null)
            EnsureOdataType(app);
        return app;
    }

    /// <summary>
    /// If the Graph SDK deserialized the app into a concrete subclass but left
    /// <see cref="MobileApp.OdataType"/> null, derive it from the runtime type.
    /// </summary>
    private static void EnsureOdataType(MobileApp app)
    {
        if (!string.IsNullOrEmpty(app.OdataType)) return;

        // 1. If the SDK deserialized into a concrete subclass, derive from the type name.
        var typeName = app.GetType().Name;
        if (typeName != nameof(MobileApp))
        {
            app.OdataType = $"#microsoft.graph.{char.ToLowerInvariant(typeName[0])}{typeName[1..]}";
            return;
        }

        // 2. Some items land as base MobileApp but still carry @odata.type in AdditionalData.
        if (app.AdditionalData?.TryGetValue("@odata.type", out var val) == true
            && val is string odataStr && !string.IsNullOrEmpty(odataStr))
        {
            app.OdataType = odataStr;
        }
    }

    public async Task<List<MobileAppAssignment>> GetAssignmentsAsync(string appId, CancellationToken cancellationToken = default)
    {
        var response = await _graphClient.DeviceAppManagement.MobileApps[appId]
            .Assignments.GetAsync(cancellationToken: cancellationToken);

        return response?.Value ?? [];
    }

    public async Task AssignApplicationAsync(string appId, List<MobileAppAssignment> assignments, CancellationToken cancellationToken = default)
    {
        await _graphClient.DeviceAppManagement.MobileApps[appId]
            .Assign.PostAsync(
                new Microsoft.Graph.Beta.DeviceAppManagement.MobileApps.Item.Assign.AssignPostRequestBody
                {
                    MobileAppAssignments = assignments
                },
                cancellationToken: cancellationToken);
    }
}
