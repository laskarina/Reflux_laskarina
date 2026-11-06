using System.Diagnostics;
using System.Text;
using Reflux.HistoryViewer.Core;
using Xunit;
using Xunit.Abstractions;

namespace Reflux.HistoryViewer.Tests;

public class HistoryStoreTests : IDisposable
{
    readonly TempDir _dir = new();
    readonly ITestOutputHelper _output;

    public HistoryStoreTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Dispose() => _dir.Dispose();

    HistoryStore OpenStore() => new(HistoryStore.DefaultDbPath(_dir.Path));

    [Fact]
    public void ImportsEveryPlayAndAggregatesPerChart()
    {
        var path = _dir.Write("playlog.jsonl",
            Lines.Play(recordedAt: "2026-11-01T10:00:00Z", exScore: 3300, lamp: "NC", missCount: 40) +
            Lines.Play(recordedAt: "2026-11-02T10:00:00Z", exScore: 3412, lamp: "HC", missCount: 24) +
            Lines.Play(recordedAt: "2026-11-03T10:00:00Z", exScore: 3200, lamp: "F", missCount: null, prematureEnd: true) +
            Lines.Play(songId: "26071", title: "Verflucht", difficulty: "SPL", recordedAt: "2026-11-03T11:00:00Z", exScore: 2500, lamp: "EC", missCount: 90, playSide: "P2") +
            Lines.Play(songId: "25094", difficulty: "SPH", recordedAt: "2026-10-20T11:00:00Z", exScore: 1800, lamp: "FC", missCount: 0, level: 10, totalNotes: 1100));
        using var store = OpenStore();

        var result = store.ImportPlaylog(path);
        Assert.True(result.FileFound);
        Assert.Equal(5, result.Inserted);
        Assert.Equal(0, result.Skipped);
        Assert.False(result.Rebuilt);
        Assert.Equal(new FileInfo(path).Length, result.Offset);
        Assert.Equal(3, result.ChartIds.Count);

        var charts = store.QueryCharts();
        Assert.Equal(3, charts.Count);
        Assert.Equal("Verflucht", charts[0].Title); /* newest last play first */
        var sleepless = Assert.Single(charts, c => c.SongId == "25094" && c.Difficulty == "SPA");
        Assert.Equal(3, sleepless.PlayCount);
        Assert.Equal(3412, sleepless.BestExScore);
        Assert.Equal("HC", sleepless.BestLamp);
        Assert.Equal("HARD CLEAR", sleepless.BestLampText);
        Assert.Equal(24, sleepless.MinMissCount); /* null BP of the premature play is ignored */
        Assert.Equal("2026-11-03T10:00:00Z", sleepless.LastPlayedAt);
        Assert.Equal(12, sleepless.Level);
        Assert.Equal(2096, sleepless.TotalNotes);

        var plays = store.QueryPlays(sleepless.Id);
        Assert.Equal(3, plays.Count);
        Assert.Equal("2026-11-03T10:00:00Z", plays[0].RecordedAt);
        Assert.True(plays[0].PrematureEnd);
        Assert.Null(plays[0].MissCount);
        Assert.Equal("2026-11-01T10:00:00Z", store.QueryPlays(sleepless.Id, newestFirst: false)[0].RecordedAt);

        var header = store.GetChart(sleepless.Id);
        Assert.NotNull(header);
        Assert.Equal(3, header.PlayCount);

        var stats = store.GetStats();
        Assert.Equal(3, stats.Charts);
        Assert.Equal(5, stats.Plays);
        Assert.Equal(0, stats.SkippedLines);
    }

