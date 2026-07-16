namespace Intune.Commander.Core.Services;

public interface IExportNormalizer
{
    Task NormalizeDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);
    string NormalizeJson(string json);
}
