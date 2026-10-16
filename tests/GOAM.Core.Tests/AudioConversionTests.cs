using Goam.Core.Audio;

namespace Goam.Core.Tests;

public class AudioConversionTests
{
    private static readonly AudioConverter Converter = new(new AudioDecoder());

    private static WavInfo ConvertAsset(string asset, TempDir dir)
    {
        string output = dir.Combine("out.wav");
        Converter.ConvertToCsgoWav(Assets.Path(asset), output);
        return WavReader.Read(output);
    }

    private static void AssertCsgoFormatTone(WavInfo wav)
    {
        // Exact container format CS:GO needs
        Assert.Equal(1, wav.Channels);
        Assert.Equal(22050, wav.SampleRate);
        Assert.Equal(16, wav.Bits);

        // ~1 second of a 440 Hz tone at 0.4 FS (MP3 adds a few ms of codec padding)
        Assert.InRange(wav.Seconds, 0.95, 1.12);
        Assert.InRange(wav.Peak, 10000, 14000);
    }

    [Fact]
    public void WavAt22kStaysCsgoFormat()
    {
        using var dir = new TempDir();
        AssertCsgoFormatTone(ConvertAsset(Assets.Wav22k, dir));
    }

    [Fact]
    public void OggVorbisStereo44kBecomesMono22k()
    {
        using var dir = new TempDir();
        AssertCsgoFormatTone(ConvertAsset(Assets.Ogg, dir));
    }

    [MediaFoundationFact]
    public void Mp3Stereo44kBecomesMono22k()
    {
        using var dir = new TempDir();
        AssertCsgoFormatTone(ConvertAsset(Assets.Mp3, dir));
    }

    [MediaFoundationFact]
    public void Flac48kMonoBecomes22k()
    {
        using var dir = new TempDir();
        AssertCsgoFormatTone(ConvertAsset(Assets.Flac, dir));
    }

    /// <summary>Signal power at <paramref name="hz"/> (Goertzel), normalised by length.</summary>
    private static double PowerAt(WavInfo wav, double hz)
    {
        int n = Math.Min(wav.Samples.Length, wav.SampleRate); // first second
        double w = 2 * Math.PI * hz / wav.SampleRate;
        double coeff = 2 * Math.Cos(w);
        double s1 = 0, s2 = 0;
        for (int i = 0; i < n; i++)
        {
            double s0 = wav.Samples[i] / 32768.0 + coeff * s1 - s2;
            s2 = s1;
            s1 = s0;
        }

        return (s1 * s1 + s2 * s2 - coeff * s1 * s2) / n;
    }

    [Fact]
    public void StereoSourceIsDownmixedByAveragingBothChannels()
    {
        // The test asset has 440 Hz on the left and 660 Hz on the right: both must survive the mono mix.
        using var dir = new TempDir();
        WavInfo wav = ConvertAsset(Assets.Ogg, dir);

        double p440 = PowerAt(wav, 440);
        double p660 = PowerAt(wav, 660);
        double off = Math.Max(PowerAt(wav, 550), PowerAt(wav, 1000));

        Assert.True(p440 > off * 20, $"440 Hz missing (p440={p440}, off={off})");
        Assert.True(p660 > off * 20, $"660 Hz missing (p660={p660}, off={off})");
        // equal-amplitude channels -> comparable level after averaging
        Assert.InRange(p440 / p660, 0.5, 2.0);
    }

    [MediaFoundationFact]
    public void Mp3StereoDownmixKeepsBothChannels()
    {
        // Same property as the OGG test, through the native Media Foundation path.
        using var dir = new TempDir();
        WavInfo wav = ConvertAsset(Assets.Mp3, dir);

        double p440 = PowerAt(wav, 440);
        double p660 = PowerAt(wav, 660);
        double off = Math.Max(PowerAt(wav, 550), PowerAt(wav, 1000));

        Assert.True(p440 > off * 20, $"440 Hz missing (p440={p440}, off={off})");
        Assert.True(p660 > off * 20, $"660 Hz missing (p660={p660}, off={off})");
        Assert.InRange(p440 / p660, 0.5, 2.0);
    }

    [MediaFoundationFact]
    public void ResamplingFrom48kKeepsTheTonePitch()
    {
        using var dir = new TempDir();
        WavInfo wav = ConvertAsset(Assets.Flac, dir);

        double p440 = PowerAt(wav, 440);
        Assert.True(p440 > PowerAt(wav, 400) * 20);
        Assert.True(p440 > PowerAt(wav, 500) * 20);
    }

    [Fact]
    public void GarbageFileIsReportedAsUnsupported()
    {
        using var dir = new TempDir();
        string bad = dir.Combine("not-audio.mp3");
        File.WriteAllBytes(bad, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });

        Assert.Throws<UnsupportedAudioFormatException>(
            () => Converter.ConvertToCsgoWav(bad, dir.Combine("out.wav")));
        Assert.False(File.Exists(dir.Combine("out.wav")));
    }

    [Fact]
    public void VoiceInputIsAttenuatedByTheVolumeCurve()
    {
        using var dir = new TempDir();
        string song = dir.Combine("song.wav");
        File.Copy(Assets.Path(Assets.Wav22k), song);
        int sourcePeak = WavReader.Read(song).Peak;

        string voice = dir.Combine("voice_input.wav");
        double db = Volume.GetDbAdjust(100); // -25 dB
        Converter.WriteVoiceInput(song, voice, db);

        WavInfo result = WavReader.Read(voice);
        Assert.Equal(22050, result.SampleRate);
        Assert.Equal(1, result.Channels);
        double expected = sourcePeak * Math.Pow(10, db / 20);
        Assert.InRange(result.Peak, expected * 0.97, expected * 1.03);
    }

    [Fact]
    public void WavWriterProducesPythonCompatible44ByteHeader()
    {
        short[] pcm = { 0, 1, -1, 32767, -32768 };
        byte[] bytes = WavFile.ToBytes(pcm);

        Assert.Equal(44 + 10, bytes.Length);
        Assert.Equal(
            new byte[]
            {
                0x52, 0x49, 0x46, 0x46, 46, 0, 0, 0, // RIFF, 36 + 10
                0x57, 0x41, 0x56, 0x45,                 // WAVE
                0x66, 0x6D, 0x74, 0x20, 16, 0, 0, 0,    // fmt , 16
                1, 0, 1, 0,                             // PCM, 1 channel
                0x22, 0x56, 0, 0,                       // 22050
                0x44, 0xAC, 0, 0,                       // byte rate 44100
                2, 0, 16, 0,                            // block align, bits
                0x64, 0x61, 0x74, 0x61, 10, 0, 0, 0,    // data, 10
            },
            bytes[..44]);
    }

    [Fact]
    public void WavFastPathReadsOnlyTheCsgoFormat()
    {
        using var dir = new TempDir();
        string ok = dir.Combine("ok.wav");
        File.Copy(Assets.Path(Assets.Wav22k), ok);
        Assert.True(WavFile.TryReadCsgoPcm(ok, out short[] samples));
        Assert.Equal(22050, samples.Length);

        string stereo = dir.Combine("stereo.wav");
        using (var fs = File.Create(stereo))
        {
            WavFile.Write(fs, new short[200], sampleRate: 44100, channels: 2);
        }

        Assert.False(WavFile.TryReadCsgoPcm(stereo, out _));
        Assert.False(WavFile.TryReadCsgoPcm(dir.Combine("missing.wav"), out _));
    }
}
