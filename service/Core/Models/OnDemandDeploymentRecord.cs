namespace Intune.Commander.Core.Models;

public sealed class OnDemandDeploymentRecord
{
    public required string ScriptId { get; init; }
    public required string ScriptName { get; init; }
    public required string DeviceId { get; init; }
    public required string DeviceName { get; init; }
    public DateTimeOffset DispatchedAt { get; init; } = DateTimeOffset.UtcNow;
    public bool Succeeded { get; set; }
    public string? ErrorMessage { get; set; }
}
