namespace Reflux.HistoryViewer.Core;

/// <summary>
/// One play, as read from a history/playlog.jsonl line (schema_version 1) or converted from sessions/*.json.
/// Everything except the timestamp and the difficulty may be missing and is then null.
/// </summary>
public sealed class PlayRecord
{
    public const string PlaylogSource = "playlog";
    public const string SessionJsonSource = "session_json";

    /// <summary>UTC timestamp, always normalized to "yyyy-MM-ddTHH:mm:ssZ" so that string order is time order.</summary>
    public required string RecordedAt { get; init; }
    /// <summary>SPB ... DPL</summary>
    public required string Difficulty { get; init; }

    public string? GameVersion { get; init; }
    public string? RefluxVersion { get; init; }
    public string? SongId { get; init; }
    public string? Title { get; init; }
    public string? TitleEnglish { get; init; }
    public string? Artist { get; init; }
    public string? Genre { get; init; }
    public string? Bpm { get; init; }
    public int? Level { get; init; }
    public int? TotalNotes { get; init; }
    public string? PlaySide { get; init; }
    public bool DataAvailable { get; init; } = true;
    public int? ExScore { get; init; }
    public string? Grade { get; init; }
    public string? Lamp { get; init; }
    public int? MissCount { get; init; }
    public string? GaugeType { get; init; }
    public int? GaugePercent { get; init; }
    public int? PGreat { get; init; }
    public int? Great { get; init; }
    public int? Good { get; init; }
    public int? Bad { get; init; }
    public int? Poor { get; init; }
    public int? ComboBreak { get; init; }
    public int? Fast { get; init; }
    public int? Slow { get; init; }
    public string? Style { get; init; }
    public string? Style2 { get; init; }
    public string? Assist { get; init; }
    public string? Range { get; init; }
    public bool? Flip { get; init; }
    public bool? Battle { get; init; }
    public bool? Hran { get; init; }
    public bool? PrematureEnd { get; init; }
    public string Source { get; init; } = PlaylogSource;
}
