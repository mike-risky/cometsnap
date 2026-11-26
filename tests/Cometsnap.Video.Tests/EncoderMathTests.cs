namespace Cometsnap.Video.Tests;

public class EncoderMathTests
{
    [Fact]
    public void Odd_sizes_become_even()
    {
        Assert.Equal(new ScreenRect(5, 7, 100, 58), EncoderMath.EvenSize(new ScreenRect(5, 7, 101, 59)));
        Assert.Equal(new ScreenRect(0, 0, 2, 2), EncoderMath.EvenSize(new ScreenRect(0, 0, 1, 1)));
    }

    [Theory]
    [InlineData(1920, 1080, 1)]
    [InlineData(3840, 2160, 1)]
    [InlineData(5120, 1440, 2)]
    [InlineData(7680, 4320, 2)]
    [InlineData(10240, 2160, 4)]
    public void Huge_regions_are_scaled_to_fit_the_encoder(int w, int h, int factor) =>
        Assert.Equal(factor, EncoderMath.DownscaleFactor(w, h));

    [Fact]
    public void Bitrate_is_clamped()
    {
        Assert.Equal(1_500_000, EncoderMath.DefaultBitrate(100, 100, 30));
        Assert.Equal(6_220_800, EncoderMath.DefaultBitrate(1920, 1080, 30));
        Assert.Equal(40_000_000, EncoderMath.DefaultBitrate(3840, 2160, 60));
    }

    [Fact]
    public void Gif_delays_do_not_drift_and_stay_at_least_two()
    {
        var total = 0;
        for (var i = 0; i < 100; i++)
            total += EncoderMath.GifDelay(TimeSpan.FromMilliseconds(i * 66.6667), TimeSpan.FromMilliseconds((i + 1) * 66.6667));
        Assert.InRange(total, 666, 667);
        Assert.Equal(2, EncoderMath.GifDelay(TimeSpan.Zero, TimeSpan.Zero));
    }
}
