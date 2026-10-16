using System.Net;
using System.Security.Cryptography;
using System.Text;
using Goam.Core.YouTube;

namespace Goam.Core.Tests;

public class YouTubeUrlTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("http://youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ")]
    [InlineData("https://music.youtube.com/watch?v=dQw4w9WgXcQ&list=RDAMVM")]
    [InlineData("https://m.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("  https://www.youtube.com/playlist?list=PLabc123  ")]
    [InlineData("HTTPS://WWW.YOUTUBE.COM/watch?v=dQw4w9WgXcQ")]
    public void AcceptsYouTubeLinks(string url)
    {
        Assert.True(YouTubeUrl.IsValid(url));
        Assert.True(YouTubeUrl.TryNormalize(url, out string n));
        Assert.StartsWith("http", n);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello world")]
    [InlineData("https://vimeo.com/12345")]
    [InlineData("https://youtube.com.evil.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://evil.com/youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://notyoutube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("javascript:alert(1)//youtube.com/x")]
    [InlineData("file:///C:/youtube.com/x")]
    [InlineData("ftp://youtube.com/x")]
    [InlineData("-o X:\\x youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?v=a\nb")]
    [InlineData("https://youtube.com/")]
    public void RejectsEverythingElse(string url)
    {
        Assert.False(YouTubeUrl.IsValid(url));
    }

    [Fact]
    public void RejectsCredentialsInTheLink()
    {
        // built at runtime so the source never contains an address-like literal
        string url = "https://user:pw" + '@' + "youtube.com/watch?v=dQw4w9WgXcQ";
        Assert.False(YouTubeUrl.IsValid(url));
    }

    [Fact]
    public void RejectsGiantInput()
    {
        Assert.False(YouTubeUrl.IsValid("https://youtube.com/watch?v=" + new string('a', 5000)));
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=PL123&t=40s", "https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?si=abc", "https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/playlist?list=PLabc", "https://www.youtube.com/playlist?list=PLabc")]
    public void CleanReducesToTheVideoId(string input, string expected)
    {
        Assert.Equal(expected, YouTubeUrl.Clean(input));
    }
}

public class YtDlpClientTests
{
    private const string Url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";

    [Fact]
    public void DownloadArgumentsKeepTheUrlAsOneArgumentAfterDoubleDash()
    {
        IReadOnlyList<string> args = YtDlpClient.BuildDownloadArguments(Url, @"X:\data\downloads\%(id)s.%(ext)s");

        Assert.Equal(Url, args[^1]);
        Assert.Equal("--", args[^2]);

        int f = args.ToList().IndexOf("-f");
        Assert.Equal(YtDlpClient.AudioFormat, args[f + 1]);
        Assert.Contains("--no-playlist", args);
        Assert.Contains("never", args); // --fixup never: no ffmpeg needed
    }

    [Fact]
    public void AHostileUrlStillTravelsAsASingleDataArgument()
    {
        string hostile = "https://youtube.com/watch?v=x --exec calc.exe & del *";
        IReadOnlyList<string> args = YtDlpClient.BuildDownloadArguments(hostile, "out");

        Assert.Equal(hostile, args[^1]);
        Assert.Equal("--", args[^2]);
        Assert.Equal(1, args.Count(a => a.Contains("calc.exe")));
    }

    [Fact]
    public void ProbeArgumentsLimitPlaylistInspection()
    {
        var quick = YtDlpClient.BuildProbeArguments(Url, fullPlaylist: false);
        Assert.Contains("--flat-playlist", quick);
        Assert.Equal("3", quick[quick.ToList().IndexOf("--playlist-end") + 1]);
        Assert.Equal(Url, quick[^1]);

        var full = YtDlpClient.BuildProbeArguments(Url, fullPlaylist: true);
        Assert.DoesNotContain("--playlist-end", full);
    }

    [Fact]
    public void SingleVideoJsonIsNotAPlaylist()
    {
        var result = YtDlpClient.ParseProbeJson("{\"id\":\"dQw4w9WgXcQ\",\"title\":\"Song\",\"formats\":[]}");
        Assert.False(result.IsPlaylist);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void PlaylistJsonListsOnlySafeEntries()
    {
        string json = """
        {
          "title": "My Mix",
          "entries": [
            {"id":"aaaaaaaaaaa","title":"One","url":"https://www.youtube.com/watch?v=aaaaaaaaaaa"},
            {"id":"bbbbbbbbbbb","title":"Two"},
            {"id":"ccccccccccc","title":"Evil","url":"https://evil.example/x"},
            null,
            {"id":"ddddddddddd","title":"Four","url":"https://youtu.be/ddddddddddd"}
          ]
        }
        """;

        var result = YtDlpClient.ParseProbeJson(json);

        Assert.True(result.IsPlaylist);
        Assert.Equal("My Mix", result.Title);
        Assert.Equal(new[] { "One", "Two", "Four" }, result.Entries.Select(e => e.Title).ToArray());
        Assert.All(result.Entries, e => Assert.True(YouTubeUrl.IsValid(e.Url)));
    }

    [Theory]
    [InlineData("GOAMPROG 512 1024 NA", true, 0.5)]
    [InlineData("GOAMPROG 512 NA 2048", true, 0.25)]
    [InlineData("GOAMPROG 4096 1024 NA", true, 1.0)]
    [InlineData("GOAMPROG NA NA NA", true, null)]
    [InlineData("[download] 10%", false, null)]
    public void ProgressLines(string line, bool recognised, double? fraction)
    {
        Assert.Equal(recognised, YtDlpClient.TryParseProgress(line, out double? f));
        Assert.Equal(fraction, f);
    }

    [Theory]
    [InlineData("ERROR: [youtube] x: Unable to download API page: [WinError 10013] Intento de acceso a un socket", true)]
    [InlineData("ERROR: Unable to download webpage: <urlopen error [Errno 11001] getaddrinfo failed>", true)]
    [InlineData("ERROR: The read operation timed out", true)]
    [InlineData("ERROR: [youtube] x: Video unavailable. This video is private", false)]
    [InlineData("ERROR: Requested format is not available", false)]
    [InlineData("", false)]
    public void RecognisesTransientNetworkErrors(string details, bool transient)
    {
        Assert.Equal(transient, YtDlpClient.IsTransientNetworkError(details));
    }

    [Fact]
    public void ResultLineKeepsTabsInsideTheTitle()
    {
        Assert.True(YtDlpClient.TryParseResult("GOAMOUT\tabc\tX:\\d\\abc.m4a\tA\tB title", out string id, out string path, out string title));
        Assert.Equal("abc", id);
        Assert.Equal("X:\\d\\abc.m4a", path);
        Assert.Equal("A\tB title", title);
    }
}

public class YtDlpManagerTests
{
    private static readonly byte[] FakeExe = Encoding.ASCII.GetBytes("MZ-fake-yt-dlp-binary");

    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private sealed class FakeGitHub : HttpMessageHandler
    {
        public byte[] Exe = FakeExe;
        public string Sums = string.Empty;
        public int ExeRequests;
        public int FailuresRemaining;
        public int TotalRequests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            TotalRequests++;
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new HttpRequestException("simulated connection refusal");
            }

            string url = request.RequestUri!.ToString();
            if (url.EndsWith("SHA2-256SUMS", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Sums) });
            }

            if (url.EndsWith("yt-dlp.exe", StringComparison.Ordinal))
            {
                ExeRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Exe) });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static (YtDlpManager Manager, AppPaths Paths, FakeGitHub Http) Create(TempDir dir)
    {
        var paths = new AppPaths(dir.Combine("data"), dir.Combine("tools"), isOverridden: true);
        var http = new FakeGitHub { Sums = $"{Sha(FakeExe)}  yt-dlp.exe\n{new string('0', 64)}  yt-dlp\n" };
        var manager = new YtDlpManager(paths, new HttpClient(http), baseUrl: "https://example.test/dl/");
        return (manager, paths, http);
    }

    [Fact]
    public void ParsesTheChecksumFileFormat()
    {
        string sums =
            "1fa6733c37ea6fb51c99ad8fe785e7b7e5f3246c9b980230329d4fb72ed8d4d6  yt-dlp\n" +
            "66674953FE251B89F4D08C5F0E35E0728679BD67AB3D7D05C0562AF101DD3E7A  yt-dlp.exe\r\n" +
            "072aad4f2a7604e92155f61a275a4752dc64046c8f6d90df3710525d94cd37c1  yt-dlp.tar.gz\n";

        Assert.Equal("66674953fe251b89f4d08c5f0e35e0728679bd67ab3d7d05c0562af101dd3e7a",
            YtDlpManager.ParseChecksum(sums, "yt-dlp.exe"));
        Assert.Equal("1fa6733c37ea6fb51c99ad8fe785e7b7e5f3246c9b980230329d4fb72ed8d4d6",
            YtDlpManager.ParseChecksum(sums, "yt-dlp"));
        Assert.Null(YtDlpManager.ParseChecksum(sums, "missing.exe"));
        Assert.Null(YtDlpManager.ParseChecksum("garbage", "yt-dlp.exe"));
    }

    [Fact]
    public async Task InstallsAndVerifiesOnFirstUse()
    {
        using var dir = new TempDir();
        var (manager, paths, http) = Create(dir);

        string exe = await manager.EnsureReadyAsync(null, CancellationToken.None);

        Assert.Equal(paths.YtDlpExe, exe);
        Assert.Equal(FakeExe, File.ReadAllBytes(exe));
        Assert.Equal(Sha(FakeExe), File.ReadAllText(paths.YtDlpHashFile));
        Assert.Equal(1, http.ExeRequests);

        // second call: trusted local copy, no new download
        await manager.EnsureReadyAsync(null, CancellationToken.None);
        Assert.Equal(1, http.ExeRequests);
    }

    [Fact]
    public async Task ChecksumMismatchDiscardsTheDownload()
    {
        using var dir = new TempDir();
        var (manager, paths, http) = Create(dir);
        http.Exe = Encoding.ASCII.GetBytes("tampered binary");

        await Assert.ThrowsAsync<InvalidDataException>(
            () => manager.EnsureReadyAsync(null, CancellationToken.None));

        Assert.False(File.Exists(paths.YtDlpExe));
        Assert.False(File.Exists(Path.Combine(paths.ToolsDir, "yt-dlp.exe.download")));
        Assert.False(File.Exists(paths.YtDlpHashFile));
    }

    [Fact]
    public async Task ModifiedLocalCopyIsReplacedByTheVerifiedOne()
    {
        using var dir = new TempDir();
        var (manager, paths, http) = Create(dir);
        await manager.EnsureReadyAsync(null, CancellationToken.None);
        File.WriteAllText(paths.YtDlpExe, "someone replaced me");

        await manager.EnsureReadyAsync(null, CancellationToken.None);

        Assert.Equal(FakeExe, File.ReadAllBytes(paths.YtDlpExe));
        Assert.Equal(2, http.ExeRequests);
    }

    [Fact]
    public async Task UpdateOnlyDownloadsWhenTheReleaseDiffers()
    {
        using var dir = new TempDir();
        var (manager, paths, http) = Create(dir);
        await manager.EnsureReadyAsync(null, CancellationToken.None);
        Assert.False(await manager.UpdateIfNewerAsync(null, CancellationToken.None));
        Assert.Equal(1, http.ExeRequests);

        byte[] newer = Encoding.ASCII.GetBytes("newer-yt-dlp-release");
        http.Exe = newer;
        http.Sums = $"{Sha(newer)}  yt-dlp.exe\n";

        Assert.True(await manager.UpdateIfNewerAsync(null, CancellationToken.None));
        Assert.Equal(newer, File.ReadAllBytes(paths.YtDlpExe));
        Assert.Equal(Sha(newer), File.ReadAllText(paths.YtDlpHashFile));
    }

    [Fact]
    public async Task TransientConnectionFailuresAreRetried()
    {
        using var dir = new TempDir();
        var (manager, paths, http) = Create(dir);
        manager.SetRetryDelay(TimeSpan.FromMilliseconds(10));
        http.FailuresRemaining = 2; // the first two connections are refused

        await manager.EnsureReadyAsync(null, CancellationToken.None);

        Assert.True(File.Exists(paths.YtDlpExe));
        Assert.True(http.TotalRequests >= 4); // 2 refused + sums + exe
    }

    [Fact]
    public async Task PersistentConnectionFailureEventuallyGivesUp()
    {
        using var dir = new TempDir();
        var (manager, paths, http) = Create(dir);
        manager.SetRetryDelay(TimeSpan.FromMilliseconds(10));
        http.FailuresRemaining = 100;

        await Assert.ThrowsAsync<HttpRequestException>(
            () => manager.EnsureReadyAsync(null, CancellationToken.None));
        Assert.Equal(4, http.TotalRequests);
        Assert.False(File.Exists(paths.YtDlpExe));
    }

    [Fact]
    public void PeriodicCheckInterval()
    {
        DateTime now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(YtDlpManager.IsCheckDue(null, now));
        Assert.False(YtDlpManager.IsCheckDue(now.AddDays(-2), now));
        Assert.True(YtDlpManager.IsCheckDue(now.AddDays(-8), now));
    }
}
