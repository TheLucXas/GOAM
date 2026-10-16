using Goam.Core.Game;
using Goam.Core.Localization;
using Goam.Core.Songs;

namespace Goam.Core.Tests;

public class RelayParserTests
{
    [Theory]
    [InlineData("bind \"=\" \"3\"", "=", 3)]
    [InlineData("bind = 12", "=", 12)]
    [InlineData("BIND \"KP_INS\" \"7\"\r\nbind \"x\" \"goam_play\"", "kp_ins", 7)]
    [InlineData("unbindall\nbind \"x\" \"goam_play\"\nbind '=' '5'\nbind \"mouse1\" \"+attack\"", "=", 5)]
    [InlineData("bind \"f6\" \"2\"\nbind \"f6\" \"9\"", "f6", 2)]
    public void FindsTheBoundNumber(string content, string key, int expected)
    {
        Assert.Equal(expected, RelayParser.ParseIndex(content, key));
    }

    [Theory]
    [InlineData("", "=")]
    [InlineData("bind \"x\" \"goam_play\"", "=")]
    [InlineData("bind \"=\" \"notanumber\"", "=")]
    [InlineData("bind \"=\" \"3\"", "")]
    public void ReturnsNullWhenThereIsNoNumber(string content, string key)
    {
        Assert.Null(RelayParser.ParseIndex(content, key));
    }

    [Fact]
    public void KeysWithRegexCharactersAreMatchedLiterally()
    {
        Assert.Equal(4, RelayParser.ParseIndex("bind \".\" \"4\"", "."));
        Assert.Null(RelayParser.ParseIndex("bind \"a\" \"4\"", "."));
        Assert.Equal(6, RelayParser.ParseIndex("bind \"[\" \"6\"", "["));
    }
}

public class CfgBuilderTests
{
    private static IReadOnlyList<SongEntry> Songs(params string[] stems) =>
        stems.Select((s, i) => new SongEntry(i + 1, s, s + ".wav", @"X:\songs\" + s + ".wav")).ToArray();

    [Fact]
    public void TracksCfgListsSongsInOrderWithCrLf()
    {
        var t = new Localizer("en");
        string cfg = CfgBuilder.BuildTracksCfg(t, Songs("Alpha", "Beta"));

        Assert.Equal(
            "echo \"GOAM song list:\"\r\necho \"1 - Alpha\"\r\necho \"2 - Beta\"\r\n",
            cfg);
    }

    [Fact]
    public void CurrentTrackCfgUsesTheLanguage()
    {
        Assert.Equal("echo \"Now playing: Alpha\"\r\n", CfgBuilder.BuildCurrentTrackCfg(new Localizer("en"), "Alpha"));
        Assert.Equal("echo \"Reproduciendo: Alpha\"\r\n", CfgBuilder.BuildCurrentTrackCfg(new Localizer("es"), "Alpha"));
    }

    [Fact]
    public void MainCfgKeepsTheCommandLayoutTheGameDependsOn()
    {
        var t = new Localizer("en");
        string cfg = CfgBuilder.BuildMainCfg(t, "x", "=", Songs("Alpha", "Beta"));
        string[] lines = cfg.Split("\r\n");

        Assert.Equal("alias list_tracks \"exec goam_tracks.cfg\"", lines[0]);
        Assert.Contains("alias update_cfg \"host_writeconfig goam_relay_key\"", lines);
        Assert.Contains(
            "alias goam_on \"voice_inputfromfile 1; voice_loopback 1; +voicerecord; alias goam_play goam_off\"",
            lines);
        Assert.Contains(
            "alias goam_off \"-voicerecord; voice_inputfromfile 0; voice_loopback 0; alias goam_play goam_on\"",
            lines);
        Assert.Contains("bind x goam_play", lines);
        Assert.Contains("alias 1 \"bind = 1; update_cfg; echo Sound selected: Alpha\"", lines);
        Assert.Contains("alias 2 \"bind = 2; update_cfg; echo Sound selected: Beta\"", lines);
        Assert.Contains("echo \"Press x to play  |  li = song list  |  np = current song\"", lines);
        Assert.EndsWith("\r\n", cfg);
        Assert.DoesNotContain("\r\n", cfg.Replace("\r\n", string.Empty)); // CRLF only, no stray CR or LF
        Assert.DoesNotContain('\n', cfg.Replace("\r\n", string.Empty));
    }

    [Fact]
    public void SongNamesCannotInjectConsoleCommands()
    {
        var t = new Localizer("en");
        string evil = "x\"; quit; echo \"// pwn";
        string cfg = CfgBuilder.BuildMainCfg(t, "x", "=", Songs(evil))
                     + CfgBuilder.BuildTracksCfg(t, Songs(evil));

        Assert.DoesNotContain("quit; echo \"", cfg);
        Assert.DoesNotContain("; quit", cfg);
        foreach (string line in cfg.Split("\r\n").Where(l => l.StartsWith("alias 1 ", StringComparison.Ordinal)))
        {
            // exactly the 3 separators we write ourselves: bind; update_cfg; echo
            Assert.Equal(2, line.Count(c => c == ';'));
            Assert.Equal(2, line.Count(c => c == '"'));
        }
    }

    [Fact]
    public void SanitiserNeutralisesQuotesSemicolonsAndComments()
    {
        Assert.Equal("a'b,c/ /d", CfgBuilder.SanitizeForCfg("a\"b;c//d"));
        Assert.Equal("clean", CfgBuilder.SanitizeForCfg("cl\r\nean"));
    }
}

public class SteamLocatorTests
{
    [Fact]
    public void PicksTheMostRecentUserFolderThatHasCsgo()
    {
        using var dir = new TempDir();
        string userdata = dir.Sub("userdata");
        string older = Path.Combine(userdata, "111");
        string newer = Path.Combine(userdata, "222");
        string noGame = Path.Combine(userdata, "333");
        Directory.CreateDirectory(Path.Combine(older, SteamLocator.CsgoAppId));
        Directory.CreateDirectory(Path.Combine(newer, SteamLocator.CsgoAppId));
        Directory.CreateDirectory(noGame);
        Directory.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-5));
        Directory.SetLastWriteTimeUtc(newer, DateTime.UtcNow.AddDays(-1));
        Directory.SetLastWriteTimeUtc(noGame, DateTime.UtcNow);

        Assert.Equal(newer, SteamLocator.FindUserFolder(userdata));
    }

