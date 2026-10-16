using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Goam.Core.Config;

/// <summary>
/// Reader for the plain-text config written by GOAM 1.x (Python):
/// one <c>key="value"</c> per line, only known keys, value = first double-quoted token.
/// </summary>
public static class LegacyConfig
{
    public static readonly IReadOnlyList<string> Keys = new[]
    {
        "csgo_dir", "goam_play_key", "goam_relay_key", "volume_percent", "songs_dir",
        "language", "start_with_windows", "start_minimized", "start_minimized_windows",
        "minimize_to_tray",
    };

    private static readonly Regex QuotedValue = new("\"([^\"]*)\"", RegexOptions.Compiled);

    /// <summary>Parity with goam.py parse_config_line(): returns (key, value) or (null, null).</summary>
    public static (string? Key, string? Value) ParseLine(string line)
    {
        int eq = line.IndexOf('=');
        if (eq < 0)
        {
            return (null, null);
        }

        string rawKey = line[..eq].Trim();
        string rawValue = line[(eq + 1)..];

        // Exact match so 'start_minimized' never matches 'start_minimized_windows'.
        string? key = Keys.FirstOrDefault(k => string.Equals(k, rawKey, StringComparison.Ordinal));
        if (key is null)
        {
            return (null, null);
        }

        Match m = QuotedValue.Match(rawValue);
        return (key, m.Success ? m.Groups[1].Value : null);
    }

    public static IReadOnlyDictionary<string, string?> Parse(string text)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (string line in text.Split('\n'))
        {
            (string? key, string? value) = ParseLine(line.TrimEnd('\r'));
            if (key is not null)
            {
                result[key] = value;
            }
        }

        return result;
    }

    /// <summary>Reads the file as UTF-8, falling back to Windows-1252 (what Python used on Windows).</summary>
    public static string ReadFile(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }

    public static AppConfig ToConfig(IReadOnlyDictionary<string, string?> values)
    {
        string? Get(string key) => values.TryGetValue(key, out string? v) && !string.IsNullOrWhiteSpace(v) ? v : null;
        bool Flag(string key) => Get(key) == "1";

        var cfg = new AppConfig
        {
            CsgoDir = Get("csgo_dir"),
            SongsDir = Get("songs_dir"),
            Language = Get("language"),
            StartWithWindows = Flag("start_with_windows"),
            StartMinimized = Flag("start_minimized"),
            StartMinimizedWindows = Flag("start_minimized_windows"),
            MinimizeToTray = Flag("minimize_to_tray"),
        };

        if (Get("goam_play_key") is { } play)
        {
            cfg.PlayKey = play;
        }

        if (Get("goam_relay_key") is { } relay)
        {
            cfg.RelayKey = relay;
        }

        if (int.TryParse(Get("volume_percent"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int vol))
        {
            cfg.VolumePercent = vol;
        }

        return cfg;
    }
}
