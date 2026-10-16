using System.Globalization;
using System.Text.Json;
using Goam.Core.Diagnostics;

namespace Goam.Core.YouTube;

public sealed record PlaylistEntry(string Url, string Title);

public sealed record ProbeResult(bool IsPlaylist, string? Title, IReadOnlyList<PlaylistEntry> Entries);

public sealed record DownloadedAudio(string FilePath, string Title, string VideoId);

/// <summary>Download progress as a 0..1 fraction (null when yt-dlp does not know the total yet).</summary>
public sealed record DownloadProgress(double? Fraction);

public sealed class YtDlpException : Exception
{
    public YtDlpException(string message, int exitCode, string details)
        : base(message)
    {
        ExitCode = exitCode;
        Details = details;
    }

    public int ExitCode { get; }
    public string Details { get; }
}

/// <summary>Thin, testable wrapper around the external yt-dlp.exe.</summary>
public sealed class YtDlpClient
{
    /// <summary>
    /// Formats Media Foundation / NVorbis can decode without ffmpeg. YouTube always offers m4a (AAC-LC),
    /// so no re-encode is needed. GOAM 1.x asked for mp3/ogg/flac first, which YouTube never has, and then
    /// got webm/opus that its decoder could not read.
    /// </summary>
    public const string AudioFormat =
        "bestaudio[ext=m4a]/bestaudio[ext=mp3]/bestaudio[ext=ogg]/bestaudio[ext=flac]/bestaudio/best";

    private const string ProgressPrefix = "GOAMPROG ";
    private const string ResultPrefix = "GOAMOUT\t";

    private static readonly IReadOnlyDictionary<string, string> Utf8Environment = new Dictionary<string, string>
    {
        ["PYTHONIOENCODING"] = "utf-8",
        ["PYTHONUTF8"] = "1",
        ["NO_COLOR"] = "1",
    };

    private readonly YtDlpManager _manager;
    private readonly AppPaths _paths;
    private readonly IAppLog _log;
    private bool _updatedThisSession;

    public YtDlpClient(YtDlpManager manager, AppPaths paths, IAppLog? log = null)
    {
        _manager = manager;
        _paths = paths;
        _log = log ?? NullLog.Instance;
    }

    // ---- argument builders (pure, unit-tested) -------------------------------------------------

    public static IReadOnlyList<string> BuildProbeArguments(string url, bool fullPlaylist)
    {
        var args = new List<string>
        {
            "--no-warnings", "--no-colors", "--ignore-config", "--skip-download", "--flat-playlist",
        };
        if (!fullPlaylist)
        {
            args.Add("--playlist-end");
            args.Add("3");
        }

        args.Add("-J");
        args.Add("--");
        args.Add(url);
        return args;
    }

    public static IReadOnlyList<string> BuildDownloadArguments(string url, string outputTemplate) => new[]
    {
        "--no-warnings", "--no-colors", "--ignore-config", "--newline", "--no-playlist",
        "--fixup", "never", "--no-mtime", "--windows-filenames",
        "-f", AudioFormat,
        "-o", outputTemplate,
        "--progress-template",
        "download:" + ProgressPrefix + "%(progress.downloaded_bytes)s %(progress.total_bytes)s %(progress.total_bytes_estimate)s",
        "--print", "after_move:" + ResultPrefix + "%(id)s\t%(filepath)s\t%(title)s",
        "--no-simulate",
        "--", url,
    };

    // ---- parsing (pure, unit-tested) -----------------------------------------------------------

