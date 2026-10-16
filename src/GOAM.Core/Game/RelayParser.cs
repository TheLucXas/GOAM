using System.Text.RegularExpressions;

namespace Goam.Core.Game;

/// <summary>
/// Reads the song number out of goam_relay_key.cfg, which the game rewrites through
/// <c>host_writeconfig goam_relay_key</c> every time an in-game alias runs
/// <c>bind &lt;relay key&gt; &lt;N&gt;</c>.
/// </summary>
public static class RelayParser
{
    /// <summary>Returns the number bound to <paramref name="relayKey"/>, or null if there is none.</summary>
    public static int? ParseIndex(string? content, string relayKey)
    {
        if (string.IsNullOrEmpty(content) || string.IsNullOrWhiteSpace(relayKey))
        {
            return null;
        }

        // Port of goam.py: bind\s+["']?KEY["']?\s+["']?(\d+)["']?   (case-insensitive, first match wins)
        var regex = new Regex(
            $"bind\\s+[\"']?{Regex.Escape(relayKey)}[\"']?\\s+[\"']?(\\d+)[\"']?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        try
        {
            Match m = regex.Match(content);
            return m.Success && int.TryParse(m.Groups[1].Value, out int n) ? n : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    /// <summary>Reads the relay file tolerating the game writing to it at the same moment.</summary>
    public static string? ReadFile(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            return sr.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
