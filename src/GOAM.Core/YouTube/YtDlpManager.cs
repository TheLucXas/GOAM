using System.Globalization;
using System.Security.Cryptography;
using Goam.Core.Diagnostics;

namespace Goam.Core.YouTube;

/// <summary>
/// Installs and updates the official yt-dlp.exe. GOAM never embeds yt-dlp: YouTube breaks it every few
/// weeks, so it is downloaded on first use, verified against the SHA2-256SUMS file of the same GitHub
/// release, and refreshed when a download fails and on a periodic check.
/// </summary>
public sealed class YtDlpManager
{
    public const string ReleaseBaseUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/";
    public const string ExeAssetName = "yt-dlp.exe";
    public const string SumsAssetName = "SHA2-256SUMS";
    private const long MaxExeBytes = 200L * 1024 * 1024;

    private static readonly TimeSpan DefaultCheckInterval = TimeSpan.FromDays(7);

    private readonly AppPaths _paths;
    private readonly HttpClient _http;
    private readonly IAppLog _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _baseUrl;

    public YtDlpManager(AppPaths paths, HttpClient http, IAppLog? log = null, string? baseUrl = null)
    {
        _paths = paths;
        _http = http;
        _log = log ?? NullLog.Instance;
        _baseUrl = baseUrl ?? ReleaseBaseUrl;
    }

    public string ExePath => _paths.YtDlpExe;

    public bool ExeExists => File.Exists(_paths.YtDlpExe);

    public static bool IsCheckDue(DateTime? lastCheckUtc, DateTime nowUtc, TimeSpan? interval = null) =>
        lastCheckUtc is null || nowUtc - lastCheckUtc.Value >= (interval ?? DefaultCheckInterval);

    /// <summary>Finds <paramref name="fileName"/> in a "hash  name" checksum list (case-insensitive hash).</summary>
    public static string? ParseChecksum(string sumsText, string fileName)
    {
        foreach (string raw in sumsText.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length < 66)
            {
                continue;
            }

            int split = line.IndexOfAny(new[] { ' ', '\t' });
            if (split != 64)
            {
                continue;
            }

            string hash = line[..64];
            string name = line[64..].Trim().TrimStart('*');
            if (string.Equals(name, fileName, StringComparison.Ordinal) && IsHex64(hash))
            {
                return hash.ToLowerInvariant();
            }
        }

        return null;
    }

    public static string ComputeSha256(string path)
    {
        using FileStream fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    /// <summary>
    /// Returns the path of a verified yt-dlp.exe, downloading it if it is missing or if it does not match
    /// the hash recorded when it was verified.
    /// </summary>
    public async Task<string> EnsureReadyAsync(IProgress<double>? progress, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (IsLocalCopyTrusted())
            {
                return _paths.YtDlpExe;
            }

            _log.Info("yt-dlp.exe is missing or unverified; downloading the official release.");
            await DownloadVerifiedAsync(progress, ct).ConfigureAwait(false);
            return _paths.YtDlpExe;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Checks GitHub for a newer build and installs it. Returns true if the exe was replaced.</summary>
    public async Task<bool> UpdateIfNewerAsync(IProgress<double>? progress, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string sums = await GetStringAsync(_baseUrl + SumsAssetName, ct).ConfigureAwait(false);
            string? expected = ParseChecksum(sums, ExeAssetName)
                               ?? throw new InvalidDataException("SHA2-256SUMS has no entry for yt-dlp.exe.");

            if (File.Exists(_paths.YtDlpExe))
            {
                string current = ComputeSha256(_paths.YtDlpExe);
                if (string.Equals(current, expected, StringComparison.Ordinal))
                {
                    WriteHashFile(current);
                    return false;
                }
            }

            await DownloadAsync(expected, progress, ct).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsLocalCopyTrusted()
    {
        try
        {
            if (!File.Exists(_paths.YtDlpExe) || !File.Exists(_paths.YtDlpHashFile))
            {
                return false;
            }

            string recorded = File.ReadAllText(_paths.YtDlpHashFile).Trim().ToLowerInvariant();
            return IsHex64(recorded)
                   && string.Equals(recorded, ComputeSha256(_paths.YtDlpExe), StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task DownloadVerifiedAsync(IProgress<double>? progress, CancellationToken ct)
    {
        string sums = await GetStringAsync(_baseUrl + SumsAssetName, ct).ConfigureAwait(false);
        string expected = ParseChecksum(sums, ExeAssetName)
                          ?? throw new InvalidDataException("SHA2-256SUMS has no entry for yt-dlp.exe.");
        await DownloadAsync(expected, progress, ct).ConfigureAwait(false);
    }

    private async Task DownloadAsync(string expectedSha256, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(_paths.ToolsDir);
        string tmp = Path.Combine(_paths.ToolsDir, "yt-dlp.exe.download");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl + ExeAssetName);
            using HttpResponseMessage response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            long? total = response.Content.Headers.ContentLength;
            if (total is > MaxExeBytes)
            {
                throw new InvalidDataException("yt-dlp.exe is unexpectedly large; refusing to download it.");
            }

            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long received = 0;
            await using (Stream net = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var file = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            {
                byte[] buffer = new byte[1 << 16];
                int n;
                while ((n = await net.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    received += n;
                    if (received > MaxExeBytes)
                    {
                        throw new InvalidDataException("yt-dlp.exe is unexpectedly large; aborting.");
                    }

                    hasher.AppendData(buffer, 0, n);
                    await file.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                    if (total is > 0)
                    {
                        progress?.Report((double)received / total.Value);
                    }
                }
            }

            string actual = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
            if (!string.Equals(actual, expectedSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"yt-dlp.exe checksum mismatch (expected {expectedSha256[..12]}..., got {actual[..12]}...). The file was discarded.");
            }

            File.Move(tmp, _paths.YtDlpExe, overwrite: true);
            WriteHashFile(actual);
            _log.Info($"yt-dlp.exe installed and verified (sha256 {actual[..12]}...).");
        }
        finally
        {
            try
            {
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }
            }
            catch (IOException)
            {
                // leave it; replaced next time
            }
        }
    }

    private void WriteHashFile(string hash)
    {
        Directory.CreateDirectory(_paths.ToolsDir);
        File.WriteAllText(_paths.YtDlpHashFile, hash.ToLower(CultureInfo.InvariantCulture));
    }

    private async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private static bool IsHex64(string s) =>
        s.Length == 64 && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    /// <summary>One shared client with a sensible User-Agent and timeout for GitHub downloads.</summary>
    public static HttpClient CreateHttpClient(string appVersion)
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            ConnectTimeout = TimeSpan.FromSeconds(20),
        })
        {
            Timeout = TimeSpan.FromMinutes(10),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"GOAM/{appVersion}");
        return client;
    }
}
