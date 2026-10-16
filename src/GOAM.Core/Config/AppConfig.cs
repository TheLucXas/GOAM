using System.Text.Json.Serialization;

namespace Goam.Core.Config;

/// <summary>User settings, persisted as %APPDATA%\GOAM\config.json.</summary>
public sealed class AppConfig
{
    public const int CurrentSchemaVersion = 1;
    public const int DefaultVolumePercent = 70;
    public const string DefaultPlayKey = "x";
    public const string DefaultRelayKey = "=";

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Folder that contains csgo.exe (the CS:GO Legacy install).</summary>
    public string? CsgoDir { get; set; }

    /// <summary>Folder with the converted .wav songs. Null until first-run setup finishes.</summary>
    public string? SongsDir { get; set; }

    public string PlayKey { get; set; } = DefaultPlayKey;
    public string RelayKey { get; set; } = DefaultRelayKey;

    /// <summary>0-200; see <see cref="Audio.Volume"/>.</summary>
    public int VolumePercent { get; set; } = DefaultVolumePercent;

    /// <summary>"es" or "en".</summary>
    public string? Language { get; set; }

    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool StartMinimizedWindows { get; set; }
    public bool MinimizeToTray { get; set; }

    /// <summary>Last time we checked GitHub for a newer yt-dlp.</summary>
    public DateTime? YtDlpLastCheckUtc { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppConfig))]
internal sealed partial class AppConfigJsonContext : JsonSerializerContext
{
}
