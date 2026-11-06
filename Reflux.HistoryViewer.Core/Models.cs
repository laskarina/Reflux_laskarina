namespace Reflux.HistoryViewer.Core;

/// <summary>One row of the song list: a chart (song x difficulty) with aggregates over its plays.</summary>
public sealed record ChartSummary(
    long Id,
    string? SongId,
    string Difficulty,
    string? Title,
    string? TitleEnglish,
    string? Artist,
    string? Genre,
    string? Bpm,
    int? Level,
    int? TotalNotes,
    int PlayCount,
    int? BestExScore,
    int? BestLampRank,
    int? MinMissCount,
    string? LastPlayedAt)
{
    public string? BestLamp => LampInfo.Code(BestLampRank);
    public string? BestLampText => LampInfo.Text(BestLamp);
    public DateTime? LastPlayedLocal => PlaylogParser.ToLocalTime(LastPlayedAt);
    /// <summary>"SP" or "DP"</summary>
    public string Side => Difficulty.Length >= 2 ? Difficulty[..2] : Difficulty;
    /// <summary>Stable key for remembering the selected chart across runs.</summary>
    public string Key => (SongId ?? "title:" + Title) + "|" + Difficulty;
}

/// <summary>One play as shown in the chart detail list.</summary>
public sealed record PlayRow(
    long Id,
    long ChartId,
    string RecordedAt,
    string? PlaySide,
    bool DataAvailable,
    int? ExScore,
    string? Grade,
    string? Lamp,
    int? MissCount,
    string? GaugeType,
    int? GaugePercent,
    int? PGreat,
    int? Great,
    int? Good,
    int? Bad,
    int? Poor,
    int? ComboBreak,
    int? Fast,
    int? Slow,
    string? Style,
    string? Style2,
    string? Assist,
    string? Range,
    bool? Flip,
    bool? Battle,
    bool? Hran,
    bool? PrematureEnd,
    string? GameVersion,
    string Source)
{
    public DateTime? RecordedAtLocal => PlaylogParser.ToLocalTime(RecordedAt);
    public string? LampText => LampInfo.Text(Lamp);
}

/// <summary>Filters for the song list. Every member is optional; all of them end up in the SQL WHERE / HAVING clause.</summary>
public sealed class ChartFilter
{
    public static readonly ChartFilter None = new();

    /// <summary>Case-insensitive substring match against title and title_english.</summary>
    public string? TitleContains { get; init; }
    public int? Level { get; init; }
    /// <summary>"SP" or "DP"</summary>
    public string? Side { get; init; }
    /// <summary>"B", "N", "H", "A" or "L"</summary>
    public string? DifficultyLetter { get; init; }
    /// <summary>Best lamp rank (see <see cref="LampInfo.Rank"/>) must equal this value.</summary>
    public int? BestLampRank { get; init; }
    /// <summary>Only plays recorded at or after this UTC timestamp ("yyyy-MM-ddTHH:mm:ssZ") are considered.</summary>
    public string? FromUtc { get; init; }
    /// <summary>Only plays recorded before this UTC timestamp are considered.</summary>
    public string? ToUtc { get; init; }
}

/// <summary>Per-file import bookkeeping (table import_state).</summary>
public sealed record ImportState(long Id, string Path, string Kind, long Offset, long Size, long Skipped, string UpdatedAt);

/// <summary>Outcome of one import run.</summary>
public sealed record ImportResult(
    string Path,
    bool FileFound,
    int Inserted,
    int Skipped,
    bool Rebuilt,
    long Offset,
    long? FirstPlayId,
    long? LastPlayId,
    IReadOnlySet<long> ChartIds)
{
    public static ImportResult NotFound(string path) => new(path, false, 0, 0, false, 0, null, null, new HashSet<long>());
    public bool Changed => Inserted > 0 || Rebuilt;
}

public readonly record struct ImportProgress(int Lines, long BytesDone, long BytesTotal);

/// <summary>Plays per local day and how many of them set a new EX score best for their chart.</summary>
public sealed record DailySummary(string Day, int Plays, int NewBests);

public sealed record StoreStats(int Charts, int Plays, long SkippedLines);
