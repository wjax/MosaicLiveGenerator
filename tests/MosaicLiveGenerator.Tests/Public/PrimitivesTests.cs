using Xunit;

namespace MosaicLiveGenerator.Tests.Public;

public class PrimitivesTests
{
    [Fact]
    public void VideoSource_DefaultsFitToLetterbox()
    {
        var s = new VideoSource("cam1", new Uri("udp://127.0.0.1:5000"), SourceProtocol.MpegTsUdp);
        Assert.Equal(TileFit.Letterbox, s.Fit);
    }

    [Fact]
    public void NormRect_IsValueType()
    {
        var a = new NormRect(0, 0, 0.5, 0.5);
        var b = a with { Width = 1.0 };
        Assert.NotEqual(a, b);
        Assert.Equal(0.5, a.Width);
    }
}
