using MosaicLiveGenerator.Process;
using MosaicLiveGenerator.Tests.Process;
using Xunit;

namespace MosaicLiveGenerator.Tests.Sessions;

public class MosaicSessionStopTests
{
    private static MosaicSessionOptions MakeOptions(string ffmpegPath) => new(
        Sources: new[] { new VideoSource("a", new Uri("udp://127.0.0.1:5001"), SourceProtocol.MpegTsUdp) },
        Layout: Layout.Grid(1, 1),
        Output: new OutputOptions(new Uri("udp://127.0.0.1:6000")),
        Ffmpeg: new FfmpegOptions(BinaryPath: ffmpegPath, StartupTimeout: TimeSpan.FromSeconds(2)));

    private static (string ffmpegPath, IDisposable cleanup) FakeBinary()
    {
        var tmp = Path.GetTempFileName();
        return (tmp, new DeleteOnDispose(tmp));
    }

    private sealed class DeleteOnDispose : IDisposable
    {
        private readonly string _path;
        public DeleteOnDispose(string p) => _path = p;
        public void Dispose() { try { File.Delete(_path); } catch { } }
    }

    [Fact]
    public async Task StopAsync_FromRunning_SendsGracefulQuitThenExits()
    {
        var (path, cleanup) = FakeBinary();
        using var _ = cleanup;

        var fake = new FakeProcessHost();
        var session = new MosaicSession(MakeOptions(path), null, () => fake);

        var startTask = session.StartAsync();
        await Task.Delay(20);
        fake.EmitStderr("frame=  1 fps=25");
        await startTask;

        var stopTask = session.StopAsync();
        await Task.Delay(20);
        Assert.Equal(1, fake.GracefulQuitCount);

        // simulate ffmpeg actually exiting in response to 'q'
        fake.EmitExit(new ProcessExitInfo(ExitCode: 0, TimedOutOnGraceful: false));
        await stopTask;

        Assert.Equal(SessionState.Stopped, session.State);
        Assert.Equal(0, fake.KillCount);
    }

    [Fact]
    public async Task StopAsync_NoExitWithin5s_FallsBackToKill()
    {
        var (path, cleanup) = FakeBinary();
        using var _ = cleanup;

        var fake = new FakeProcessHost();
        var session = new MosaicSession(MakeOptions(path), null, () => fake);

        var startTask = session.StartAsync();
        await Task.Delay(20);
        fake.EmitStderr("frame=  1");
        await startTask;

        // do not emit exit
        // shorten grace via cancellation to keep test fast
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await session.StopAsync(cts.Token);

        Assert.Equal(SessionState.Stopped, session.State);
        Assert.Equal(1, fake.KillCount);
    }

    [Fact]
    public async Task UnexpectedExit_WhileRunning_FiresFaultedEventAndSetsLastError()
    {
        var (path, cleanup) = FakeBinary();
        using var _ = cleanup;

        var fake = new FakeProcessHost();
        var session = new MosaicSession(MakeOptions(path), null, () => fake);

        var startTask = session.StartAsync();
        await Task.Delay(20);
        fake.EmitStderr("frame=  1");
        await startTask;

        FaultedEventArgs? captured = null;
        session.Faulted += (_, e) => captured = e;

        fake.EmitStderr("Segmentation fault");
        fake.EmitExit(new ProcessExitInfo(ExitCode: 139, TimedOutOnGraceful: false));

        // give the event handler a tick
        await Task.Delay(20);

        Assert.Equal(SessionState.Faulted, session.State);
        Assert.NotNull(captured);
        Assert.IsType<MosaicRuntimeException>(session.LastError);
    }

    [Fact]
    public async Task DisposeAsync_StopsAndCleansUp()
    {
        var (path, cleanup) = FakeBinary();
        using var _ = cleanup;

        var fake = new FakeProcessHost();
        var session = new MosaicSession(MakeOptions(path), null, () => fake);

        var startTask = session.StartAsync();
        await Task.Delay(20);
        fake.EmitStderr("frame=  1");
        await startTask;

        // arrange immediate exit on quit
        fake.EmitExit(new ProcessExitInfo(0, false));

        await session.DisposeAsync();

        Assert.Equal(SessionState.Stopped, session.State);
        Assert.True(fake.Disposed);
    }
}
