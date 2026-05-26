using MosaicLiveGenerator.Composition;
using Xunit;

namespace MosaicLiveGenerator.Tests.Composition;

public class LayoutMathTests
{
    [Theory]
    [InlineData(0.0, 0.0, 1.0, 1.0)]
    [InlineData(0.0, 0.0, 0.5, 0.5)]
    [InlineData(0.5, 0.5, 0.5, 0.5)]
    public void ValidateRect_AcceptsInRangeRects(double x, double y, double w, double h)
    {
        LayoutMath.ValidateRect(new NormRect(x, y, w, h), index: 0);
    }

    [Theory]
    [InlineData(-0.01, 0, 0.5, 0.5)]
    [InlineData(0, -0.01, 0.5, 0.5)]
    [InlineData(0, 0, 0.0, 0.5)]
    [InlineData(0, 0, 0.5, 0.0)]
    [InlineData(0.6, 0, 0.5, 0.5)]    // x+w > 1
    [InlineData(0, 0.6, 0.5, 0.5)]    // y+h > 1
    public void ValidateRect_RejectsOutOfRange(double x, double y, double w, double h)
    {
        Assert.Throws<ArgumentException>(
            () => LayoutMath.ValidateRect(new NormRect(x, y, w, h), index: 0));
    }

    [Fact]
    public void HasOverlaps_DetectsOverlappingPair()
    {
        var rects = new[] {
            new NormRect(0, 0, 0.6, 0.6),
            new NormRect(0.4, 0.4, 0.6, 0.6) // overlaps with [0]
        };

        var overlaps = LayoutMath.FindOverlaps(rects);

        Assert.Single(overlaps);
        Assert.Equal((0, 1), overlaps[0]);
    }

    [Fact]
    public void HasOverlaps_EdgeContactIsNotOverlap()
    {
        var rects = new[] {
            new NormRect(0, 0, 0.5, 1.0),
            new NormRect(0.5, 0, 0.5, 1.0)
        };

        Assert.Empty(LayoutMath.FindOverlaps(rects));
    }

    [Fact]
    public void ToPixelRect_RoundsToEvenDimensions()
    {
        // 0.333... × 1920 = 639.36 ; should round to 640 (even)
        var px = LayoutMath.ToPixelRect(new NormRect(0, 0, 1.0 / 3.0, 1.0 / 3.0), 1920, 1080);

        Assert.Equal(0, px.X);
        Assert.Equal(0, px.Y);
        Assert.True(px.Width % 2 == 0, $"width {px.Width} not even");
        Assert.True(px.Height % 2 == 0, $"height {px.Height} not even");
        // expected: 640x360
        Assert.Equal(640, px.Width);
        Assert.Equal(360, px.Height);
    }
}
