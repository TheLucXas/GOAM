using Goam.Core.Audio;

namespace Goam.Core.Tests;

public class VolumeTests
{
    [Theory]
    [InlineData(0, -100.0)]
    [InlineData(-5, -100.0)]
    [InlineData(1, -49.75)]
    [InlineData(50, -37.5)]
    [InlineData(79, -30.25)]
    [InlineData(80, -30.0)]
    [InlineData(90, -27.5)]
    [InlineData(100, -25.0)]
    [InlineData(101, 0.08)]
    [InlineData(150, 4.0)]
    [InlineData(200, 8.0)]
    public void DbCurveMatchesGoam1x(int percent, double expectedDb)
    {
        Assert.Equal(expectedDb, Volume.GetDbAdjust(percent), 6);
    }

    [Fact]
    public void ZeroDbLeavesSamplesUntouched()
    {
        short[] data = { 1, -1, 1000, -32768, 32767 };
        short[] copy = (short[])data.Clone();
        Volume.ApplyDb(data, 0.0);
        Assert.Equal(copy, data);
    }

    [Fact]
    public void SixDbDownHalvesAmplitude()
    {
        short[] data = { 10000, -10000, 20000, -20000, 1, -1, 0 };
        Volume.ApplyDb(data, -6.0206);
        // 10^(-6.0206/20) rounds to 0.5f; results are truncated toward zero
        Assert.Equal(new short[] { 5000, -5000, 10000, -10000, 0, 0, 0 }, data);
    }

    [Fact]
    public void GainIsClampedToInt16Range()
    {
        short[] data = { 30000, -30000, 32767, -32768 };
        Volume.ApplyDb(data, 20.0); // x10
        Assert.Equal(new short[] { 32767, -32768, 32767, -32768 }, data);
    }

    [Fact]
    public void MinusHundredDbSilencesEverything()
    {
        short[] data = { 32767, -32768, 12345 };
        Volume.ApplyDb(data, Volume.GetDbAdjust(0));
        Assert.All(data, s => Assert.Equal(0, s));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(1000)]
    [InlineData(4097)]
    public void SimdPathEqualsScalarReference(int length)
    {
        var rng = new Random(1234 + length);
        short[] simd = new short[length];
        for (int i = 0; i < length; i++)
        {
            simd[i] = (short)rng.Next(short.MinValue, short.MaxValue + 1);
        }

        short[] scalar = (short[])simd.Clone();

        foreach (double db in new[] { -25.0, -3.3, 0.08, 4.0, 8.0, -100.0 })
        {
            float factor = Volume.GainFactor(db);
            short[] a = (short[])simd.Clone();
            short[] b = (short[])scalar.Clone();
            Volume.ApplyFactor(a, factor);
            for (int i = 0; i < b.Length; i++)
            {
                b[i] = Volume.ScaleSample(b[i], factor);
            }

            Assert.Equal(b, a);
        }
    }
}
