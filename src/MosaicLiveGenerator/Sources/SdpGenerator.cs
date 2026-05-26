using System.Net;

namespace MosaicLiveGenerator.Sources;

internal static class SdpGenerator
{
    public static string BuildSdp(VideoSource source, int index)
    {
        if (source.Protocol != SourceProtocol.RtpH264)
            throw new InvalidOperationException($"SDP only generated for RtpH264, got {source.Protocol}.");

        var port = source.Uri.Port;
        var sdpAddr = ResolveSdpAddress(source.Uri.Host);

        return
            $"v=0\n" +
            $"o=- 0 0 IN IP4 {sdpAddr}\n" +
            $"s=MosaicLiveGenerator source {index}\n" +
            $"c=IN IP4 {sdpAddr}\n" +
            $"t=0 0\n" +
            $"m=video {port} RTP/AVP 96\n" +
            $"a=rtpmap:96 H264/90000\n" +
            $"a=fmtp:96 packetization-mode=1\n";
    }

    private static string ResolveSdpAddress(string host)
    {
        if (IPAddress.TryParse(host, out var ip) && IsMulticastV4(ip))
            return ip.ToString();
        return "0.0.0.0";
    }

    private static bool IsMulticastV4(IPAddress ip)
    {
        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var first = ip.GetAddressBytes()[0];
        return first >= 224 && first <= 239;
    }
}
