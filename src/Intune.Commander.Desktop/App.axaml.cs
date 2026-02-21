using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Classic.Avalonia.Theme;
using Intune.Commander.Core.Extensions;
using Intune.Commander.Core.Services;
using Intune.Commander.Desktop.Models;
using Intune.Commander.Desktop.Services;
using Intune.Commander.Desktop.ViewModels;
using Intune.Commander.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;
using Syncfusion.Licensing;

namespace Intune.Commander.Desktop;

public partial class App : Application
{
    public static ServiceProvider? Services { get; private set; }
    public static AppTheme CurrentTheme { get; private set; } = AppTheme.Fluent;

    private const string ClassicOverridesUri = "avares://Intune.Commander.Desktop/Assets/ClassicThemeOverrides.axaml";

    public static void ApplyTheme(AppTheme theme)
    {
        CurrentTheme = theme;
        var app = Application.Current!;

        // Locate existing main theme style (ClassicTheme or FluentTheme) by type
        var themeIndex = app.Styles
            .Select((s, i) => new { s, i })
            .FirstOrDefault(x => x.s is ClassicTheme || x.s is FluentTheme)
            ?.i ?? -1;

        IStyle newTheme = theme == AppTheme.Classic ? new ClassicTheme() : new FluentTheme();
        if (themeIndex >= 0)
            app.Styles[themeIndex] = newTheme;
        else
        {
            app.Styles.Insert(0, newTheme);
            themeIndex = 0;
        }

        // Locate existing DataGrid style include by source URI
        const string classicDataGrid = "avares://Classic.Avalonia.Theme.DataGrid/Classic.axaml";
        const string fluentDataGrid = "avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml";
        var dataGridIndex = app.Styles
            .Select((s, i) => new { s, i })
            .FirstOrDefault(x =>
            {
                if (x.s is not StyleInclude si || si.Source == null) return false;
                var src = si.Source.ToString();
                return src.Contains("Classic.Avalonia.Theme.DataGrid", StringComparison.OrdinalIgnoreCase) ||
                       src.Contains("Avalonia.Controls.DataGrid/Themes", StringComparison.OrdinalIgnoreCase);
            })
            ?.i ?? -1;

        var newDataGrid = new StyleInclude(new Uri("avares://Intune.Commander.Desktop"))
        {
            Source = new Uri(theme == AppTheme.Classic ? classicDataGrid : fluentDataGrid)
        };

        if (dataGridIndex >= 0)
            app.Styles[dataGridIndex] = newDataGrid;
        else
            app.Styles.Insert(themeIndex + 1, newDataGrid);

        // Apply or remove Classic brush overrides.
        // The overrides redefine Fluent-specific SystemControl* resource keys so that
        // the nav panel, toolbar, and status bar render correctly under Classic theme.
        var overridesKey = new Uri(ClassicOverridesUri);
        var existingOverrides = app.Resources.MergedDictionaries
            .OfType<ResourceInclude>()
            .FirstOrDefault(r => r.Source == overridesKey);

        if (theme == AppTheme.Classic && existingOverrides == null)
        {
            app.Resources.MergedDictionaries.Add(new ResourceInclude(overridesKey)
            {
                Source = overridesKey
            });
        }
        else if (theme != AppTheme.Classic && existingOverrides != null)
        {
            app.Resources.MergedDictionaries.Remove(existingOverrides);
        }

        AppSettingsService.Save(new AppSettings { Theme = theme });
    }

    public override void Initialize()
    {
        // Register Syncfusion license using key from environment variable (see project documentation for licensing details)
        var syncfusionLicense = Environment.GetEnvironmentVariable("SYNCFUSION_LICENSE_KEY");
        if (!string.IsNullOrEmpty(syncfusionLicense))
        {
            SyncfusionLicenseProvider.RegisterLicense(syncfusionLicense);
        }
        
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();

            // Apply saved theme (replaces default FluentTheme from AXAML if Classic was saved)
            var savedSettings = AppSettingsService.Load();
            if (savedSettings.Theme != AppTheme.Fluent)
                ApplyTheme(savedSettings.Theme);
            else
                CurrentTheme = AppTheme.Fluent;

            var services = new ServiceCollection();
            services.AddIntuneCommanderCore();
            services.AddTransient<MainWindowViewModel>();
            Services = services.BuildServiceProvider();

            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
