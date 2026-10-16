using Goam.Core.Audio;
using Goam.Core.Config;
using Goam.Core.Diagnostics;
using Goam.Core.Game;
using Goam.Core.Localization;
using Goam.Core.Platform;
using Goam.Core.Songs;

namespace Goam.Core.Tests;

public class EngineTests : IDisposable
{
    private sealed class FakeSteam : ISteamLocator
    {
        public string? RelayPath { get; set; }
        public string? FindSteamExe() => null;
        public string? FindRelayCfgPath() => RelayPath;
    }

    private readonly TempDir _dir = new();
    private readonly FakeSteam _steam = new();
    private readonly AppPaths _paths;
    private readonly string _songs;
    private readonly string _csgo;
    private readonly AppConfig _config;
    private readonly List<GoamEngine> _engines = new();

    public EngineTests()
    {
        _paths = new AppPaths(_dir.Combine("data"), _dir.Combine("tools"), isOverridden: true);
        _songs = _dir.Sub("songs");
        _csgo = _dir.Sub("csgo");
        foreach (string name in new[] { "Alpha", "Beta", "Gamma" })
        {
            File.Copy(Assets.Path(Assets.Wav22k), Path.Combine(_songs, name + ".wav"));
        }

        _config = new AppConfig { SongsDir = _songs, CsgoDir = _csgo, Language = "en", VolumePercent = 100 };
    }

    private GoamEngine CreateEngine()
    {
        var engine = new GoamEngine(
            _config, new ConfigStore(_paths), _paths, new Localizer("en"), _steam,
            new AudioConverter(new AudioDecoder()));
        _engines.Add(engine);
        return engine;
    }

    private string VoiceInput => Path.Combine(_csgo, "voice_input.wav");
    private string CfgDir => Path.Combine(_csgo, "csgo", "cfg");

    public void Dispose()
    {
        foreach (GoamEngine e in _engines)
        {
            e.Dispose();
        }

        _dir.Dispose();
    }

