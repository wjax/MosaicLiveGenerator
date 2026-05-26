using MosaicLiveGenerator.Sources;
using Xunit;

namespace MosaicLiveGenerator.Tests.Sources;

public class InputArgBuilderTests
{
    [Fact]
    public void UdpMpegTs_EmitsLowLatencyAndReconnectFlags()
    {
        var src = new VideoSource("cam1", new Uri("udp://239.0.0.1:5004"), SourceProtocol.MpegTsUdp);

        var args = InputArgBuilder.Build(src, index: 0, sdpDirectory: "/tmp");

        Assert.Equal(
            new[]
            {
                "-fflags", "nobuffer+genpts",
                "-flags", "low_delay",
                "-probesize", "32",
                "-analyzeduration", "0",
                "-i", "udp://239.0.0.1:5004?fifo_size=1000000&overrun_nonfatal=1&reconnect=1&reconnect_streamed=1&reconnect_delay_max=2"
            },
            args);
    }

    [Fact]
    public void RtpH264_ReferencesSdpFileAndWhitelistsProtocols()
    {
        var src = new VideoSource("rtp1", new Uri("rtp://127.0.0.1:5004"), SourceProtocol.RtpH264);

        var args = InputArgBuilder.Build(src, index: 3, sdpDirectory: "/tmp/session-abc");

        Assert.Contains("-protocol_whitelist", args);
        Assert.Contains("file,udp,rtp,crypto", args);
        Assert.Contains("/tmp/session-abc/src-3.sdp", args[^1]);
        Assert.Equal("-i", args[^2]);
    }

    [Fact]
    public void InputArgs_AlwaysStartWithLowLatencyBlock()
    {
        var src = new VideoSource("x", new Uri("udp://127.0.0.1:1234"), SourceProtocol.MpegTsUdp);
        var args = InputArgBuilder.Build(src, 0, "/tmp");

        Assert.Equal("-fflags", args[0]);
        Assert.Equal("nobuffer+genpts", args[1]);
        Assert.Equal("-flags", args[2]);
        Assert.Equal("low_delay", args[3]);
    }
}
