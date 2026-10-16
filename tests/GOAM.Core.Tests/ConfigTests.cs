using Goam.Core.Config;
using Goam.Core.Localization;

namespace Goam.Core.Tests;

public class LegacyConfigTests
{
    [Theory]
    [InlineData("csgo_dir=\"X:\\Games\\CSGO\"", "csgo_dir", "X:\\Games\\CSGO")]
    [InlineData("goam_relay_key=\"=\"", "goam_relay_key", "=")]
    [InlineData("  volume_percent  =  \"85\"  ", "volume_percent", "85")]
    [InlineData("start_minimized=\"1\"", "start_minimized", "1")]
    [InlineData("start_minimized_windows=\"0\"", "start_minimized_windows", "0")]
    [InlineData("language=\"es\" trailing junk", "language", "es")]
    public void ParsesKnownKeys(string line, string key, string value)
    {
        Assert.Equal((key, value), LegacyConfig.ParseLine(line));
    }

    [Theory]
    [InlineData("no equals sign here")]
    [InlineData("unknown_key=\"x\"")]
    [InlineData("start=\"1\"")]
    [InlineData("")]
    public void IgnoresUnknownOrMalformedLines(string line)
    {
        Assert.Equal((null, null), LegacyConfig.ParseLine(line));
    }

    [Fact]
    public void KnownKeyWithoutQuotesYieldsNullValue()
    {
        Assert.Equal(("songs_dir", (string?)null), LegacyConfig.ParseLine("songs_dir=X:\\Music"));
    }

    [Fact]
    public void ParsesAFullPythonConfigIncludingCrLf()
    {
        string text = "csgo_dir=\"X:\\Steam\\Counter-Strike Global Offensive\"\r\n"
                      + "goam_play_key=\"x\"\r\ngoam_relay_key=\"=\"\r\nvolume_percent=\"55\"\r\n"
                      + "songs_dir=\"X:\\Songs\"\r\nlanguage=\"en\"\r\nstart_with_windows=\"1\"\r\n"
                      + "start_minimized=\"0\"\r\nstart_minimized_windows=\"1\"\r\nminimize_to_tray=\"1\"";

        AppConfig cfg = LegacyConfig.ToConfig(LegacyConfig.Parse(text));

        Assert.Equal("X:\\Steam\\Counter-Strike Global Offensive", cfg.CsgoDir);
        Assert.Equal("X:\\Songs", cfg.SongsDir);
        Assert.Equal("x", cfg.PlayKey);
        Assert.Equal("=", cfg.RelayKey);
        Assert.Equal(55, cfg.VolumePercent);
        Assert.Equal("en", cfg.Language);
        Assert.True(cfg.StartWithWindows);
        Assert.False(cfg.StartMinimized);
        Assert.True(cfg.StartMinimizedWindows);
        Assert.True(cfg.MinimizeToTray);
    }
}

public class ConfigStoreTests
{
    private static AppPaths PathsIn(TempDir dir) => new(dir.Combine("data"), dir.Combine("tools"), isOverridden: true);

    [Fact]
    public void FirstRunYieldsDefaultsAndFlagsFirstRun()
    {
        using var dir = new TempDir();
        var result = new ConfigStore(PathsIn(dir)).Load();

        Assert.True(result.IsFirstRun);
        Assert.False(result.MigratedFromLegacy);
        Assert.Equal(70, result.Config.VolumePercent);
        Assert.Equal("x", result.Config.PlayKey);
        Assert.Equal("=", result.Config.RelayKey);
        Assert.Null(result.Config.SongsDir);
        Assert.Contains(result.Config.Language, Localizer.SupportedLanguages);
    }

    [Fact]
    public void SavedConfigRoundTrips()
    {
        using var dir = new TempDir();
        var paths = PathsIn(dir);
        var store = new ConfigStore(paths);
        var cfg = store.Load().Config;
        cfg.SongsDir = dir.Combine("songs");
        cfg.CsgoDir = dir.Combine("csgo");
        cfg.VolumePercent = 123;
        cfg.PlayKey = "f6";
        cfg.RelayKey = "kp_ins";
        cfg.Language = "es";
        cfg.MinimizeToTray = true;
        store.Save(cfg);

        var again = new ConfigStore(paths).Load();
        Assert.False(again.IsFirstRun);
        Assert.Equal(cfg.SongsDir, again.Config.SongsDir);
        Assert.Equal(cfg.CsgoDir, again.Config.CsgoDir);
        Assert.Equal(123, again.Config.VolumePercent);
        Assert.Equal("f6", again.Config.PlayKey);
        Assert.Equal("kp_ins", again.Config.RelayKey);
        Assert.Equal("es", again.Config.Language);
        Assert.True(again.Config.MinimizeToTray);
    }

