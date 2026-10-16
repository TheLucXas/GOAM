using Goam.Core.Audio;
using Goam.Core.Diagnostics;

namespace Goam.Core.Songs;

/// <summary>Converts a downloaded or local audio file into a song in the songs folder.</summary>
public sealed class SongImporter
{
    private readonly AudioConverter _converter;
    private readonly IAppLog _log;

    public SongImporter(AudioConverter converter, IAppLog? log = null)
    {
        _converter = converter;
        _log = log ?? NullLog.Instance;
    }

    /// <summary>
    /// Writes <paramref name="songsDir"/>\&lt;safe name&gt;.wav and returns the stem.
    /// <paramref name="deleteSource"/> is true only for GOAM's own temporary downloads; a user's local
    /// file is never touched.
    /// </summary>
    /// <exception cref="UnsupportedAudioFormatException">The input cannot be decoded.</exception>
    public string Import(string inputPath, string title, string songsDir, bool deleteSource, CancellationToken ct = default)
    {
        Directory.CreateDirectory(songsDir);
        string stem = SongNames.FromTitle(title);
        string target = SongNames.ResolveInside(songsDir, stem + SongNames.Extension);

        try
        {
            _converter.ConvertToCsgoWav(inputPath, target, ct);
        }
        finally
        {
            if (deleteSource
                && File.Exists(inputPath)
                && !string.Equals(Path.GetFullPath(inputPath), target, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Delete(inputPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _log.Warn($"Could not remove temporary file: {ex.Message}");
                }
            }
        }

        return stem;
    }

    /// <summary>Empties GOAM's private download scratch folder (never the songs folder).</summary>
    public static void CleanDownloads(string downloadsDir, IAppLog? log = null)
    {
        try
        {
            if (!Directory.Exists(downloadsDir))
            {
                return;
            }

            foreach (string file in Directory.EnumerateFiles(downloadsDir))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    log?.Warn($"Could not remove leftover download: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Warn($"Could not clean downloads folder: {ex.Message}");
        }
    }
}
