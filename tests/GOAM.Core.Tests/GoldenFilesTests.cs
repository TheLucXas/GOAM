using System.Text;
using Goam.Core.Audio;
using Goam.Core.Game;
using Goam.Core.Localization;
using Goam.Core.Songs;

namespace Goam.Core.Tests;

/// <summary>
/// The golden files in tests/assets/golden were produced by the real GOAM 1.x code (goam.py at tag
/// v1.x-python-final) for: songs "Alpha", "beta song", "Gamma-3" (the 22050 Hz tone asset), English,
/// play key f6, relay key kp_ins, volume 85 %, song 2 selected. GOAM 2 must write the same bytes.
/// </summary>
public class GoldenFilesTests
{
    private static string Golden(string name) => Path.Combine(AppContext.BaseDirectory, "assets", "golden", name);

    private static IReadOnlyList<SongEntry> Songs() => new[]
    {
        new SongEntry(1, "Alpha", "Alpha.wav", @"X:\songs\Alpha.wav"),
        new SongEntry(2, "beta song", "beta song.wav", @"X:\songs\beta song.wav"),
        new SongEntry(3, "Gamma-3", "Gamma-3.wav", @"X:\songs\Gamma-3.wav"),
    };

    private static string Text(string name) => File.ReadAllText(Golden(name), new UTF8Encoding(false));

    [Fact]
    public void MainCfgIsIdenticalToGoam1x() =>
        Assert.Equal(Text("goam.cfg"), CfgBuilder.BuildMainCfg(new Localizer("en"), "f6", "kp_ins", Songs()));

    [Fact]
    public void TracksCfgIsIdenticalToGoam1x() =>
        Assert.Equal(Text("goam_tracks.cfg"), CfgBuilder.BuildTracksCfg(new Localizer("en"), Songs()));

    [Fact]
    public void CurrentTrackCfgIsIdenticalToGoam1x() =>
        Assert.Equal(Text("current_track.cfg"), CfgBuilder.BuildCurrentTrackCfg(new Localizer("en"), "beta song"));

    [Fact]
    public void VoiceInputWavIsByteIdenticalToGoam1x()
    {
        using var dir = new TempDir();
        string target = dir.Combine("voice_input.wav");

        new AudioConverter(new AudioDecoder())
            .WriteVoiceInput(Assets.Path(Assets.Wav22k), target, Volume.GetDbAdjust(85));

        Assert.Equal(File.ReadAllBytes(Golden("voice_input.wav")), File.ReadAllBytes(target));
    }
}
