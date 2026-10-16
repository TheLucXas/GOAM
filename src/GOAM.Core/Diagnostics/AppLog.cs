using System.Text;

namespace Goam.Core.Diagnostics;

/// <summary>Minimal logging abstraction. GOAM never prints to the console and sends nothing anywhere.</summary>
public interface IAppLog
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}

public sealed class NullLog : IAppLog
{
    public static readonly NullLog Instance = new();
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? exception = null) { }
}

/// <summary>Thread-safe file log with size-based rotation (goam.log, goam.1.log, ...).</summary>
public sealed class FileLog : IAppLog
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly int _keep;

    public FileLog(string path, long maxBytes = 512 * 1024, int keep = 3)
    {
        _path = path;
        _maxBytes = maxBytes;
        _keep = Math.Max(1, keep);
    }

    public string Path => _path;

    public void Info(string message) => Write("INFO ", message);
    public void Warn(string message) => Write("WARN ", message);

    public void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private void Write(string level, string message)
    {
        try
        {
            lock (_gate)
            {
                string? dir = System.IO.Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                RotateIfNeeded();
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}";
                File.AppendAllText(_path, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    private void RotateIfNeeded()
    {
        var info = new FileInfo(_path);
        if (!info.Exists || info.Length < _maxBytes)
        {
            return;
        }

        string basePath = System.IO.Path.Combine(
            info.DirectoryName!, System.IO.Path.GetFileNameWithoutExtension(_path));
        string ext = System.IO.Path.GetExtension(_path);

        string oldest = $"{basePath}.{_keep}{ext}";
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (int i = _keep - 1; i >= 1; i--)
        {
            string from = $"{basePath}.{i}{ext}";
            if (File.Exists(from))
            {
                File.Move(from, $"{basePath}.{i + 1}{ext}");
            }
        }

        File.Move(_path, $"{basePath}.1{ext}");
    }

    /// <summary>Returns the last <paramref name="lines"/> log lines (newest last), or an empty list.</summary>
    public IReadOnlyList<string> Tail(int lines)
    {
        try
        {
            lock (_gate)
            {
                if (!File.Exists(_path))
                {
                    return Array.Empty<string>();
                }

                using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs, Encoding.UTF8);
                var queue = new Queue<string>(lines + 1);
                string? line;
                while ((line = sr.ReadLine()) is not null)
                {
                    queue.Enqueue(line);
                    if (queue.Count > lines)
                    {
                        queue.Dequeue();
                    }
                }

                return queue.ToArray();
            }
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
