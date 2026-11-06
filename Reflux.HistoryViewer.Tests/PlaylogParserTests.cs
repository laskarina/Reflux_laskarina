using Reflux.HistoryViewer.Core;
using Xunit;

namespace Reflux.HistoryViewer.Tests;

public class PlaylogParserTests
{
    [Fact]
    public void ParsesTheDesignDocumentExample()
    {
        const string line = """{"schema_version":1,"recorded_at":"2026-11-06T12:34:56Z","game_version":"P2D:J:B:A:2026080500","reflux_version":"1.16.5","song_id":"25094","title":"Sleepless Days","title_english":"Sleepless Days","artist":"Sota Fujimori","genre":"TECHNO","bpm":"150","difficulty":"SPA","level":12,"total_notes":2096,"play_side":"P1","data_available":true,"ex_score":3412,"grade":"AA","lamp":"HC","lamp_text":"HARD CLEAR","miss_count":24,"gauge_type":"HARD","gauge_percent":86,"judge":{"pgreat":1420,"great":572,"good":80,"bad":10,"poor":14,"combo_break":21,"fast":310,"slow":352},"options":{"style":"RANDOM","style2":null,"assist":"OFF","range":"SUDDEN+","flip":false,"battle":false,"hran":false},"premature_end":false}""";
        var r = PlaylogParser.TryParse(line);
        Assert.NotNull(r);
        Assert.Equal("2026-11-06T12:34:56Z", r.RecordedAt);
        Assert.Equal("P2D:J:B:A:2026080500", r.GameVersion);
        Assert.Equal("25094", r.SongId);
        Assert.Equal("SPA", r.Difficulty);
        Assert.Equal(12, r.Level);
        Assert.Equal(2096, r.TotalNotes);
        Assert.Equal("P1", r.PlaySide);
        Assert.True(r.DataAvailable);
        Assert.Equal(3412, r.ExScore);
        Assert.Equal("AA", r.Grade);
        Assert.Equal("HC", r.Lamp);
        Assert.Equal(24, r.MissCount);
        Assert.Equal("HARD", r.GaugeType);
        Assert.Equal(86, r.GaugePercent);
        Assert.Equal(1420, r.PGreat);
        Assert.Equal(21, r.ComboBreak);
        Assert.Equal(352, r.Slow);
        Assert.Equal("RANDOM", r.Style);
        Assert.Null(r.Style2);
        Assert.Equal("SUDDEN+", r.Range);
        Assert.False(r.Flip);
        Assert.False(r.PrematureEnd);
        Assert.Equal(PlayRecord.PlaylogSource, r.Source);
    }

    [Fact]
    public void MissingKeysBecomeNullAndUnknownKeysAreIgnored()
    {
        var r = PlaylogParser.TryParse("""{"recorded_at":"2026-01-02T03:04:05Z","difficulty":"DPA","future_key":{"x":1},"schema_version":99}""");
        Assert.NotNull(r);
        Assert.Null(r.SongId);
        Assert.Null(r.ExScore);
        Assert.Null(r.Lamp);
        Assert.Null(r.MissCount);
        Assert.True(r.DataAvailable);
        Assert.Null(r.PrematureEnd);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"recorded_at\":\"2026-01-02T03:04:05Z\"}")]
    [InlineData("{\"difficulty\":\"SPA\"}")]
    [InlineData("{\"recorded_at\":\"yesterday\",\"difficulty\":\"SPA\"}")]
    [InlineData("{\"recorded_at\":\"2026-01-02T03:04:05Z\",\"difficulty\":\"SPX\"}")]
    [InlineData("{\"recorded_at\":\"2026-01-02T03:04:05Z\",\"difficulty\":\"SPA\"} trailing")]
    [InlineData("{\"recorded_at\":\"2026-01-02T03:04:05Z\",\"difficulty\":\"SPA\"")]
    public void BrokenLinesReturnNull(string line)
    {
        Assert.Null(PlaylogParser.TryParse(line));
    }

    [Fact]
    public void TimestampsAreNormalizedToUtc()
    {
        var r = PlaylogParser.TryParse("""{"recorded_at":"2026-11-06T21:34:56+09:00","difficulty":"spa"}""");
        Assert.NotNull(r);
        Assert.Equal("2026-11-06T12:34:56Z", r.RecordedAt);
        Assert.Equal("SPA", r.Difficulty);
    }

    [Fact]
    public void LampCodesRoundTripThroughTextAndRank()
    {
        foreach (var code in LampInfo.Codes)
        {
            var rank = LampInfo.Rank(code);
            Assert.NotNull(rank);
            Assert.Equal(code, LampInfo.Code(rank));
        }
        Assert.Equal("HARD CLEAR", LampInfo.Text("HC"));
        Assert.Equal("HC", LampInfo.CodeFromText("HARD CLEAR"));
        Assert.Equal("FC", LampInfo.CodeFromText("FULL COMBO"));
        Assert.Equal("NC", LampInfo.CodeFromText("CLEAR"));
        Assert.Null(LampInfo.CodeFromText("SOMETHING ELSE"));
        Assert.Null(LampInfo.Rank("5"));
        Assert.Null(LampInfo.Rank(null));
        Assert.Equal(8, LampInfo.Rank("PFC"));
    }
}
