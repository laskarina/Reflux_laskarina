using System.IO;

namespace Reflux.HistoryViewer.Services;

/// <summary>Finds Reflux's history/ directory relative to the viewer executable.</summary>
public static class HistoryLocator
{
    public const string DirName = "history";
    public const string FileName = "playlog.jsonl";

    public static string ExeDir
    {
        get
        {
            var processPath = Environment.ProcessPath;
            var dir = processPath is null ? null : Path.GetDirectoryName(processPath);
            return string.IsNullOrEmpty(dir) ? AppContext.BaseDirectory : dir;
        }
    }

    public static string PlaylogPath(string historyDir) => Path.Combine(historyDir, FileName);

    /// <summary>
    /// Looks for history/playlog.jsonl next to the exe, then in the parent folder. If only the directory exists
    /// (no play recorded yet) it is accepted as well. Returns null when nothing is found.
    /// </summary>
    public static string? FindDefault()
    {
        var candidates = Candidates().Select(b => Path.Combine(b, DirName)).ToList();
        return candidates.FirstOrDefault(d => File.Exists(PlaylogPath(d))) ?? candidates.FirstOrDefault(Directory.Exists);
    }

    /// <summary>Accepts either the history folder itself or a Reflux folder that contains history/.</summary>
    public static string ResolveChosenFolder(string folder)
    {
        if (File.Exists(PlaylogPath(folder)))
        {
            return folder;
        }
        var sub = Path.Combine(folder, DirName);
        if (Directory.Exists(sub))
        {
            return sub;
        }
        return string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)), DirName, StringComparison.OrdinalIgnoreCase) ? folder : sub;
    }

    static IEnumerable<string> Candidates()
    {
        var exe = ExeDir;
        yield return exe;
        var parent = Directory.GetParent(exe)?.FullName;
        if (parent is not null)
        {
            yield return parent;
        }
    }
}
