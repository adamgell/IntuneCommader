using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services;

public interface IManagedDeviceService
{
    Task<List<ManagedDevice>> ListManagedDevicesAsync(CancellationToken cancellationToken = default);

    // M14 — device detail (a single managedDevice with the inventory select set).
    Task<ManagedDevice?> GetManagedDeviceAsync(string id, CancellationToken cancellationToken = default);

    // M14 — execute a Graph managedDevice *action* verb (syncDevice, rebootNow, wipe,
    // retire, …). `action` is the OData action segment; `bodyJson` is the optional raw
    // JSON parameter object Graph expects for parameterized actions (e.g. wipe's
    // keepUserData, windowsDefenderScan's quickScan). Posts to
    // /deviceManagement/managedDevices/{id}/{action} and expects 204.
    Task ExecuteDeviceActionAsync(string deviceId, string action, string? bodyJson = null, CancellationToken cancellationToken = default);

    // M14 — device action HISTORY. Reads the managedDevice's `deviceActionResults`
    // property (Graph's per-device record of remote actions: actionName + actionState +
    // start/last-updated timestamps). Volatile — the caller fetches this straight from
    // Graph (no blob cache). Returns an empty list when the device has no action history.
    Task<List<DeviceActionResult>> GetDeviceActionResultsAsync(string id, CancellationToken cancellationToken = default);
}