    [Fact]
    public void BrokenLinesAreSkippedAndCounted()
    {
        var path = _dir.Write("playlog.jsonl",
            Lines.Play(recordedAt: "2026-11-01T10:00:00Z") +
            "this is not json\n" +
            "{\"recorded_at\":\"2026-11-01T10:00:00Z\"}\n" +
            "[1,2]\n" +
            Lines.Play(recordedAt: "2026-11-02T10:00:00Z"));
        using var store = OpenStore();

        var result = store.ImportPlaylog(path);
        Assert.Equal(2, result.Inserted);
        Assert.Equal(3, result.Skipped);
        Assert.Equal(2, store.GetStats().Plays);
        Assert.Equal(3, store.GetImportState(path)!.Skipped);
        Assert.Equal(new FileInfo(path).Length, result.Offset); /* broken lines are consumed too */
    }

    [Fact]
    public void TrailingLineWithoutNewlineIsDeferredUntilComplete()
    {
        var full = Lines.Play(recordedAt: "2026-11-02T10:00:00Z");
        var head = full[..(full.Length / 2)];
        var path = _dir.Write("playlog.jsonl", Lines.Play(recordedAt: "2026-11-01T10:00:00Z") + head);
        using var store = OpenStore();

        var first = store.ImportPlaylog(path);
        Assert.Equal(1, first.Inserted);
        Assert.Equal(0, first.Skipped);
        Assert.Equal(Encoding.UTF8.GetByteCount(Lines.Play(recordedAt: "2026-11-01T10:00:00Z")), first.Offset);
        var state = store.GetImportState(path)!;
        Assert.Equal(new FileInfo(path).Length, state.Size);
        Assert.True(state.Offset < state.Size);

        _dir.Append("playlog.jsonl", full[(full.Length / 2)..]);
        var second = store.ImportPlaylog(path);
        Assert.Equal(1, second.Inserted);
        Assert.Equal(0, second.Skipped);
        Assert.Equal(2, store.GetStats().Plays);
        Assert.Equal(new FileInfo(path).Length, second.Offset);
    }

    [Fact]
    public void IncrementalImportOnlyAddsAppendedLines()
    {
        var path = _dir.Write("playlog.jsonl", Lines.Play(recordedAt: "2026-11-01T10:00:00Z") + Lines.Play(recordedAt: "2026-11-02T10:00:00Z"));
        using var store = OpenStore();
        Assert.Equal(2, store.ImportPlaylog(path).Inserted);

        var unchanged = store.ImportPlaylog(path);
        Assert.Equal(0, unchanged.Inserted);
        Assert.False(unchanged.Changed);

        _dir.Append("playlog.jsonl", Lines.Play(recordedAt: "2026-11-03T10:00:00Z", exScore: 3500));
        var result = store.ImportPlaylog(path);
        Assert.Equal(1, result.Inserted);
        Assert.False(result.Rebuilt);
        Assert.NotNull(result.FirstPlayId);
        Assert.Equal(result.FirstPlayId, result.LastPlayId);
        Assert.Equal(3, store.GetStats().Plays);
        Assert.Equal(3500, store.QueryCharts()[0].BestExScore);
    }

    [Fact]
    public void ShrunkFileIsReimportedFromScratch()
    {
        var path = _dir.Write("playlog.jsonl",
            Lines.Play(recordedAt: "2026-11-01T10:00:00Z") +
            Lines.Play(recordedAt: "2026-11-02T10:00:00Z") +
            Lines.Play(songId: "26071", title: "Verflucht", difficulty: "SPL", recordedAt: "2026-11-03T10:00:00Z"));
        using var store = OpenStore();
        Assert.Equal(3, store.ImportPlaylog(path).Inserted);
        Assert.Equal(2, store.GetStats().Charts);

        _dir.Write("playlog.jsonl", Lines.Play(recordedAt: "2026-11-05T10:00:00Z", exScore: 100));
        var result = store.ImportPlaylog(path);
        Assert.True(result.Rebuilt);
        Assert.Equal(1, result.Inserted);
        var stats = store.GetStats();
        Assert.Equal(1, stats.Plays);
        Assert.Equal(1, stats.Charts); /* the chart that lost all its plays is gone */
        Assert.Equal(100, store.QueryCharts()[0].BestExScore);
        Assert.Equal(0, store.GetImportState(path)!.Skipped);
    }

