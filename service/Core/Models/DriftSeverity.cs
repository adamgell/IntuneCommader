using System.Text.Json.Serialization;

namespace Intune.Commander.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<DriftSeverity>))]
public enum DriftSeverity
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}
