using System;
using System.Collections.ObjectModel;
using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Intune.Commander.Core.Models;
using Intune.Commander.Desktop.Converters;

using Intune.Commander.Desktop.ViewModels;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace Intune.Commander.Desktop.Views;

public partial class MainWindow : Window
{
    private DataGrid? _mainDataGrid;
    private MainWindowViewModel? _vm;
    private bool _pendingGridRebuild;
    private DebugLogWindow? _debugLogWindow;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        // Ensure all child windows are closed and the process exits cleanly.
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _mainDataGrid = this.FindControl<DataGrid>("MainDataGrid");

        var importButton = this.FindControl<Button>("ImportButton");
        if (importButton != null)
            importButton.Click += OnImportClick;

        var groupLookupButton = this.FindControl<Button>("GroupLookupButton");
        if (groupLookupButton != null)
            groupLookupButton.Click += OnGroupLookupClick;

        var columnChooserButton = this.FindControl<Button>("ColumnChooserButton");
        if (columnChooserButton != null)
            columnChooserButton.Click += OnColumnChooserClick;

        var overviewNavButton = this.FindControl<Button>("OverviewNavButton");
        if (overviewNavButton != null)
            overviewNavButton.Click += OnOverviewNavClick;

        AttachViewModelIfAvailable("Loaded");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // Ctrl+Shift+L — open debug log (F12 is reserved for Avalonia DevTools)
        if (e.Key == Key.L && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            OnDebugLogLinkPressed(this, null!);
            e.Handled = true;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        AttachViewModelIfAvailable("DataContextChanged");
    }

