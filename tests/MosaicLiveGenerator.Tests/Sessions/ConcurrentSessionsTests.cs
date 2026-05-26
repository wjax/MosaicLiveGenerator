using MosaicLiveGenerator.Process;
using MosaicLiveGenerator.Tests.Process;
using Xunit;

namespace MosaicLiveGenerator.Tests.Sessions;

public class ConcurrentSessionsTests
{
    private static MosaicSessionOptions Opts(int outputPort, string path) => new(
        Sources: new[] { new VideoSource("a", new Uri("udp://127.0.0.1:5001"), SourceProtocol.MpegTsUdp) },
        Layout: Layout.Grid(1, 1),
        Output: new OutputOptions(new Uri($"udp://127.0.0.1:{outputPort}")),
        Ffmpeg: new FfmpegOptions(BinaryPath: path, StartupTimeout: TimeSpan.FromSeconds(2)));

    [Fact]
    public async Task TwoSessions_RunIndependently()
    {
        var path = Path.GetTempFileName();
        try
        {
            var fakeA = new FakeProcessHost();
            var fakeB = new FakeProcessHost();
            var sA = new MosaicSession(Opts(6001, path), null, () => fakeA);
            var sB = new MosaicSession(Opts(6002, path), null, () => fakeB);

            var ta = sA.StartAsync();
            var tb = sB.StartAsync();
            await Task.Delay(20);
            fakeA.EmitStderr("frame=  1");
            fakeB.EmitStderr("frame=  1");
            await Task.WhenAll(ta, tb);

            Assert.Equal(SessionState.Running, sA.State);
            Assert.Equal(SessionState.Running, sB.State);

            // fault one — the other stays Running
            fakeA.EmitExit(new ProcessExitInfo(139, false));
            await Task.Delay(20);
            Assert.Equal(SessionState.Faulted, sA.State);
            Assert.Equal(SessionState.Running, sB.State);

            fakeB.EmitExit(new ProcessExitInfo(0, false));
            await sB.StopAsync();
            await sA.StopAsync();

            Assert.Equal(SessionState.Stopped, sA.State);
            Assert.Equal(SessionState.Stopped, sB.State);
        }
        finally { File.Delete(path); }
    }
}
