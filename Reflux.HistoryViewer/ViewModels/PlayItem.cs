using Reflux.HistoryViewer.Core;

namespace Reflux.HistoryViewer.ViewModels;

/// <summary>One row of the play list of the selected chart, with display helpers for the DataGrid.</summary>
public sealed class PlayItem
{
    public PlayItem(PlayRow row, int index, bool isNew)
    {
        Row = row;
        Index = index;
        IsNew = isNew;
    }

    public PlayRow Row { get; }
    /// <summary>1-based chronological play number (also the x value in the graphs).</summary>
    public int Index { get; }
    /// <summary>True for rows added by the last live import while this chart was displayed.</summary>
    public bool IsNew { get; }

    public DateTime? RecordedAtLocal => Row.RecordedAtLocal;
    public bool DataAvailable => Row.DataAvailable;
    public string UnavailableMark => Row.DataAvailable ? "" : "※";
    public int? ExScore => Row.ExScore;
    public string? Grade => Row.Grade;
    /// <summary>Empty when the play data was not available (H-RAN, BATTLE, assist).</summary>
    public string? LampText => Row.DataAvailable ? Row.LampText : null;
    public int? MissCount => Row.MissCount;
    public string? GaugeType => Row.GaugeType;
    public int? GaugePercent => Row.GaugePercent;
    public string StyleText => string.IsNullOrEmpty(Row.Style2) ? Row.Style ?? "" : $"{Row.Style} / {Row.Style2}";
    public string? Assist => Row.Assist;
    public string? Range => Row.Range;
    public string FlipMark => Mark(Row.Flip);
    public string BattleMark => Mark(Row.Battle);
    public string HranMark => Mark(Row.Hran);
    public int? PGreat => Row.PGreat;
    public int? Great => Row.Great;
    public int? Good => Row.Good;
    public int? Bad => Row.Bad;
    public int? Poor => Row.Poor;
    public int? ComboBreak => Row.ComboBreak;
    public int? Fast => Row.Fast;
    public int? Slow => Row.Slow;
    public string PrematureMark => Mark(Row.PrematureEnd);
    public string? PlaySide => Row.PlaySide;
    public string SourceText => Row.Source == PlayRecord.SessionJsonSource ? "旧JSON" : "";
    public string? GameVersion => Row.GameVersion;

    static string Mark(bool? value) => value == true ? "●" : "";
}
