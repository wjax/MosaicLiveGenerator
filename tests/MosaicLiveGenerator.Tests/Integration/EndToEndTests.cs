using MosaicLiveGenerator.Process;
using Xunit;

namespace MosaicLiveGenerator.Tests.Integration;

[Trait("Category", "Integration")]
public class EndToEndTests
{
    [Fact]
    public async Task TwoSyntheticSources_ProduceComposedOutput()
    {
        // Skip if ffmpeg unavailable
        if (FfmpegPathResolver.TryFindOnPath() is null)
        {
            return; // xUnit skip pattern; alternative: throw SkipException with Xunit.SkippableFact
        }

        await using var src1 = new SyntheticSource(port: 17001);
        await using var src2 = new SyntheticSource(port: 17002);

        // Let the synthetic sources warm up
        await Task.Delay(1500);

        var options = new MosaicSessionOptions(
            Sources: new[]
            {
                new VideoSource("syn1", new Uri("udp://127.0.0.1:17001"), SourceProtocol.MpegTsUdp),
                new VideoSource("syn2", new Uri("udp://127.0.0.1:17002"), SourceProtocol.MpegTsUdp),
            },
            Layout: Layout.Grid(1, 2),
            Output: new OutputOptions(
                Uri: new Uri("udp://127.0.0.1:18000"),
                Width: 1280, Height: 720, FrameRate: 25,
                BitrateKbps: 2000),
            Ffmpeg: new FfmpegOptions(StartupTimeout: TimeSpan.FromSeconds(15)));

        await using var session = new MosaicSession(options);
        await session.StartAsync();

        Assert.Equal(SessionState.Running, session.State);

        await Task.Delay(2000); // run for 2s
        await session.StopAsync();

        Assert.Equal(SessionState.Stopped, session.State);
    }
}
