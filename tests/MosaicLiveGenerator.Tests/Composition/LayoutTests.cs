using Xunit;

namespace MosaicLiveGenerator.Tests.Composition;

public class LayoutTests
{
    [Fact]
    public void Grid_2x2_ProducesFourQuarterCanvasRects()
    {
        var layout = Layout.Grid(2, 2);

        var cells = layout.ToCells(4);

        Assert.Equal(4, cells.Count);
        Assert.Equal(new NormRect(0.0, 0.0, 0.5, 0.5), cells[0]);
        Assert.Equal(new NormRect(0.5, 0.0, 0.5, 0.5), cells[1]);
        Assert.Equal(new NormRect(0.0, 0.5, 0.5, 0.5), cells[2]);
        Assert.Equal(new NormRect(0.5, 0.5, 0.5, 0.5), cells[3]);
    }

    [Fact]
    public void Grid_3x3_ProducesNineCellsInRowMajorOrder()
    {
        var cells = Layout.Grid(3, 3).ToCells(9);

        Assert.Equal(9, cells.Count);
        // cell 4 (index 4) = middle: row 1, col 1
        var third = 1.0 / 3.0;
        Assert.Equal(third, cells[4].X, precision: 6);
        Assert.Equal(third, cells[4].Y, precision: 6);
        Assert.Equal(third, cells[4].Width, precision: 6);
        Assert.Equal(third, cells[4].Height, precision: 6);
    }

    [Fact]
    public void Grid_RequiresMatchingSourceCount()
    {
        var layout = Layout.Grid(2, 2);
        Assert.Throws<ArgumentException>(() => layout.ToCells(5));
    }

    [Fact]
    public void Custom_RoundTrips()
    {
        var rects = new[] {
            new NormRect(0, 0, 0.5, 0.5),
            new NormRect(0.5, 0, 0.5, 1.0)
        };
        var layout = Layout.Custom(rects);

        var cells = layout.ToCells(2);

        Assert.Equal(rects, cells);
    }

    [Fact]
    public void Custom_RequiresMatchingSourceCount()
    {
        var layout = Layout.Custom(new[] { new NormRect(0, 0, 1, 1) });
        Assert.Throws<ArgumentException>(() => layout.ToCells(2));
    }
}
