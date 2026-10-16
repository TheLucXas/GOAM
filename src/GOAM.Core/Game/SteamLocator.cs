using Microsoft.Win32;

namespace Goam.Core.Game;

/// <summary>Finds Steam, its userdata folder and the relay cfg that CS:GO writes.</summary>
public interface ISteamLocator
{
    /// <summary>Full path of steam.exe, or null.</summary>
    string? FindSteamExe();

    /// <summary>Path of goam_relay_key.cfg inside the most recently used Steam account folder, or null.</summary>
    string? FindRelayCfgPath();

    /// <summary>The CS:GO install folder (contains csgo.exe) found in any Steam library, or null.</summary>
    string? FindCsgoDir();
}

public sealed class SteamLocator : ISteamLocator
{
    /// <summary>Steam AppID of CS:GO (Legacy).</summary>
    public const string CsgoAppId = "4465480";

    public string? FindSteamExe()
    {
        string? root = FindSteamRoot();
        if (root is null)
        {
            return null;
        }

        string exe = Path.Combine(root, "steam.exe");
        return File.Exists(exe) ? exe : null;
    }

    public string? FindRelayCfgPath()
    {
        string? root = FindSteamRoot();
        if (root is null)
        {
            return null;
        }

        string? user = FindUserFolder(Path.Combine(root, "userdata"));
        return user is null
            ? null
            : Path.Combine(user, CsgoAppId, "local", "cfg", "goam_relay_key.cfg");
    }

    public string? FindCsgoDir()
    {
        string? root = FindSteamRoot();
        if (root is null)
        {
            return null;
        }

        var libraries = new List<string> { root };
        try
        {
            string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                libraries.AddRange(ParseLibraryFolders(File.ReadAllText(vdf)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // fall back to the main library only
        }

        foreach (string lib in libraries)
        {
            string candidate = Path.Combine(lib, "steamapps", "common", "Counter-Strike Global Offensive");
            if (File.Exists(Path.Combine(candidate, "csgo.exe")))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Extracts every <c>"path"  "..."</c> value from Steam's libraryfolders.vdf.</summary>
    internal static IEnumerable<string> ParseLibraryFolders(string vdf)
    {
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(vdf, "\"path\"\\s+\"([^\"]+)\""))
        {
            string path = m.Groups[1].Value.Replace("\\\\", "\\");
            if (path.Length > 0)
            {
                yield return path;
            }
        }
    }

    /// <summary>Most-recently-modified userdata\&lt;id&gt; folder that contains the CS:GO app folder.</summary>
    internal static string? FindUserFolder(string userdataDir)
    {
        try
        {
            if (!Directory.Exists(userdataDir))
            {
                return null;
            }

            return new DirectoryInfo(userdataDir)
                .EnumerateDirectories()
                .Where(d => Directory.Exists(Path.Combine(d.FullName, CsgoAppId)))
                .OrderByDescending(d => d.LastWriteTimeUtc)
                .Select(d => d.FullName)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Environment variable that forces the Steam folder (tests and sandboxed runs).</summary>
    public const string SteamDirVariable = "GOAM_STEAM_DIR";

    private static string? FindSteamRoot()
    {
        string? forced = Environment.GetEnvironmentVariable(SteamDirVariable);
        if (!string.IsNullOrWhiteSpace(forced))
        {
            string full = Path.GetFullPath(forced.Trim().Trim('"'));
            return Directory.Exists(full) ? full : null;
        }

        string?[] candidates =
        {
            ReadRegistry(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
            ReadRegistry(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
        };

        foreach (string? c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c))
            {
                continue;
            }

            try
            {
                string full = Path.GetFullPath(c.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(full))
                {
                    return full;
                }
            }
            catch (ArgumentException)
            {
                // malformed registry value, try the next candidate
            }
        }

        return null;
    }

    private static string? ReadRegistry(RegistryHive hive, string subKey, string name)
    {
        try
        {
            using RegistryKey? key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(subKey);
            return key?.GetValue(name) as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
