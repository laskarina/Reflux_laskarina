using System.IO;

namespace Reflux.HistoryViewer.Services;

/// <summary>
/// Raises <see cref="Changed"/> (on a thread-pool thread) about a second after playlog.jsonl was last written to.
/// A FileSystemWatcher does the work; a slow poll of the file size backs it up for the cases where change
/// notifications are not delivered.
/// </summary>
public sealed class PlaylogWatcher : IDisposable
{
    readonly FileSystemWatcher _watcher;
    readonly System.Timers.Timer _debounce;
    readonly System.Timers.Timer _poll;
    readonly string _path;
    long _lastLength = -1;
    DateTime _lastWrite;

    public event Action? Changed;

    public PlaylogWatcher(string directory, string fileName, int debounceMs = 1000, int pollMs = 5000)
    {
        _path = Path.Combine(directory, fileName);
        Snapshot();
        _watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
        };
        _watcher.Changed += OnFileEvent;
        _watcher.Created += OnFileEvent;
        _watcher.Deleted += OnFileEvent;
        _watcher.Renamed += OnFileEvent;
        _debounce = new System.Timers.Timer(debounceMs) { AutoReset = false };
        _debounce.Elapsed += (_, _) => Fire();
        _poll = new System.Timers.Timer(pollMs) { AutoReset = true };
        _poll.Elapsed += (_, _) =>
        {
            if (Snapshot())
            {
                Kick();
            }
        };
        _watcher.EnableRaisingEvents = true;
        _poll.Start();
    }

    void OnFileEvent(object sender, FileSystemEventArgs e) => Kick();

    void Kick()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    void Fire()
    {
        Snapshot();
        Changed?.Invoke();
    }

    /// <summary>Records the file's current size and time; true when they changed since the last snapshot.</summary>
    bool Snapshot()
    {
        var info = new FileInfo(_path);
        long length = info.Exists ? info.Length : -1;
        var write = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;
        bool changed = length != _lastLength || write != _lastWrite;
        _lastLength = length;
        _lastWrite = write;
        return changed;
    }

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _debounce.Dispose();
        _poll.Dispose();
    }
}
