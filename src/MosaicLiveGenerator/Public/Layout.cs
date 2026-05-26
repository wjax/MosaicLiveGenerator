using MosaicLiveGenerator.Composition;

namespace MosaicLiveGenerator;

public abstract record Layout
{
    public abstract IReadOnlyList<NormRect> ToCells(int sourceCount);

    public static Layout Grid(int rows, int cols)
    {
        if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (cols <= 0) throw new ArgumentOutOfRangeException(nameof(cols));
        return new GridLayout(rows, cols);
    }

    public static Layout Custom(IReadOnlyList<NormRect> cells)
    {
        if (cells is null) throw new ArgumentNullException(nameof(cells));
        if (cells.Count == 0) throw new ArgumentException("At least one cell required.", nameof(cells));
        return new CustomLayout(cells.ToArray());
    }
}