    private static async Task<bool> WaitFor(Func<bool> condition, int timeoutMs = 6000)
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
    public void InitializeWritesCfgFilesAndListsSongs()
    {
        GoamEngine engine = CreateEngine();
        engine.Initialize();

        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, engine.Songs.Select(s => s.Stem).ToArray());
        Assert.True(File.Exists(Path.Combine(CfgDir, "goam.cfg")));
        Assert.True(File.Exists(Path.Combine(CfgDir, "goam_tracks.cfg")));
        Assert.Contains("alias 3 \"bind = 3;", File.ReadAllText(Path.Combine(CfgDir, "goam.cfg")));
        Assert.Contains("echo \"2 - Beta\"", File.ReadAllText(Path.Combine(CfgDir, "goam_tracks.cfg")));
        Assert.False(File.Exists(VoiceInput)); // nothing selected yet, like GOAM 1.x
    }

    [Fact]
    public void SelectingASongWritesVoiceInputAtTheConfiguredVolume()
    {
        GoamEngine engine = CreateEngine();
        engine.Initialize();

        Assert.True(engine.SelectSong(2));

        Assert.Equal(2, engine.SelectedIndex);
        WavInfo wav = WavReader.Read(VoiceInput);
        Assert.Equal(22050, wav.SampleRate);
        Assert.Equal(1, wav.Channels);
        double expected = WavReader.Read(Path.Combine(_songs, "Beta.wav")).Peak * Math.Pow(10, -25.0 / 20);
        Assert.InRange(wav.Peak, expected * 0.97, expected * 1.03);
        Assert.Contains("Now playing: Beta", File.ReadAllText(Path.Combine(CfgDir, "current_track.cfg")));
    }

    [Fact]
    public void InvalidIndexIsIgnored()
    {
        GoamEngine engine = CreateEngine();
        engine.Initialize();
        engine.SelectSong(1);

        Assert.False(engine.SelectSong(0));
        Assert.False(engine.SelectSong(99));
        Assert.Equal(1, engine.SelectedIndex);
    }

    [Fact]
    public void StoppingRemovesEveryGeneratedFileAndStartingRestoresTheCfgs()
    {
        GoamEngine engine = CreateEngine();
        engine.Initialize();
        engine.SelectSong(1);
        Assert.True(File.Exists(VoiceInput));

        engine.SetRunning(false);

        Assert.False(File.Exists(VoiceInput));
        Assert.False(File.Exists(Path.Combine(CfgDir, "goam.cfg")));
        Assert.False(File.Exists(Path.Combine(CfgDir, "goam_tracks.cfg")));
        Assert.False(File.Exists(Path.Combine(CfgDir, "current_track.cfg")));
        Assert.False(engine.IsRunning);

        // selecting while stopped must not put files back into the game folder
        engine.SelectSong(2);
        Assert.False(File.Exists(VoiceInput));

        engine.SetRunning(true);
        Assert.True(File.Exists(Path.Combine(CfgDir, "goam.cfg")));
    }

    [Fact]
    public void ShutdownCleansUp()
    {
        GoamEngine engine = CreateEngine();
        engine.Initialize();
        engine.SelectSong(3);

        engine.Shutdown();

        Assert.False(File.Exists(VoiceInput));
        Assert.False(File.Exists(Path.Combine(CfgDir, "goam.cfg")));
    }

    [Fact]
    public async Task InGameRelayChangeSwitchesTheSong()
    {
        string relay = Path.Combine(_dir.Sub("steam"), "goam_relay_key.cfg");
        _steam.RelayPath = relay;
        GoamEngine engine = CreateEngine();
        engine.Initialize();

        File.WriteAllText(relay, "bind \"=\" \"3\"\r\n");

        Assert.True(await WaitFor(() => engine.SelectedIndex == 3 && File.Exists(VoiceInput)),
            "relay change to song 3 was not applied");

        File.WriteAllText(relay, "bind \"=\" \"2\"\r\n");
        Assert.True(await WaitFor(() => engine.SelectedIndex == 2), "relay change to song 2 was not applied");
    }

    [Fact]
    public void StartupRestoresTheTrackFromTheRelayFile()
    {
        string relay = Path.Combine(_dir.Sub("steam"), "goam_relay_key.cfg");
        File.WriteAllText(relay, "bind \"=\" \"2\"");
        _steam.RelayPath = relay;

        GoamEngine engine = CreateEngine();
        engine.Initialize();

        Assert.Equal(2, engine.SelectedIndex);
    }

    [Fact]
    public void RenameKeepsTheSameSongSelectedAndRewritesTheCfg()
    {
        GoamEngine engine = CreateEngine();
        engine.Initialize();
        engine.SelectSong(1); // Alpha

        string stem = engine.Rename(engine.Songs[0], "Zulu");

        Assert.Equal("Zulu", stem);
        Assert.Equal(new[] { "Beta", "Gamma", "Zulu" }, engine.Songs.Select(s => s.Stem).ToArray());
        Assert.Equal("Zulu", engine.SelectedSong!.Stem);
        Assert.Contains("echo \"3 - Zulu\"", File.ReadAllText(Path.Combine(CfgDir, "goam_tracks.cfg")));
    }

    [Fact]
    public void DeleteRemovesTheFileAndUpdatesTheList()
    {
        GoamEngine engine = CreateEngine();
        engine.Initialize();

        engine.Delete(engine.Songs[1]); // Beta

        Assert.False(File.Exists(Path.Combine(_songs, "Beta.wav")));
        Assert.Equal(new[] { "Alpha", "Gamma" }, engine.Songs.Select(s => s.Stem).ToArray());
    }

    [Fact]
    public void ChangingTheKeysRegeneratesTheCfgAndPersistsTheConfig()
    {
        GoamEngine engine = CreateEngine();
        engine.Initialize();

        engine.SetPlayKey("F6");
        engine.SetRelayKey("kp_ins");

        string main = File.ReadAllText(Path.Combine(CfgDir, "goam.cfg"));
        Assert.Contains("bind f6 goam_play", main);
        Assert.Contains("alias 1 \"bind kp_ins 1;", main);
        Assert.Throws<ArgumentException>(() => engine.SetPlayKey("a; quit"));

        var reloaded = new ConfigStore(_paths).Load().Config;
        Assert.Equal("f6", reloaded.PlayKey);
        Assert.Equal("kp_ins", reloaded.RelayKey);
    }

    [Fact]
    public void ImportedSongIsSelected()
    {
        GoamEngine engine = CreateEngine();
        engine.Initialize();

        File.Copy(Assets.Path(Assets.Wav22k), Path.Combine(_songs, "Aaa.wav"));
        engine.AddAndSelect("Aaa");

        Assert.Equal("Aaa", engine.SelectedSong!.Stem);
        Assert.Equal(1, engine.SelectedIndex);
        Assert.True(File.Exists(VoiceInput));
    }

    [Fact]
    public void ImporterConvertsAndOnlyDeletesTemporarySources()
    {
        var importer = new SongImporter(new AudioConverter(new AudioDecoder()));
        string local = Path.Combine(_dir.Path, "My Local Track.wav");
        File.Copy(Assets.Path(Assets.Wav22k), local);

        string stem = importer.Import(local, "My Local Track", _songs, deleteSource: false);
        Assert.Equal("My_Local_Track", stem);
        Assert.True(File.Exists(Path.Combine(_songs, "My_Local_Track.wav")));
        Assert.True(File.Exists(local)); // user's file untouched

        string temp = Path.Combine(_dir.Path, "dl.wav");
        File.Copy(Assets.Path(Assets.Wav22k), temp);
        importer.Import(temp, "Downloaded: Song?", _songs, deleteSource: true);
        Assert.True(File.Exists(Path.Combine(_songs, "Downloaded__Song.wav")));
        Assert.False(File.Exists(temp));
    }

    [Fact]
    public void CleanDownloadsOnlyTouchesItsOwnFolder()
    {
        string downloads = _dir.Sub("dl");
        File.WriteAllText(Path.Combine(downloads, "abc.m4a.part"), "x");
        File.WriteAllText(Path.Combine(_songs, "keep.m4a"), "user file with a 'temp' extension");

        SongImporter.CleanDownloads(downloads);

        Assert.Empty(Directory.GetFiles(downloads));
        Assert.True(File.Exists(Path.Combine(_songs, "keep.m4a")));
    }
}

