namespace Goam.Core;

/// <summary>
/// Every folder GOAM reads or writes, resolved in one place.
/// <c>GOAM_DATA_DIR</c> and <c>GOAM_TOOLS_DIR</c> override the defaults so tests and
/// sandboxed runs never touch the real %APPDATA% / %LOCALAPPDATA%.
/// </summary>
public sealed class AppPaths
{
    public const string DataDirVariable = "GOAM_DATA_DIR";
    public const string ToolsDirVariable = "GOAM_TOOLS_DIR";

    public AppPaths(string dataDir, string toolsDir, bool isOverridden = false)
    {
        DataDir = Path.GetFullPath(dataDir);
        ToolsDir = Path.GetFullPath(toolsDir);
        IsOverridden = isOverridden;
    }

    /// <summary>Config, logs, default songs and download scratch space.</summary>
    public string DataDir { get; }

    /// <summary>Where the external yt-dlp.exe lives.</summary>
    public string ToolsDir { get; }

    /// <summary>True when the data folder came from GOAM_DATA_DIR (sandbox / test mode).</summary>
    public bool IsOverridden { get; }

    public string ConfigFile => Path.Combine(DataDir, "config.json");

    /// <summary>The key="value" file written by GOAM 1.x (Python). Read-only for migration.</summary>
    public string LegacyConfigFile => Path.Combine(DataDir, "config");

    public string LogsDir => Path.Combine(DataDir, "logs");
    public string LogFile => Path.Combine(LogsDir, "goam.log");

    /// <summary>Scratch folder for raw downloads; the only folder GOAM ever auto-cleans.</summary>
    public string DownloadsDir => Path.Combine(DataDir, "downloads");

    public string DefaultSongsDir => Path.Combine(DataDir, "Songs");

    public string YtDlpExe => Path.Combine(ToolsDir, "yt-dlp.exe");

    /// <summary>SHA-256 of the yt-dlp.exe we verified against the official release, one line of hex.</summary>
    public string YtDlpHashFile => Path.Combine(ToolsDir, "yt-dlp.exe.sha256");

    public static AppPaths FromEnvironment(Func<string, string?>? getEnv = null)
    {
        getEnv ??= Environment.GetEnvironmentVariable;

        string? dataOverride = Clean(getEnv(DataDirVariable));
        string? toolsOverride = Clean(getEnv(ToolsDirVariable));

        string appData = Clean(getEnv("APPDATA"))
                         ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string localAppData = Clean(getEnv("LOCALAPPDATA"))
                              ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        string data = dataOverride ?? Path.Combine(appData, "GOAM");
        string tools = toolsOverride ?? Path.Combine(localAppData, "GOAM", "tools");
        return new AppPaths(data, tools, isOverridden: dataOverride is not null);
    }

    private static string? Clean(string? value)
    {
        value = value?.Trim().Trim('"');
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
