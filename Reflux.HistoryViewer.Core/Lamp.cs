namespace Reflux.HistoryViewer.Core;

/// <summary>
/// Clear lamps in the same order as Reflux's own Lamp enum. The numeric value is stored as plays.lamp_rank
/// so that "best lamp" can be computed with MAX() in SQL.
/// </summary>
public enum Lamp
{
    NP = 0,
    F = 1,
    AC = 2,
    EC = 3,
    NC = 4,
    HC = 5,
    EX = 6,
    FC = 7,
    PFC = 8,
}

public static class LampInfo
{
    static readonly Dictionary<string, int> ranks = Enum.GetValues<Lamp>().ToDictionary(l => l.ToString(), l => (int)l, StringComparer.Ordinal);

    /// <summary>All lamp codes in rank order (NP ... PFC).</summary>
    public static IReadOnlyList<string> Codes { get; } = Enum.GetValues<Lamp>().Select(l => l.ToString()).ToArray();

    /// <summary>Rank (0..8) for a lamp code such as "HC", or null for unknown / null input.</summary>
    public static int? Rank(string? code) => code is not null && ranks.TryGetValue(code, out var r) ? r : null;

    /// <summary>Lamp code for a rank produced by <see cref="Rank"/> (or MAX(lamp_rank) in SQL).</summary>
    public static string? Code(long? rank) => rank is >= 0 and <= 8 ? ((Lamp)rank.Value).ToString() : null;

    /// <summary>Human readable text, same wording as Reflux's PlayData.ExpandLamp().</summary>
    public static string? Text(string? code) => code switch
    {
        "NP" => "NO PLAY",
        "F" => "FAILED",
        "AC" => "ASSIST CLEAR",
        "EC" => "EASY CLEAR",
        "NC" => "CLEAR",
        "HC" => "HARD CLEAR",
        "EX" => "EX HARD CLEAR",
        "FC" => "FULL COMBO",
        "PFC" => "FULL COMBO",
        _ => code,
    };

    /// <summary>
    /// Reverse of <see cref="Text"/>, used when importing sessions/*.json which only has the expanded wording.
    /// "FULL COMBO" maps to FC. Unknown text yields null.
    /// </summary>
    public static string? CodeFromText(string? text) => text?.Trim().ToUpperInvariant() switch
    {
        "NO PLAY" => "NP",
        "FAILED" => "F",
        "ASSIST CLEAR" => "AC",
        "EASY CLEAR" => "EC",
        "CLEAR" => "NC",
        "HARD CLEAR" => "HC",
        "EX HARD CLEAR" => "EX",
        "FULL COMBO" => "FC",
        _ => null,
    };
}
