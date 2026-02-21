using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Intune.Commander.Desktop.ViewModels;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace Intune.Commander.Desktop.Views;

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
        {
            _vm.ImportProfilesRequested -= OnImportProfilesRequested;
            _vm.ConfirmDeleteProfile -= OnConfirmDeleteProfile;
        }

        _vm = DataContext as LoginViewModel;

        if (_vm != null)
        {
            _vm.ImportProfilesRequested += OnImportProfilesRequested;
            _vm.ConfirmDeleteProfile += OnConfirmDeleteProfile;
        }
    }

    private async Task<bool> OnConfirmDeleteProfile(string profileName)
    {
        var box = MessageBoxManager.GetMessageBoxStandard(
            "Delete Profile",
            $"Delete profile \"{profileName}\"? This cannot be undone.",
            ButtonEnum.YesNo,
            Icon.Warning);
        var result = await box.ShowWindowDialogAsync(
            TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException());
        return result == ButtonResult.Yes;
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

    private async void OnCopyDeviceCodeClick(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || string.IsNullOrEmpty(_vm.DeviceUserCode)) return;

        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(_vm.DeviceUserCode);
                if (sender is Button btn)
                {
                    btn.Content = "Copied!";
                    await Task.Delay(1500);
                    btn.Content = "Copy Code";
                }
            }
        }
        catch { /* clipboard not available */ }
    }
}
