using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using IntuneManager.Core.Models;
using IntuneManager.Desktop.Converters;
using IntuneManager.Desktop.ViewModels;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace IntuneManager.Desktop.Views;

public partial class MainWindow : Window
{
    private DataGrid? _mainDataGrid;
    private MainWindowViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _mainDataGrid = this.FindControl<DataGrid>("MainDataGrid");

        var importButton = this.FindControl<Button>("ImportButton");
        if (importButton != null)
            importButton.Click += OnImportClick;

        var columnChooserButton = this.FindControl<Button>("ColumnChooserButton");
        if (columnChooserButton != null)
            columnChooserButton.Click += OnColumnChooserClick;

        if (DataContext is MainWindowViewModel vm)
        {
            _vm = vm;
            vm.SwitchProfileRequested += OnSwitchProfileRequested;
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
            or nameof(MainWindowViewModel.IsOverviewCategory))
        {
            RebuildDataGridColumns();
            BindDataGridSource();
        }
        else if (e.PropertyName is nameof(MainWindowViewModel.FilteredDeviceConfigurations)
            or nameof(MainWindowViewModel.FilteredCompliancePolicies)
            or nameof(MainWindowViewModel.FilteredApplications)
            or nameof(MainWindowViewModel.FilteredAppAssignmentRows))
        {
            BindDataGridSource();
        }
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

        popup.Child = new Border
        {
            Background = Avalonia.Media.Brushes.White,
            BorderBrush = Avalonia.Media.Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4),
            Child = stack
        };

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
}
