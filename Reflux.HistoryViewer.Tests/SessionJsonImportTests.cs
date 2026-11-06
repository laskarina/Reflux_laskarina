using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reflux.HistoryViewer.Core;
using Xunit;

namespace Reflux.HistoryViewer.Tests;

public class SessionJsonImportTests : IDisposable
{
    readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    static readonly long ExampleMs = new DateTimeOffset(2026, 11, 6, 12, 34, 56, TimeSpan.Zero).ToUnixTimeMilliseconds();

    /// <summary>An entry exactly as Reflux's PlayData.GetJsonEntry() writes it.</summary>
    static JObject Entry(string title = "Sleepless Days", string playtype = "SP", string difficulty = "ANOTHER", long? timeMs = null, int score = 3412, string lamp = "HARD CLEAR", int? bp = 24, JToken? gauge = null)
    {
        var meta = new JObject { ["fast"] = 310, ["slow"] = 352, ["comboBreak"] = 21, ["gauge"] = gauge ?? new JValue(86) };
        if (bp is int b)
        {
            meta["bp"] = b;
        }
        return new JObject
        {
            ["score"] = score,
            ["lamp"] = lamp,
            ["matchType"] = "title",
            ["identifier"] = title,
            ["playtype"] = playtype,
            ["difficulty"] = difficulty,
            ["timeAchieved"] = timeMs ?? ExampleMs,
            ["hitData"] = new JObject { ["pgreat"] = 1420, ["great"] = 572, ["good"] = 80, ["bad"] = 10, ["poor"] = 14 },
            ["hitMeta"] = meta,
        };
    }

    static string SessionFile(params JToken[] entries) =>
        new JObject { ["head"] = new JObject { ["service"] = "Infinitas", ["game"] = "iidx" }, ["body"] = new JArray(entries) }.ToString();

    HistoryStore OpenStore() => new(HistoryStore.DefaultDbPath(_dir.Path));

    [Fact]
    public void ConvertsSessionEntriesIntoPlays()
    {
        var path = _dir.Write("Session_2026_11_06_21_00_00.json", SessionFile(
            Entry(),
            Entry(title: "Broken Sword", playtype: "DP", difficulty: "HYPER", timeMs: ExampleMs + 600_000, score: 2000, lamp: "FULL COMBO", bp: null)));
        using var store = OpenStore();

        var result = store.ImportSessionJson(path);
        Assert.True(result.FileFound);
        Assert.False(result.AlreadyImported);
        Assert.Equal(2, result.Inserted);
        Assert.Equal(0, result.Skipped);

        var charts = store.QueryCharts();
        Assert.Equal(2, charts.Count);
        Assert.All(charts, c => Assert.Null(c.SongId));
        var spa = Assert.Single(charts, c => c.Difficulty == "SPA");
        Assert.Equal("Sleepless Days", spa.Title);
        Assert.Null(spa.Level);
        var play = Assert.Single(store.QueryPlays(spa.Id));
        Assert.Equal("2026-11-06T12:34:56Z", play.RecordedAt);
        Assert.Equal(3412, play.ExScore);
        Assert.Equal("HC", play.Lamp);
        Assert.Equal("HARD CLEAR", play.LampText);
        Assert.Equal(24, play.MissCount);
        Assert.Equal(86, play.GaugePercent); /* hitMeta.gauge is the gauge percentage in that format */
        Assert.Null(play.GaugeType);
        Assert.Equal(1420, play.PGreat);
        Assert.Equal(14, play.Poor);
        Assert.Equal(21, play.ComboBreak);
        Assert.Equal(310, play.Fast);
        Assert.Equal(352, play.Slow);
        Assert.Null(play.Grade);
        Assert.Null(play.Style);
        Assert.Null(play.PrematureEnd);
        Assert.Null(play.GameVersion);
        Assert.True(play.DataAvailable);
        Assert.Equal(PlayRecord.SessionJsonSource, play.Source);

        var dph = Assert.Single(charts, c => c.Difficulty == "DPH");
        var dpPlay = Assert.Single(store.QueryPlays(dph.Id));
        Assert.Equal("FC", dpPlay.Lamp);
        Assert.Null(dpPlay.MissCount);
        Assert.Equal("2026-11-06T12:44:56Z", dpPlay.RecordedAt);

        var state = store.GetImportState(path);
        Assert.NotNull(state);
        Assert.Equal(HistoryStore.SessionJsonKind, state.Kind);
    }

    [Fact]
    public void TheSameFileIsNeverImportedTwice()
    {
        var path = _dir.Write("Session_a.json", SessionFile(Entry()));
        using var store = OpenStore();
        Assert.Equal(1, store.ImportSessionJson(path).Inserted);

        var again = store.ImportSessionJson(path);
        Assert.True(again.AlreadyImported);
        Assert.Equal(0, again.Inserted);
        Assert.Equal(1, store.GetStats().Plays);

        /* a different file with the same content is a different session and is imported */
        var other = _dir.Write("Session_b.json", SessionFile(Entry()));
        Assert.Equal(1, store.ImportSessionJson(other).Inserted);
        Assert.Equal(2, store.GetStats().Plays);
    }

