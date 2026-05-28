namespace MosaicLiveGenerator.Sources;

internal static class InputArgBuilder
{
    public static IReadOnlyList<string> Build(VideoSource source, int index, string sdpDirectory)
    {
        var args = new List<string>
        {
            "-fflags", "nobuffer+genpts",
            "-flags", "low_delay",
            "-probesize", "32",
            "-analyzeduration", "0",
        };

        switch (source.Protocol)
        {
            case SourceProtocol.MpegTsUdp:
                args.Add("-i");
                args.Add(BuildUdpUri(source.Uri));
                break;
            case SourceProtocol.RtpH264:
                args.Add("-protocol_whitelist");
                args.Add("file,udp,rtp,crypto");
                args.Add("-i");
                args.Add(Path.Combine(sdpDirectory, $"src-{index}.sdp").Replace('\\', '/'));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(source));
        }

        return args;
    }

    private static string BuildUdpUri(Uri uri)
    {
        // Append low-latency query params + reuse=1 (SO_REUSEADDR) so the mosaic
        // can recover from orphan binders, and so multicast inputs can be shared
        // with other listeners (ffplay, recorders, additional mosaics, etc.).
        var baseUri = $"udp://{uri.Host}:{uri.Port}";
        return baseUri + "?fifo_size=1000000&overrun_nonfatal=1&reconnect=1&reconnect_streamed=1&reconnect_delay_max=2&reuse=1";
    }
}
