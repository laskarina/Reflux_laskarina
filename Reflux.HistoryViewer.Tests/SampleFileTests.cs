using System.Text;
using Reflux.HistoryViewer.Core;
using Xunit;

namespace Reflux.HistoryViewer.Tests;

/// <summary>Exercises the repository's history/sample.jsonl the way the viewer does: import, song list, chart detail, then append and truncate.</summary>
public class SampleFileTests : IDisposable
{
    static readonly string SamplePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "history", "sample.jsonl"));

    readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void SampleFileImportsCompletely()
    {
        Assert.True(File.Exists(SamplePath), $"sample not found at {SamplePath}");
        var path = _dir.File("playlog.jsonl");
        File.Copy(SamplePath, path);
        using var store = new HistoryStore(HistoryStore.DefaultDbPath(_dir.Path));

        var result = store.ImportPlaylog(path);
        Assert.Equal(13, result.Inserted);
        Assert.Equal(0, result.Skipped);

        var charts = store.QueryCharts();
        Assert.Equal(6, charts.Count);
        Assert.Equal(new[] { "SPL", "SPA", "SPH", "DPA", "SPH", "SPA" }, charts.Select(c => c.Difficulty)); /* newest last play first */

        var sleepless = Assert.Single(charts, c => c.SongId == "25094" && c.Difficulty == "SPA");
        Assert.Equal(4, sleepless.PlayCount);
        Assert.Equal(3412, sleepless.BestExScore);
        Assert.Equal("HARD CLEAR", sleepless.BestLampText);
        Assert.Equal(24, sleepless.MinMissCount);
        Assert.Equal("Sota Fujimori", sleepless.Artist);

        var plays = store.QueryPlays(sleepless.Id);
        Assert.Equal(4, plays.Count);
        Assert.True(plays[0].PrematureEnd);
        Assert.Equal("EX HARD", plays[0].GaugeType);
        Assert.Null(plays[0].MissCount);
        Assert.Equal("2026-11-06T12:34:56Z", plays[1].RecordedAt); /* the design document's example line */
        Assert.Equal("P2D:J:B:A:2026080500", plays[1].GameVersion);
        Assert.All(plays, p => Assert.Equal("P1", p.PlaySide));

        var dp = Assert.Single(charts, c => c.Difficulty == "DPA");
        var dpPlays = store.QueryPlays(dp.Id);
        Assert.Equal(2, dpPlays.Count);
        Assert.All(dpPlays, p => Assert.Equal("DP", p.PlaySide));
        Assert.All(dpPlays, p => Assert.False(string.IsNullOrEmpty(p.Style2)));
        Assert.True(dpPlays[0].Flip);

        var cookie = Assert.Single(charts, c => c.SongId == "12004");
        var cookiePlays = store.QueryPlays(cookie.Id);
        Assert.Equal(3, cookiePlays.Count);
        Assert.Equal(2, cookiePlays.Count(p => !p.DataAvailable));
        Assert.All(cookiePlays.Where(p => !p.DataAvailable), p => { Assert.Null(p.Lamp); Assert.Null(p.GaugePercent); Assert.Null(p.MissCount); });
        Assert.Single(cookiePlays, p => p.Hran == true);
        Assert.Single(cookiePlays, p => p.Assist == "AUTO SCRATCH");
        Assert.Equal("HARD CLEAR", cookie.BestLampText); /* only the available play counts for the lamp */
        Assert.Equal(12, cookie.MinMissCount);

        var verflucht = Assert.Single(charts, c => c.SongId == "26071");
        Assert.Single(store.QueryPlays(verflucht.Id), p => p.Battle == true && !p.DataAvailable);
        Assert.Single(store.QueryPlays(verflucht.Id), p => p.PlaySide == "P2");

        Assert.Equal(13, store.QueryDaily().Sum(d => d.Plays));
    }

    [Fact]
    public void AppendingToTheSampleImportsOnlyTheNewLinesAndTruncatingRebuilds()
    {
        var path = _dir.File("playlog.jsonl");
        File.Copy(SamplePath, path);
        using var store = new HistoryStore(HistoryStore.DefaultDbPath(_dir.Path));
        Assert.Equal(13, store.ImportPlaylog(path).Inserted);

        /* append: only the new line is read */
        _dir.Append("playlog.jsonl", Lines.Play(recordedAt: "2026-11-07T10:00:00Z", exScore: 3500, lamp: "EX", missCount: 10));
        var appended = store.ImportPlaylog(path);
        Assert.Equal(1, appended.Inserted);
        Assert.False(appended.Rebuilt);
        Assert.Equal(14, store.GetStats().Plays);
        var sleepless = Assert.Single(store.QueryCharts(), c => c.SongId == "25094" && c.Difficulty == "SPA");
        Assert.Equal(5, sleepless.PlayCount);
        Assert.Equal(3500, sleepless.BestExScore);
        Assert.Equal("EX", sleepless.BestLamp);

        /* truncate to the first three lines: the file is read again from the start */
        var firstThree = File.ReadLines(path, Encoding.UTF8).Take(3).ToArray();
        File.WriteAllText(path, string.Join("\n", firstThree) + "\n", new UTF8Encoding(false));
        var rebuilt = store.ImportPlaylog(path);
        Assert.True(rebuilt.Rebuilt);
        Assert.Equal(3, rebuilt.Inserted);
        Assert.Equal(3, store.GetStats().Plays);
        Assert.Single(store.QueryCharts());
    }
}
