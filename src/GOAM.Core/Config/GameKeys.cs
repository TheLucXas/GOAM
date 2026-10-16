using System.Text.RegularExpressions;

namespace Goam.Core.Config;

/// <summary>
/// Validation for Source-engine key names that GOAM writes into .cfg files
/// (<c>bind &lt;key&gt; goam_play</c>, <c>alias N "bind &lt;relay&gt; N; ..."</c>).
/// Anything that could terminate the command or inject another one is rejected.
/// </summary>
public static class GameKeys
{
    // Named keys: f5, kp_ins, mouse4, mwheelup, enter, space, ...
    private static readonly Regex Named = new("^[a-z0-9_]{1,24}$", RegexOptions.Compiled);

    // Single punctuation keys that Source accepts literally. ';' and '"' are intentionally absent.
    private const string SymbolKeys = "=-[]\\',./`";

    public static string Normalize(string? raw) => (raw ?? string.Empty).Trim().ToLowerInvariant();

    public static bool IsValid(string? key)
    {
        string k = Normalize(key);
        if (k.Length == 0)
        {
            return false;
        }

        if (k.Length == 1 && SymbolKeys.Contains(k[0]))
        {
            return true;
        }

        return Named.IsMatch(k);
    }
}