    private void AttachViewModelIfAvailable(string reason)
    {
        if (_vm != null)
        {
            _vm.SwitchProfileRequested -= OnSwitchProfileRequested;
            _vm.CopyDetailsRequested -= OnCopyDetailsRequested;
            _vm.ViewRawJsonRequested -= OnViewRawJsonRequested;
            _vm.SaveFileRequested -= OnSaveFileRequested;
            _vm.OpenAfterExportRequested -= OnOpenAfterExportRequested;
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm = null;
        }

        if (DataContext is MainWindowViewModel vm)
        {
            _vm = vm;
            vm.SwitchProfileRequested += OnSwitchProfileRequested;
            vm.CopyDetailsRequested += OnCopyDetailsRequested;
            vm.ViewRawJsonRequested += OnViewRawJsonRequested;
            vm.SaveFileRequested += OnSaveFileRequested;
            vm.OpenAfterExportRequested += OnOpenAfterExportRequested;
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.ActiveColumns)
            or nameof(MainWindowViewModel.IsDeviceConfigCategory)
            or nameof(MainWindowViewModel.IsCompliancePolicyCategory)
            or nameof(MainWindowViewModel.IsApplicationCategory)
            or nameof(MainWindowViewModel.IsAppAssignmentsCategory)
            or nameof(MainWindowViewModel.IsSettingsCatalogCategory)
            or nameof(MainWindowViewModel.IsEndpointSecurityCategory)
            or nameof(MainWindowViewModel.IsAdministrativeTemplatesCategory)
            or nameof(MainWindowViewModel.IsEnrollmentConfigurationsCategory)
            or nameof(MainWindowViewModel.IsAppProtectionPoliciesCategory)
            or nameof(MainWindowViewModel.IsManagedDeviceAppConfigurationsCategory)
            or nameof(MainWindowViewModel.IsTargetedManagedAppConfigurationsCategory)
            or nameof(MainWindowViewModel.IsTermsAndConditionsCategory)
            or nameof(MainWindowViewModel.IsScopeTagsCategory)
            or nameof(MainWindowViewModel.IsRoleDefinitionsCategory)
            or nameof(MainWindowViewModel.IsIntuneBrandingCategory)
            or nameof(MainWindowViewModel.IsAzureBrandingCategory)
            or nameof(MainWindowViewModel.IsConditionalAccessCategory)
            or nameof(MainWindowViewModel.IsAssignmentFiltersCategory)
            or nameof(MainWindowViewModel.IsPolicySetsCategory)
            or nameof(MainWindowViewModel.IsAutopilotProfilesCategory)
            or nameof(MainWindowViewModel.IsDeviceHealthScriptsCategory)
            or nameof(MainWindowViewModel.IsMacCustomAttributesCategory)
            or nameof(MainWindowViewModel.IsFeatureUpdatesCategory)
            or nameof(MainWindowViewModel.IsNamedLocationsCategory)
            or nameof(MainWindowViewModel.IsAuthenticationStrengthsCategory)
            or nameof(MainWindowViewModel.IsAuthenticationContextsCategory)
            or nameof(MainWindowViewModel.IsTermsOfUseCategory)
            or nameof(MainWindowViewModel.IsDeviceManagementScriptsCategory)
            or nameof(MainWindowViewModel.IsDeviceShellScriptsCategory)
            or nameof(MainWindowViewModel.IsComplianceScriptsCategory)
            or nameof(MainWindowViewModel.IsDynamicGroupsCategory)
            or nameof(MainWindowViewModel.IsAssignedGroupsCategory)
            or nameof(MainWindowViewModel.IsOverviewCategory))
        {
            // Multiple category properties fire in sequence during a single
            // category change.  Defer the rebuild to avoid repeated
            // clear-and-bind cycles that can momentarily pair new data with
            // old columns (or vice-versa), producing binding errors.
            if (!_pendingGridRebuild)
            {
                _pendingGridRebuild = true;
                Dispatcher.UIThread.Post(() =>
                {
                    _pendingGridRebuild = false;
                    RebuildDataGridColumns();
                    BindDataGridSource();
                }, DispatcherPriority.Render);
            }
        }
        else if (e.PropertyName is nameof(MainWindowViewModel.FilteredDeviceConfigurations)
            or nameof(MainWindowViewModel.FilteredCompliancePolicies)
            or nameof(MainWindowViewModel.FilteredApplications)
            or nameof(MainWindowViewModel.FilteredAppAssignmentRows)
            or nameof(MainWindowViewModel.FilteredSettingsCatalogPolicies)
            or nameof(MainWindowViewModel.FilteredEndpointSecurityIntents)
            or nameof(MainWindowViewModel.FilteredAdministrativeTemplates)
            or nameof(MainWindowViewModel.FilteredEnrollmentConfigurations)
            or nameof(MainWindowViewModel.FilteredAppProtectionPolicies)
            or nameof(MainWindowViewModel.FilteredManagedDeviceAppConfigurations)
            or nameof(MainWindowViewModel.FilteredTargetedManagedAppConfigurations)
            or nameof(MainWindowViewModel.FilteredTermsAndConditionsCollection)
            or nameof(MainWindowViewModel.FilteredScopeTags)
            or nameof(MainWindowViewModel.FilteredRoleDefinitions)
            or nameof(MainWindowViewModel.FilteredIntuneBrandingProfiles)
            or nameof(MainWindowViewModel.FilteredAzureBrandingLocalizations)
            or nameof(MainWindowViewModel.FilteredConditionalAccessPolicies)
            or nameof(MainWindowViewModel.FilteredAssignmentFilters)
            or nameof(MainWindowViewModel.FilteredPolicySets)
            or nameof(MainWindowViewModel.FilteredAutopilotProfiles)
            or nameof(MainWindowViewModel.FilteredDeviceHealthScripts)
            or nameof(MainWindowViewModel.FilteredMacCustomAttributes)
            or nameof(MainWindowViewModel.FilteredFeatureUpdateProfiles)
            or nameof(MainWindowViewModel.FilteredNamedLocations)
            or nameof(MainWindowViewModel.FilteredAuthenticationStrengthPolicies)
            or nameof(MainWindowViewModel.FilteredAuthenticationContextClassReferences)
            or nameof(MainWindowViewModel.FilteredTermsOfUseAgreements)
            or nameof(MainWindowViewModel.FilteredDeviceManagementScripts)
            or nameof(MainWindowViewModel.FilteredDeviceShellScripts)
            or nameof(MainWindowViewModel.FilteredComplianceScripts)
            or nameof(MainWindowViewModel.FilteredDynamicGroupRows)
            or nameof(MainWindowViewModel.FilteredAssignedGroupRows))
        {
            // Only rebind when the changed collection matches the active
            // category so we never pair mismatched columns/data.
            if (IsActiveFilteredCollection(e.PropertyName))
                BindDataGridSource();
        }
    }

    /// <summary>
    /// Returns true when the property name is the filtered collection that
    /// the currently selected nav category actually displays.
    /// </summary>
    private bool IsActiveFilteredCollection(string? propertyName)
    {
        if (_vm == null) return false;
        return propertyName switch
        {
            nameof(MainWindowViewModel.FilteredDeviceConfigurations) => _vm.IsDeviceConfigCategory,
            nameof(MainWindowViewModel.FilteredCompliancePolicies)  => _vm.IsCompliancePolicyCategory,
            nameof(MainWindowViewModel.FilteredApplications)        => _vm.IsApplicationCategory,
            nameof(MainWindowViewModel.FilteredAppAssignmentRows)   => _vm.IsAppAssignmentsCategory,
            nameof(MainWindowViewModel.FilteredSettingsCatalogPolicies) => _vm.IsSettingsCatalogCategory,
            nameof(MainWindowViewModel.FilteredEndpointSecurityIntents) => _vm.IsEndpointSecurityCategory,
            nameof(MainWindowViewModel.FilteredAdministrativeTemplates) => _vm.IsAdministrativeTemplatesCategory,
            nameof(MainWindowViewModel.FilteredEnrollmentConfigurations) => _vm.IsEnrollmentConfigurationsCategory,
            nameof(MainWindowViewModel.FilteredAppProtectionPolicies) => _vm.IsAppProtectionPoliciesCategory,
            nameof(MainWindowViewModel.FilteredManagedDeviceAppConfigurations) => _vm.IsManagedDeviceAppConfigurationsCategory,
            nameof(MainWindowViewModel.FilteredTargetedManagedAppConfigurations) => _vm.IsTargetedManagedAppConfigurationsCategory,
            nameof(MainWindowViewModel.FilteredTermsAndConditionsCollection) => _vm.IsTermsAndConditionsCategory,
            nameof(MainWindowViewModel.FilteredScopeTags) => _vm.IsScopeTagsCategory,
            nameof(MainWindowViewModel.FilteredRoleDefinitions) => _vm.IsRoleDefinitionsCategory,
            nameof(MainWindowViewModel.FilteredIntuneBrandingProfiles) => _vm.IsIntuneBrandingCategory,
            nameof(MainWindowViewModel.FilteredAzureBrandingLocalizations) => _vm.IsAzureBrandingCategory,
            nameof(MainWindowViewModel.FilteredConditionalAccessPolicies) => _vm.IsConditionalAccessCategory,
            nameof(MainWindowViewModel.FilteredAssignmentFilters)   => _vm.IsAssignmentFiltersCategory,
            nameof(MainWindowViewModel.FilteredPolicySets)          => _vm.IsPolicySetsCategory,
            nameof(MainWindowViewModel.FilteredAutopilotProfiles) => _vm.IsAutopilotProfilesCategory,
            nameof(MainWindowViewModel.FilteredDeviceHealthScripts) => _vm.IsDeviceHealthScriptsCategory,
            nameof(MainWindowViewModel.FilteredMacCustomAttributes) => _vm.IsMacCustomAttributesCategory,
            nameof(MainWindowViewModel.FilteredFeatureUpdateProfiles) => _vm.IsFeatureUpdatesCategory,
            nameof(MainWindowViewModel.FilteredNamedLocations) => _vm.IsNamedLocationsCategory,
            nameof(MainWindowViewModel.FilteredAuthenticationStrengthPolicies) => _vm.IsAuthenticationStrengthsCategory,
            nameof(MainWindowViewModel.FilteredAuthenticationContextClassReferences) => _vm.IsAuthenticationContextsCategory,
            nameof(MainWindowViewModel.FilteredTermsOfUseAgreements) => _vm.IsTermsOfUseCategory,
            nameof(MainWindowViewModel.FilteredDeviceManagementScripts) => _vm.IsDeviceManagementScriptsCategory,
            nameof(MainWindowViewModel.FilteredDeviceShellScripts) => _vm.IsDeviceShellScriptsCategory,
            nameof(MainWindowViewModel.FilteredComplianceScripts) => _vm.IsComplianceScriptsCategory,
            nameof(MainWindowViewModel.FilteredDynamicGroupRows)    => _vm.IsDynamicGroupsCategory,
            nameof(MainWindowViewModel.FilteredAssignedGroupRows)   => _vm.IsAssignedGroupsCategory,
            _ => false
        };
    }

    private void BindDataGridSource()
    {
        if (_mainDataGrid == null || _vm == null) return;

        // Clear existing bindings first
        _mainDataGrid.ClearValue(DataGrid.ItemsSourceProperty);
        _mainDataGrid.ClearValue(DataGrid.SelectedItemProperty);

        if (_vm.IsDeviceConfigCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredDeviceConfigurations)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedConfiguration)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsCompliancePolicyCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredCompliancePolicies)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedCompliancePolicy)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsApplicationCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredApplications)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedApplication)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsAppAssignmentsCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredAppAssignmentRows)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedAppAssignmentRow)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsSettingsCatalogCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredSettingsCatalogPolicies)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedSettingsCatalogPolicy)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsEndpointSecurityCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredEndpointSecurityIntents)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedEndpointSecurityIntent)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsAdministrativeTemplatesCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredAdministrativeTemplates)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedAdministrativeTemplate)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsEnrollmentConfigurationsCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredEnrollmentConfigurations)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedEnrollmentConfiguration)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsAppProtectionPoliciesCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredAppProtectionPolicies)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedAppProtectionPolicy)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsManagedDeviceAppConfigurationsCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredManagedDeviceAppConfigurations)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedManagedDeviceAppConfiguration)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsTargetedManagedAppConfigurationsCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredTargetedManagedAppConfigurations)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedTargetedManagedAppConfiguration)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsTermsAndConditionsCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredTermsAndConditionsCollection)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedTermsAndConditions)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsScopeTagsCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredScopeTags)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedScopeTag)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsRoleDefinitionsCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredRoleDefinitions)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedRoleDefinition)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsIntuneBrandingCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredIntuneBrandingProfiles)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedIntuneBrandingProfile)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsAzureBrandingCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredAzureBrandingLocalizations)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedAzureBrandingLocalization)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsConditionalAccessCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredConditionalAccessPolicies)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedConditionalAccessPolicy)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsAssignmentFiltersCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredAssignmentFilters)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedAssignmentFilter)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsPolicySetsCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredPolicySets)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedPolicySet)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsAutopilotProfilesCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredAutopilotProfiles)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedAutopilotProfile)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsDeviceHealthScriptsCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredDeviceHealthScripts)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedDeviceHealthScript)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsMacCustomAttributesCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredMacCustomAttributes)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedMacCustomAttribute)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsFeatureUpdatesCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredFeatureUpdateProfiles)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedFeatureUpdateProfile)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsNamedLocationsCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredNamedLocations)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedNamedLocation)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsAuthenticationStrengthsCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredAuthenticationStrengthPolicies)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedAuthenticationStrengthPolicy)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsAuthenticationContextsCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredAuthenticationContextClassReferences)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedAuthenticationContextClassReference)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsTermsOfUseCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredTermsOfUseAgreements)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedTermsOfUseAgreement)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsDeviceManagementScriptsCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredDeviceManagementScripts)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedDeviceManagementScript)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsDeviceShellScriptsCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredDeviceShellScripts)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedDeviceShellScript)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsComplianceScriptsCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredComplianceScripts)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedComplianceScript)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsDynamicGroupsCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredDynamicGroupRows)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedDynamicGroupRow)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else if (_vm.IsAssignedGroupsCategory)
        {
            _mainDataGrid.Bind(DataGrid.ItemsSourceProperty,
                new Binding(nameof(_vm.FilteredAssignedGroupRows)) { Source = _vm });
            _mainDataGrid.Bind(DataGrid.SelectedItemProperty,
                new Binding(nameof(_vm.SelectedAssignedGroupRow)) { Source = _vm, Mode = BindingMode.TwoWay });
        }
        else
        {
            _mainDataGrid.ItemsSource = null;
        }
    }

    private void RebuildDataGridColumns()
    {
        if (_mainDataGrid == null || _vm == null) return;

        _mainDataGrid.Columns.Clear();
        var columns = _vm.ActiveColumns;
        if (columns == null) return;

        foreach (var col in columns)
        {
            if (!col.IsVisible) continue;

            IBinding binding;

            if (col.BindingPath == "Computed:ODataType")
            {
                binding = new Binding("OdataType") { Converter = ODataTypeConverter.Instance };
            }
            else if (col.BindingPath == "Computed:Platform")
            {
                binding = new Binding("OdataType") { Converter = PlatformConverter.Instance };
            }
            else if (col.BindingPath == "Computed:RoleScopeTags")
            {
                binding = new Binding("RoleScopeTagIds") { Converter = StringListConverter.Instance };
            }
            else
            {
                binding = new Binding(col.BindingPath);
            }

            var dgCol = new DataGridTextColumn
            {
                Header = col.Header,
                Binding = binding,
                Width = col.IsStar
                    ? new DataGridLength(1, DataGridLengthUnitType.Star)
                    : new DataGridLength(col.Width)
            };

            _mainDataGrid.Columns.Add(dgCol);
        }
    }

    // --- Column chooser flyout ---

    private void OnColumnChooserClick(object? sender, RoutedEventArgs e)
    {
        if (_vm?.ActiveColumns == null || sender is not Button btn) return;

        var popup = new Avalonia.Controls.Primitives.Popup
        {
            PlacementTarget = btn,
            Placement = PlacementMode.BottomEdgeAlignedRight,
            IsLightDismissEnabled = true
        };

        var stack = new StackPanel { Spacing = 4, Margin = new Thickness(8) };

        foreach (var col in _vm.ActiveColumns)
        {
            var cb = new CheckBox
            {
                Content = col.Header,
                IsChecked = col.IsVisible,
                Tag = col
            };
            cb.IsCheckedChanged += (_, _) =>
            {
                if (cb.Tag is DataGridColumnConfig config)
                {
                    config.IsVisible = cb.IsChecked == true;
                    RebuildDataGridColumns();
                }
            };
            stack.Children.Add(cb);
        }

        var popupBorder = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4),
            Child = stack
        };
        popupBorder.Bind(Border.BackgroundProperty, this.GetResourceObservable("SystemControlBackgroundAltHighBrush"));
        popupBorder.Bind(Border.BorderBrushProperty, this.GetResourceObservable("SystemControlForegroundBaseMediumBrush"));
        popup.Child = popupBorder;

        // Avalonia popups need to be in the visual tree
        if (this.Content is Panel panel)
        {
            panel.Children.Add(popup);
            popup.Open();
            popup.Closed += (_, _) =>
            {
                panel.Children.Remove(popup);
            };
        }
    }

    private void OnAssignmentReportClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;

        var reportVm = _vm.CreateAssignmentReportViewModel();
        if (reportVm == null) return;

        var window = new AssignmentReportWindow
        {
            DataContext = reportVm
        };
        window.Show(this);
    }

    private void OnGroupLookupClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;

        var lookupVm = _vm.CreateGroupLookupViewModel();
        if (lookupVm == null) return;

        var window = new GroupLookupWindow
        {
            DataContext = lookupVm
        };
        window.Show(this); // non-modal so the user can still browse
    }

    private async Task<bool> OnSwitchProfileRequested(TenantProfile target)
    {
        var box = MessageBoxManager.GetMessageBoxStandard(
            "Switch Profile",
            $"Switch to \"{target.Name}\"?\nYou will be disconnected from the current tenant.",
            ButtonEnum.YesNo,
            MsBox.Avalonia.Enums.Icon.Info);

        var result = await box.ShowWindowDialogAsync(this);
        return result == ButtonResult.Yes;
    }

    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Import Folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            var folderPath = folders[0].Path.LocalPath;
            if (DataContext is MainWindowViewModel vm)
            {
                await vm.ImportFromFolderCommand.ExecuteAsync(folderPath);
            }
        }
    }

    private async void OnCopyDetailsRequested(string text)
    {
        try
        {
            var clipboard = GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(text);
        }
        catch { /* clipboard not available */ }
    }

    private void OnViewRawJsonRequested(string title, string json)
    {
        var window = new RawJsonWindow(title, json);
        window.Show(this);
    }

    private void OnDebugLogLinkPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_debugLogWindow == null || !_debugLogWindow.IsVisible)
        {
            _debugLogWindow = new DebugLogWindow();
            _debugLogWindow.Closed += (_, _) => _debugLogWindow = null;
            _debugLogWindow.Show();
        }
        else
        {
            _debugLogWindow.Activate();
        }
    }

    private void OnCheckForUpdatesClick(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/adamgell/IntuneCommader/releases");
    }

    private async void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = version != null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "dev";

        var box = MessageBoxManager.GetMessageBoxStandard(
            "About Intune Commander",
            $"Intune Commander {versionText}\n\n" +
            "A .NET 8 / Avalonia desktop app for managing\n" +
            "Microsoft Intune configurations across clouds.\n\n" +
            "https://github.com/adamgell/IntuneCommader",
            ButtonEnum.Ok,
            MsBox.Avalonia.Enums.Icon.Info);

        await box.ShowAsPopupAsync(this);
    }

    private void OnOverviewNavClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        // Find the Overview category in the flat NavCategories list
        var overview = _vm.NavCategories.FirstOrDefault(c => c.Name == "Overview");
        if (overview != null)
            _vm.SelectedCategory = overview;
    }

    private void OnNavItemClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is Button btn && btn.Tag is NavCategory category)
        {
            // Find the matching category in the flat NavCategories list to maintain reference equality
            var match = _vm.NavCategories.FirstOrDefault(c => c.Name == category.Name);
            if (match != null)
                _vm.SelectedCategory = match;
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", url);
            else
                Process.Start("xdg-open", url);
        }
        catch { /* best effort */ }
    }

    private async Task<bool> OnOpenAfterExportRequested(string filePath)
    {
        var box = MessageBoxManager.GetMessageBoxStandard(
            "Export Complete",
            $"PowerPoint exported successfully.\n\nWould you like to open {Path.GetFileName(filePath)}?",
            ButtonEnum.YesNo,
            MsBox.Avalonia.Enums.Icon.Success);

        var result = await box.ShowWindowDialogAsync(this);
        return result == ButtonResult.Yes;
    }

    private async Task<string?> OnSaveFileRequested(string defaultFileName, string filter)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return null;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save File",
            SuggestedFileName = defaultFileName,
            FileTypeChoices = filter.Contains("pptx") 
                ? new[] { new FilePickerFileType("PowerPoint Presentation") { Patterns = new[] { "*.pptx" } } }
                : null
        });

        return file?.Path.LocalPath;
    }
}
