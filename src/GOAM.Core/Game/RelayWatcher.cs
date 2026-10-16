using Goam.Core.Diagnostics;

namespace Goam.Core.Game;

/// <summary>
/// Watches the relay cfg without ever touching the UI thread: a FileSystemWatcher (debounced) for fast
/// reaction plus a ~1 s poll as a safety net (watchers miss events on some network/AV setups, and the
/// Steam userdata folder may not exist yet when GOAM starts).
/// </summary>
public sealed class RelayWatcher : IDisposable
{
    private readonly Func<string?> _pathProvider;
    private readonly Action _onChanged;
    private readonly IAppLog _log;
    private readonly TimeSpan _debounce;
    private readonly object _gate = new();

    private readonly Timer _pollTimer;
    private readonly Timer _debounceTimer;
    private FileSystemWatcher? _watcher;
    private string? _watchedDirectory;
    private (DateTime WriteUtc, long Length)? _lastSignature;
    private bool _checking;
    private bool _disposed;

    public RelayWatcher(
        Func<string?> pathProvider,
        Action onChanged,
        IAppLog? log = null,
        TimeSpan? debounce = null,
        TimeSpan? pollInterval = null)
    {
        _pathProvider = pathProvider;
        _onChanged = onChanged;
        _log = log ?? NullLog.Instance;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(150);
        TimeSpan poll = pollInterval ?? TimeSpan.FromSeconds(1);

        _debounceTimer = new Timer(_ => Check(), null, Timeout.Infinite, Timeout.Infinite);
        _pollTimer = new Timer(_ => Check(), null, poll, poll);
    }

    /// <summary>Forget what we saw so the next check reports the file as changed (used after a rebuild).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _lastSignature = null;
        }
    }

    /// <summary>Run a check right now on the calling thread (used by tests and after Reset).</summary>
    public void CheckNow() => Check();

    private void Check()
    {
        bool raise = false;
        lock (_gate)
        {
            if (_disposed || _checking)
            {
                return;
            }

            _checking = true;
            try
            {
                string? path = SafeGetPath();
                EnsureWatcher(path);
                if (path is null || !File.Exists(path))
                {
                    return;
                }

                var info = new FileInfo(path);
                var signature = (info.LastWriteTimeUtc, info.Length);
                if (_lastSignature != signature)
                {
                    _lastSignature = signature;
                    raise = true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Warn($"Relay check failed: {ex.Message}");
            }
            finally
            {
                _checking = false;
            }
        }

        if (raise)
        {
            try
            {
                _onChanged();
            }
            catch (Exception ex)
            {
                _log.Error("Relay change handler failed", ex);
            }
        }
    }

    private string? SafeGetPath()
    {
        try
        {
            return _pathProvider();
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not resolve the relay file path: {ex.Message}");
            return null;
        }
    }

    private void EnsureWatcher(string? path)
    {
        string? dir = path is null ? null : Path.GetDirectoryName(path);
        if (string.Equals(dir, _watchedDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        DisposeWatcher();
        _watchedDirectory = dir;
        if (dir is null || !Directory.Exists(dir))
        {
            _watchedDirectory = null;
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(dir)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
                Filter = Path.GetFileName(path!),
                IncludeSubdirectories = false,
            };
            watcher.Changed += OnFsEvent;
            watcher.Created += OnFsEvent;
            watcher.Renamed += OnFsEvent;
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or PlatformNotSupportedException)
        {
            _log.Warn($"FileSystemWatcher unavailable, relying on polling: {ex.Message}");
            _watchedDirectory = null;
        }
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        try
        {
            _debounceTimer.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // shutting down
        }
    }

    private void DisposeWatcher()
    {
        FileSystemWatcher? w = _watcher;
        _watcher = null;
        if (w is not null)
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _pollTimer.Dispose();
        _debounceTimer.Dispose();
        DisposeWatcher();
    }
}
