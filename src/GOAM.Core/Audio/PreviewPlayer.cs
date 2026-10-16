using Goam.Core.Diagnostics;
using Goam.Core.Songs;
using NAudio.Wave;

namespace Goam.Core.Audio;

/// <summary>
/// Plays the first few seconds of a song through the default output device.
/// Clicking the playing song again stops it; another song switches. Volume follows the main slider
/// (linear up to 100 %, dB gain above), same as GOAM 1.x.
/// </summary>
public sealed class PreviewPlayer : IDisposable
{
    public const int PreviewSeconds = 5;

    private readonly IAudioDecoder _decoder;
    private readonly IAppLog _log;
    private readonly object _gate = new();
    private WaveOutEvent? _output;
    private string? _stem;
    private bool _disposed;

    public PreviewPlayer(IAudioDecoder decoder, IAppLog? log = null)
    {
        _decoder = decoder;
        _log = log ?? NullLog.Instance;
    }

    /// <summary>Raised (on an arbitrary thread) when playback starts or stops.</summary>
    public event EventHandler? StateChanged;

    public string? CurrentStem
    {
        get
        {
            lock (_gate)
            {
                return _stem;
            }
        }
    }

    public void Toggle(SongEntry song, int volumePercent)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_stem == song.Stem)
            {
                StopLocked();
                RaiseStateChanged();
                return;
            }

            StopLocked();

            try
            {
                short[] pcm = LoadPreview(song.FullPath, volumePercent);
                byte[] wav = new byte[pcm.Length * 2];
                Buffer.BlockCopy(pcm, 0, wav, 0, wav.Length);

                var source = new RawSourceWaveStream(
                    new MemoryStream(wav),
                    new WaveFormat(CsgoFormat.SampleRate, CsgoFormat.BitsPerSample, CsgoFormat.Channels));
                var output = new WaveOutEvent { DesiredLatency = 120 };
                output.PlaybackStopped += (_, _) => OnPlaybackStopped(output, source);
                output.Init(source);
                output.Play();

                _output = output;
                _stem = song.Stem;
            }
            catch (Exception ex)
            {
                _log.Warn($"Preview failed for '{song.FileName}': {ex.Message}");
                StopLocked();
            }
        }

        RaiseStateChanged();
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopLocked();
        }

        RaiseStateChanged();
    }

    private short[] LoadPreview(string path, int volumePercent)
    {
        short[] pcm = WavFile.TryReadCsgoPcm(path, out short[] fast) ? fast : _decoder.DecodeToCsgoPcm(path);
        int max = CsgoFormat.SampleRate * PreviewSeconds;
        if (pcm.Length > max)
        {
            pcm = pcm.AsSpan(0, max).ToArray();
        }

        if (volumePercent <= 100)
        {
            Volume.ApplyFactor(pcm, Math.Clamp(volumePercent, 0, 100) / 100f);
        }
        else
        {
            Volume.ApplyDb(pcm, Volume.GetDbAdjust(volumePercent));
        }

        return pcm;
    }

    private void OnPlaybackStopped(WaveOutEvent output, IDisposable source)
    {
        bool changed = false;
        lock (_gate)
        {
            if (ReferenceEquals(_output, output))
            {
                _output = null;
                _stem = null;
                changed = true;
            }
        }

        try
        {
            output.Dispose();
            source.Dispose();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // already torn down
        }

        if (changed)
        {
            RaiseStateChanged();
        }
    }

    private void StopLocked()
    {
        WaveOutEvent? output = _output;
        _output = null;
        _stem = null;
        if (output is not null)
        {
            try
            {
                output.Stop();
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                // already stopped
            }
        }
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            StopLocked();
        }
    }
}
