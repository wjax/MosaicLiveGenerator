using Xunit;

namespace MosaicLiveGenerator.Tests.Public;

public class OptionsTests
{
    [Fact]
    public void OutputOptions_DefaultsMatchSpec()
    {
        var o = new OutputOptions(new Uri("udp://127.0.0.1:5000"));

        Assert.Equal(OutputProtocol.UdpMpegTs, o.Protocol);
        Assert.Equal(1920, o.Width);
        Assert.Equal(1080, o.Height);
        Assert.Equal(25, o.FrameRate);
        Assert.Equal(6000, o.BitrateKbps);
        Assert.Equal(1, o.GopSeconds);
        Assert.True(o.LowLatency);
    }

    [Fact]
    public void LayoutOptions_DefaultsAreUnobtrusive()
    {
        var l = new LayoutOptions();

        Assert.Equal("black", l.BackgroundColor);
        Assert.Equal(0, l.BorderPx);
        Assert.False(l.ShowLabels);
    }

    [Fact]
    public void FfmpegOptions_DefaultsAreSensible()
    {
        var f = new FfmpegOptions();

        Assert.Null(f.BinaryPath);
        Assert.True(f.LogStderr);
    }
}
