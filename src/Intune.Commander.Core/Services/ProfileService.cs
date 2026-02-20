using System.Text.Json;
using Intune.Commander.Core.Models;

namespace Intune.Commander.Core.Services;

public class ProfileService
{
    private readonly string _profilePath;
    private readonly string? _legacyProfilePath;
    private readonly IProfileEncryptionService? _encryption;
    private ProfileStore _store;

    // Marker prefix to detect encrypted files.
    // Preserved as a compatibility constant — new saves also use this marker so
    // the format is stable across the rename and existing tooling keeps working.
    private const string EncryptedMarker = "INTUNEMANAGER_ENC:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <param name="profilePath">
    ///   Explicit path for profiles.json. Defaults to the current app-data location.
    ///   Pass null to use the default (enables legacy migration on first load).
    /// </param>
    /// <param name="encryption">Optional encryption service.</param>
    /// <param name="legacyProfilePath">
    ///   Override for the legacy profiles.json path used during migration testing.
    ///   Only effective when <paramref name="profilePath"/> is also specified.
    ///   When <paramref name="profilePath"/> is null the real legacy default is used.
    /// </param>
    public ProfileService(string? profilePath = null, IProfileEncryptionService? encryption = null,
        string? legacyProfilePath = null)
    {
        if (profilePath == null)
        {
            _profilePath = GetDefaultProfilePath();
            _legacyProfilePath = GetLegacyProfilePath();
        }
        else
        {
            _profilePath = profilePath;
            _legacyProfilePath = legacyProfilePath;
        }
        _encryption = encryption;
        _store = new ProfileStore();
    }

    public IReadOnlyList<TenantProfile> Profiles => _store.Profiles.AsReadOnly();
    public string? ActiveProfileId => _store.ActiveProfileId;

    public TenantProfile? GetActiveProfile()
    {
        return _store.Profiles.FirstOrDefault(p => p.Id == _store.ActiveProfileId);
    }

    public void SetActiveProfile(string profileId)
    {
        var profile = _store.Profiles.FirstOrDefault(p => p.Id == profileId)
            ?? throw new ArgumentException($"Profile '{profileId}' not found");

        profile.LastUsed = DateTime.UtcNow;
        _store.ActiveProfileId = profileId;
    }

    public TenantProfile AddProfile(TenantProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
            throw new ArgumentException("Profile name is required");
        if (string.IsNullOrWhiteSpace(profile.TenantId))
            throw new ArgumentException("Tenant ID is required");
        if (string.IsNullOrWhiteSpace(profile.ClientId))
            throw new ArgumentException("Client ID is required");

        _store.Profiles.Add(profile);

        if (_store.ActiveProfileId == null)
            _store.ActiveProfileId = profile.Id;

        return profile;
    }

    public void RemoveProfile(string profileId)
    {
        var profile = _store.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile != null)
        {
            _store.Profiles.Remove(profile);
            if (_store.ActiveProfileId == profileId)
                _store.ActiveProfileId = _store.Profiles.FirstOrDefault()?.Id;
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        // Phase 3 runtime migration: if the primary path doesn't exist but a legacy
        // path does, load from the legacy location. After a successful load we
        // re-save to the primary path so subsequent launches use the new location.
        var pathToLoad = _profilePath;
        var migratingFromLegacy = false;

        if (!File.Exists(pathToLoad) && _legacyProfilePath != null && File.Exists(_legacyProfilePath))
        {
            pathToLoad = _legacyProfilePath;
            migratingFromLegacy = true;
        }

        if (!File.Exists(pathToLoad))
        {
            _store = new ProfileStore();
            return;
        }

        var raw = await File.ReadAllTextAsync(pathToLoad, cancellationToken);

        string json;
        if (_encryption is not null && raw.StartsWith(EncryptedMarker))
        {
            // Encrypted file — decrypt the payload after the marker.
            // ProfileEncryptionService.Decrypt tries the current purpose string first,
            // then falls back to the legacy "IntuneManager.Profiles.v1" purpose so
            // profiles written before the rename remain readable.
            try
            {
                json = _encryption.Decrypt(raw[EncryptedMarker.Length..]);
            }
            catch
            {
                // Decryption failed (keys rotated, corrupted, etc.)
                // Fall back to empty store — caller can surface the error
                _store = new ProfileStore();
                return;
            }
        }
        else if (_encryption is not null && !raw.StartsWith(EncryptedMarker))
        {
            // Plaintext file with encryption enabled — migrate on next save
            json = raw;
        }
        else
        {
            json = raw;
        }

        _store = JsonSerializer.Deserialize<ProfileStore>(json, JsonOptions) ?? new ProfileStore();

        // If we loaded from the legacy location, persist to the new location immediately
        // so the next launch reads from the correct path.
        if (migratingFromLegacy)
            await SaveAsync(cancellationToken);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_profilePath);
        if (directory != null)
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(_store, JsonOptions);

        string output;
        if (_encryption is not null)
        {
            output = EncryptedMarker + _encryption.Encrypt(json);
        }
        else
        {
            output = json;
        }

        await File.WriteAllTextAsync(_profilePath, output, cancellationToken);
    }

    private static string GetDefaultProfilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "Intune.Commander", "profiles.json");
    }

    // Legacy path from before the product rename. Read-only compatibility constant —
    // used only to detect and migrate existing user data during Phase 3 migration.
    private static string GetLegacyProfilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "IntuneManager", "profiles.json");
    }
}
