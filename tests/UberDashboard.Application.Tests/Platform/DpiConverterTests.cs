using UberDashboard.Application.Platform;

namespace UberDashboard.Application.Tests.Platform;

public sealed class DpiConverterTests
{
    [Fact]
    public void ToDip_returns_same_value_at_100_percent()
    {
        Assert.Equal(96d, DpiConverter.ToDip(96, 96));
    }

    [Theory]
    [InlineData(150, 144u, 100d)]
    [InlineData(200, 192u, 100d)]
    [InlineData(75, 72u, 100d)]
    public void ToDip_scales_pixels_by_dpi(int pixels, uint dpi, double expected)
    {
        Assert.Equal(expected, DpiConverter.ToDip(pixels, dpi), 6);
    }

    [Theory]
    [InlineData(100d, 96u, 100)]
    [InlineData(100d, 192u, 200)]
    [InlineData(100d, 144u, 150)]
    public void ToPixels_scales_dip_by_dpi(double dip, uint dpi, int expected)
    {
        Assert.Equal(expected, DpiConverter.ToPixels(dip, dpi));
    }

    [Theory]
    [InlineData(100.5d, 101)]
    [InlineData(-100.5d, -101)]
    [InlineData(100.4d, 100)]
    public void ToPixels_rounds_half_away_from_zero(double dip, int expected)
    {
        Assert.Equal(expected, DpiConverter.ToPixels(dip, 96));
    }

    [Fact]
    public void Zero_dpi_falls_back_to_default_dpi()
    {
        Assert.Equal(96d, DpiConverter.ToDip(96, 0));
        Assert.Equal(100, DpiConverter.ToPixels(100d, 0));
    }

    [Theory]
    [InlineData(40, 96u)]
    [InlineData(150, 144u)]
    [InlineData(300, 192u)]
    public void Dip_pixel_round_trip_is_stable(int pixels, uint dpi)
    {
        Assert.Equal(pixels, DpiConverter.ToPixels(DpiConverter.ToDip(pixels, dpi), dpi));
    }
}