    [Fact]
    public void ResetImportDropsRowsAndReadsAgain()
    {
        var path = _dir.Write("playlog.jsonl", Lines.Play(recordedAt: "2026-11-01T10:00:00Z") + Lines.Play(recordedAt: "2026-11-02T10:00:00Z"));
        using var store = OpenStore();
        Assert.Equal(2, store.ImportPlaylog(path).Inserted);
        store.ResetImport(path);
        Assert.Equal(0, store.GetStats().Plays);
        Assert.Null(store.GetImportState(path));
        Assert.Equal(2, store.ImportPlaylog(path).Inserted);
        Assert.Equal(1, store.GetStats().Charts);
    }

    [Fact]
    public void RowsWithoutSongIdAreGroupedByTitleAndDifficulty()
    {
        var path = _dir.Write("playlog.jsonl",
            Lines.Play(songId: null, title: "Sleepless Days", difficulty: "SPA", recordedAt: "2025-01-01T10:00:00Z", exScore: 3000, level: null, totalNotes: null) +
            Lines.Play(songId: null, title: "Sleepless Days", difficulty: "SPA", recordedAt: "2025-01-02T10:00:00Z", exScore: 3100, level: null, totalNotes: null) +
            Lines.Play(songId: null, title: "Sleepless Days", difficulty: "SPH", recordedAt: "2025-01-03T10:00:00Z", exScore: 1500, level: null, totalNotes: null) +
            Lines.Play(songId: "25094", title: "Sleepless Days", difficulty: "SPA", recordedAt: "2026-11-01T10:00:00Z", exScore: 3412) +
            Lines.Play(songId: null, title: "Sleepless Days", difficulty: "SPA", recordedAt: "2026-11-02T10:00:00Z", exScore: 3200, level: null, totalNotes: null));
        using var store = OpenStore();
        Assert.Equal(5, store.ImportPlaylog(path).Inserted);

        var charts = store.QueryCharts();
        Assert.Equal(2, charts.Count);
        var spa = Assert.Single(charts, c => c.Difficulty == "SPA");
        Assert.Equal("25094", spa.SongId); /* chart created from title-only rows was upgraded when the song id arrived */
        Assert.Equal(4, spa.PlayCount);
        Assert.Equal(12, spa.Level);
        Assert.Equal(3412, spa.BestExScore);
        var sph = Assert.Single(charts, c => c.Difficulty == "SPH");
        Assert.Null(sph.SongId);
        Assert.Equal(1, sph.PlayCount);
    }

    [Fact]
    public void SongIdIsTheIdentityEvenWhenTheTitleChanges()
    {
        var path = _dir.Write("playlog.jsonl",
            Lines.Play(songId: "25094", title: "Sleepless Days", recordedAt: "2026-11-01T10:00:00Z") +
            Lines.Play(songId: "25094", title: "Sleepless Days (renamed)", recordedAt: "2026-11-02T10:00:00Z") +
            Lines.Play(songId: "99999", title: "Sleepless Days", recordedAt: "2026-11-03T10:00:00Z"));
        using var store = OpenStore();
        store.ImportPlaylog(path);
        var charts = store.QueryCharts();
        Assert.Equal(2, charts.Count);
        var renamed = Assert.Single(charts, c => c.SongId == "25094");
        Assert.Equal(2, renamed.PlayCount);
        Assert.Equal("Sleepless Days (renamed)", renamed.Title); /* latest title wins */
        Assert.Equal(1, Assert.Single(charts, c => c.SongId == "99999").PlayCount);
    }

