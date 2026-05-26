using MosaicLiveGenerator.Process;
using MosaicLiveGenerator.Tests.Process;
using Xunit;

namespace MosaicLiveGenerator.Tests.Sessions;

public class MosaicSessionStartTests
{
    private static MosaicSessionOptions MakeOptions(string ffmpegPath) => new(
        Sources: new[] {
            new VideoSource("a", new Uri("udp://127.0.0.1:5001"), SourceProtocol.MpegTsUdp),
            new VideoSource("b", new Uri("udp://127.0.0.1:5002"), SourceProtocol.MpegTsUdp),
        },
        Layout: Layout.Grid(1, 2),
        Output: new OutputOptions(new Uri("udp://127.0.0.1:6000")),
        Ffmpeg: new FfmpegOptions(BinaryPath: ffmpegPath, StartupTimeout: TimeSpan.FromSeconds(2)));

    private static string FakeFfmpegPath()
    {
        var tmp = Path.GetTempFileName();
        return tmp; // its existence is all we need; we never actually exec it
    }

    [Fact]
    public async Task StartAsync_ReachesRunningOnFirstFrameLine()
    {
        var ffmpegPath = FakeFfmpegPath();
        try
        {
            var fake = new FakeProcessHost();
            var session = new MosaicSession(MakeOptions(ffmpegPath), logger: null, () => fake);

            var startTask = session.StartAsync();
            // Simulate ffmpeg printing a frame= line ~after start
            await Task.Delay(20);
            fake.EmitStderr("frame=    1 fps=0.0 q=24.0 size=...");
            await startTask;

            Assert.Equal(SessionState.Running, session.State);
            Assert.NotNull(fake.StartedExecutable);
            Assert.Contains("-filter_complex", fake.StartedArgs!);
        }
        finally { File.Delete(ffmpegPath); }
    }

    [Fact]
    public async Task StartAsync_TransitionsThroughStartingState()
    {
        var ffmpegPath = FakeFfmpegPath();
        try
        {
            var fake = new FakeProcessHost();
            var session = new MosaicSession(MakeOptions(ffmpegPath), logger: null, () => fake);

            var states = new List<SessionState>();
            session.StateChanged += (_, e) => states.Add(e.NewState);

            var startTask = session.StartAsync();
            await Task.Delay(20);
            fake.EmitStderr("frame=  1 fps=0.0");
            await startTask;

            Assert.Equal(new[] { SessionState.Starting, SessionState.Running }, states);
        }
        finally { File.Delete(ffmpegPath); }
    }
}
