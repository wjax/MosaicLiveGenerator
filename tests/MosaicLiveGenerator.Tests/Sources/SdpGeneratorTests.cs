using MosaicLiveGenerator.Sources;
using Xunit;

namespace MosaicLiveGenerator.Tests.Sources;

public class SdpGeneratorTests
{
    [Fact]
    public void UnicastHost_IsReplacedWithListenAllInterfaces()
    {
        var source = new VideoSource("cam1", new Uri("rtp://10.1.2.3:5004"), SourceProtocol.RtpH264);

        var sdp = SdpGenerator.BuildSdp(source, index: 0);

        Assert.Contains("c=IN IP4 0.0.0.0", sdp);
        Assert.Contains("o=- 0 0 IN IP4 0.0.0.0", sdp);
        Assert.Contains("m=video 5004 RTP/AVP 96", sdp);
        Assert.Contains("a=rtpmap:96 H264/90000", sdp);
        Assert.Contains("a=fmtp:96 packetization-mode=1", sdp);
        Assert.Contains("MosaicLiveGenerator source 0", sdp);
    }

    [Theory]
    [InlineData("224.0.0.1")]
    [InlineData("239.1.2.3")]
    [InlineData("232.0.0.5")]
    public void MulticastHost_IsPreservedInSdp(string mcastAddr)
    {
        var source = new VideoSource("mcast", new Uri($"rtp://{mcastAddr}:5004"), SourceProtocol.RtpH264);

        var sdp = SdpGenerator.BuildSdp(source, index: 1);

        Assert.Contains($"c=IN IP4 {mcastAddr}", sdp);
        Assert.Contains($"o=- 0 0 IN IP4 {mcastAddr}", sdp);
    }

    [Fact]
    public void Sdp_EndsWithNewline()
    {
        var source = new VideoSource("x", new Uri("rtp://127.0.0.1:5004"), SourceProtocol.RtpH264);
        var sdp = SdpGenerator.BuildSdp(source, index: 0);
        Assert.EndsWith("\n", sdp);
    }
}
