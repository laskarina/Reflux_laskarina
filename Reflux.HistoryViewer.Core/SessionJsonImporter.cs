using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Reflux.HistoryViewer.Core;

/// <summary>
/// Converts sessions/Session_*.json files (written by Reflux's PlayData.GetJsonEntry() when savejson = true)
/// into <see cref="PlayRecord"/>s. The file looks like {"head": {...}, "body": [ {play}, ... ]}.
/// These entries carry no song id, level, note count, options or game version; those stay null.
/// Note that hitMeta.gauge in that format is the gauge percentage at the end of the play (an integer),
/// not the gauge type.
/// </summary>
public static class SessionJsonImporter
{
    public sealed record ParseResult(IReadOnlyList<PlayRecord> Records, int Skipped);

    /// <summary>Reads and converts one file. Throws <see cref="JsonException"/> when the file is not a Reflux session file.</summary>
    public static ParseResult ParseFile(string path) => Parse(File.ReadAllText(path));

    public static ParseResult Parse(string json)
    {
        JObject root;
        using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None })
        {
            root = JObject.Load(reader);
        }
        if (root["body"] is not JArray body)
        {
            throw new JsonException("Not a Reflux session file: \"body\" array not found");
        }
        var records = new List<PlayRecord>(body.Count);
        int skipped = 0;
        foreach (var token in body)
        {
            var record = token is JObject entry ? Convert(entry) : null;
            if (record is null)
            {
                skipped++;
            }
            else
            {
                records.Add(record);
            }
        }
        return new ParseResult(records, skipped);
    }

    /// <summary>One body entry to a record, or null when title, timestamp or difficulty are unusable.</summary>
    public static PlayRecord? Convert(JObject entry)
    {
        var title = PlaylogParser.Str(entry["identifier"]);
        var difficulty = DifficultyCode(PlaylogParser.Str(entry["playtype"]), PlaylogParser.Str(entry["difficulty"]));
        var recordedAt = Timestamp(entry["timeAchieved"]);
        if (string.IsNullOrWhiteSpace(title) || difficulty is null || recordedAt is null)
        {
            return null;
        }
        var hit = entry["hitData"] as JObject;
        var meta = entry["hitMeta"] as JObject;
        var gauge = meta?["gauge"];
        return new PlayRecord
        {
            RecordedAt = recordedAt,
            Difficulty = difficulty,
            Title = title,
            ExScore = PlaylogParser.Int(entry["score"]),
            Lamp = LampInfo.CodeFromText(PlaylogParser.Str(entry["lamp"])),
            PGreat = PlaylogParser.Int(hit?["pgreat"]),
            Great = PlaylogParser.Int(hit?["great"]),
            Good = PlaylogParser.Int(hit?["good"]),
            Bad = PlaylogParser.Int(hit?["bad"]),
            Poor = PlaylogParser.Int(hit?["poor"]),
            Fast = PlaylogParser.Int(meta?["fast"]),
            Slow = PlaylogParser.Int(meta?["slow"]),
            ComboBreak = PlaylogParser.Int(meta?["comboBreak"]),
            GaugePercent = gauge is { Type: JTokenType.Integer or JTokenType.Float } ? PlaylogParser.Int(gauge) : null,
            GaugeType = gauge is { Type: JTokenType.String } ? PlaylogParser.Str(gauge) : null,
            MissCount = PlaylogParser.Int(meta?["bp"]),
            DataAvailable = true,
            Source = PlayRecord.SessionJsonSource,
        };
    }

    /// <summary>"SP" + "ANOTHER" to "SPA" etc.; null for anything else.</summary>
    public static string? DifficultyCode(string? playtype, string? difficulty)
    {
        var side = playtype?.Trim().ToUpperInvariant();
        if (side is not ("SP" or "DP"))
        {
            return null;
        }
        var letter = difficulty?.Trim().ToUpperInvariant() switch
        {
            "BEGINNER" => "B",
            "NORMAL" => "N",
            "HYPER" => "H",
            "ANOTHER" => "A",
            "LEGGENDARIA" => "L",
            _ => null,
        };
        return letter is null ? null : side + letter;
    }

    /// <summary>timeAchieved is UNIX time in milliseconds; an ISO string is accepted too.</summary>
    static string? Timestamp(JToken? token)
    {
        if (token is null)
        {
            return null;
        }
        try
        {
            switch (token.Type)
            {
                case JTokenType.Integer:
                    return FromUnixMilliseconds((long)token);
                case JTokenType.Float:
                    return FromUnixMilliseconds((long)Math.Round((double)token));
                case JTokenType.String:
                    var text = (string?)token;
                    return long.TryParse(text, out var ms) ? FromUnixMilliseconds(ms) : PlaylogParser.NormalizeTimestamp(text);
                default:
                    return null;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    static string? FromUnixMilliseconds(long ms) => ms <= 0 ? null : PlaylogParser.FormatTimestamp(DateTimeOffset.FromUnixTimeMilliseconds(ms));
}
