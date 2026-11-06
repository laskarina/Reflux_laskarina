using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reflux.HistoryViewer.Services;

/// <summary>Persisted in %APPDATA%\Reflux.HistoryViewer\settings.json.</summary>
public sealed class ViewerSettings
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public string? HistoryDir { get; set; }
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
    /// <summary>Height of the song list pane as a fraction of the two panes.</summary>
    public double SplitterRatio { get; set; } = 0.42;
    public string? LastChartKey { get; set; }
    public bool NewestFirst { get; set; } = true;

    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Reflux.HistoryViewer", "settings.json");

    public static ViewerSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<ViewerSettings>(File.ReadAllText(FilePath), Options) ?? new ViewerSettings();
            }
        }
        catch (Exception)
        {
            /* unreadable settings are simply replaced on the next save */
        }
        return new ViewerSettings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception)
        {
            /* settings are a convenience; never fail the app for them */
        }
    }
}
