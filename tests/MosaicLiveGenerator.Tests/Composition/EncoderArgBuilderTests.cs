using MosaicLiveGenerator.Composition;
using Xunit;

namespace MosaicLiveGenerator.Tests.Composition;

public class EncoderArgBuilderTests
{
    [Fact]
    public void LowLatency_IncludesIntraRefreshAndUltrafast()
    {
        var output = new OutputOptions(
            Uri: new Uri("udp://127.0.0.1:5000"),
            Width: 1920, Height: 1080,
            FrameRate: 25,
            BitrateKbps: 6000,
            GopSeconds: 1,
            LowLatency: true);

        var args = EncoderArgBuilder.Build(output);

        Assert.Contains("-c:v", args);
        Assert.Contains("libx264", args);
        Assert.Contains("-preset", args);
        Assert.Contains("ultrafast", args);
        Assert.Contains("-tune", args);
        Assert.Contains("zerolatency", args);
        Assert.Contains("-bf", args);
        Assert.Contains("0", args);
        Assert.Contains("-refs", args);
        Assert.Contains("1", args);
        Assert.Contains("-x264-params", args);

        // x264-params: intra-refresh, GOP-derived keyint, no lookahead
        var x264ParamsIdx = args.IndexOf("-x264-params");
        var x264Params = args[x264ParamsIdx + 1];
        Assert.Contains("intra-refresh=1", x264Params);
        Assert.Contains("keyint=25", x264Params);     // FrameRate * GopSeconds
        Assert.Contains("min-keyint=25", x264Params);
        Assert.Contains("rc-lookahead=0", x264Params);
        Assert.Contains("bframes=0", x264Params);
        Assert.Contains("sliced-threads=1", x264Params);
        Assert.Contains("nal-hrd=cbr", x264Params);

        Assert.Contains("-b:v", args);
        Assert.Contains("6000k", args);
        Assert.Contains("-maxrate", args);
        Assert.Contains("-bufsize", args);
        // bufsize == bitrate per spec
        var bufsizeIdx = args.IndexOf("-bufsize");
        Assert.Equal("6000k", args[bufsizeIdx + 1]);

        Assert.Contains("-flush_packets", args);
        Assert.Contains("-an", args);
    }

    [Fact]
    public void Balanced_DropsAggressiveX264ParamsAndUsesVeryfast()
    {
        var output = new OutputOptions(
            Uri: new Uri("udp://127.0.0.1:5000"),
            LowLatency: false);

        var args = EncoderArgBuilder.Build(output);

        var presetIdx = args.IndexOf("-preset");
        Assert.Equal("veryfast", args[presetIdx + 1]);
        Assert.DoesNotContain("-x264-params", args);
    }

    [Fact]
    public void Gop_ScaledByFrameRate()
    {
        var output = new OutputOptions(
            Uri: new Uri("udp://127.0.0.1:5000"),
            FrameRate: 30, GopSeconds: 2,
            LowLatency: true);

        var args = EncoderArgBuilder.Build(output);

        var gIdx = args.IndexOf("-g");
        Assert.Equal("60", args[gIdx + 1]);
    }
}

internal static class TestListExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> list, T value)
    {
        for (var i = 0; i < list.Count; i++)
            if (Equals(list[i], value)) return i;
        return -1;
    }

    public static bool Contains<T>(this IReadOnlyList<T> list, T value) => list.IndexOf(value) >= 0;
}
