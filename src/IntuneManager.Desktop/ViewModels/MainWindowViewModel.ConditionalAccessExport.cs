using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace IntuneManager.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    // Event to request save file dialog from code-behind
    public event Func<string, string, Task<string?>>? SaveFileRequested;

    [RelayCommand(CanExecute = nameof(CanExportConditionalAccessPowerPoint))]
    private async Task ExportConditionalAccessPowerPointAsync(CancellationToken cancellationToken)
    {
        if (_conditionalAccessPptExportService == null || ActiveProfile == null)
        {
            StatusText = "Cannot export: Service not initialized";
            return;
        }

        IsBusy = true;
        StatusText = "Exporting Conditional Access policies to PowerPoint...";
        ClearError();

        try
        {
            // Request file save location from UI
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var defaultFileName = $"ConditionalAccessPolicies-{ActiveProfile.Name}-{timestamp}.pptx";
            
            var outputPath = await RequestSaveFileAsync(defaultFileName, "PowerPoint Presentation (*.pptx)");
            
            if (string.IsNullOrEmpty(outputPath))
            {
                StatusText = "Export cancelled";
                return;
            }

            DebugLog.Log("Export", $"Exporting CA policies to: {outputPath}");

            await _conditionalAccessPptExportService.ExportAsync(
                outputPath,
                ActiveProfile.Name,
                cancellationToken);

            StatusText = $"Successfully exported Conditional Access policies to: {Path.GetFileName(outputPath)}";
            DebugLog.Log("Export", $"CA PowerPoint export completed: {outputPath}");
        }
        catch (OperationCanceledException)
        {
            StatusText = "Export cancelled";
            DebugLog.Log("Export", "CA PowerPoint export cancelled by user");
        }
        catch (Exception ex)
        {
            SetError($"Failed to export Conditional Access policies: {ex.Message}");
            DebugLog.LogError("Export", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanExportConditionalAccessPowerPoint()
    {
        return IsConnected && 
               _conditionalAccessPptExportService != null && 
               ConditionalAccessPolicies.Count > 0;
    }

    private async Task<string?> RequestSaveFileAsync(string defaultFileName, string filter)
    {
        if (SaveFileRequested != null)
        {
            return await SaveFileRequested.Invoke(defaultFileName, filter);
        }
        return null;
    }
}
