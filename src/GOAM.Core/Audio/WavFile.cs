using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace Goam.Core.Audio;

/// <summary>The one output format CS:GO's voice_inputfromfile accepts: 22050 Hz, mono, 16-bit PCM.</summary>
public static class CsgoFormat
{
    public const int SampleRate = 22050;
    public const int Channels = 1;
    public const int BitsPerSample = 16;
}

/// <summary>Minimal RIFF/WAVE reader/writer for PCM16. Header layout matches Python's wave module (44 bytes).</summary>
public static class WavFile
{
    public const int HeaderSize = 44;

    public static void Write(Stream stream, ReadOnlySpan<short> samples,
        int sampleRate = CsgoFormat.SampleRate, int channels = CsgoFormat.Channels)
    {
        int dataBytes = checked(samples.Length * sizeof(short));
        Span<byte> header = stackalloc byte[HeaderSize];

        Encoding.ASCII.GetBytes("RIFF").CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], 36 + dataBytes);
        Encoding.ASCII.GetBytes("WAVE").CopyTo(header[8..]);
        Encoding.ASCII.GetBytes("fmt ").CopyTo(header[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(header[20..], 1); // PCM
        BinaryPrimitives.WriteInt16LittleEndian(header[22..], (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], sampleRate * channels * 2);
        BinaryPrimitives.WriteInt16LittleEndian(header[32..], (short)(channels * 2));
        BinaryPrimitives.WriteInt16LittleEndian(header[34..], 16);
        Encoding.ASCII.GetBytes("data").CopyTo(header[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[40..], dataBytes);

        stream.Write(header);
        if (BitConverter.IsLittleEndian)
        {
            stream.Write(MemoryMarshal.AsBytes(samples));
        }
        else
        {
            byte[] swapped = new byte[dataBytes];
            for (int i = 0; i < samples.Length; i++)
            {
                BinaryPrimitives.WriteInt16LittleEndian(swapped.AsSpan(i * 2), samples[i]);
            }

            stream.Write(swapped);
        }
    }

    public static byte[] ToBytes(ReadOnlySpan<short> samples)
    {
        using var ms = new MemoryStream(HeaderSize + samples.Length * 2);
        Write(ms, samples);
        return ms.ToArray();
    }

    /// <summary>
    /// Fast path: reads PCM16/mono/22050 Hz WAV data directly (the format GOAM itself writes).
    /// Returns false for anything else so the caller can run the generic decoder.
    /// </summary>
    public static bool TryReadCsgoPcm(string path, out short[] samples)
    {
        samples = Array.Empty<short>();
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);

            if (fs.Length < HeaderSize
                || new string(br.ReadChars(4)) != "RIFF")
            {
                return false;
            }

            br.ReadInt32();
            if (new string(br.ReadChars(4)) != "WAVE")
            {
                return false;
            }

            bool formatOk = false;
            while (fs.Position + 8 <= fs.Length)
            {
                string id = new(br.ReadChars(4));
                int size = br.ReadInt32();
                if (size < 0)
                {
                    return false;
                }

                if (id == "fmt ")
                {
                    if (size < 16)
                    {
                        return false;
                    }

                    short tag = br.ReadInt16();
                    short channels = br.ReadInt16();
                    int rate = br.ReadInt32();
                    br.ReadInt32();
                    br.ReadInt16();
                    short bits = br.ReadInt16();
                    formatOk = tag == 1 && channels == CsgoFormat.Channels
                               && rate == CsgoFormat.SampleRate && bits == CsgoFormat.BitsPerSample;
                    if (!formatOk)
                    {
                        return false;
                    }

                    fs.Position += size - 16 + (size & 1);
                }
                else if (id == "data")
                {
                    if (!formatOk)
                    {
                        return false;
                    }

                    long available = fs.Length - fs.Position;
                    int bytes = (int)Math.Min(size, available) & ~1;
                    byte[] raw = br.ReadBytes(bytes);
                    samples = new short[raw.Length / 2];
                    Buffer.BlockCopy(raw, 0, samples, 0, raw.Length);
                    if (!BitConverter.IsLittleEndian)
                    {
                        for (int i = 0; i < samples.Length; i++)
                        {
                            samples[i] = BinaryPrimitives.ReverseEndianness(samples[i]);
                        }
                    }

                    return true;
                }
                else
                {
                    fs.Position += size + (size & 1);
                }
            }

            return false;
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or UnauthorizedAccessException
                                       or ArgumentException or OverflowException)
        {
            return false;
        }
    }
}
