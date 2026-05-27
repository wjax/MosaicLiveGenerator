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

    [Fact]
    public void Nvidia_LowLatency_UsesH264NvencWithCbrAndNoBFrames()
    {
        var output = new OutputOptions(
            Uri: new Uri("udp://127.0.0.1:5000"),
            FrameRate: 25, GopSeconds: 1,
            BitrateKbps: 4000,
            LowLatency: true,
            HwAccel: HwAccel.Nvidia);

        var args = EncoderArgBuilder.Build(output);

        // Encoder
        var cvIdx = args.IndexOf("-c:v");
        Assert.Equal("h264_nvenc", args[cvIdx + 1]);

        // Modern NVENC preset/tune
        var presetIdx = args.IndexOf("-preset");
        Assert.Equal("p1", args[presetIdx + 1]);
        var tuneIdx = args.IndexOf("-tune");
        Assert.Equal("ll", args[tuneIdx + 1]);

        // CBR + matching bitrate / buffer
        var rcIdx = args.IndexOf("-rc");
        Assert.Equal("cbr", args[rcIdx + 1]);
        var bvIdx = args.IndexOf("-b:v");
        Assert.Equal("4000k", args[bvIdx + 1]);
        var bufsizeIdx = args.IndexOf("-bufsize");
        Assert.Equal("4000k", args[bufsizeIdx + 1]);

        // No B-frames, no scene-cut, GOP-derived keyint
        var bfIdx = args.IndexOf("-bf");
        Assert.Equal("0", args[bfIdx + 1]);
        var noSceneIdx = args.IndexOf("-no-scenecut");
        Assert.Equal("1", args[noSceneIdx + 1]);
        var gIdx = args.IndexOf("-g");
        Assert.Equal("25", args[gIdx + 1]); // FrameRate * GopSeconds

        Assert.Contains("-zerolatency", args);
        Assert.Contains("-an", args);

        // Must NOT carry libx264-specific knobs
        Assert.DoesNotContain("libx264", args);
        Assert.DoesNotContain("-x264-params", args);
    }

    [Fact]
    public void Nvidia_Balanced_UsesP4HqAndAllowsBFrames()
    {
        var output = new OutputOptions(
            Uri: new Uri("udp://127.0.0.1:5000"),
            LowLatency: false,
            HwAccel: HwAccel.Nvidia);

        var args = EncoderArgBuilder.Build(output);

        var presetIdx = args.IndexOf("-preset");
        Assert.Equal("p4", args[presetIdx + 1]);
        var tuneIdx = args.IndexOf("-tune");
        Assert.Equal("hq", args[tuneIdx + 1]);
        var bfIdx = args.IndexOf("-bf");
        Assert.Equal("2", args[bfIdx + 1]);
        Assert.DoesNotContain("-zerolatency", args);
    }

    [Fact]
    public void Intel_LowLatency_UsesH264QsvWithCbrAndLookaheadOff()
    {
        var output = new OutputOptions(
            Uri: new Uri("udp://127.0.0.1:5000"),
            FrameRate: 25, GopSeconds: 1,
            BitrateKbps: 4000,
            LowLatency: true,
            HwAccel: HwAccel.Intel);

        var args = EncoderArgBuilder.Build(output);

        var cvIdx = args.IndexOf("-c:v");
        Assert.Equal("h264_qsv", args[cvIdx + 1]);

        var presetIdx = args.IndexOf("-preset");
        Assert.Equal("veryfast", args[presetIdx + 1]);

        var pixIdx = args.IndexOf("-pix_fmt");
        Assert.Equal("nv12", args[pixIdx + 1]);

        // CBR via b:v + maxrate + bufsize
        var bvIdx = args.IndexOf("-b:v");
        Assert.Equal("4000k", args[bvIdx + 1]);
        var bufsizeIdx = args.IndexOf("-bufsize");
        Assert.Equal("4000k", args[bufsizeIdx + 1]);

        var bfIdx = args.IndexOf("-bf");
        Assert.Equal("0", args[bfIdx + 1]);

        var lookIdx = args.IndexOf("-look_ahead");
        Assert.Equal("0", args[lookIdx + 1]);

        Assert.Contains("-an", args);

        Assert.DoesNotContain("libx264", args);
        Assert.DoesNotContain("h264_nvenc", args);
        Assert.DoesNotContain("-x264-params", args);
    }

    [Fact]
    public void Intel_Balanced_UsesMediumPresetAndAllowsBFrames()
    {
        var output = new OutputOptions(
            Uri: new Uri("udp://127.0.0.1:5000"),
            LowLatency: false,
            HwAccel: HwAccel.Intel);

        var args = EncoderArgBuilder.Build(output);

        var presetIdx = args.IndexOf("-preset");
        Assert.Equal("medium", args[presetIdx + 1]);
        var bfIdx = args.IndexOf("-bf");
        Assert.Equal("2", args[bfIdx + 1]);
        Assert.DoesNotContain("-look_ahead", args);
    }

    [Fact]
    public void Default_HwAccel_IsNoneSoLibx264StillUsed()
    {
        // Sanity check the non-breaking-default: omitting HwAccel yields libx264.
        var output = new OutputOptions(new Uri("udp://127.0.0.1:5000"));
        var args = EncoderArgBuilder.Build(output);
        Assert.Contains("libx264", args);
        Assert.DoesNotContain("h264_nvenc", args);
        Assert.DoesNotContain("h264_qsv", args);
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

    public static int Count<T>(this IReadOnlyList<T> list, Func<T, bool> predicate)
    {
        var count = 0;
        for (var i = 0; i < list.Count; i++)
            if (predicate(list[i])) count++;
        return count;
    }
}
