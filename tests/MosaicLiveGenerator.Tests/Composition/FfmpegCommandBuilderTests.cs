using MosaicLiveGenerator.Composition;
using Xunit;

namespace MosaicLiveGenerator.Tests.Composition;

public class FfmpegCommandBuilderTests
{
    [Fact]
    public void TwoSourceMosaic_ProducesExpectedArgSkeleton()
    {
        var sources = new[] {
            new VideoSource("cam1", new Uri("udp://127.0.0.1:5001"), SourceProtocol.MpegTsUdp),
            new VideoSource("cam2", new Uri("udp://127.0.0.1:5002"), SourceProtocol.MpegTsUdp),
        };
        var options = new MosaicSessionOptions(
            Sources: sources,
            Layout: Layout.Grid(1, 2),
            Output: new OutputOptions(new Uri("udp://127.0.0.1:6000")));

        var (args, _) = FfmpegCommandBuilder.Build(options, sdpDirectory: "/tmp/none");

        // -y for overwrite, -hide_banner first
        Assert.Equal("-hide_banner", args[0]);
        Assert.Equal("-y", args[1]);

        // input args appear twice (one per source)
        Assert.Equal(2, args.Count(a => a == "-fflags"));

        // filter_complex appears once
        Assert.Equal(1, args.Count(a => a == "-filter_complex"));

        // map [out]:v
        var mapIdx = args.IndexOf("-map");
        Assert.Equal("[out]", args[mapIdx + 1]);

        // ends with the output URI
        Assert.EndsWith("?pkt_size=1316", args[^1]);
    }

    [Fact]
    public void OneSource_RtpInputAndUdpOutput_HasSdpInputAndMuxdelay()
    {
        var sources = new[] {
            new VideoSource("rtp1", new Uri("rtp://127.0.0.1:5004"), SourceProtocol.RtpH264),
        };
        var options = new MosaicSessionOptions(
            Sources: sources,
            Layout: Layout.Custom(new[] { new NormRect(0, 0, 1, 1) }),
            Output: new OutputOptions(new Uri("udp://127.0.0.1:6000")));

        var (args, _) = FfmpegCommandBuilder.Build(options, sdpDirectory: "/tmp/session-x");

        Assert.Contains("file,udp,rtp,crypto", args);
        Assert.Contains("/tmp/session-x/src-0.sdp", args[args.IndexOf("-i") + 1]);
        Assert.Contains("-muxdelay", args);
    }

    [Fact]
    public void Build_ReturnsRectsForCallerToLog()
    {
        var sources = new[] {
            new VideoSource("a", new Uri("udp://127.0.0.1:5001"), SourceProtocol.MpegTsUdp),
            new VideoSource("b", new Uri("udp://127.0.0.1:5002"), SourceProtocol.MpegTsUdp),
        };
        var options = new MosaicSessionOptions(
            Sources: sources,
            Layout: Layout.Grid(1, 2),
            Output: new OutputOptions(new Uri("udp://127.0.0.1:6000")));

        var (_, placements) = FfmpegCommandBuilder.Build(options, "/tmp");

        Assert.Equal(2, placements.Count);
        Assert.Equal(0, placements[0].Rect.X);
        Assert.Equal(960, placements[1].Rect.X); // 1920/2
    }
}