    [Fact]
    public void LegacyPythonConfigIsMigratedAndLeftUntouched()
    {
        using var dir = new TempDir();
        var paths = PathsIn(dir);
        Directory.CreateDirectory(paths.DataDir);
        string legacy = "songs_dir=\"" + dir.Combine("MySongs") + "\"\nvolume_percent=\"40\"\nlanguage=\"es\"\ngoam_play_key=\"f7\"";
        File.WriteAllText(paths.LegacyConfigFile, legacy);

        var result = new ConfigStore(paths).Load();

        Assert.True(result.MigratedFromLegacy);
        Assert.False(result.IsFirstRun); // songs_dir was present
        Assert.Equal(40, result.Config.VolumePercent);
        Assert.Equal("f7", result.Config.PlayKey);
        Assert.Equal("es", result.Config.Language);
        Assert.True(File.Exists(paths.ConfigFile));
        Assert.Equal(legacy, File.ReadAllText(paths.LegacyConfigFile));
    }

    [Fact]
    public void ExistingJsonWinsOverLegacyFile()
    {
        using var dir = new TempDir();
        var paths = PathsIn(dir);
        var store = new ConfigStore(paths);
        var cfg = store.Load().Config;
        cfg.VolumePercent = 11;
        store.Save(cfg);
        File.WriteAllText(paths.LegacyConfigFile, "volume_percent=\"99\"");

        var result = new ConfigStore(paths).Load();
        Assert.False(result.MigratedFromLegacy);
        Assert.Equal(11, result.Config.VolumePercent);
    }

    [Fact]
    public void CorruptJsonFallsBackToDefaultsAndKeepsABackup()
    {
        using var dir = new TempDir();
        var paths = PathsIn(dir);
        Directory.CreateDirectory(paths.DataDir);
        File.WriteAllText(paths.ConfigFile, "{ this is not json");

        var result = new ConfigStore(paths).Load();

        Assert.Equal(70, result.Config.VolumePercent);
        Assert.True(File.Exists(paths.ConfigFile + ".bad"));
    }

    [Fact]
    public void InvalidValuesAreRepaired()
    {
        var cfg = new AppConfig
        {
            VolumePercent = 999,
            PlayKey = "bad key; quit",
            RelayKey = "\"",
            Language = "fr",
            CsgoDir = "   ",
        };

        ConfigStore.Normalize(cfg);

        Assert.Equal(200, cfg.VolumePercent);
        Assert.Equal("x", cfg.PlayKey);
        Assert.Equal("=", cfg.RelayKey);
        Assert.Contains(cfg.Language, Localizer.SupportedLanguages);
        Assert.Null(cfg.CsgoDir);
    }

    [Theory]
    [InlineData("x", true)]
    [InlineData("=", true)]
    [InlineData("kp_ins", true)]
    [InlineData("mouse4", true)]
    [InlineData("F6", true)]
    [InlineData("[", true)]
    [InlineData("", false)]
    [InlineData(";", false)]
    [InlineData("\"", false)]
    [InlineData("a b", false)]
    [InlineData("x; quit", false)]
    [InlineData("kp_ins\n", true)]
    [InlineData("this_key_name_is_far_too_long", false)]
    public void GameKeyValidation(string key, bool valid)
    {
        Assert.Equal(valid, GameKeys.IsValid(key));
    }
}

public class AppPathsTests
{
    [Fact]
    public void EnvironmentVariablesOverrideDefaults()
    {
        var env = new Dictionary<string, string?>
        {
            ["GOAM_DATA_DIR"] = @"X:\sandbox\data",
            ["GOAM_TOOLS_DIR"] = @"X:\sandbox\tools",
            ["APPDATA"] = @"X:\fake\roaming",
            ["LOCALAPPDATA"] = @"X:\fake\local",
        };

        var paths = AppPaths.FromEnvironment(k => env.GetValueOrDefault(k));

        Assert.True(paths.IsOverridden);
        Assert.Equal(@"X:\sandbox\data", paths.DataDir);
        Assert.Equal(@"X:\sandbox\tools", paths.ToolsDir);
        Assert.Equal(@"X:\sandbox\data\config.json", paths.ConfigFile);
        Assert.Equal(@"X:\sandbox\tools\yt-dlp.exe", paths.YtDlpExe);
    }

    [Fact]
    public void DefaultsLiveUnderAppDataAndLocalAppData()
    {
        var env = new Dictionary<string, string?>
        {
            ["APPDATA"] = @"X:\fake\roaming",
            ["LOCALAPPDATA"] = @"X:\fake\local",
        };

        var paths = AppPaths.FromEnvironment(k => env.GetValueOrDefault(k));

        Assert.False(paths.IsOverridden);
        Assert.Equal(@"X:\fake\roaming\GOAM", paths.DataDir);
        Assert.Equal(@"X:\fake\local\GOAM\tools", paths.ToolsDir);
    }
}
