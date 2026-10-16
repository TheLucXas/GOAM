using System.Globalization;
using System.Text;

namespace Goam.Core.Songs;

/// <summary>
/// All file-name rules in one place: turning titles into file names, validating renames,
/// and keeping every operation inside the songs folder.
/// </summary>
public static class SongNames
{
    public const int MaxStemLength = 100;
    public const string Extension = ".wav";
    public const string FallbackStem = "song";

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Port of goam.py's title -> file name rule: every non-alphanumeric character becomes '_',
    /// leading/trailing '_' are stripped, at most 100 characters. Never returns an empty or reserved name.
    /// </summary>
    public static string FromTitle(string? title)
    {
        var sb = new StringBuilder();
        foreach (Rune rune in (title ?? string.Empty).EnumerateRunes())
        {
            sb.Append(IsAlphanumeric(rune) ? rune.ToString() : "_");
        }

        string s = TakeRunes(sb.ToString().Trim('_'), MaxStemLength);
        if (s.Length == 0)
        {
            return FallbackStem;
        }

        return IsReservedDeviceName(s) ? s + "_" : s;
    }

    /// <summary>
    /// Validates a user-typed name for renaming. Path separators and other invalid characters become '_',
    /// a typed ".wav" suffix is dropped, trailing dots/spaces are trimmed.
    /// </summary>
    public static bool TryNormalizeRename(string? input, out string stem)
    {
        stem = string.Empty;
        string s = (input ?? string.Empty).Trim();
        if (s.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            s = s[..^Extension.Length];
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            sb.Append(char.IsControl(c) || Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        s = TakeRunes(sb.ToString().Trim().TrimEnd('.', ' '), MaxStemLength).TrimEnd('.', ' ');
        if (s.Length == 0 || s.All(c => c == '_') || IsReservedDeviceName(s))
        {
            return false;
        }

        stem = s;
        return true;
    }

    public static bool IsReservedDeviceName(string stem)
    {
        int dot = stem.IndexOf('.');
        string head = dot >= 0 ? stem[..dot] : stem;
        return ReservedDeviceNames.Contains(head.TrimEnd());
    }

    /// <summary>
    /// Returns the full path of <paramref name="fileName"/> inside <paramref name="directory"/>,
    /// or throws if the result would escape that folder (path traversal) or the name has a path part.
    /// </summary>
    public static string ResolveInside(string directory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.IndexOfAny(new[] { '\\', '/', ':' }) >= 0
            || fileName is "." or "..")
        {
            throw new ArgumentException("Invalid file name.", nameof(fileName));
        }

        string root = Path.GetFullPath(directory);
        string full = Path.GetFullPath(Path.Combine(root, fileName));
        string? parent = Path.GetDirectoryName(full);
        if (parent is null || !string.Equals(
                parent.TrimEnd(Path.DirectorySeparatorChar),
                root.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("File name escapes the songs folder.", nameof(fileName));
        }

        return full;
    }

    /// <summary>Same rule as Python's str.isalnum(): letters and numbers of any script.</summary>
    internal static bool IsAlphanumeric(Rune rune)
    {
        switch (Rune.GetUnicodeCategory(rune))
        {
            case UnicodeCategory.UppercaseLetter:
            case UnicodeCategory.LowercaseLetter:
            case UnicodeCategory.TitlecaseLetter:
            case UnicodeCategory.ModifierLetter:
            case UnicodeCategory.OtherLetter:
            case UnicodeCategory.DecimalDigitNumber:
            case UnicodeCategory.LetterNumber:
            case UnicodeCategory.OtherNumber:
                return true;
            default:
                return false;
        }
    }

    private static string TakeRunes(string s, int max)
    {
        if (s.Length <= max)
        {
            return s;
        }

        var sb = new StringBuilder();
        int taken = 0;
        foreach (Rune r in s.EnumerateRunes())
        {
            if (taken++ >= max)
            {
                break;
            }

            sb.Append(r.ToString());
        }

        return sb.ToString();
    }
}
