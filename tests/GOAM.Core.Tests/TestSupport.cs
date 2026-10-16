using System.Buffers.Binary;
using NAudio.Wave;

namespace Goam.Core.Tests;

/// <summary>A scratch folder that is removed when disposed.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "goam-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());

    public string Sub(string name)
    {
        string p = Combine(name);
        Directory.CreateDirectory(p);
        return p;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal static class Assets
{
    public static string Path(string name) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "assets", name);

    public const string Mp3 = "tone-440hz-stereo-44100.mp3";
    public const string Ogg = "tone-440hz-stereo-44100.ogg";
    public const string Flac = "tone-440hz-mono-48000.flac";
    public const string Wav22k = "tone-440hz-mono-22050.wav";
}

internal static class Platform
{
    public static readonly bool MediaFoundationAvailable = Probe();

    private static bool Probe()
    {
        try
        {
            using var reader = new MediaFoundationReader(Assets.Path(Assets.Mp3));
            return reader.WaveFormat.SampleRate > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>[Fact] that is skipped (with a reason) on machines without Windows Media Foundation.</summary>
public sealed class MediaFoundationFactAttribute : FactAttribute
{
    public MediaFoundationFactAttribute()
    {
        if (!Platform.MediaFoundationAvailable)
        {
            Skip = "Windows Media Foundation is not available on this machine.";
        }
    }
}

internal sealed record WavInfo(int Channels, int SampleRate, int Bits, int DataBytes, short[] Samples)
{
    public double Seconds => (double)Samples.Length / (SampleRate * Channels);
    public int Peak => Samples.Length == 0 ? 0 : Samples.Max(s => Math.Abs((int)s));
}

internal static class WavReader
{
    public static WavInfo Read(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(b, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(b, 8, 4));
        Assert.Equal("fmt ", System.Text.Encoding.ASCII.GetString(b, 12, 4));
        Assert.Equal(16, BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(16)));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(20)));
        int channels = BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(22));
        int rate = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(24));
        int byteRate = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(28));
        int blockAlign = BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(32));
        int bits = BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(34));
        Assert.Equal("data", System.Text.Encoding.ASCII.GetString(b, 36, 4));
        int dataBytes = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(40));
        Assert.Equal(b.Length - 44, dataBytes);
        Assert.Equal(b.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(4)));
        Assert.Equal(rate * channels * bits / 8, byteRate);
        Assert.Equal(channels * bits / 8, blockAlign);

        short[] samples = new short[dataBytes / 2];
        Buffer.BlockCopy(b, 44, samples, 0, dataBytes);
        return new WavInfo(channels, rate, bits, dataBytes, samples);
    }
}
