using Goam.Core.Diagnostics;

namespace Goam.Core.Audio;

/// <summary>Turns arbitrary audio files into CS:GO-ready WAVs and writes voice_input.wav.</summary>
public sealed class AudioConverter
{
    private readonly IAudioDecoder _decoder;
    private readonly IAppLog _log;

    public AudioConverter(IAudioDecoder decoder, IAppLog? log = null)
    {
        _decoder = decoder;
        _log = log ?? NullLog.Instance;
    }

    /// <summary>Decodes <paramref name="input"/> and writes a 22050 Hz mono PCM16 WAV to <paramref name="outputWav"/>.</summary>
    /// <exception cref="UnsupportedAudioFormatException">The input cannot be decoded.</exception>
    public void ConvertToCsgoWav(string input, string outputWav, CancellationToken ct = default)
    {
        short[] pcm = _decoder.DecodeToCsgoPcm(input, ct);
        WriteWav(outputWav, pcm);
    }

    /// <summary>
    /// Reads an already-converted song, applies <paramref name="db"/> of gain and writes the result
    /// to <paramref name="targetWav"/> (voice_input.wav). If decoding fails the raw file is copied,
    /// like GOAM 1.x did.
    /// </summary>
    public void WriteVoiceInput(string songWav, string targetWav, double db, CancellationToken ct = default)
    {
        short[] pcm;
        if (!WavFile.TryReadCsgoPcm(songWav, out pcm))
        {
            try
            {
                pcm = _decoder.DecodeToCsgoPcm(songWav, ct);
            }
            catch (UnsupportedAudioFormatException ex)
            {
                _log.Warn($"Could not decode '{Path.GetFileName(songWav)}', copying the raw file: {ex.Message}");
                File.Copy(songWav, targetWav, overwrite: true);
                return;
            }
        }

        Volume.ApplyDb(pcm, db);
        WriteWav(targetWav, pcm);
    }

    internal static void WriteWav(string path, ReadOnlySpan<short> pcm)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Write next to the target and swap in, so the game never sees a half-written file.
        string tmp = path + ".tmp";
        try
        {
            using (FileStream fs = new(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            {
                WavFile.Write(fs, pcm);
            }

            try
            {
                File.Move(tmp, path, overwrite: true);
                return;
            }
            catch (IOException)
            {
                // Target busy (e.g. the game is reading it): fall through to a direct write.
            }

            using FileStream direct = new(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
            WavFile.Write(direct, pcm);
        }
        finally
        {
            try
            {
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }
            }
            catch (IOException)
            {
                // leave it; harmless
            }
        }
    }
}