public class MiscCoreTests
{
    [Fact]
    public void EveryEnglishStringExistsInSpanishAndViceVersa()
    {
        Assert.Empty(Localizer.MissingKeys("es"));
        var en = Localizer.Table("en").Keys;
        Assert.Empty(Localizer.Table("es").Keys.Except(en));
    }

    [Fact]
    public void FormatStringsAcceptTheirArguments()
    {
        foreach (string lang in Localizer.SupportedLanguages)
        {
            var t = new Localizer(lang);
            Assert.Contains("70%", t.T("volume_label", 70, -27.5));
            Assert.Contains("-27.5", t.T("volume_label", 70, -27.5));
            Assert.Contains("+4.0", t.T("volume_label", 150, 4.0));
            Assert.Contains("3", t.T("track_label_fmt", 3, "Song"));
            Assert.Contains("Song", t.T("track_label_fmt", 3, "Song"));
        }
    }

    [Fact]
    public void LanguageNormalisationAndToggle()
    {
        Assert.Equal("es", Localizer.Normalize("es-AR"));
        Assert.Equal("en", Localizer.Normalize("EN-us"));
        Assert.Null(Localizer.Normalize("fr"));

        var t = new Localizer("es");
        Assert.Equal("en", t.Toggle());
        Assert.Equal("es", t.Toggle());
        Assert.Equal("missing_key", t.T("missing_key"));
    }

    [Fact]
    public void SandboxedPathsNeverUseTheRealRegistry()
    {
        using var dir = new TempDir();
        var paths = new AppPaths(dir.Combine("d"), dir.Combine("t"), isOverridden: true);

        IStartupRegistration reg = StartupRegistrationFactory.Create(paths);

        Assert.IsType<FileStartupRegistration>(reg);
        Assert.False(reg.IsEnabled());
        reg.SetEnabled(true, @"X:\Apps\GOAM.exe");
        Assert.True(reg.IsEnabled());
        Assert.Contains("--startup", File.ReadAllText(Path.Combine(paths.DataDir, "startup-registration.txt")));
        reg.SetEnabled(false, @"X:\Apps\GOAM.exe");
        Assert.False(reg.IsEnabled());
    }

    [Fact]
    public void NormalRunsUseTheRegistryImplementation()
    {
        var paths = new AppPaths(@"X:\x\d", @"X:\x\t", isOverridden: false);
        Assert.IsType<RegistryStartupRegistration>(StartupRegistrationFactory.Create(paths));
    }

    [Fact]
    public void ReportContainsNoPersonalData()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var snapshot = new DiagnosticSnapshot(
            Language: "en", CsgoDirConfigured: true, CsgoExeFound: true,
            CsgoDir: Path.Combine(profile, "Steam", "csgo"), SongsDirExists: true,
            SongsDir: Path.Combine(profile, "Music", "songs"), SongCount: 12, SteamFound: true,
            RelayCfgExists: false, GoamRunning: true, YtDlpPresent: true, YtDlpVersion: "2026.08.19",
            StartWithWindows: false,
            LogTail: new[] { $"ERROR could not open {Path.Combine(profile, "x.wav")} for {Environment.UserName}" });

        string report = DiagnosticReport.Build(snapshot, Redactor.ForCurrentUser());

        Assert.Contains("GOAM version", report);
        Assert.Contains("2026.08.19", report);
        Assert.DoesNotContain(profile, report, StringComparison.OrdinalIgnoreCase);
        if (Environment.UserName.Length >= 3)
        {
            Assert.DoesNotContain(Environment.UserName, report, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("%USERPROFILE%", report);
    }

    [Fact]
    public void LogRotatesAndKeepsTheNewestEntries()
    {
        using var dir = new TempDir();
        string file = dir.Combine("logs", "goam.log");
        var log = new FileLog(file, maxBytes: 400, keep: 2);

        for (int i = 0; i < 30; i++)
        {
            log.Info($"entry number {i:000} " + new string('x', 30));
        }

        Assert.True(File.Exists(file));
        Assert.True(File.Exists(dir.Combine("logs", "goam.1.log")));
        Assert.True(File.Exists(dir.Combine("logs", "goam.2.log")));
        Assert.False(File.Exists(dir.Combine("logs", "goam.3.log")));
        Assert.Contains("entry number 029", log.Tail(5).Last());
    }
}
