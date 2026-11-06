using System.IO;
using System.Windows.Input;
using Reflux.HistoryViewer.Core;
using Reflux.HistoryViewer.Services;

namespace Reflux.HistoryViewer.ViewModels;

public sealed record FilterOption<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

/// <summary>Series for the two graphs of the selected chart. X is the chronological play number (1-based).</summary>
public sealed class GraphData
{
    public double[] ExX { get; init; } = Array.Empty<double>();
    public double[] ExY { get; init; } = Array.Empty<double>();
    public double[] ExPrematureX { get; init; } = Array.Empty<double>();
    public double[] ExPrematureY { get; init; } = Array.Empty<double>();
    public double[] BpX { get; init; } = Array.Empty<double>();
    public double[] BpY { get; init; } = Array.Empty<double>();
    public double[] BpPrematureX { get; init; } = Array.Empty<double>();
    public double[] BpPrematureY { get; init; } = Array.Empty<double>();
}

/// <summary>
/// State of the main window. Talks to <see cref="HistoryStore"/> only (always on a thread-pool thread) and to a
/// <see cref="PlaylogWatcher"/> that triggers incremental imports while Reflux is running.
/// </summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    readonly ViewerSettings _settings;
    readonly Func<string?> _pickFolder;
    readonly Func<string, string, bool> _confirm;
    readonly SynchronizationContext _ui;
    readonly SemaphoreSlim _importGate = new(1, 1);
    readonly List<RelayCommand> _storeCommands = new();

    HistoryStore? _store;
    PlaylogWatcher? _watcher;
    string? _historyDir;
    bool _importQueued;
    bool _firstLoadDone;
    long? _highlightFrom;
    string? _restoreChartKey;
    bool _suppressSelection;
    bool _suppressRefresh;
    CancellationTokenSource? _searchDebounce;
    IReadOnlyList<PlayRow> _chronological = Array.Empty<PlayRow>();

    string _searchText = "";
    FilterOption<int?> _selectedLevel;
    FilterOption<string?> _selectedSide;
    FilterOption<string?> _selectedDifficulty;
    FilterOption<int?> _selectedLamp;
    FilterOption<int?> _selectedPeriod;
    IReadOnlyList<ChartSummary> _charts = Array.Empty<ChartSummary>();
    ChartSummary? _selectedChart;
    ChartSummary? _detailChart;
    IReadOnlyList<PlayItem> _plays = Array.Empty<PlayItem>();
    GraphData? _graph;
    bool _newestFirst;
    IReadOnlyList<DailySummary> _daily = Array.Empty<DailySummary>();
    int _selectedTab;
    string _watchedFile = "";
    string _lastImportText = "-";
    long _skippedLines;
    string _progressText = "";
    bool _isImporting;
    string _statusMessage = "";
    string? _dbPath;

    public MainViewModel(ViewerSettings settings, Func<string?> pickFolder, Func<string, string, bool> confirm)
    {
        _settings = settings;
        _pickFolder = pickFolder;
        _confirm = confirm;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _newestFirst = settings.NewestFirst;
        _restoreChartKey = settings.LastChartKey;

        var levels = new List<FilterOption<int?>> { new("すべて", null) };
        levels.AddRange(Enumerable.Range(1, 12).Select(l => new FilterOption<int?>(l.ToString(), l)));
        Levels = levels;
        Sides = new FilterOption<string?>[] { new("すべて", null), new("SP", "SP"), new("DP", "DP") };
        Difficulties = new FilterOption<string?>[] { new("すべて", null), new("BEGINNER", "B"), new("NORMAL", "N"), new("HYPER", "H"), new("ANOTHER", "A"), new("LEGGENDARIA", "L") };
        var lamps = new List<FilterOption<int?>> { new("すべて", null) };
        lamps.AddRange(LampInfo.Codes.Skip(1).Select(c => new FilterOption<int?>($"{c} ({LampInfo.Text(c)})", LampInfo.Rank(c))));
        Lamps = lamps;
        Periods = new FilterOption<int?>[] { new("すべて", null), new("今日", 1), new("7日間", 7), new("30日間", 30), new("90日間", 90), new("1年", 365) };
        _selectedLevel = Levels[0];
        _selectedSide = Sides[0];
        _selectedDifficulty = Difficulties[0];
        _selectedLamp = Lamps[0];
        _selectedPeriod = Periods[0];

        OpenFolderCommand = new RelayCommand(_ => _ = ChooseFolderAsync());
        ReimportCommand = StoreCommand(_ => _ = ImportAsync());
        RebuildCommand = StoreCommand(_ => _ = RebuildAsync());
        ClearFiltersCommand = new RelayCommand(_ => ClearFilters());
        ShowSongsCommand = new RelayCommand(_ => SelectedTab = 0);
        ShowDailyCommand = new RelayCommand(_ => SelectedTab = 1);
    }

    /// <summary>Raised after a list refresh re-selected a chart, so the view can scroll it into view.</summary>
    public event EventHandler? SelectionRestored;

    public string VersionText => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "?";

    #region Filters

    public IReadOnlyList<FilterOption<int?>> Levels { get; }
    public IReadOnlyList<FilterOption<string?>> Sides { get; }
    public IReadOnlyList<FilterOption<string?>> Difficulties { get; }
    public IReadOnlyList<FilterOption<int?>> Lamps { get; }
    public IReadOnlyList<FilterOption<int?>> Periods { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value ?? ""))
            {
                Raise(nameof(HasFilter));
                ScheduleRefresh(debounce: true);
            }
        }
    }

    public FilterOption<int?> SelectedLevel
    {
        get => _selectedLevel;
        set => SetFilter(ref _selectedLevel, value ?? Levels[0]);
    }

    public FilterOption<string?> SelectedSide
    {
        get => _selectedSide;
        set => SetFilter(ref _selectedSide, value ?? Sides[0]);
    }

    public FilterOption<string?> SelectedDifficulty
    {
        get => _selectedDifficulty;
        set => SetFilter(ref _selectedDifficulty, value ?? Difficulties[0]);
    }

    public FilterOption<int?> SelectedLamp
    {
        get => _selectedLamp;
        set => SetFilter(ref _selectedLamp, value ?? Lamps[0]);
    }

    public FilterOption<int?> SelectedPeriod
    {
        get => _selectedPeriod;
        set => SetFilter(ref _selectedPeriod, value ?? Periods[0]);
    }

    public bool HasFilter =>
        !string.IsNullOrWhiteSpace(SearchText) || SelectedLevel.Value is not null || SelectedSide.Value is not null ||
        SelectedDifficulty.Value is not null || SelectedLamp.Value is not null || SelectedPeriod.Value is not null;

    void SetFilter<T>(ref FilterOption<T> field, FilterOption<T> value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (Set(ref field, value, propertyName))
        {
            Raise(nameof(HasFilter));
            ScheduleRefresh(debounce: false);
        }
    }

    ChartFilter BuildFilter() => new()
    {
        TitleContains = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
        Level = SelectedLevel.Value,
        Side = SelectedSide.Value,
        DifficultyLetter = SelectedDifficulty.Value,
        BestLampRank = SelectedLamp.Value,
        FromUtc = PeriodStart(SelectedPeriod.Value),
    };

    static string? PeriodStart(int? days)
    {
        if (days is null)
        {
            return null;
        }
        var today = DateTime.Today;
        var from = days == 365 ? today.AddYears(-1) : today.AddDays(-(days.Value - 1));
        return PlaylogParser.FormatTimestamp(from.ToUniversalTime());
    }

    void ClearFilters()
    {
        _suppressRefresh = true;
        try
        {
            SearchText = "";
            SelectedLevel = Levels[0];
            SelectedSide = Sides[0];
            SelectedDifficulty = Difficulties[0];
            SelectedLamp = Lamps[0];
            SelectedPeriod = Periods[0];
        }
        finally
        {
            _suppressRefresh = false;
        }
        ScheduleRefresh(debounce: false);
    }

    void ScheduleRefresh(bool debounce)
    {
        if (_suppressRefresh)
        {
            return;
        }
        _searchDebounce?.Cancel();
        _searchDebounce = null;
        if (!debounce)
        {
            _ = RefreshChartsAsync();
            return;
        }
        var cts = _searchDebounce = new CancellationTokenSource();
        _ = DelayedRefreshAsync(cts.Token);
    }

    async Task DelayedRefreshAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(250, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        await RefreshChartsAsync();
    }

    #endregion

    #region Song list

    public IReadOnlyList<ChartSummary> Charts
    {
        get => _charts;
        private set
        {
            if (Set(ref _charts, value))
            {
                Raise(nameof(ChartCount));
            }
        }
    }

    public int ChartCount => Charts.Count;

    public ChartSummary? SelectedChart
    {
        get => _selectedChart;
        set
        {
            if (!Set(ref _selectedChart, value) || _suppressSelection)
            {
                return;
            }
            _highlightFrom = null;
            _ = LoadDetailAsync();
        }
    }

    async Task RefreshChartsAsync(bool reloadDetail = false)
    {
        var store = _store;
        if (store is null)
        {
            return;
        }
        var filter = BuildFilter();
        IReadOnlyList<ChartSummary> charts;
        try
        {
            charts = await Task.Run(() => store.QueryCharts(filter));
        }
        catch (Exception ex)
        {
            StatusMessage = "曲一覧の取得に失敗しました: " + ex.Message;
            return;
        }
        var previousId = SelectedChart?.Id;
        _suppressSelection = true;
        try
        {
            Charts = charts;
            ChartSummary? next = previousId is long id ? charts.FirstOrDefault(c => c.Id == id) : null;
            if (next is null && _restoreChartKey is { } key)
            {
                next = charts.FirstOrDefault(c => c.Key == key);
            }
            if (next is not null)
            {
                _restoreChartKey = null;
            }
            SelectedChart = next;
        }
        finally
        {
            _suppressSelection = false;
        }
        if (SelectedChart?.Id != previousId)
        {
            _highlightFrom = null;
            reloadDetail = true;
        }
        if (reloadDetail)
        {
            await LoadDetailAsync();
        }
        SelectionRestored?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Chart detail

    public ChartSummary? DetailChart
    {
        get => _detailChart;
        private set
        {
            if (Set(ref _detailChart, value))
            {
                Raise(nameof(HasDetail));
            }
        }
    }

    public bool HasDetail => DetailChart is not null;

    public IReadOnlyList<PlayItem> Plays
    {
        get => _plays;
        private set => Set(ref _plays, value);
    }

    public GraphData? Graph
    {
        get => _graph;
        private set => Set(ref _graph, value);
    }

    public bool NewestFirst
    {
        get => _newestFirst;
        set
        {
            if (Set(ref _newestFirst, value))
            {
                _settings.NewestFirst = value;
                Raise(nameof(OrderLabel));
                BuildPlayItems();
            }
        }
    }

    public string OrderLabel => NewestFirst ? "新しい順" : "古い順";

    async Task LoadDetailAsync()
    {
        var chart = SelectedChart;
        var store = _store;
        if (chart is null || store is null)
        {
            DetailChart = null;
            _chronological = Array.Empty<PlayRow>();
            Plays = Array.Empty<PlayItem>();
            Graph = null;
            return;
        }
        ChartSummary? header;
        IReadOnlyList<PlayRow> plays;
        try
        {
            (header, plays) = await Task.Run(() => (store.GetChart(chart.Id), store.QueryPlays(chart.Id, newestFirst: false)));
        }
        catch (Exception ex)
        {
            StatusMessage = "プレー一覧の取得に失敗しました: " + ex.Message;
            return;
        }
        if (SelectedChart?.Id != chart.Id)
        {
            return; /* the selection moved on while we were loading */
        }
        DetailChart = header ?? chart;
        _chronological = plays;
        BuildPlayItems();
        BuildGraph();
    }

    void BuildPlayItems()
    {
        var items = new List<PlayItem>(_chronological.Count);
        for (int i = 0; i < _chronological.Count; i++)
        {
            var row = _chronological[i];
            items.Add(new PlayItem(row, i + 1, _highlightFrom is long from && row.Id >= from));
        }
        if (NewestFirst)
        {
            items.Reverse();
        }
        Plays = items;
    }

    void BuildGraph()
    {
        var exX = new List<double>();
        var exY = new List<double>();
        var exPx = new List<double>();
        var exPy = new List<double>();
        var bpX = new List<double>();
        var bpY = new List<double>();
        var bpPx = new List<double>();
        var bpPy = new List<double>();
        for (int i = 0; i < _chronological.Count; i++)
        {
            var row = _chronological[i];
            double x = i + 1;
            bool premature = row.PrematureEnd == true;
            if (row.ExScore is int ex)
            {
                exX.Add(x);
                exY.Add(ex);
                if (premature)
                {
                    exPx.Add(x);
                    exPy.Add(ex);
                }
            }
            if (row.MissCount is int bp)
            {
                bpX.Add(x);
                bpY.Add(bp);
                if (premature)
                {
                    bpPx.Add(x);
                    bpPy.Add(bp);
                }
            }
        }
        Graph = new GraphData
        {
            ExX = exX.ToArray(), ExY = exY.ToArray(), ExPrematureX = exPx.ToArray(), ExPrematureY = exPy.ToArray(),
            BpX = bpX.ToArray(), BpY = bpY.ToArray(), BpPrematureX = bpPx.ToArray(), BpPrematureY = bpPy.ToArray(),
        };
    }

    #endregion

    #region Daily summary

    public IReadOnlyList<DailySummary> Daily
    {
        get => _daily;
        private set => Set(ref _daily, value);
    }

    public int SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (Set(ref _selectedTab, value) && value == 1)
            {
                _ = LoadDailyAsync();
            }
        }
    }

    async Task LoadDailyAsync()
    {
        var store = _store;
        if (store is null)
        {
            return;
        }
        try
        {
            Daily = await Task.Run(() => store.QueryDaily());
        }
        catch (Exception ex)
        {
            StatusMessage = "日別サマリの取得に失敗しました: " + ex.Message;
        }
    }

    #endregion

    #region Status

    public string WatchedFile
    {
        get => _watchedFile;
        private set => Set(ref _watchedFile, value);
    }

    public string LastImportText
    {
        get => _lastImportText;
        private set => Set(ref _lastImportText, value);
    }

    public long SkippedLines
    {
        get => _skippedLines;
        private set => Set(ref _skippedLines, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => Set(ref _progressText, value);
    }

    public bool IsImporting
    {
        get => _isImporting;
        private set => Set(ref _isImporting, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => Set(ref _statusMessage, value);
    }

    public string? DbPath
    {
        get => _dbPath;
        private set => Set(ref _dbPath, value);
    }

    #endregion

    #region Commands

    public ICommand OpenFolderCommand { get; }
    public ICommand ReimportCommand { get; }
    public ICommand RebuildCommand { get; }
    public ICommand ClearFiltersCommand { get; }
    public ICommand ShowSongsCommand { get; }
    public ICommand ShowDailyCommand { get; }

    RelayCommand StoreCommand(Action<object?> execute)
    {
        var command = new RelayCommand(execute, _ => _store is not null);
        _storeCommands.Add(command);
        return command;
    }

    #endregion

    #region Lifecycle / import

    /// <summary>Locate the history folder (saved setting, then next to the exe, then ask) and start importing.</summary>
    public async Task InitializeAsync()
    {
        var dir = _settings.HistoryDir is { } saved && Directory.Exists(saved) ? saved : HistoryLocator.FindDefault();
        if (dir is null)
        {
            var chosen = _pickFolder();
            dir = chosen is null ? null : HistoryLocator.ResolveChosenFolder(chosen);
        }
        if (dir is null)
        {
            StatusMessage = "history フォルダが選択されていません。ファイル > フォルダを開く から選んでください";
            return;
        }
        await OpenHistoryDirAsync(dir);
    }

    public async Task OpenHistoryDirAsync(string dir)
    {
        CloseStore();
        _historyDir = Path.GetFullPath(dir);
        _settings.HistoryDir = _historyDir;
        _settings.Save();
        var dbPath = HistoryStore.DefaultDbPath(_historyDir);
        try
        {
            _store = await Task.Run(() => new HistoryStore(dbPath));
        }
        catch (Exception ex)
        {
            StatusMessage = "viewer.db を開けませんでした: " + ex.Message;
            return;
        }
        DbPath = dbPath;
        WatchedFile = HistoryLocator.PlaylogPath(_historyDir);
        _firstLoadDone = false;
        try
        {
            _watcher = new PlaylogWatcher(_historyDir, HistoryLocator.FileName);
            _watcher.Changed += OnPlaylogChanged;
        }
        catch (Exception ex)
        {
            StatusMessage = "ファイルの監視を開始できませんでした: " + ex.Message;
        }
        foreach (var command in _storeCommands)
        {
            command.RaiseCanExecuteChanged();
        }
        await ImportAsync();
    }

    void OnPlaylogChanged() => _ui.Post(_ => _ = ImportAsync(), null);

    async Task ChooseFolderAsync()
    {
        var chosen = _pickFolder();
        if (chosen is null)
        {
            return;
        }
        await OpenHistoryDirAsync(HistoryLocator.ResolveChosenFolder(chosen));
    }

    async Task RebuildAsync()
    {
        if (_store is null)
        {
            return;
        }
        if (!_confirm("全件取り込み直し", "viewer.db から playlog.jsonl 由来のデータを削除し、ファイルを最初から取り込み直します。\n（playlog.jsonl 自体は変更しません）\n\n実行しますか？"))
        {
            return;
        }
        await ImportAsync(reset: true);
    }

    /// <summary>Incremental import of playlog.jsonl. Runs at most once at a time; a request made meanwhile is queued.</summary>
    async Task ImportAsync(bool reset = false)
    {
        var store = _store;
        var historyDir = _historyDir;
        if (store is null || historyDir is null)
        {
            return;
        }
        if (!await _importGate.WaitAsync(0))
        {
            _importQueued = true;
            return;
        }
        try
        {
            IsImporting = true;
            ProgressText = "取り込み中...";
            var path = HistoryLocator.PlaylogPath(historyDir);
            var progress = new Progress<ImportProgress>(p => ProgressText = $"取り込み中... {p.Lines:N0} 行");
            ImportResult result;
            try
            {
                result = await Task.Run(() =>
                {
                    if (reset)
                    {
                        store.ResetImport(path);
                    }
                    return store.ImportPlaylog(path, progress);
                });
                SkippedLines = (await Task.Run(() => store.GetImportState(path)))?.Skipped ?? 0;
            }
            catch (Exception ex)
            {
                StatusMessage = "取り込みに失敗しました: " + ex.Message;
                return;
            }
            LastImportText = DateTime.Now.ToString("HH:mm:ss");
            if (!result.FileFound)
            {
                StatusMessage = "playlog.jsonl がまだありません。Reflux でプレーすると作成されます";
            }
            else if (result.Inserted > 0)
            {
                StatusMessage = $"{result.Inserted:N0} 件のプレーを取り込みました" + (result.Rebuilt ? "（全件取り込み直し）" : "") + (result.Skipped > 0 ? $"、{result.Skipped:N0} 行を読み飛ばしました" : "");
            }
            else if (result.Rebuilt)
            {
                StatusMessage = "取り込み直しました";
            }
            bool affectsSelection = SelectedChart is { } selected && result.ChartIds.Contains(selected.Id);
            if (affectsSelection && result.FirstPlayId is long first)
            {
                _highlightFrom = first;
            }
            if (result.Changed || !_firstLoadDone)
            {
                _firstLoadDone = true;
                await RefreshChartsAsync(reloadDetail: affectsSelection || result.Rebuilt);
                if (SelectedTab == 1)
                {
                    await LoadDailyAsync();
                }
            }
        }
        finally
        {
            IsImporting = false;
            ProgressText = "";
            _importGate.Release();
            if (_importQueued)
            {
                _importQueued = false;
                _ = ImportAsync();
            }
        }
    }

    /// <summary>Copy view state that belongs to the settings file (the window saves its own bounds).</summary>
    public void SaveState()
    {
        _settings.LastChartKey = SelectedChart?.Key;
        _settings.NewestFirst = NewestFirst;
    }

    void CloseStore()
    {
        if (_watcher is not null)
        {
            _watcher.Changed -= OnPlaylogChanged;
            _watcher.Dispose();
            _watcher = null;
        }
        var store = _store;
        _store = null;
        if (store is not null)
        {
            _ = Task.Run(store.Dispose); /* may have to wait for a running import; keep the UI thread free */
        }
        _suppressSelection = true;
        Charts = Array.Empty<ChartSummary>();
        SelectedChart = null;
        _suppressSelection = false;
        DetailChart = null;
        _chronological = Array.Empty<PlayRow>();
        Plays = Array.Empty<PlayItem>();
        Graph = null;
        Daily = Array.Empty<DailySummary>();
    }

    public void Dispose()
    {
        _searchDebounce?.Cancel();
        _watcher?.Dispose();
        _watcher = null;
        _store?.Dispose();
        _store = null;
    }

    #endregion
}
