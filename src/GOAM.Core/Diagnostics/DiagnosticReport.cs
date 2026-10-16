using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Goam.Core.Diagnostics;

/// <summary>Replaces personal values (user name, profile paths, machine name) before text leaves the app.</summary>
public sealed class Redactor
{
    private readonly List<(string Value, string Placeholder)> _rules = new();

    public Redactor Add(string? value, string placeholder)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.Length >= 3)
        {
            _rules.Add((value, placeholder));
            _rules.Sort((a, b) => b.Value.Length.CompareTo(a.Value.Length)); // longest first
        }

        return this;
    }

    public string Apply(string text)
    {
        foreach ((string value, string placeholder) in _rules)
        {
            text = text.Replace(value, placeholder, StringComparison.OrdinalIgnoreCase);
            // also the forward-slash spelling
            text = text.Replace(value.Replace('\\', '/'), placeholder, StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }

    public static Redactor ForCurrentUser(AppPaths? paths = null)
    {
        var r = new Redactor();
        if (paths is not null)
        {
            r.Add(paths.DataDir, "%GOAM_DATA%");
            r.Add(paths.ToolsDir, "%GOAM_TOOLS%");
        }

        r.Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%");
        r.Add(Environment.UserName, "<user>");
        r.Add(Environment.MachineName, "<machine>");
        return r;
    }
}

public sealed record DiagnosticSnapshot(
    string Language,
    bool CsgoDirConfigured,
    bool CsgoExeFound,
    string? CsgoDir,
    bool SongsDirExists,
    string? SongsDir,
    int SongCount,
    bool SteamFound,
    bool RelayCfgExists,
    bool GoamRunning,
    bool YtDlpPresent,
    string? YtDlpVersion,
    bool StartWithWindows,
    IReadOnlyList<string> LogTail);

/// <summary>The text behind the "Copy report" button: everything useful for an issue, nothing personal.</summary>
public static class DiagnosticReport
{
    public static string AppVersion()
    {
        Assembly asm = Assembly.GetEntryAssembly() ?? typeof(DiagnosticReport).Assembly;
        string? v = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(v))
        {
            v = asm.GetName().Version?.ToString(3) ?? "unknown";
        }

        int plus = v.IndexOf('+');
        return plus >= 0 ? v[..plus] : v;
    }

    public static string Build(DiagnosticSnapshot s, Redactor redactor)
    {
        var sb = new StringBuilder();
        sb.AppendLine("GOAM diagnostic report");
        sb.AppendLine("======================");
        sb.AppendLine($"GOAM version : {AppVersion()}");
        sb.AppendLine($"OS           : {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        sb.AppendLine($".NET runtime : {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"Language     : {s.Language}");
        sb.AppendLine($"GOAM running : {YesNo(s.GoamRunning)}");
        sb.AppendLine($"Start w/ Win : {YesNo(s.StartWithWindows)}");
        sb.AppendLine();
        sb.AppendLine($"CS:GO folder : {(s.CsgoDirConfigured ? redactor.Apply(s.CsgoDir ?? "?") : "(not set)")}");
        sb.AppendLine($"  csgo.exe   : {(s.CsgoDirConfigured ? YesNo(s.CsgoExeFound) : "n/a")}");
        sb.AppendLine($"Songs folder : {(s.SongsDir is null ? "(not set)" : redactor.Apply(s.SongsDir))}");
        sb.AppendLine($"  exists     : {YesNo(s.SongsDirExists)}   songs: {s.SongCount}");
        sb.AppendLine($"Steam found  : {YesNo(s.SteamFound)}");
        sb.AppendLine($"Relay cfg    : {(s.RelayCfgExists ? "present" : "not present (play a song in-game once)")}");
        sb.AppendLine($"yt-dlp       : {(s.YtDlpPresent ? s.YtDlpVersion ?? "installed (version unknown)" : "not installed yet")}");
        sb.AppendLine();
        sb.AppendLine("Last log lines");
        sb.AppendLine("--------------");
        if (s.LogTail.Count == 0)
        {
            sb.AppendLine("(empty)");
        }
        else
        {
            foreach (string line in s.LogTail)
            {
                sb.AppendLine(redactor.Apply(line));
            }
        }

        return sb.ToString();
    }

    private static string YesNo(bool value) => value ? "yes" : "no";
}
