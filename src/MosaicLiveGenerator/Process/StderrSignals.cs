namespace MosaicLiveGenerator.Process;

internal sealed record StartupErrorSignal(MosaicStartupReason Reason, string Detail);

internal sealed record SourceConnectivitySignal(int SourceIndex, SourceConnectivity NewState, string RawLine);

internal sealed record OutputSdpSignal(string Sdp);