    [Fact]
    public void SessionRowsJoinPlaylogRowsByTitleAndDifficulty()
    {
        var playlog = _dir.Write("playlog.jsonl", Lines.Play(songId: "25094", title: "Sleepless Days", difficulty: "SPA", recordedAt: "2026-11-06T12:34:56Z"));
        var session = _dir.Write("Session_old.json", SessionFile(
            Entry(timeMs: new DateTimeOffset(2025, 3, 1, 10, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), score: 3000, lamp: "CLEAR"),
            Entry(title: "Sleepless Days", difficulty: "HYPER", timeMs: new DateTimeOffset(2025, 3, 1, 10, 5, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), score: 2500, lamp: "EASY CLEAR")));
        using var store = OpenStore();
        store.ImportPlaylog(playlog);
        store.ImportSessionJson(session);

        var charts = store.QueryCharts();
        Assert.Equal(2, charts.Count);
        var spa = Assert.Single(charts, c => c.Difficulty == "SPA");
        Assert.Equal("25094", spa.SongId);
        Assert.Equal(12, spa.Level);
        Assert.Equal(2, spa.PlayCount);
        Assert.Equal(3412, spa.BestExScore);
        var plays = store.QueryPlays(spa.Id, newestFirst: false);
        Assert.Equal(PlayRecord.SessionJsonSource, plays[0].Source);
        Assert.Equal(PlayRecord.PlaylogSource, plays[1].Source);
        Assert.Null(Assert.Single(charts, c => c.Difficulty == "SPH").SongId);

        /* the opposite order (sessions first, then the playlog) merges too and upgrades the chart with the song id */
        using var store2 = new HistoryStore(_dir.File("other.db"));
        store2.ImportSessionJson(session);
        store2.ImportPlaylog(playlog);
        var spa2 = Assert.Single(store2.QueryCharts(), c => c.Difficulty == "SPA");
        Assert.Equal("25094", spa2.SongId);
        Assert.Equal(2, spa2.PlayCount);
    }

    [Fact]
    public void BrokenEntriesAreSkippedAndCounted()
    {
        var missingTitle = Entry();
        missingTitle.Remove("identifier");
        var path = _dir.Write("Session_x.json", SessionFile(
            Entry(),
            missingTitle,
            Entry(difficulty: "ULTIMATE"),
            Entry(playtype: "TP"),
            new JValue("not an object"),
            Entry(timeMs: 0)));
        using var store = OpenStore();
        var result = store.ImportSessionJson(path);
        Assert.Equal(1, result.Inserted);
        Assert.Equal(5, result.Skipped);
        Assert.Equal(5, store.GetImportState(path)!.Skipped);
    }

    [Fact]
    public void FilesThatAreNotSessionFilesThrowAndChangeNothing()
    {
        using var store = OpenStore();
        Assert.Throws<JsonException>(() => store.ImportSessionJson(_dir.Write("a.json", "{\"foo\": 1}")));
        Assert.ThrowsAny<JsonException>(() => store.ImportSessionJson(_dir.Write("b.json", "not json at all")));
        Assert.False(store.ImportSessionJson(_dir.File("missing.json")).FileFound);
        Assert.Equal(0, store.GetStats().Plays);
        Assert.Empty(store.GetImportStates());
    }

    [Fact]
    public void GaugeStringIsKeptAsGaugeType()
    {
        var path = _dir.Write("Session_g.json", SessionFile(Entry(gauge: new JValue("HARD"))));
        using var store = OpenStore();
        store.ImportSessionJson(path);
        var play = Assert.Single(store.QueryPlays(store.QueryCharts()[0].Id));
        Assert.Equal("HARD", play.GaugeType);
        Assert.Null(play.GaugePercent);
    }

    [Theory]
    [InlineData("SP", "BEGINNER", "SPB")]
    [InlineData("SP", "NORMAL", "SPN")]
    [InlineData("sp", "hyper", "SPH")]
    [InlineData("SP", "ANOTHER", "SPA")]
    [InlineData("SP", "LEGGENDARIA", "SPL")]
    [InlineData("DP", "ANOTHER", "DPA")]
    [InlineData("DP", "LEGGENDARIA", "DPL")]
    [InlineData("SP", "BLACK ANOTHER", null)]
    [InlineData("TP", "ANOTHER", null)]
    [InlineData(null, "ANOTHER", null)]
    public void DifficultyCodesAreDerivedFromPlaytypeAndDifficulty(string? playtype, string? difficulty, string? expected)
    {
        Assert.Equal(expected, SessionJsonImporter.DifficultyCode(playtype, difficulty));
    }

    [Theory]
    [InlineData("FAILED", "F")]
    [InlineData("ASSIST CLEAR", "AC")]
    [InlineData("EASY CLEAR", "EC")]
    [InlineData("CLEAR", "NC")]
    [InlineData("HARD CLEAR", "HC")]
    [InlineData("EX HARD CLEAR", "EX")]
    [InlineData("FULL COMBO", "FC")]
    public void LampWordingMapsBackToCodes(string text, string code)
    {
        var record = SessionJsonImporter.Convert(Entry(lamp: text));
        Assert.NotNull(record);
        Assert.Equal(code, record.Lamp);
    }
}
