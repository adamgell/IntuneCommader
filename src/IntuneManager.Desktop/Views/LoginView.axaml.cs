using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using IntuneManager.Desktop.ViewModels;

namespace IntuneManager.Desktop.Views;

public partial class LoginView : UserControl
{
    private LoginViewModel? _vm;

    public LoginView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null)
            _vm.ImportProfilesRequested -= OnImportProfilesRequested;

        _vm = DataContext as LoginViewModel;

        if (_vm != null)
            _vm.ImportProfilesRequested += OnImportProfilesRequested;
    }

    private async Task<string?> OnImportProfilesRequested()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Profiles from JSON",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("JSON files") { Patterns = ["*.json"] },
                new FilePickerFileType("All files") { Patterns = ["*"] }
            ]
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }
}
