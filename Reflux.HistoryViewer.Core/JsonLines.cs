using System.Text;

namespace Reflux.HistoryViewer.Core;

/// <summary>
/// Byte-exact line reader for JSON Lines files that are being appended to by another process.
/// Only lines terminated by '\n' are returned, together with the absolute file offset just past the
/// newline, so an importer can resume from exactly that offset next time. A trailing fragment without
/// a newline is left for the next run.
/// </summary>
internal static class JsonLines
{
    public static IEnumerable<(string Line, long EndOffset)> Read(Stream stream, long startOffset, CancellationToken cancellationToken = default)
    {
        stream.Seek(startOffset, SeekOrigin.Begin);
        var buffer = new byte[64 * 1024];
        var pending = new MemoryStream();
        long position = startOffset; /* absolute offset of buffer[0] */
        bool fileStart = startOffset == 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int lineStart = 0;
            for (int i = 0; i < read; i++)
            {
                if (buffer[i] != (byte)'\n')
                {
                    continue;
                }
                pending.Write(buffer, lineStart, i - lineStart);
                yield return (Decode(pending, ref fileStart), position + i + 1);
                pending.SetLength(0);
                lineStart = i + 1;
            }
            if (lineStart < read)
            {
                pending.Write(buffer, lineStart, read - lineStart);
            }
            position += read;
        }
    }

    static string Decode(MemoryStream pending, ref bool fileStart)
    {
        var bytes = pending.GetBuffer().AsSpan(0, (int)pending.Length);
        if (fileStart)
        {
            fileStart = false;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                bytes = bytes[3..]; /* UTF-8 BOM */
            }
        }
        if (bytes.Length > 0 && bytes[^1] == (byte)'\r')
        {
            bytes = bytes[..^1];
        }
        return Encoding.UTF8.GetString(bytes);
    }
}
