using System.Text;
using Goam.Core.Localization;
using Goam.Core.Songs;

namespace Goam.Core.Game;

/// <summary>
/// Builds the three .cfg files GOAM puts in csgo\cfg. The command layout is exactly what GOAM 1.x
/// wrote (the game and users' muscle memory depend on it); only song names are made safe.
/// </summary>
public static class CfgBuilder
{
    private const string NewLine = "\r\n"; // GOAM 1.x wrote through Python text mode on Windows => CRLF

    /// <summary>
    /// Song names are echoed inside quoted console commands. ';' would start a new command,
    /// '"' would end the string and '//' starts a comment, so they are neutralised.
    /// </summary>
    public static string SanitizeForCfg(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (char.IsControl(c))
            {
                continue;
            }

            sb.Append(c switch
            {
                '"' => '\'',
                ';' => ',',
                _ => c,
            });
        }

        return sb.ToString().Replace("//", "/ /", StringComparison.Ordinal);
    }

    public static string BuildTracksCfg(Localizer t, IReadOnlyList<SongEntry> songs)
    {
        var sb = new StringBuilder();
        sb.Append("echo \"").Append(SanitizeForCfg(t["cfg_tracks_header"])).Append('"').Append(NewLine);
        foreach (SongEntry song in songs)
        {
            sb.Append("echo \"").Append(song.Index).Append(" - ").Append(SanitizeForCfg(song.Stem)).Append('"')
                .Append(NewLine);
        }

        return sb.ToString();
    }

    public static string BuildCurrentTrackCfg(Localizer t, string stem)
    {
        string msg = SanitizeForCfg(t.T("cfg_now_playing", stem));
        return $"echo \"{msg}\"{NewLine}";
    }

    public static string BuildMainCfg(Localizer t, string playKey, string relayKey, IReadOnlyList<SongEntry> songs)
    {
        string hint = SanitizeForCfg(t.T("cfg_play_hint", playKey));
        var lines = new List<string>
        {
            "alias list_tracks \"exec goam_tracks.cfg\"",
            "alias list list_tracks",
            "alias li list_tracks",
            "alias current_track \"exec current_track.cfg\"",
            "alias curtrack current_track",
            "alias nowplaying current_track",
            "alias np current_track",
            "alias goam_play \"goam_on\"",
            "alias goam_on \"voice_inputfromfile 1; voice_loopback 1; +voicerecord; alias goam_play goam_off\"",
            "alias goam_off \"-voicerecord; voice_inputfromfile 0; voice_loopback 0; alias goam_play goam_on\"",
            "alias update_cfg \"host_writeconfig goam_relay_key\"",
            $"bind {playKey} goam_play",
            string.Empty,

            // ASCII-art banner shown in the console when the cfg loads
            "echo \"======================================\"",
            "echo \"                                     \"",
            "echo \"  ██████   ██████   █████  ███    ███\"",
            "echo \" ██       ██    ██ ██   ██ ████  ████\"",
            "echo \" ██   ███ ██    ██ ███████ ██ ████ ██\"",
            "echo \" ██    ██ ██    ██ ██   ██ ██  ██  ██\"",
            "echo \"  ██████   ██████  ██   ██ ██      ██\"",
            "echo \"                                     \"",
            "echo \"======================================\"",
            "echo \"                                     \"",
            $"echo \"{hint}\"",
            "echo \"                                     \"",
            "echo \"                                     \"",
            "echo \"                                     \"",
            string.Empty,
        };

        string selectedPrefix = SanitizeForCfg(t["cfg_track_selected"]);
        foreach (SongEntry song in songs)
        {
            lines.Add(
                $"alias {song.Index} \"bind {relayKey} {song.Index}; update_cfg; echo {selectedPrefix} {SanitizeForCfg(song.Stem)}\"");
        }

        return string.Join(NewLine, lines) + NewLine;
    }
}
