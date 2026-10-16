using Goam.Core.Audio;
using Goam.Core.Config;
using Goam.Core.Diagnostics;
using Goam.Core.Game;
using Goam.Core.Localization;
using Goam.Core.Songs;

namespace Goam.Core;

/// <summary>
/// The app's brain, free of any UI: holds the song list and the active track, keeps CS:GO's files in sync
/// (voice_input.wav + the three .cfg files) and reacts to the in-game relay. All public members are
/// thread-safe; <see cref="StateChanged"/> may fire on any thread.
/// </summary>
public sealed class GoamEngine : IDisposable
{
    private static readonly TimeSpan RelayPathTtlWhenKnown = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RelayPathTtlWhenMissing = TimeSpan.FromSeconds(3);

    private readonly object _gate = new();
    private readonly object _relayPathGate = new();
    private readonly AppConfig _config;
    private readonly ConfigStore _store;
    private readonly AppPaths _paths;
    private readonly Localizer _t;
    private readonly ISteamLocator _steam;
    private readonly AudioConverter _converter;
    private readonly IAppLog _log;

    private SongLibrary _library;
    private GameFiles? _files;
    private IReadOnlyList<SongEntry> _songs = Array.Empty<SongEntry>();
    private int _selected = 1;
    private bool _running = true;
    private bool _disposed;
    private RelayWatcher? _watcher;

    private string? _relayPath;
    private DateTime _relayPathAt = DateTime.MinValue;

    public GoamEngine(
        AppConfig config,
        ConfigStore store,
        AppPaths paths,
        Localizer localizer,
        ISteamLocator steam,
        AudioConverter converter,
        IAppLog? log = null)
    {
        _config = config;
        _store = store;
        _paths = paths;
        _t = localizer;
        _steam = steam;
        _converter = converter;
        _log = log ?? NullLog.Instance;
        _library = new SongLibrary(config.SongsDir ?? paths.DefaultSongsDir);
    }

    /// <summary>Fires after the song list, the active song or the running state changed.</summary>
    public event EventHandler? StateChanged;

    public IReadOnlyList<SongEntry> Songs => Volatile.Read(ref _songs);

    // The getters below are lock-free on purpose: the UI thread reads them while a background thread may be
    // busy decoding audio under _gate, and the UI must never wait for that.

    /// <summary>1-based number of the active song.</summary>
    public int SelectedIndex => Volatile.Read(ref _selected);

    public SongEntry? SelectedSong
    {
        get
        {
            IReadOnlyList<SongEntry> songs = Songs;
            int index = SelectedIndex;
            return index >= 1 && index <= songs.Count ? songs[index - 1] : null;
        }
    }

    public bool IsRunning => Volatile.Read(ref _running);

    public string SongsDir => Volatile.Read(ref _library).Directory;

    public AppConfig Config => _config;

    // ---- lifecycle -----------------------------------------------------------------------------

    public void Initialize()
    {
        lock (_gate)
        {
            ApplyDirectories_NoLock();
            ReloadSongs_NoLock(preserveSelection: false);
            _selected = InitialTrack_NoLock();
            WriteCfgs_NoLock();
        }

        _watcher = new RelayWatcher(ResolveRelayPath, OnRelayChanged, _log);
        RaiseStateChanged();
    }

    /// <summary>Stops watching and removes GOAM's files from the game folder (as GOAM 1.x did on exit).</summary>
    public void Shutdown()
    {
        RelayWatcher? watcher;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            watcher = _watcher;
            _watcher = null;
            CleanupFiles_NoLock();
        }

