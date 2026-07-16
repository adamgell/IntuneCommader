using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;
using System.Text;

namespace Intune.Commander.Core.Services;

public class ManagedDeviceService(GraphServiceClient graphClient) : IManagedDeviceService
{
    private readonly GraphServiceClient _graphClient = graphClient;

    private static readonly string[] ManagedDeviceSelect =
    [
        "id", "deviceName", "userId", "userDisplayName", "userPrincipalName", "emailAddress",
        "managedDeviceOwnerType", "managementState", "enrolledDateTime", "lastSyncDateTime",
        "operatingSystem", "osVersion", "complianceState", "jailBroken",
        "managementAgent", "model", "manufacturer", "serialNumber", "imei",
        "azureADDeviceId", "deviceRegistrationState", "deviceCategoryDisplayName",
        "isSupervised", "isEncrypted", "partnerReportedThreatState",
        "wiFiMacAddress", "ethernetMacAddress", "totalStorageSpaceInBytes", "freeStorageSpaceInBytes"
    ];

    public async Task<List<ManagedDevice>> ListManagedDevicesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<ManagedDevice>();

        var response = await _graphClient.DeviceManagement.ManagedDevices.GetAsync(req =>
        {
            req.QueryParameters.Top = 999;
            req.QueryParameters.Select = ManagedDeviceSelect;
        }, cancellationToken);

        while (response != null)
        {
            if (response.Value != null)
                result.AddRange(response.Value);

            if (!string.IsNullOrEmpty(response.OdataNextLink))
            {
                response = await _graphClient.DeviceManagement.ManagedDevices
                    .WithUrl(response.OdataNextLink)
                    .GetAsync(cancellationToken: cancellationToken);
            }
            else
            {
                break;
            }
        }

        return result;
    }

    public async Task<ManagedDevice?> GetManagedDeviceAsync(string id, CancellationToken cancellationToken = default)
        => await _graphClient.DeviceManagement.ManagedDevices[id].GetAsync(req =>
        {
            req.QueryParameters.Select = ManagedDeviceSelect;
        }, cancellationToken);

    // Device action HISTORY. `deviceActionResults` is a structural (complex-type)
    // collection property on managedDevice — selected, not expanded — so one GET of
    // managedDevices/{id} with a narrow $select carries the whole action log. Each
    // element is a Graph `deviceActionResult` (ActionName/ActionState/StartDateTime/
    // LastUpdatedDateTime — validated against Microsoft.Graph.Beta.xml). Returns [] when
    // Graph omits the property (device never actioned).
    public async Task<List<DeviceActionResult>> GetDeviceActionResultsAsync(string id, CancellationToken cancellationToken = default)
    {
        var dev = await _graphClient.DeviceManagement.ManagedDevices[id].GetAsync(req =>
        {
            req.QueryParameters.Select = ["id", "deviceName", "deviceActionResults"];
        }, cancellationToken);
        return dev?.DeviceActionResults ?? [];
    }

    public async Task ExecuteDeviceActionAsync(string deviceId, string action, string? bodyJson = null, CancellationToken cancellationToken = default)
    {
        // managedDevice actions aren't typed Core methods (the fork is list-only), and a
        // typed Kiota builder per verb would be ~12 near-identical switch arms. Instead
        // POST the OData action segment generically through the request adapter — Graph's
        // own action endpoints, just reached at a lower level than the generated builders.
        var requestInfo = new RequestInformation
        {
            HttpMethod = Method.POST,
            UrlTemplate = "{+baseurl}/deviceManagement/managedDevices/{managedDeviceId}/{action}",
        };
        requestInfo.PathParameters.Add("baseurl", _graphClient.RequestAdapter.BaseUrl!);
        requestInfo.PathParameters.Add("managedDeviceId", deviceId);
        requestInfo.PathParameters.Add("action", action);
        if (!string.IsNullOrWhiteSpace(bodyJson))
            requestInfo.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(bodyJson)), "application/json");

        await _graphClient.RequestAdapter.SendNoContentAsync(requestInfo, cancellationToken: cancellationToken);
    }
}
