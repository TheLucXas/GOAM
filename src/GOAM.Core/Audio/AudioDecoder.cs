using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NVorbis;

namespace Goam.Core.Audio;

/// <summary>Raised when a file cannot be decoded (wrong format, corrupt, codec missing).</summary>
public sealed class UnsupportedAudioFormatException : Exception
{
    public UnsupportedAudioFormatException(string path, Exception? inner = null)
        : base($"Unsupported or unreadable audio file: {Path.GetFileName(path)}", inner)
    {
        FilePath = path;
    }

    public string FilePath { get; }
}

public interface IAudioDecoder
{
    /// <summary>Decodes any supported file to mono, 22050 Hz, signed 16-bit samples.</summary>
    short[] DecodeToCsgoPcm(string path, CancellationToken cancellationToken = default);
}

/// <summary>
/// Decoder built on the Windows Media Foundation codecs (MP3, WAV, FLAC, M4A/AAC, WebM/Opus ...)
/// plus NVorbis for OGG, which Media Foundation does not read.
/// Output is always mono, 22050 Hz, 16-bit - the format CS:GO expects.
/// </summary>
public sealed class AudioDecoder : IAudioDecoder
{
    private const int ReadChunkSamples = CsgoFormat.SampleRate; // one second

    public short[] DecodeToCsgoPcm(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Audio file not found.", path);
        }

        IDisposable? owner = null;
        try
        {
            ISampleProvider provider = Open(path, out owner);
            using (owner)
            {
                return ReadAll(provider, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not UnsupportedAudioFormatException)
        {
            throw new UnsupportedAudioFormatException(path, ex);
        }
    }

    private static ISampleProvider Open(string path, out IDisposable owner)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();

        if (ext == ".ogg")
        {
            var vorbis = new VorbisReader(path);
            owner = vorbis;
            return new VorbisSampleProvider(vorbis);
        }

        if (ext == ".wav")
        {
            try
            {
                var wav = new WaveFileReader(path);
                owner = wav;
                return wav.ToSampleProvider();
            }
            catch (Exception ex) when (ex is FormatException or InvalidDataException or NotSupportedException
                                           or ArgumentException)
            {
                // exotic WAV (ADPCM, extensible ...): let Media Foundation try.
            }
        }

        var mf = new MediaFoundationReader(path);
        owner = mf;
        return mf.ToSampleProvider();
    }

    private static short[] ReadAll(ISampleProvider source, CancellationToken ct)
    {
        ISampleProvider pipeline = source;
        if (pipeline.WaveFormat.Channels > 1)
        {
            pipeline = new MonoDownmixSampleProvider(pipeline);
        }

        if (pipeline.WaveFormat.SampleRate != CsgoFormat.SampleRate)
        {
            pipeline = new WdlResamplingSampleProvider(pipeline, CsgoFormat.SampleRate);
        }

        float[] chunk = new float[ReadChunkSamples];
        short[] buffer = new short[CsgoFormat.SampleRate * 30];
        int count = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int n = pipeline.Read(chunk, 0, chunk.Length);
            if (n <= 0)
            {
                break;
            }

            if (count + n > buffer.Length)
            {
                Array.Resize(ref buffer, Math.Max(buffer.Length * 2, count + n));
            }

            for (int i = 0; i < n; i++)
            {
                buffer[count + i] = FloatToPcm16(chunk[i]);
            }

            count += n;
        }

        if (count == 0)
        {
            throw new InvalidDataException("The file contains no audio samples.");
        }

        return count == buffer.Length ? buffer : buffer.AsSpan(0, count).ToArray();
    }

    internal static short FloatToPcm16(float sample)
    {
        float scaled = sample * 32767f;
        if (scaled >= 32767f)
        {
            return short.MaxValue;
        }

        if (scaled <= -32768f)
        {
            return short.MinValue;
        }

        return (short)(scaled + (scaled >= 0f ? 0.5f : -0.5f));
    }

    private sealed class VorbisSampleProvider : ISampleProvider
    {
        private readonly VorbisReader _reader;

        public VorbisSampleProvider(VorbisReader reader)
        {
            _reader = reader;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(reader.SampleRate, reader.Channels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count) => _reader.ReadSamples(buffer, offset, count);
    }

    /// <summary>Average of all channels (what miniaudio's default channel mix does for N-to-mono).</summary>
    private sealed class MonoDownmixSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _channels;
        private float[] _scratch = Array.Empty<float>();

        public MonoDownmixSampleProvider(ISampleProvider source)
        {
            _source = source;
            _channels = source.WaveFormat.Channels;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int needed = count * _channels;
            if (_scratch.Length < needed)
            {
                _scratch = new float[needed];
            }

            int frames = _source.Read(_scratch, 0, needed) / _channels;
            float inv = 1f / _channels;
            for (int f = 0; f < frames; f++)
            {
                float sum = 0f;
                int baseIdx = f * _channels;
                for (int c = 0; c < _channels; c++)
                {
                    sum += _scratch[baseIdx + c];
                }

                buffer[offset + f] = sum * inv;
            }

            return frames;
        }
    }
}
