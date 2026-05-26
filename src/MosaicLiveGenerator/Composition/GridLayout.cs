namespace MosaicLiveGenerator.Composition;

internal sealed record GridLayout(int Rows, int Cols) : Layout
{
    public override IReadOnlyList<NormRect> ToCells(int sourceCount)
    {
        var expected = Rows * Cols;
        if (sourceCount != expected)
            throw new ArgumentException(
                $"Grid({Rows}x{Cols}) expects {expected} sources, got {sourceCount}.",
                nameof(sourceCount));

        var w = 1.0 / Cols;
        var h = 1.0 / Rows;
        var result = new NormRect[expected];
        for (var r = 0; r < Rows; r++)
        for (var c = 0; c < Cols; c++)
            result[r * Cols + c] = new NormRect(c * w, r * h, w, h);
        return result;
    }
}