        watcher?.Dispose();
    }

    public void Dispose() => Shutdown();

    // ---- user actions --------------------------------------------------------------------------

    /// <summary>Makes song <paramref name="index"/> (1-based) the active one and writes voice_input.wav.</summary>
    public bool SelectSong(int index)
    {
        lock (_gate)
        {
            if (index < 1 || index > _songs.Count)
            {
                _log.Warn($"Ignoring invalid song index {index} (have {_songs.Count}).");
                return false;
            }

            _selected = index;
            if (_running)
            {
                WriteVoice_NoLock(_songs[index - 1]);
            }
        }

        RaiseStateChanged();
        return true;
    }

    public void SetRunning(bool running)
    {
        RelayWatcher? watcher;
        lock (_gate)
        {
            if (_running == running)
            {
                return;
            }

            _running = running;
            if (running)
            {
                ReloadSongs_NoLock(preserveSelection: true);
                WriteCfgs_NoLock();
            }
            else
            {
                CleanupFiles_NoLock();
            }

            watcher = _watcher;
        }

        if (running && watcher is not null)
        {
            // Same as GOAM 1.x: after (re)starting, honour whatever the game's relay currently says.
            watcher.Reset();
            watcher.CheckNow();
        }

        RaiseStateChanged();
    }

    /// <summary>Re-reads the songs folder and rewrites the cfg files (Options > Reload, after edits).</summary>
    public void ReloadSongs()
    {
        lock (_gate)
        {
            ReloadSongs_NoLock(preserveSelection: true);
            WriteCfgs_NoLock();
        }

        RaiseStateChanged();
    }

    /// <summary>Rewrites the cfg files only (play key, relay key or language changed).</summary>
    public void RefreshCfgs()
    {
        lock (_gate)
        {
            WriteCfgs_NoLock();
        }

        RaiseStateChanged();
    }

    /// <summary>Re-applies the current volume to voice_input.wav for the active song.</summary>
    public void ReapplyVolume()
    {
        lock (_gate)
        {
            if (_running && _selected >= 1 && _selected <= _songs.Count)
            {
                WriteVoice_NoLock(_songs[_selected - 1]);
            }
        }
    }

    public void SetVolume(int percent)
    {
        lock (_gate)
        {
            _config.VolumePercent = Math.Clamp(percent, Volume.Min, Volume.Max);
            SaveConfig_NoLock();
        }
    }

    public void SetPlayKey(string key)
    {
        string normalized = GameKeys.Normalize(key);
        if (!GameKeys.IsValid(normalized))
        {
            throw new ArgumentException("Invalid key.", nameof(key));
        }

        lock (_gate)
        {
            _config.PlayKey = normalized;
            SaveConfig_NoLock();
            WriteCfgs_NoLock();
        }

        RaiseStateChanged();
    }

    public void SetRelayKey(string key)
    {
        string normalized = GameKeys.Normalize(key);
        if (!GameKeys.IsValid(normalized))
        {
            throw new ArgumentException("Invalid key.", nameof(key));
        }

        lock (_gate)
        {
            _config.RelayKey = normalized;
            SaveConfig_NoLock();
            WriteCfgs_NoLock();
        }

        RaiseStateChanged();
    }

    public void SetCsgoDir(string path)
    {
        RelayWatcher? watcher;
        lock (_gate)
        {
            if (_running)
            {
                CleanupFiles_NoLock(); // leave nothing behind in the previous install
            }

            _config.CsgoDir = path;
            SaveConfig_NoLock();
            ApplyDirectories_NoLock();
            WriteCfgs_NoLock();
            watcher = _watcher;
        }

        watcher?.Reset();
        RaiseStateChanged();
    }

    public void SetSongsDir(string path)
    {
        lock (_gate)
        {
            _config.SongsDir = path;
            SaveConfig_NoLock();
            ApplyDirectories_NoLock();
            ReloadSongs_NoLock(preserveSelection: false);
            _selected = Math.Clamp(_selected, 1, Math.Max(1, _songs.Count));
            WriteCfgs_NoLock();
        }

        RaiseStateChanged();
    }

    public void SetLanguage(string language)
    {
        lock (_gate)
        {
            _t.Language = language;
            _config.Language = _t.Language;
            SaveConfig_NoLock();
            WriteCfgs_NoLock();
        }

        RaiseStateChanged();
    }

    public void SaveSettings()
    {
        lock (_gate)
        {
            SaveConfig_NoLock();
        }
    }

    public void Delete(SongEntry song)
    {
        lock (_gate)
        {
            _library.Delete(song);
            ReloadSongs_NoLock(preserveSelection: true);
            WriteCfgs_NoLock();
        }

        RaiseStateChanged();
    }

    /// <summary>Renames a song and returns its new stem. Throws <see cref="SongNameException"/>.</summary>
    public string Rename(SongEntry song, string newName)
    {
        string newStem;
        lock (_gate)
        {
            bool wasSelected = _selected >= 1 && _selected <= _songs.Count
                               && _songs[_selected - 1].FileName == song.FileName;
            newStem = _library.Rename(song, newName);
            ReloadSongs_NoLock(preserveSelection: !wasSelected);
            if (wasSelected)
            {
                SelectByStem_NoLock(newStem);
            }

            WriteCfgs_NoLock();
        }

        RaiseStateChanged();
        return newStem;
    }

    /// <summary>After an import: reload, then make the new song the active one (GOAM 1.x auto-selected it).</summary>
    public void AddAndSelect(string stem)
    {
        lock (_gate)
        {
            ReloadSongs_NoLock(preserveSelection: true);
            SelectByStem_NoLock(stem);
            WriteCfgs_NoLock();
            if (_running && _selected >= 1 && _selected <= _songs.Count)
            {
                WriteVoice_NoLock(_songs[_selected - 1]);
            }
        }

        RaiseStateChanged();
    }

    // ---- information used by the UI / diagnostics -----------------------------------------------

    public string? RelayCfgPath => ResolveRelayPath();

    public string? FindSteamExe() => _steam.FindSteamExe();

    // ---- internals -----------------------------------------------------------------------------

    private void ApplyDirectories_NoLock()
    {
        string songsDir = _config.SongsDir ?? _paths.DefaultSongsDir;
        _library = new SongLibrary(songsDir);
        try
        {
            Directory.CreateDirectory(_library.Directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("Could not create the songs folder", ex);
        }

        _files = !string.IsNullOrWhiteSpace(_config.CsgoDir) && Directory.Exists(_config.CsgoDir)
            ? new GameFiles(_config.CsgoDir)
            : null;
    }

    private void ReloadSongs_NoLock(bool preserveSelection)
    {
        string? previousStem = preserveSelection && _selected >= 1 && _selected <= _songs.Count
            ? _songs[_selected - 1].Stem
            : null;

        IReadOnlyList<SongEntry> loaded;
        try
        {
            loaded = _library.Load();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("Could not read the songs folder", ex);
            loaded = Array.Empty<SongEntry>();
        }

        Volatile.Write(ref _songs, loaded);

        if (previousStem is not null)
        {
            SongEntry? same = loaded.FirstOrDefault(s => s.Stem == previousStem);
            if (same is not null)
            {
                _selected = same.Index;
            }
        }

        _selected = Math.Clamp(_selected, 1, Math.Max(1, loaded.Count));
    }

    private void SelectByStem_NoLock(string stem)
    {
        SongEntry? found = _songs.FirstOrDefault(s => s.Stem == stem);
        if (found is not null)
        {
            _selected = found.Index;
        }
    }

    private int InitialTrack_NoLock()
    {
        string? path = ResolveRelayPath();
        if (path is not null && File.Exists(path))
        {
            int? idx = RelayParser.ParseIndex(RelayParser.ReadFile(path), _config.RelayKey);
            if (idx is >= 1 && idx <= _songs.Count)
            {
                _log.Info($"Restoring track {idx} from the in-game relay.");
                return idx.Value;
            }
        }

        return 1;
    }

    private void WriteCfgs_NoLock()
    {
        if (!_running || _files is null)
        {
            return;
        }

        try
        {
            _files.WriteCfg(_files.TracksCfg, CfgBuilder.BuildTracksCfg(_t, _songs));
            _files.WriteCfg(
                _files.MainCfg,
                CfgBuilder.BuildMainCfg(_t, _config.PlayKey, _config.RelayKey, _songs));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("Could not write the CS:GO cfg files", ex);
        }
    }

    private void WriteVoice_NoLock(SongEntry song)
    {
        if (_files is null)
        {
            _log.Warn("CS:GO directory not configured; voice_input.wav was not written.");
            return;
        }

        try
        {
            double db = Volume.GetDbAdjust(_config.VolumePercent);
            _converter.WriteVoiceInput(song.FullPath, _files.VoiceInputWav, db);
            _files.WriteCfg(_files.CurrentTrackCfg, CfgBuilder.BuildCurrentTrackCfg(_t, song.Stem));
            _log.Info($"Active song: {song.Index} ({_config.VolumePercent}% = {db:+0.0;-0.0} dB)");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("Could not write voice_input.wav", ex);
        }
    }

    private void CleanupFiles_NoLock()
    {
        try
        {
            _files?.Cleanup();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"Cleanup error: {ex.Message}");
        }
    }

    private void SaveConfig_NoLock()
    {
        try
        {
            _store.Save(_config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("Could not save config.json", ex);
        }
    }

    private void OnRelayChanged()
    {
        int target;
        lock (_gate)
        {
            if (!_running || _disposed)
            {
                return;
            }

            target = _selected;
        }

        string? path = ResolveRelayPath();
        if (path is not null)
        {
            string? content = RelayParser.ReadFile(path);
            int lookups = 0;
            // The game may still be flushing the file when the event arrives; retry briefly.
            while (content is null && lookups++ < 3)
            {
                Thread.Sleep(50);
                content = RelayParser.ReadFile(path);
            }

            int? idx = RelayParser.ParseIndex(content, _config.RelayKey);
            if (idx.HasValue)
            {
                target = idx.Value;
            }
        }

        SelectSong(target);
    }

    private string? ResolveRelayPath()
    {
        lock (_relayPathGate)
        {
            TimeSpan ttl = _relayPath is null ? RelayPathTtlWhenMissing : RelayPathTtlWhenKnown;
            if (DateTime.UtcNow - _relayPathAt >= ttl)
            {
                _relayPath = _steam.FindRelayCfgPath();
                _relayPathAt = DateTime.UtcNow;
            }

            return _relayPath;
        }
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
