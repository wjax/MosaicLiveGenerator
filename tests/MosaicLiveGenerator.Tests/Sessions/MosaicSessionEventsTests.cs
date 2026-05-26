using MosaicLiveGenerator.Process;
using MosaicLiveGenerator.Tests.Process;
using Xunit;

namespace MosaicLiveGenerator.Tests.Sessions;

public class MosaicSessionEventsTests
{
    private static MosaicSessionOptions MakeOptions(string ffmpegPath) => new(
        Sources: new[] {
            new VideoSource("cam1", new Uri("udp://127.0.0.1:5001"), SourceProtocol.MpegTsUdp),
            new VideoSource("cam2", new Uri("udp://127.0.0.1:5002"), SourceProtocol.MpegTsUdp),
        },
        Layout: Layout.Grid(1, 2),
        Output: new OutputOptions(new Uri("rtp://127.0.0.1:6000"), Protocol: OutputProtocol.RtpH264),
        Ffmpeg: new FfmpegOptions(BinaryPath: ffmpegPath, StartupTimeout: TimeSpan.FromSeconds(2)));

    [Fact]
    public async Task SourceConnectivityChanged_FiresOnConnectionRefused()
    {
        var path = Path.GetTempFileName();
        try
        {
            var fake = new FakeProcessHost();
            var session = new MosaicSession(MakeOptions(path), null, () => fake);

            var events = new List<SourceConnectivityChangedEventArgs>();
            session.SourceConnectivityChanged += (_, e) => events.Add(e);

            var startTask = session.StartAsync();
            await Task.Delay(20);
            fake.EmitStderr("frame=  1 fps=25");
            await startTask;

            fake.EmitStderr("Input #1, mpegts, from 'udp://127.0.0.1:5002':");
            fake.EmitStderr("[udp @ 0x55] Connection refused");

            // give events time to dispatch
            await Task.Delay(20);

            Assert.Single(events);
            Assert.Equal(1, events[0].SourceIndex);
            Assert.Equal("cam2", events[0].SourceName);
            Assert.Equal(SourceConnectivity.Disconnected, events[0].NewConnectivity);

            // Verify SourceStates reflects it
            Assert.Equal(SourceConnectivity.Disconnected, session.SourceStates[1].Connectivity);
            Assert.Equal(SourceConnectivity.Unknown, session.SourceStates[0].Connectivity);

            await session.StopAsync();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task OutputSdp_PopulatedAfterStart_ForRtpOutput()
    {
        var path = Path.GetTempFileName();
        try
        {
            var fake = new FakeProcessHost();
            var session = new MosaicSession(MakeOptions(path), null, () => fake);

            var startTask = session.StartAsync();
            await Task.Delay(20);
            fake.EmitStderr("SDP:");
            fake.EmitStderr("v=0");
            fake.EmitStderr("o=- 0 0 IN IP4 127.0.0.1");
            fake.EmitStderr("m=video 6000 RTP/AVP 96");
            fake.EmitStderr("a=rtpmap:96 H264/90000");
            fake.EmitStderr("");                 // SDP terminator
            fake.EmitStderr("frame=  1 fps=25");
            await startTask;

            Assert.NotNull(session.OutputSdp);
            Assert.Contains("m=video 6000", session.OutputSdp!);

            fake.EmitExit(new ProcessExitInfo(0, false));
            await session.StopAsync();
        }
        finally { File.Delete(path); }
    }
}
