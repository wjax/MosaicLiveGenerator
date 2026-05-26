namespace MosaicLiveGenerator;

public sealed class SessionStateChangedEventArgs : EventArgs
{
    public SessionState OldState { get; init; }
    public SessionState NewState { get; init; }
}

public sealed class SourceConnectivityChangedEventArgs : EventArgs
{
    public int SourceIndex { get; init; }
    public string SourceName { get; init; } = "";
    public SourceConnectivity OldConnectivity { get; init; }
    public SourceConnectivity NewConnectivity { get; init; }
}

public sealed class FaultedEventArgs : EventArgs
{
    public Exception Error { get; init; } = default!;
}
