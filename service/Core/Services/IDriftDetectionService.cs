using Intune.Commander.Core.Models;

namespace Intune.Commander.Core.Services;

public interface IDriftDetectionService
{
    Task<DriftReport> CompareAsync(
        string baselinePath,
        string currentPath,
        DriftSeverity minSeverity = DriftSeverity.Low,
        IEnumerable<string>? objectTypes = null,
        CancellationToken cancellationToken = default);
}
