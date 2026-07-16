namespace Intune.Commander.Core.Services;

/// <summary>
/// Service for exporting Conditional Access policies to PowerPoint format.
/// </summary>
public interface IConditionalAccessPptExportService
{
    /// <summary>
    /// Exports Conditional Access policies and related data to a PowerPoint presentation.
    /// </summary>
    /// <param name="outputPath">Full path where the .pptx file should be saved.</param>
    /// <param name="tenantName">Name of the tenant for metadata.</param>
    /// <param name="cancellationToken">Cancellation token for async operation.</param>
    /// <returns>Task representing the async operation.</returns>
    Task ExportAsync(
        string outputPath,
        string tenantName,
        CancellationToken cancellationToken = default);
}