    [Fact]
    public void FiltersAreAppliedInSql()
    {
        var path = _dir.Write("playlog.jsonl",
            Lines.Play(songId: "25094", title: "Sleepless Days", difficulty: "SPA", recordedAt: "2026-11-01T10:00:00Z", lamp: "HC", level: 12) +
            Lines.Play(songId: "25094", title: "Sleepless Days", difficulty: "SPA", recordedAt: "2026-11-02T10:00:00Z", lamp: "EX", level: 12) +
            Lines.Play(songId: "26071", title: "Verflucht", difficulty: "SPL", recordedAt: "2026-10-01T10:00:00Z", lamp: "F", level: 12) +
            Lines.Play(songId: "20035", title: "100% minimoo-G", difficulty: "DPA", recordedAt: "2026-09-01T10:00:00Z", lamp: "NC", level: 10, playSide: "DP", style2: "MIRROR") +
            Lines.Play(songId: "12004", title: "Colorful Cookie", difficulty: "SPH", recordedAt: "2026-11-03T10:00:00Z", lamp: null, dataAvailable: false, missCount: null, level: 8));
        using var store = OpenStore();
        store.ImportPlaylog(path);

        Assert.Equal(4, store.QueryCharts().Count);
        Assert.Equal(new[] { "Sleepless Days" }, store.QueryCharts(new ChartFilter { TitleContains = "sleep" }).Select(c => c.Title));
        Assert.Equal(new[] { "100% minimoo-G" }, store.QueryCharts(new ChartFilter { TitleContains = "100%" }).Select(c => c.Title)); /* % is escaped */
        Assert.Empty(store.QueryCharts(new ChartFilter { TitleContains = "_" }));
        Assert.Equal(2, store.QueryCharts(new ChartFilter { Level = 12 }).Count);
        Assert.Equal(new[] { "DPA" }, store.QueryCharts(new ChartFilter { Side = "DP" }).Select(c => c.Difficulty));
        Assert.Equal(3, store.QueryCharts(new ChartFilter { Side = "SP" }).Count);
        Assert.Equal(new[] { "SPL" }, store.QueryCharts(new ChartFilter { DifficultyLetter = "L" }).Select(c => c.Difficulty));
        Assert.Equal(new[] { "Sleepless Days" }, store.QueryCharts(new ChartFilter { BestLampRank = LampInfo.Rank("EX") }).Select(c => c.Title));
        Assert.Empty(store.QueryCharts(new ChartFilter { BestLampRank = LampInfo.Rank("HC") })); /* best lamp is EX, not HC */
        Assert.Equal(new[] { "Colorful Cookie", "Sleepless Days" }, store.QueryCharts(new ChartFilter { FromUtc = "2026-11-01T00:00:00Z" }).Select(c => c.Title));
        Assert.Equal(new[] { "Verflucht", "100% minimoo-G" }, store.QueryCharts(new ChartFilter { ToUtc = "2026-11-01T00:00:00Z" }).Select(c => c.Title));
        var inPeriod = Assert.Single(store.QueryCharts(new ChartFilter { FromUtc = "2026-11-01T00:00:00Z", ToUtc = "2026-11-02T00:00:00Z" }));
        Assert.Equal(1, inPeriod.PlayCount); /* aggregates only count plays inside the period */
        Assert.Equal("HC", inPeriod.BestLamp);
        var unavailable = Assert.Single(store.QueryCharts(new ChartFilter { Level = 8 }));
        Assert.Null(unavailable.BestLamp);
        Assert.Null(unavailable.MinMissCount);
        Assert.False(store.QueryPlays(unavailable.Id)[0].DataAvailable);
    }

    [Fact]
    public void DailySummaryCountsPlaysAndNewBests()
    {
        var path = _dir.Write("playlog.jsonl",
            Lines.Play(recordedAt: "2026-11-06T12:00:00Z", exScore: 3000) +
            Lines.Play(recordedAt: "2026-11-06T12:05:00Z", exScore: 2900) +
            Lines.Play(recordedAt: "2026-11-06T12:10:00Z", exScore: 3100) +
            Lines.Play(songId: "26071", title: "Verflucht", difficulty: "SPL", recordedAt: "2026-11-06T12:15:00Z", exScore: 2500));
        using var store = OpenStore();
        store.ImportPlaylog(path);
        var day = Assert.Single(store.QueryDaily());
        Assert.Equal(4, day.Plays);
        Assert.Equal(3, day.NewBests); /* first play of each chart and the 3100 */
    }