    public static ProbeResult ParseProbeJson(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        string? title = root.TryGetProperty("title", out JsonElement t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()
            : null;

        var entries = new List<PlaylistEntry>();
        if (root.TryGetProperty("entries", out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement e in arr.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string? id = Str(e, "id");
                string? url = Str(e, "url");
                string entryTitle = Str(e, "title") ?? id ?? "song";

                string? candidate = url is not null && url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? url
                    : id is not null ? $"https://www.youtube.com/watch?v={id}" : null;

                if (candidate is not null && YouTubeUrl.TryNormalize(candidate, out string normalized))
                {
                    entries.Add(new PlaylistEntry(normalized, entryTitle));
                }
            }
        }

        // goam.py: a playlist is "more than one entry".
        bool isPlaylist = entries.Count > 1;
        return new ProbeResult(isPlaylist, isPlaylist ? title ?? "Playlist" : null, entries);

        static string? Str(JsonElement el, string name) =>
            el.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    public static bool TryParseProgress(string line, out double? fraction)
    {
        fraction = null;
        if (!line.StartsWith(ProgressPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string[] parts = line[ProgressPrefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 1
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double done))
        {
            for (int i = 1; i < parts.Length && i <= 2; i++)
            {
                if (double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double total)
                    && total > 0)
                {
                    fraction = Math.Clamp(done / total, 0, 1);
                    break;
                }
            }
        }

        return true;
    }

    internal static bool TryParseResult(string line, out string id, out string path, out string title)
    {
        id = path = title = string.Empty;
        if (!line.StartsWith(ResultPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string[] parts = line[ResultPrefix.Length..].Split('\t', 3);
        if (parts.Length < 2)
        {
            return false;
        }

        id = parts[0];
        path = parts[1];
        title = parts.Length > 2 ? parts[2] : parts[0];
        return true;
    }

    // ---- operations ----------------------------------------------------------------------------

    public async Task<string?> GetVersionAsync(CancellationToken ct)
    {
        if (!_manager.ExeExists)
        {
            return null;
        }

        string? version = null;
        int code = await ProcessRunner.RunAsync(
            _manager.ExePath, new[] { "--version" }, line => version ??= line.Trim(), null, ct, Utf8Environment)
            .ConfigureAwait(false);
        return code == 0 ? version : null;
    }

    public async Task<ProbeResult> ProbeAsync(
        string url, bool fullPlaylist, IProgress<double>? installProgress, CancellationToken ct)
    {
        string exe = await _manager.EnsureReadyAsync(installProgress, ct).ConfigureAwait(false);
        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();

        int code = await ProcessRunner.RunAsync(
            exe, BuildProbeArguments(url, fullPlaylist),
            line => stdout.AppendLine(line), line => stderr.AppendLine(line), ct, Utf8Environment)
            .ConfigureAwait(false);

        if (code != 0)
        {
            throw new YtDlpException("yt-dlp could not read the link.", code, Tail(stderr.ToString()));
        }

        return ParseProbeJson(stdout.ToString());
    }

    /// <summary>
    /// Downloads the audio of one video into GOAM's private downloads folder. If yt-dlp fails it is updated
    /// once (YouTube changes constantly) and the download is retried.
    /// </summary>
    public async Task<DownloadedAudio> DownloadAsync(
        string url, IProgress<DownloadProgress>? progress, IProgress<double>? installProgress, CancellationToken ct)
    {
        string exe = await _manager.EnsureReadyAsync(installProgress, ct).ConfigureAwait(false);
        try
        {
            return await RunDownloadAsync(exe, url, progress, ct).ConfigureAwait(false);
        }
        catch (YtDlpException ex) when (!_updatedThisSession)
        {
            _updatedThisSession = true;
            _log.Warn($"yt-dlp failed (exit {ex.ExitCode}); checking for a newer yt-dlp before retrying.");
            bool updated;
            try
            {
                updated = await _manager.UpdateIfNewerAsync(installProgress, ct).ConfigureAwait(false);
            }
            catch (Exception updateEx) when (updateEx is HttpRequestException or IOException or InvalidDataException
                                                 or TaskCanceledException)
            {
                _log.Warn($"Could not update yt-dlp: {updateEx.Message}");
                throw ex;
            }

            if (!updated)
            {
                throw;
            }

            return await RunDownloadAsync(exe, url, progress, ct).ConfigureAwait(false);
        }
    }

    private async Task<DownloadedAudio> RunDownloadAsync(
        string exe, string url, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(_paths.DownloadsDir);
        string template = Path.Combine(_paths.DownloadsDir, "%(id)s.%(ext)s");

        string? resultId = null, resultPath = null, resultTitle = null;
        var stderr = new System.Text.StringBuilder();

        int code = await ProcessRunner.RunAsync(
            exe, BuildDownloadArguments(url, template),
            line =>
            {
                if (TryParseProgress(line, out double? fraction))
                {
                    progress?.Report(new DownloadProgress(fraction));
                }
                else if (TryParseResult(line, out string id, out string path, out string title))
                {
                    resultId = id;
                    resultPath = path;
                    resultTitle = title;
                }
            },
            line => stderr.AppendLine(line),
            ct,
            Utf8Environment).ConfigureAwait(false);

        if (code != 0 || resultPath is null)
        {
            throw new YtDlpException("yt-dlp could not download the audio.", code, Tail(stderr.ToString()));
        }

        string full = Path.GetFullPath(resultPath);
        string root = Path.GetFullPath(_paths.DownloadsDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        {
            throw new YtDlpException("yt-dlp reported an unexpected output path.", code, string.Empty);
        }

        return new DownloadedAudio(full, resultTitle ?? resultId ?? "song", resultId ?? string.Empty);
    }

    private static string Tail(string text)
    {
        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(Environment.NewLine, lines.TakeLast(6));
    }
}
