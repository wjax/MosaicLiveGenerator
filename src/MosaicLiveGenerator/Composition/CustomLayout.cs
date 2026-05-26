namespace MosaicLiveGenerator.Composition;

internal sealed record CustomLayout : Layout
{
    private readonly NormRect[] _cells;

    public CustomLayout(NormRect[] cells) => _cells = cells;

    public override IReadOnlyList<NormRect> ToCells(int sourceCount)
    {
        if (sourceCount != _cells.Length)
            throw new ArgumentException(
                $"Custom layout has {_cells.Length} cells, got {sourceCount} sources.",
                nameof(sourceCount));
        return _cells;
    }
}
