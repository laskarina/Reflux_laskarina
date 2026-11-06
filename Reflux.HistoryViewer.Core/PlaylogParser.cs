using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Reflux.HistoryViewer.Core;

/// <summary>
/// Parses one line of history/playlog.jsonl. Unknown keys are ignored and missing keys become null,
/// so newer schema versions that only add keys still load. A line that is not a JSON object, or that
/// lacks a usable recorded_at / difficulty, is reported as broken (null).
/// </summary>
public static class PlaylogParser
{
    public static readonly IReadOnlyList<string> Difficulties = new[] { "SPB", "SPN", "SPH", "SPA", "SPL", "DPB", "DPN", "DPH", "DPA", "DPL" };

    const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public static PlayRecord? TryParse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }
        JObject obj;
        try
        {
            using var sr = new StringReader(line);
            using var reader = new JsonTextReader(sr) { DateParseHandling = DateParseHandling.None };
            if (!reader.Read() || reader.TokenType != JsonToken.StartObject)
            {
                return null;
            }
            obj = JObject.Load(reader);
            if (reader.Read())
            {
                return null; /* trailing content after the object: treat the line as broken */
            }
        }
        catch (JsonException)
        {
            return null;
        }

        var recordedAt = NormalizeTimestamp(Str(obj["recorded_at"]));
        var difficulty = Str(obj["difficulty"])?.Trim().ToUpperInvariant();
        if (recordedAt is null || difficulty is null || !Difficulties.Contains(difficulty))
        {
            return null;
        }
        var judge = obj["judge"] as JObject;
        var options = obj["options"] as JObject;
        var lamp = Str(obj["lamp"])?.Trim().ToUpperInvariant();
        return new PlayRecord
        {
            RecordedAt = recordedAt,
            Difficulty = difficulty,
            GameVersion = Str(obj["game_version"]),
            RefluxVersion = Str(obj["reflux_version"]),
            SongId = Str(obj["song_id"]),
            Title = Str(obj["title"]),
            TitleEnglish = Str(obj["title_english"]),
            Artist = Str(obj["artist"]),
            Genre = Str(obj["genre"]),
            Bpm = Str(obj["bpm"]),
            Level = Int(obj["level"]),
            TotalNotes = Int(obj["total_notes"]),
            PlaySide = Str(obj["play_side"]),
            DataAvailable = Bool(obj["data_available"]) ?? true,
            ExScore = Int(obj["ex_score"]),
            Grade = Str(obj["grade"]),
            Lamp = LampInfo.Rank(lamp) is null ? null : lamp,
            MissCount = Int(obj["miss_count"]),
            GaugeType = Str(obj["gauge_type"]),
            GaugePercent = Int(obj["gauge_percent"]),
            PGreat = Int(judge?["pgreat"]),
            Great = Int(judge?["great"]),
            Good = Int(judge?["good"]),
            Bad = Int(judge?["bad"]),
            Poor = Int(judge?["poor"]),
            ComboBreak = Int(judge?["combo_break"]),
            Fast = Int(judge?["fast"]),
            Slow = Int(judge?["slow"]),
            Style = Str(options?["style"]),
            Style2 = Str(options?["style2"]),
            Assist = Str(options?["assist"]),
            Range = Str(options?["range"]),
            Flip = Bool(options?["flip"]),
            Battle = Bool(options?["battle"]),
            Hran = Bool(options?["hran"]),
            PrematureEnd = Bool(obj["premature_end"]),
            Source = PlayRecord.PlaylogSource,
        };
    }

    /// <summary>Parse any ISO 8601 timestamp and return it as "yyyy-MM-ddTHH:mm:ssZ" (UTC), or null.</summary>
    public static string? NormalizeTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
        {
            return null;
        }
        return FormatTimestamp(dto);
    }

    public static string FormatTimestamp(DateTimeOffset value) => value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);

    public static string FormatTimestamp(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString(TimestampFormat, CultureInfo.InvariantCulture);

    /// <summary>Convert a stored UTC timestamp back to local time for display.</summary>
    public static DateTime? ToLocalTime(string? recordedAt)
    {
        if (recordedAt is null)
        {
            return null;
        }
        return DateTimeOffset.TryParse(recordedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto)
            ? dto.ToLocalTime().DateTime
            : null;
    }

    static string? Str(JToken? token)
    {
        if (token is null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
        {
            return null;
        }
        return token.Type == JTokenType.String ? (string?)token : token.ToString(Formatting.None);
    }

    static int? Int(JToken? token)
    {
        if (token is null)
        {
            return null;
        }
        switch (token.Type)
        {
            case JTokenType.Integer:
                var l = (long)token;
                return l is >= int.MinValue and <= int.MaxValue ? (int)l : null;
            case JTokenType.Float:
                return (int)Math.Round((double)token);
            case JTokenType.String:
                return int.TryParse((string?)token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
            default:
                return null;
        }
    }

    static bool? Bool(JToken? token)
    {
        if (token is null)
        {
            return null;
        }
        switch (token.Type)
        {
            case JTokenType.Boolean:
                return (bool)token;
            case JTokenType.Integer:
                return (long)token != 0;
            case JTokenType.String:
                return bool.TryParse((string?)token, out var b) ? b : null;
            default:
                return null;
        }
    }
}
