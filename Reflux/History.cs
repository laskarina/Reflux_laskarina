using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace Reflux
{
    /// <summary>
    /// Append-only play history (history/playlog.jsonl). One JSON object per line, one line per play.
    /// Independent of the existing tsv/json/latest outputs.
    /// </summary>
    static class History
    {
        const string DirName = "history";
        const string FileName = "playlog.jsonl";

        /// <summary>
        /// Append a single play to the history file, creating the directory on first use.
        /// The file is opened with FileShare.Read so a viewer can read it while Reflux is running.
        /// </summary>
        /// <param name="data"></param>
        public static void Append(PlayData data)
        {
            Directory.CreateDirectory(DirName);
            var line = data.GetHistoryEntry().ToString(Formatting.None) + "\n";
            using var stream = new FileStream(Path.Combine(DirName, FileName), FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(line);
        }
    }
}
