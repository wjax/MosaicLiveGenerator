namespace MosaicLiveGenerator.Diagnostics;

internal sealed class SourceStateTable
{
    private readonly object _lock = new();
    private readonly SourceState[] _states;

    public SourceStateTable(IReadOnlyList<VideoSource> sources)
    {
        _states = new SourceState[sources.Count];
        for (var i = 0; i < sources.Count; i++)
            _states[i] = new SourceState(i, sources[i].Name, SourceConnectivity.Unknown);
    }

    public IReadOnlyList<SourceState> Snapshot()
    {
        lock (_lock) return _states.ToArray();
    }

    public bool TryUpdate(int index, SourceConnectivity next, out SourceState before, out SourceState after)
    {
        before = default!;
        after = default!;
        if (index < 0 || index >= _states.Length) return false;
        lock (_lock)
        {
            var cur = _states[index];
            if (cur.Connectivity == next) return false;
            before = cur;
            after = cur with { Connectivity = next };
            _states[index] = after;
        }
        return true;
    }
}