    [Fact]
    public void NoUserdataMeansNoRelayPath()
    {
        using var dir = new TempDir();
        Assert.Null(SteamLocator.FindUserFolder(dir.Combine("userdata")));
    }
}

public class RelayWatcherTests
{
    private static async Task<bool> WaitFor(Func<bool> condition, int timeoutMs = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(25);
        }

        return condition();
    }

    [Fact]
    public async Task FiresWhenTheFileAppearsAndWhenItChanges()
    {
        using var dir = new TempDir();
        string relay = dir.Combine("goam_relay_key.cfg");
        int count = 0;

        using var watcher = new RelayWatcher(
            () => relay, () => Interlocked.Increment(ref count),
            debounce: TimeSpan.FromMilliseconds(50), pollInterval: TimeSpan.FromMilliseconds(200));

        await Task.Delay(300);
        Assert.Equal(0, Volatile.Read(ref count)); // nothing there yet

        File.WriteAllText(relay, "bind \"=\" \"1\"");
        Assert.True(await WaitFor(() => Volatile.Read(ref count) >= 1), "creation was not noticed");

        int afterCreate = Volatile.Read(ref count);
        await Task.Delay(500);
        Assert.Equal(afterCreate, Volatile.Read(ref count)); // unchanged file: no repeat

        File.WriteAllText(relay, "bind \"=\" \"2222\"");
        Assert.True(await WaitFor(() => Volatile.Read(ref count) > afterCreate), "modification was not noticed");
    }

    [Fact]
    public async Task PollingAloneIsEnoughWhenNoFolderExistsYet()
    {
        using var dir = new TempDir();
        string folder = dir.Combine("late", "cfg");
        string relay = Path.Combine(folder, "goam_relay_key.cfg");
        int count = 0;

        using var watcher = new RelayWatcher(
            () => relay, () => Interlocked.Increment(ref count),
            debounce: TimeSpan.FromMilliseconds(50), pollInterval: TimeSpan.FromMilliseconds(100));

        await Task.Delay(250);
        Directory.CreateDirectory(folder);
        File.WriteAllText(relay, "bind \"=\" \"1\"");

        Assert.True(await WaitFor(() => Volatile.Read(ref count) >= 1), "late file was not noticed");
    }

    [Fact]
    public async Task ResetReportsTheSameFileAgain()
    {
        using var dir = new TempDir();
        string relay = dir.Combine("r.cfg");
        File.WriteAllText(relay, "x");
        int count = 0;

        using var watcher = new RelayWatcher(
            () => relay, () => Interlocked.Increment(ref count),
            debounce: TimeSpan.FromMilliseconds(50), pollInterval: TimeSpan.FromMilliseconds(100));

        Assert.True(await WaitFor(() => Volatile.Read(ref count) == 1));
        watcher.Reset();
        watcher.CheckNow();
        Assert.True(await WaitFor(() => Volatile.Read(ref count) == 2));
    }
}
