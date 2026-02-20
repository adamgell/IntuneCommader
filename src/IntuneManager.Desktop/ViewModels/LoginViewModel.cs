using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Identity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntuneManager.Core.Auth;
using IntuneManager.Core.Models;
using IntuneManager.Core.Services;

namespace IntuneManager.Desktop.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly ProfileService _profileService;
    private readonly IntuneGraphClientFactory _graphClientFactory;

    [ObservableProperty]
    private string _tenantId = string.Empty;

    [ObservableProperty]
    private string _clientId = string.Empty;

    [ObservableProperty]
    private string _clientSecret = string.Empty;

    [ObservableProperty]
    private string _profileName = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private TenantProfile? _selectedProfile;

    [ObservableProperty]
    private CloudEnvironment _selectedCloud = CloudEnvironment.Commercial;

    [ObservableProperty]
    private AuthMethod _selectedAuthMethod = AuthMethod.Interactive;

    [ObservableProperty]
    private string? _tenantIdError;

    [ObservableProperty]
    private string? _clientIdError;

    /// <summary>
    /// Set during a Device Code flow to display the code and verification URL to the user.
    /// Cleared once authentication completes.
    /// </summary>
    [ObservableProperty]
    private string _deviceCodeMessage = string.Empty;

    public static CloudEnvironment[] AvailableClouds { get; } =
        Enum.GetValues<CloudEnvironment>();

    public static AuthMethod[] AvailableAuthMethods { get; } =
        Enum.GetValues<AuthMethod>();

    /// <summary>
    /// Controls visibility of the Client Secret field — only shown for ClientSecret auth.
    /// </summary>
    public bool IsClientSecretVisible => SelectedAuthMethod == AuthMethod.ClientSecret;

    /// <summary>
    /// Controls visibility of the Device Code message panel.
    /// </summary>
    public bool IsDeviceCodeMessageVisible => !string.IsNullOrEmpty(DeviceCodeMessage);

    public ObservableCollection<TenantProfile> SavedProfiles { get; } = [];

    public event EventHandler<TenantProfile>? LoginSucceeded;

    /// <summary>
    /// Raised when the user clicks "Import Profiles". The view subscribes and
    /// opens a file picker, returning the selected file path (or null if cancelled).
    /// </summary>
    public event Func<Task<string?>>? ImportProfilesRequested;

    public LoginViewModel(ProfileService profileService, IntuneGraphClientFactory graphClientFactory)
    {
        _profileService = profileService;
        _graphClientFactory = graphClientFactory;
    }

    /// <summary>
    /// Called by MainWindowViewModel after profiles are loaded asynchronously.
    /// </summary>
    public void PopulateSavedProfiles()
    {
        SavedProfiles.Clear();
        foreach (var p in _profileService.Profiles)
            SavedProfiles.Add(p);
    }

    partial void OnSelectedProfileChanged(TenantProfile? value)
    {
        if (value is null) return;
        ProfileName = value.Name;
        TenantId = value.TenantId;
        ClientId = value.ClientId;
        ClientSecret = value.ClientSecret ?? string.Empty;
        SelectedCloud = value.Cloud;
        SelectedAuthMethod = value.AuthMethod;

        // Clear validation errors when loading a saved profile
        TenantIdError = null;
        ClientIdError = null;

        LoginCommand.NotifyCanExecuteChanged();
        SaveProfileCommand.NotifyCanExecuteChanged();
        DeleteProfileCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedAuthMethodChanged(AuthMethod value)
    {
        OnPropertyChanged(nameof(IsClientSecretVisible));
        LoginCommand.NotifyCanExecuteChanged();
        SaveProfileCommand.NotifyCanExecuteChanged();
    }

    partial void OnDeviceCodeMessageChanged(string value)
    {
        OnPropertyChanged(nameof(IsDeviceCodeMessageVisible));
    }

    [RelayCommand(CanExecute = nameof(CanSaveProfile))]
    private async Task SaveProfileAsync(CancellationToken cancellationToken)
    {
        ClearError();
        try
        {
            if (SelectedProfile is not null)
            {
                // Update existing profile
                SelectedProfile.Name = ProfileName.Trim();
                SelectedProfile.TenantId = TenantId.Trim();
                SelectedProfile.ClientId = ClientId.Trim();
                SelectedProfile.ClientSecret = ClientSecret.Trim();
                SelectedProfile.Cloud = SelectedCloud;
                SelectedProfile.AuthMethod = SelectedAuthMethod;
            }
            else
            {
                // Create new profile
                var profile = new TenantProfile
                {
                    Name = string.IsNullOrWhiteSpace(ProfileName) ? $"Tenant-{TenantId.Trim()[..Math.Min(8, TenantId.Trim().Length)]}" : ProfileName.Trim(),
                    TenantId = TenantId.Trim(),
                    ClientId = ClientId.Trim(),
                    ClientSecret = ClientSecret.Trim(),
                    Cloud = SelectedCloud,
                    AuthMethod = SelectedAuthMethod
                };
                _profileService.AddProfile(profile);
                SavedProfiles.Add(profile);
            }

            await _profileService.SaveAsync(cancellationToken);
            StatusMessage = "Profile saved";

            // Clear form for next entry
            SelectedProfile = null;
            ProfileName = string.Empty;
            TenantId = string.Empty;
            ClientId = string.Empty;
            ClientSecret = string.Empty;
            SelectedCloud = CloudEnvironment.Commercial;
            SelectedAuthMethod = AuthMethod.Interactive;
            TenantIdError = null;
            ClientIdError = null;
        }
        catch (Exception ex)
        {
            SetError($"Failed to save profile: {ex.Message}");
        }
    }

    private bool CanSaveProfile()
    {
        return !string.IsNullOrWhiteSpace(TenantId)
            && !string.IsNullOrWhiteSpace(ClientId)
            && TenantIdError is null
            && ClientIdError is null;
    }

    [RelayCommand]
    private void NewProfile()
    {
        SelectedProfile = null;
        ProfileName = string.Empty;
        TenantId = string.Empty;
        ClientId = string.Empty;
        ClientSecret = string.Empty;
        SelectedCloud = CloudEnvironment.Commercial;
        SelectedAuthMethod = AuthMethod.Interactive;
        TenantIdError = null;
        ClientIdError = null;
        StatusMessage = string.Empty;
        DeviceCodeMessage = string.Empty;
        ClearError();
    }

    [RelayCommand(CanExecute = nameof(CanDeleteProfile))]
    private async Task DeleteProfileAsync(CancellationToken cancellationToken)
    {
        if (SelectedProfile is null) return;

        ClearError();
        try
        {
            _profileService.RemoveProfile(SelectedProfile.Id);
            SavedProfiles.Remove(SelectedProfile);
            await _profileService.SaveAsync(cancellationToken);

            SelectedProfile = null;
            ProfileName = string.Empty;
            TenantId = string.Empty;
            ClientId = string.Empty;
            ClientSecret = string.Empty;
            StatusMessage = "Profile deleted";
        }
        catch (Exception ex)
        {
            SetError($"Failed to delete profile: {ex.Message}");
        }
    }

    private bool CanDeleteProfile() => SelectedProfile is not null;

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync(CancellationToken cancellationToken)
    {
        ClearError();
        IsBusy = true;
        DeviceCodeMessage = string.Empty;
        StatusMessage = SelectedAuthMethod switch
        {
            AuthMethod.DeviceCode => "Waiting for device code...",
            AuthMethod.Interactive => "Opening browser...",
            _ => "Authenticating..."
        };

        try
        {
            // Save profile first if not already saved
            if (SelectedProfile is null)
                await SaveProfileAsync(cancellationToken);

            var profile = SelectedProfile!;

            Func<DeviceCodeInfo, CancellationToken, Task>? deviceCodeCallback = null;
            if (profile.AuthMethod == AuthMethod.DeviceCode)
            {
                deviceCodeCallback = (info, _) =>
                {
                    DeviceCodeMessage = info.Message;
                    StatusMessage = "Complete sign-in in your browser, then return here.";
                    return Task.CompletedTask;
                };
            }

            // Test the connection
            var client = await _graphClientFactory.CreateClientAsync(profile, deviceCodeCallback, cancellationToken);
            await client.DeviceManagement.GetAsync(cancellationToken: cancellationToken);

            DeviceCodeMessage = string.Empty;
            StatusMessage = "Connected successfully!";
            profile.LastUsed = DateTime.UtcNow;
            _profileService.SetActiveProfile(profile.Id);
            await _profileService.SaveAsync(cancellationToken);

            LoginSucceeded?.Invoke(this, profile);
        }
        catch (Exception ex)
        {
            DeviceCodeMessage = string.Empty;
            SetError($"Authentication failed: {ex.Message}");
            StatusMessage = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanLogin()
    {
        return !IsBusy
            && !string.IsNullOrWhiteSpace(TenantId)
            && !string.IsNullOrWhiteSpace(ClientId)
            && TenantIdError is null
            && ClientIdError is null;
    }

    partial void OnTenantIdChanged(string value)
    {
        ValidateTenantId(value);
        LoginCommand.NotifyCanExecuteChanged();
        SaveProfileCommand.NotifyCanExecuteChanged();
    }

    partial void OnClientIdChanged(string value)
    {
        ValidateClientId(value);
        LoginCommand.NotifyCanExecuteChanged();
        SaveProfileCommand.NotifyCanExecuteChanged();
    }

    partial void OnClientSecretChanged(string value)
    {
        LoginCommand.NotifyCanExecuteChanged();
        SaveProfileCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Pre-selects the last-active profile from the profile store.
    /// Called after profiles are loaded asynchronously.
    /// </summary>
    public void SelectActiveProfile()
    {
        var activeId = _profileService.ActiveProfileId;
        if (activeId is not null)
        {
            SelectedProfile = SavedProfiles.FirstOrDefault(p => p.Id == activeId);
        }
    }

    [RelayCommand]
    private async Task ImportProfilesAsync(CancellationToken cancellationToken)
    {
        if (ImportProfilesRequested == null) return;

        var path = await ImportProfilesRequested.Invoke();
        if (path == null) return;

        ClearError();
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var imported = ProfileImportHelper.ParseProfiles(json);

            if (imported.Count == 0)
            {
                StatusMessage = "No valid profiles found in file";
                return;
            }

            var existing = _profileService.Profiles.ToList();
            int added = 0;

            foreach (var profile in imported)
            {
                // Skip duplicates (same tenant + client)
                bool isDuplicate = existing.Any(e =>
                    string.Equals(e.TenantId, profile.TenantId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.ClientId, profile.ClientId, StringComparison.OrdinalIgnoreCase));

                if (isDuplicate) continue;

                // Always generate a fresh Id
                profile.Id = Guid.NewGuid().ToString();

                _profileService.AddProfile(profile);
                SavedProfiles.Add(profile);
                existing.Add(profile);
                added++;
            }

            await _profileService.SaveAsync(cancellationToken);
            StatusMessage = $"Imported {added} profile(s)";
        }
        catch (JsonException ex)
        {
            SetError($"Invalid JSON: {ex.Message}");
        }
        catch (Exception ex)
        {
            SetError($"Import failed: {ex.Message}");
        }
    }

    private void ValidateTenantId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            TenantIdError = null; // Don't show error for empty (not yet entered)
        else if (!Guid.TryParse(value.Trim(), out _))
            TenantIdError = "Tenant ID must be a valid GUID";
        else
            TenantIdError = null;
    }

    private void ValidateClientId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            ClientIdError = null;
        else if (!Guid.TryParse(value.Trim(), out _))
            ClientIdError = "Client ID must be a valid GUID";
        else
            ClientIdError = null;
    }
}
