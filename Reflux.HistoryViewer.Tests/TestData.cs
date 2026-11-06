using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Reflux.HistoryViewer.Tests;

/// <summary>Builds playlog.jsonl lines and temp directories for the tests.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "reflux-hv-tests", Guid.NewGuid().ToString("N"));

    public TempDir()
    {
        Directory.CreateDirectory(Path);
    }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public string Write(string name, string content)
    {
        var path = File(name);
        System.IO.File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    public void Append(string name, string content) => System.IO.File.AppendAllText(File(name), content, new UTF8Encoding(false));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public static class Lines
{
    /// <summary>A schema_version 1 record. Pass null for song_id to simulate rows that only have a title.</summary>
    public static string Play(
        string? songId = "25094",
        string title = "Sleepless Days",
        string difficulty = "SPA",
        string recordedAt = "2026-11-06T12:34:56Z",
        int exScore = 3412,
        string? lamp = "HC",
        int? missCount = 24,
        bool dataAvailable = true,
        bool prematureEnd = false,
        int? level = 12,
        int? totalNotes = 2096,
        string playSide = "P1",
        string? style2 = null,
        string grade = "AA",
        string gaugeType = "HARD")
    {
        var json = new JObject
        {
            ["schema_version"] = 1,
            ["recorded_at"] = recordedAt,
            ["game_version"] = "P2D:J:B:A:2026101400",
            ["reflux_version"] = "1.16.5",
            ["song_id"] = songId is null ? JValue.CreateNull() : new JValue(songId),
            ["title"] = title,
            ["title_english"] = title,
            ["artist"] = "Sota Fujimori",
            ["genre"] = "TECHNO",
            ["bpm"] = "150",
            ["difficulty"] = difficulty,
            ["level"] = level is null ? JValue.CreateNull() : new JValue(level),
            ["total_notes"] = totalNotes is null ? JValue.CreateNull() : new JValue(totalNotes),
            ["play_side"] = playSide,
            ["data_available"] = dataAvailable,
            ["ex_score"] = exScore,
            ["grade"] = grade,
            ["lamp"] = lamp is null ? JValue.CreateNull() : new JValue(lamp),
            ["lamp_text"] = lamp is null ? JValue.CreateNull() : new JValue(Core.LampInfo.Text(lamp)),
            ["miss_count"] = missCount is null ? JValue.CreateNull() : new JValue(missCount),
            ["gauge_type"] = gaugeType,
            ["gauge_percent"] = dataAvailable ? new JValue(86) : JValue.CreateNull(),
            ["judge"] = new JObject { ["pgreat"] = 1420, ["great"] = 572, ["good"] = 80, ["bad"] = 10, ["poor"] = 14, ["combo_break"] = 21, ["fast"] = 310, ["slow"] = 352 },
            ["options"] = new JObject { ["style"] = "RANDOM", ["style2"] = style2 is null ? JValue.CreateNull() : new JValue(style2), ["assist"] = "OFF", ["range"] = "SUDDEN+", ["flip"] = false, ["battle"] = false, ["hran"] = false },
            ["premature_end"] = prematureEnd,
        };
        return json.ToString(Formatting.None) + "\n";
    }
}
