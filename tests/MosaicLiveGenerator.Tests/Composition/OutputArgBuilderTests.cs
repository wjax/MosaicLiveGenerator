using MosaicLiveGenerator.Composition;
using Xunit;

namespace MosaicLiveGenerator.Tests.Composition;

public class OutputArgBuilderTests
{
    [Fact]
    public void UdpMpegTs_EmitsMuxdelayMuxpreloadAndPktSize()
    {
        var output = new OutputOptions(
            Uri: new Uri("udp://127.0.0.1:5000"),
            Protocol: OutputProtocol.UdpMpegTs);

        var args = OutputArgBuilder.Build(output);

        Assert.Equal(
            new[] {
                "-muxdelay", "0",
                "-muxpreload", "0",
                "-mpegts_flags", "+resend_headers+pat_pmt_at_frames",
                "-f", "mpegts",
                "udp://127.0.0.1:5000?pkt_size=1316"
            },
            args);
    }

    [Fact]
    public void Rtp_EmitsRtpUriWithPktSize()
    {
        var output = new OutputOptions(
            Uri: new Uri("rtp://127.0.0.1:6000"),
            Protocol: OutputProtocol.RtpH264);

        var args = OutputArgBuilder.Build(output);

        Assert.Equal(
            new[] {
                "-f", "rtp",
                "rtp://127.0.0.1:6000?pkt_size=1200"
            },
            args);
    }
}
