using System.Text;

namespace Goam.Core.Game;

/// <summary>Locations of everything GOAM writes into a CS:GO installation.</summary>
public sealed class GameFiles
{
    public const string CsgoExecutable = "csgo.exe";
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public GameFiles(string csgoDir)
    {
        CsgoDir = Path.GetFullPath(csgoDir);
    }

    public string CsgoDir { get; }
    public string CfgDir => Path.Combine(CsgoDir, "csgo", "cfg");
    public string VoiceInputWav => Path.Combine(CsgoDir, "voice_input.wav");
    public string MainCfg => Path.Combine(CfgDir, "goam.cfg");
    public string TracksCfg => Path.Combine(CfgDir, "goam_tracks.cfg");
    public string CurrentTrackCfg => Path.Combine(CfgDir, "current_track.cfg");

    public static bool LooksLikeCsgoDir(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return Directory.Exists(path) && File.Exists(Path.Combine(path, CsgoExecutable));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public void WriteCfg(string path, string content)
    {
        Directory.CreateDirectory(CfgDir);
        File.WriteAllText(path, content, Utf8NoBom);
    }

    /// <summary>Removes voice_input.wav and the three generated .cfg files (never anything else).</summary>
    public void Cleanup()
    {
        foreach (string file in new[] { VoiceInputWav, MainCfg, TracksCfg, CurrentTrackCfg })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }
}
