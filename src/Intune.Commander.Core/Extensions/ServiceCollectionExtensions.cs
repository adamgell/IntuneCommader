using Intune.Commander.Core.Auth;
using Intune.Commander.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Intune.Commander.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIntuneCommanderCore(this IServiceCollection services)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // DataProtection — keys stored in the user's local app data folder.
        // NOTE: SetApplicationName("IntuneManager") is intentionally preserved as a
        // read-only compatibility constant. Changing it would make all existing
        // DataProtection-encrypted profile data permanently unreadable.
        var legacyKeysPath = Path.Combine(appData, "IntuneManager", "keys");
        var keysPath = Path.Combine(appData, "Intune.Commander", "keys");

        // One-time migration: copy DataProtection key XML files from the legacy
        // location to the new one so existing encrypted profiles remain readable.
        var legacyKeyFiles = Directory.Exists(legacyKeysPath)
            ? Directory.GetFiles(legacyKeysPath, "*.xml")
            : Array.Empty<string>();

        var newKeyDirectoryHasKeys = Directory.Exists(keysPath)
            && Directory.GetFiles(keysPath, "*.xml").Length > 0;

        if (legacyKeyFiles.Length > 0 && !newKeyDirectoryHasKeys)
        {
            Directory.CreateDirectory(keysPath);

            foreach (var file in legacyKeyFiles)
            {
                var destinationPath = Path.Combine(keysPath, Path.GetFileName(file));
                if (File.Exists(destinationPath))
                    continue;

                try
                {
                    File.Copy(file, destinationPath, overwrite: false);
                }
                catch { /* best-effort — continue copying remaining keys */ }
            }
        }

        services.AddDataProtection()
            .SetApplicationName("IntuneManager") // Compatibility constant — see note above
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

        services.AddSingleton<IProfileEncryptionService, ProfileEncryptionService>();

        services.AddSingleton<IAuthenticationProvider, InteractiveBrowserAuthProvider>();
        services.AddSingleton<IntuneGraphClientFactory>();

        // ProfileService depends on IProfileEncryptionService, so resolve it via factory
        services.AddSingleton<ProfileService>(sp =>
            new ProfileService(encryption: sp.GetRequiredService<IProfileEncryptionService>()));

        services.AddTransient<IExportService, ExportService>();

        // Cache — singleton LiteDB-backed cache with encrypted storage
        services.AddSingleton<ICacheService>(sp =>
            new CacheService(sp.GetRequiredService<IDataProtectionProvider>()));

        return services;
    }
}
