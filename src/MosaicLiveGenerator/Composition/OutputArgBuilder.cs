namespace MosaicLiveGenerator.Composition;

internal static class OutputArgBuilder
{
    public static IReadOnlyList<string> Build(OutputOptions output)
    {
        return output.Protocol switch
        {
            OutputProtocol.UdpMpegTs => new[]
            {
                "-muxdelay", "0",
                "-muxpreload", "0",
                "-mpegts_flags", "+resend_headers+pat_pmt_at_frames",
                "-f", "mpegts",
                $"udp://{output.Uri.Host}:{output.Uri.Port}?pkt_size=1316",
            },
            OutputProtocol.RtpH264 => new[]
            {
                "-f", "rtp",
                $"rtp://{output.Uri.Host}:{output.Uri.Port}?pkt_size=1200",
            },
            _ => throw new ArgumentOutOfRangeException(nameof(output)),
        };
    }
}
