using System.Text.RegularExpressions;

namespace Goam.Core.YouTube;

/// <summary>Validation and cleaning of the links the user pastes (is_valid_youtube_url / clean_youtube_url).</summary>
public static class YouTubeUrl
{
    private const int MaxLength = 2048;

    // GOAM 1.x rule (plus m.youtube.com): [https://][www.](youtube.com|youtu.be|music.youtube.com)/<something>
    private static readonly Regex Shape = new(
        @"^(https?://)?(www\.|m\.)?(youtube\.com|youtu\.be|music\.youtube\.com)/.+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex VideoId = new(
        @"(?:v=|youtu\.be/)([A-Za-z0-9_-]{11})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube.com", "www.youtube.com", "m.youtube.com", "music.youtube.com", "youtu.be", "www.youtu.be",
    };

    /// <summary>True if the text looks like a YouTube link GOAM is willing to hand to yt-dlp.</summary>
    public static bool IsValid(string? url) => TryNormalize(url, out _);

    /// <summary>
    /// Validates <paramref name="url"/> and returns an absolute https URL (adds the scheme if the user
    /// omitted it). Rejects other hosts, user-info tricks, whitespace/control characters and giant strings,
    /// so the value is safe to pass as a single argument.
    /// </summary>
    public static bool TryNormalize(string? url, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        string trimmed = url.Trim();
        if (trimmed.Length > MaxLength || trimmed.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
        {
            return false;
        }

        if (!Shape.IsMatch(trimmed))
        {
            return false;
        }

        string candidate = trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : "https://" + trimmed;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !AllowedHosts.Contains(uri.Host))
        {
            return false;
        }

        normalized = uri.AbsoluteUri;
        return true;
    }

    /// <summary>
    /// Reduces a link to https://www.youtube.com/watch?v=&lt;id&gt; when it contains a video id
    /// (drops list=, t=, si= ...). Links without a recognisable id are returned unchanged.
    /// </summary>
    public static string Clean(string url)
    {
        Match m = VideoId.Match(url);
        return m.Success ? $"https://www.youtube.com/watch?v={m.Groups[1].Value}" : url;
    }
}
