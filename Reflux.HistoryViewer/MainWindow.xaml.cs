using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Reflux.HistoryViewer.Services;
using Reflux.HistoryViewer.ViewModels;
using ScottPlot;

namespace Reflux.HistoryViewer;

public partial class MainWindow : Window
{
    readonly ViewerSettings _settings;
    readonly MainViewModel _vm;
    (string Path, ListSortDirection Direction)? _chartSort;

    public MainWindow(ViewerSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _vm = new MainViewModel(settings, PickFolder, Confirm);
        DataContext = _vm;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        _vm.SelectionRestored += (_, _) => ScrollToSelectedChart();
        RestoreWindowBounds();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplySplitRatio(_settings.SplitterRatio);
        DrawPlots();
        LastPlayedColumn.SortDirection = ListSortDirection.Descending; /* the list arrives sorted this way from SQL */
        await _vm.InitializeAsync();
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        SaveWindowBounds();
        _settings.SplitterRatio = CurrentSplitRatio();
        _vm.SaveState();
        _settings.Save();
        _vm.Dispose();
    }

    void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Charts):
                /* run after the ItemsSource binding has been applied */
                Dispatcher.InvokeAsync(ReapplyChartSort, DispatcherPriority.Loaded);
                break;
            case nameof(MainViewModel.Graph):
                DrawPlots();
                break;
        }
    }

    #region Keyboard

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && Keyboard.FocusedElement is not ComboBox { IsDropDownOpen: true })
        {
            _vm.ClearFiltersCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            _vm.ReimportCommand.Execute(null);
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    #endregion

    #region Song list sorting

    void ChartsGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        /* Let the DataGrid sort as usual, but remember the choice so it survives the next list refresh. */
        var direction = e.Column.SortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        _chartSort = (e.Column.SortMemberPath, direction);
    }

    void ReapplyChartSort()
    {
        if (ChartsGrid.ItemsSource is null)
        {
            return;
        }
        if (_chartSort is not { } sort)
        {
            LastPlayedColumn.SortDirection = ListSortDirection.Descending;
            ScrollToSelectedChart();
            return;
        }
        var view = CollectionViewSource.GetDefaultView(ChartsGrid.ItemsSource);
        if (view is null)
        {
            return;
        }
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(sort.Path, sort.Direction));
        }
        foreach (var column in ChartsGrid.Columns)
        {
            column.SortDirection = column.SortMemberPath == sort.Path ? sort.Direction : null;
        }
        ScrollToSelectedChart();
    }

    void ScrollToSelectedChart()
    {
        if (ChartsGrid.SelectedItem is { } item)
        {
            ChartsGrid.ScrollIntoView(item);
        }
    }

    #endregion

    #region Graphs

    void DrawPlots()
    {
        var g = _vm.Graph;
        Draw(ExPlot, "EX SCORE", g?.ExX, g?.ExY, g?.ExPrematureX, g?.ExPrematureY);
        Draw(BpPlot, "BP", g?.BpX, g?.BpY, g?.BpPrematureX, g?.BpPrematureY);
    }

    static void Draw(ScottPlot.WPF.WpfPlot control, string title, double[]? xs, double[]? ys, double[]? prematureXs, double[]? prematureYs)
    {
        var plot = control.Plot;
        plot.Clear();
        plot.Title(title);
        plot.XLabel("play");
        if (xs is { Length: > 0 } && ys is not null)
        {
            var scatter = plot.Add.Scatter(xs, ys);
            scatter.LineWidth = 1.5f;
            scatter.MarkerSize = 5;
            scatter.Color = Colors.SteelBlue;
            if (prematureXs is { Length: > 0 } && prematureYs is not null)
            {
                /* plays that ended early get their own marker on top of the line */
                plot.Add.Markers(prematureXs, prematureYs, MarkerShape.FilledTriangleDown, 10, Colors.Red);
            }
            plot.Axes.AutoScale();
        }
        control.Refresh();
    }

    #endregion

    #region Window / splitter state

    void RestoreWindowBounds()
    {
        if (_settings is not { WindowWidth: > 200, WindowHeight: > 200 })
        {
            return;
        }
        Width = _settings.WindowWidth!.Value;
        Height = _settings.WindowHeight!.Value;
        if (_settings.WindowLeft is double left && _settings.WindowTop is double top)
        {
            double minX = SystemParameters.VirtualScreenLeft, minY = SystemParameters.VirtualScreenTop;
            double maxX = minX + SystemParameters.VirtualScreenWidth, maxY = minY + SystemParameters.VirtualScreenHeight;
            if (left > minX - 50 && top > minY - 50 && left < maxX - 100 && top < maxY - 100)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left;
                Top = top;
            }
        }
        if (_settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    void SaveWindowBounds()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            _settings.WindowLeft = bounds.Left;
            _settings.WindowTop = bounds.Top;
            _settings.WindowWidth = bounds.Width;
            _settings.WindowHeight = bounds.Height;
        }
        _settings.WindowMaximized = WindowState == WindowState.Maximized;
    }

    void ApplySplitRatio(double ratio)
    {
        ratio = double.IsFinite(ratio) ? Math.Clamp(ratio, 0.15, 0.85) : 0.42;
        UpperRow.Height = new GridLength(ratio, GridUnitType.Star);
        LowerRow.Height = new GridLength(1 - ratio, GridUnitType.Star);
    }

    double CurrentSplitRatio()
    {
        double total = UpperRow.ActualHeight + LowerRow.ActualHeight;
        return total > 0 ? UpperRow.ActualHeight / total : _settings.SplitterRatio;
    }

    void Splitter_DragCompleted(object sender, DragCompletedEventArgs e) => _settings.SplitterRatio = CurrentSplitRatio();

    #endregion

    #region Dialogs and menu handlers

    string? PickFolder(string title)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = title,
            Multiselect = false,
        };
        if (_settings.HistoryDir is { } dir && Directory.Exists(dir))
        {
            dialog.InitialDirectory = dir;
        }
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    bool Confirm(string title, string message) =>
        MessageBox.Show(this, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    void Exit_Click(object sender, RoutedEventArgs e) => Close();

    void About_Click(object sender, RoutedEventArgs e)
    {
        var text = $"Reflux History Viewer {_vm.VersionText}\n\n" +
                   $"監視中のファイル: {(_vm.WatchedFile.Length > 0 ? _vm.WatchedFile : "-")}\n" +
                   $"DB: {_vm.DbPath ?? "-"}\n" +
                   $"設定: {ViewerSettings.FilePath}\n\n" +
                   "viewer.db は playlog.jsonl から作り直せるキャッシュです。削除しても次回起動時に再作成されます。";
        MessageBox.Show(this, text, "バージョン情報", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    void OpenDbFolder_Click(object sender, RoutedEventArgs e)
    {
        var db = _vm.DbPath;
        if (db is null)
        {
            return;
        }
        try
        {
            var arguments = File.Exists(db) ? $"/select,\"{db}\"" : $"\"{Path.GetDirectoryName(db)}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Reflux History Viewer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    #endregion
}