    [Fact]
    public void BomAndCrLfAreTolerated()
    {
        var content = "﻿" + Lines.Play(recordedAt: "2026-11-01T10:00:00Z").TrimEnd('\n') + "\r\n" + Lines.Play(recordedAt: "2026-11-02T10:00:00Z").TrimEnd('\n') + "\r\n";
        var path = _dir.Write("playlog.jsonl", content);
        using var store = OpenStore();
        var result = store.ImportPlaylog(path);
        Assert.Equal(2, result.Inserted);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public void MissingFileIsReportedWithoutTouchingTheDatabase()
    {
        var path = _dir.Write("playlog.jsonl", Lines.Play());
        using var store = OpenStore();
        store.ImportPlaylog(path);
        File.Delete(path);
        var result = store.ImportPlaylog(path);
        Assert.False(result.FileFound);
        Assert.Equal(1, store.GetStats().Plays);
    }

    [Fact]
    public void DatabaseSurvivesReopenAndIsRecreatedWhenTheSchemaVersionChanges()
    {
        var path = _dir.Write("playlog.jsonl", Lines.Play());
        var dbPath = HistoryStore.DefaultDbPath(_dir.Path);
        using (var store = new HistoryStore(dbPath))
        {
            store.ImportPlaylog(path);
        }
        using (var store = new HistoryStore(dbPath))
        {
            Assert.Equal(1, store.GetStats().Plays);
            Assert.Equal(0, store.ImportPlaylog(path).Inserted);
        }
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString()))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA user_version = 999";
            cmd.ExecuteNonQuery();
        }
        using (var store = new HistoryStore(dbPath))
        {
            Assert.Equal(0, store.GetStats().Plays);
            Assert.Equal(1, store.ImportPlaylog(path).Inserted);
        }
    }

    [Fact]
    public void HundredThousandLinesImportInSeconds()
    {
        const int count = 100_000;
        var sb = new StringBuilder(count * 700);
        var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var random = new Random(1);
        for (int i = 0; i < count; i++)
        {
            int song = i % 400;
            sb.Append(Lines.Play(
                songId: (10000 + song).ToString("D5"),
                title: "Song " + song,
                difficulty: PlaylogParser.Difficulties[song % 10],
                recordedAt: PlaylogParser.FormatTimestamp(start.AddMinutes(i * 3)),
                exScore: 1000 + random.Next(2000),
                lamp: LampInfo.Codes[1 + random.Next(8)],
                missCount: random.Next(200),
                level: 1 + song % 12));
        }
        var path = _dir.Write("playlog.jsonl", sb.ToString());
        _output.WriteLine($"file size: {new FileInfo(path).Length / 1024 / 1024} MB");
        using var store = OpenStore();

        var sw = Stopwatch.StartNew();
        var result = store.ImportPlaylog(path);
        sw.Stop();
        _output.WriteLine($"import of {count} lines: {sw.ElapsedMilliseconds} ms");
        Assert.Equal(count, result.Inserted);
        Assert.Equal(0, result.Skipped);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"import took {sw.Elapsed}");

        sw.Restart();
        var charts = store.QueryCharts();
        sw.Stop();
        _output.WriteLine($"song list ({charts.Count} charts): {sw.ElapsedMilliseconds} ms");
        Assert.Equal(400, charts.Count);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"song list query took {sw.Elapsed}");

        sw.Restart();
        var plays = store.QueryPlays(charts[0].Id);
        var daily = store.QueryDaily();
        sw.Stop();
        _output.WriteLine($"chart detail ({plays.Count} plays) + daily summary ({daily.Count} days): {sw.ElapsedMilliseconds} ms");
        Assert.Equal(250, plays.Count);

        sw.Restart();
        Assert.Equal(0, store.ImportPlaylog(path).Inserted);
        sw.Stop();
        _output.WriteLine($"no-op incremental import: {sw.ElapsedMilliseconds} ms");
    }
}
