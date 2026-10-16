using System.Numerics;

namespace Goam.Core.Audio;

/// <summary>
/// Volume model. The 0-200 % slider maps to a dB gain that is applied when the song is
/// written as voice_input.wav. The curve is deliberately identical to GOAM 1.x
/// (CS:GO mic input is very hot, so 100 % is already -25 dB).
/// </summary>
public static class Volume
{
    public const int Min = 0;
    public const int Max = 200;

    /// <summary>Port of goam.py get_db_adjust().</summary>
    public static double GetDbAdjust(int percent)
    {
        if (percent <= 0)
        {
            return -100.0;
        }

        if (percent > 100)
        {
            return (percent - 100) / 100.0 * 8.0;
        }

        if (percent >= 80)
        {
            return -25.0 - (100 - percent) * (5.0 / 20.0);
        }

        return -30.0 - (80 - percent) * (2.5 / 10.0);
    }

    /// <summary>Linear amplitude factor for a dB value, as float32 (what the numpy version multiplied by).</summary>
    public static float GainFactor(double db) => (float)Math.Pow(10.0, db / 20.0);

    /// <summary>
    /// Applies <paramref name="db"/> to PCM16 samples in place: float32 multiply, clamp to the int16
    /// range, truncate toward zero. SIMD for the bulk, scalar for the tail (same results).
    /// </summary>
    public static void ApplyDb(Span<short> samples, double db)
    {
        if (db == 0.0)
        {
            return;
        }

        ApplyFactor(samples, GainFactor(db));
    }

    public static void ApplyFactor(Span<short> samples, float factor)
    {
        int i = 0;

        if (Vector.IsHardwareAccelerated && samples.Length >= Vector<short>.Count)
        {
            int width = Vector<short>.Count;
            var vFactor = new Vector<float>(factor);
            var vMin = new Vector<float>(-32768f);
            var vMax = new Vector<float>(32767f);

            for (; i <= samples.Length - width; i += width)
            {
                var v = new Vector<short>(samples.Slice(i, width));
                Vector.Widen(v, out Vector<int> lo, out Vector<int> hi);

                Vector<float> fLo = Vector.Min(Vector.Max(Vector.ConvertToSingle(lo) * vFactor, vMin), vMax);
                Vector<float> fHi = Vector.Min(Vector.Max(Vector.ConvertToSingle(hi) * vFactor, vMin), vMax);

                Vector<short> r = Vector.Narrow(Vector.ConvertToInt32(fLo), Vector.ConvertToInt32(fHi));
                r.CopyTo(samples.Slice(i, width));
            }
        }

        for (; i < samples.Length; i++)
        {
            samples[i] = ScaleSample(samples[i], factor);
        }
    }

    /// <summary>Scalar reference implementation (also used by tests to validate the SIMD path).</summary>
    internal static short ScaleSample(short sample, float factor)
    {
        float f = sample * factor;
        if (f > 32767f)
        {
            f = 32767f;
        }
        else if (f < -32768f)
        {
            f = -32768f;
        }

        return (short)f;
    }
}
