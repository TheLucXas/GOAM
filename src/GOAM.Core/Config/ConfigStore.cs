using System.Text.Json;
using Goam.Core.Audio;
using Goam.Core.Diagnostics;
using Goam.Core.Localization;

namespace Goam.Core.Config;

public sealed record ConfigLoadResult(AppConfig Config, bool MigratedFromLegacy, bool IsFirstRun);

/// <summary>Loads, validates, migrates and atomically saves <see cref="AppConfig"/>.</summary>
public sealed class ConfigStore
{
    private readonly AppPaths _paths;
    private readonly IAppLog _log;
    private readonly object _gate = new();

    public ConfigStore(AppPaths paths, IAppLog? log = null)
    {
        _paths = paths;
        _log = log ?? NullLog.Instance;
    }

    public ConfigLoadResult Load()
    {
        Directory.CreateDirectory(_paths.DataDir);
        AppConfig? config = null;
        bool migrated = false;

        if (File.Exists(_paths.ConfigFile))
        {
            try
            {
                using FileStream fs = File.OpenRead(_paths.ConfigFile);
                config = JsonSerializer.Deserialize(fs, AppConfigJsonContext.Default.AppConfig);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                _log.Error("config.json could not be read; a backup copy is kept and defaults are used", ex);
                TryBackupCorrupt();
            }
        }
        else if (File.Exists(_paths.LegacyConfigFile))
        {
            try
            {
                config = LegacyConfig.ToConfig(LegacyConfig.Parse(LegacyConfig.ReadFile(_paths.LegacyConfigFile)));
                migrated = true;
                _log.Info("Migrated settings from the GOAM 1.x 'config' file (the old file is left untouched).");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Error("Legacy config could not be read; using defaults", ex);
            }
        }

        bool isFirstRun = config?.SongsDir is null;
        config ??= new AppConfig();
        Normalize(config);

        if (migrated)
        {
            Save(config);
        }

        return new ConfigLoadResult(config, migrated, isFirstRun);
    }

    public void Save(AppConfig config)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_paths.DataDir);
            string tmp = _paths.ConfigFile + ".tmp";
            using (FileStream fs = File.Create(tmp))
            {
                JsonSerializer.Serialize(fs, config, AppConfigJsonContext.Default.AppConfig);
            }

            File.Move(tmp, _paths.ConfigFile, overwrite: true);
        }
    }

    /// <summary>Resets settings to defaults. Songs and the CS:GO folder are never touched.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            if (File.Exists(_paths.ConfigFile))
            {
                File.Delete(_paths.ConfigFile);
            }
        }
    }

    internal static void Normalize(AppConfig c)
    {
        c.SchemaVersion = AppConfig.CurrentSchemaVersion;

        c.VolumePercent = Math.Clamp(c.VolumePercent, Volume.Min, Volume.Max);

        c.PlayKey = GameKeys.IsValid(c.PlayKey) ? GameKeys.Normalize(c.PlayKey) : AppConfig.DefaultPlayKey;
        c.RelayKey = GameKeys.IsValid(c.RelayKey) ? GameKeys.Normalize(c.RelayKey) : AppConfig.DefaultRelayKey;

        c.Language = Localizer.Normalize(c.Language) ?? Localizer.DetectSystemLanguage();

        if (string.IsNullOrWhiteSpace(c.CsgoDir))
        {
            c.CsgoDir = null;
        }

        if (string.IsNullOrWhiteSpace(c.SongsDir))
        {
            c.SongsDir = null;
        }
    }

    private void TryBackupCorrupt()
    {
        try
        {
            File.Copy(_paths.ConfigFile, _paths.ConfigFile + ".bad", overwrite: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }
}
