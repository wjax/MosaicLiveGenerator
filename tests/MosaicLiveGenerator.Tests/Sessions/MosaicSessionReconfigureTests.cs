using MosaicLiveGenerator.Process;
using MosaicLiveGenerator.Tests.Process;
using Xunit;

namespace MosaicLiveGenerator.Tests.Sessions;

public class MosaicSessionReconfigureTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static MosaicSessionOptions MakeOptions(
        string ffmpegPath,
        int sourcePorts = 2,
        int rows = 1,
        int cols = 2) =>
        new(
            Sources: Enumerable.Range(0, sourcePorts)
                .Select(i => new VideoSource($"src{i}", new Uri($"udp://127.0.0.1:{5001 + i}"), SourceProtocol.MpegTsUdp))
                .ToArray(),
            Layout: Layout.Grid(rows, cols),
            Output: new OutputOptions(new Uri("udp://127.0.0.1:6000")),
            Ffmpeg: new FfmpegOptions(BinaryPath: ffmpegPath, StartupTimeout: TimeSpan.FromSeconds(2)));

    /// <summary>
    /// Returns a factory that vends FakeProcessHost instances from a queue.
    /// Each call to the factory dequeues the next fake.
    /// </summary>
    private static (Func<IProcessHost> factory, FakeProcessHost[] fakes)
        FakeFactory(int count = 2)
    {
        var arr = Enumerable.Range(0, count).Select(_ => new FakeProcessHost()).ToArray();
        var queue = new Queue<FakeProcessHost>(arr);
        return (() => queue.Dequeue(), arr);
    }

    private static string TempBinary()
    {
        var p = Path.GetTempFileName();
        return p;
    }

    // ── ReconfigureAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ReconfigureAsync_WhileRunning_TransitionsThroughReconfiguring()
    {
        var path = TempBinary();
        try
        {
            var (factory, fakes) = FakeFactory(2);
            var session = new MosaicSession(MakeOptions(path), null, factory);

            // Start initial session
            var startTask = session.StartAsync();
            await Task.Delay(20);
            fakes[0].EmitStderr("frame=  1 fps=25");
            await startTask;
            Assert.Equal(SessionState.Running, session.State);

            // Capture state transitions during reconfigure
            var states = new List<SessionState>();
            session.StateChanged += (_, e) => states.Add(e.NewState);

            // Begin reconfigure
            var newOptions = MakeOptions(path) with
            {
                Layout = Layout.Grid(2, 1),
                Sources = new[]
                {
                    new VideoSource("cam-a", new Uri("udp://127.0.0.1:5001"), SourceProtocol.MpegTsUdp),
                    new VideoSource("cam-b", new Uri("udp://127.0.0.1:5002"), SourceProtocol.MpegTsUdp),
                },
            };

            var reconfigTask = session.ReconfigureAsync(newOptions);
            await Task.Delay(20);
            // Simulate old ffmpeg responding to graceful quit
            fakes[0].EmitExit(new ProcessExitInfo(0, false));
            await Task.Delay(20);
            // New ffmpeg signals it is running
            fakes[1].EmitStderr("frame=  1 fps=25");
            await reconfigTask;

            Assert.Equal(SessionState.Running, session.State);
            // Should have seen: Reconfiguring, Starting, Running
            Assert.Contains(SessionState.Reconfiguring, states);
            Assert.Contains(SessionState.Starting, states);
            Assert.Contains(SessionState.Running, states);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ReconfigureAsync_UpdatesSourceStates()
    {
        var path = TempBinary();
        try
        {
            var (factory, fakes) = FakeFactory(2);
            var session = new MosaicSession(MakeOptions(path), null, factory);

            var startTask = session.StartAsync();
            await Task.Delay(20);
            fakes[0].EmitStderr("frame=  1 fps=25");
            await startTask;

            var newSources = new[]
            {
                new VideoSource("newA", new Uri("udp://127.0.0.1:5011"), SourceProtocol.MpegTsUdp),
                new VideoSource("newB", new Uri("udp://127.0.0.1:5012"), SourceProtocol.MpegTsUdp),
            };
            var newOptions = MakeOptions(path) with { Sources = newSources };

            var reconfigTask = session.ReconfigureAsync(newOptions);
            await Task.Delay(20);
            fakes[0].EmitExit(new ProcessExitInfo(0, false));
            await Task.Delay(20);
            fakes[1].EmitStderr("frame=  1 fps=25");
            await reconfigTask;

            Assert.Equal(2, session.SourceStates.Count);
            Assert.Equal("newA", session.SourceStates[0].Name);
            Assert.Equal("newB", session.SourceStates[1].Name);
            Assert.All(session.SourceStates, s => Assert.Equal(SourceConnectivity.Unknown, s.Connectivity));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ReconfigureAsync_ClearsOutputSdp()
    {
        var path = TempBinary();
        try
        {
            var (factory, fakes) = FakeFactory(2);
            var options = MakeOptions(path) with
            {
                Output = new OutputOptions(new Uri("rtp://127.0.0.1:6000"), Protocol: OutputProtocol.RtpH264),
                Sources = new[]
                {
                    new VideoSource("a", new Uri("udp://127.0.0.1:5001"), SourceProtocol.MpegTsUdp),
                    new VideoSource("b", new Uri("udp://127.0.0.1:5002"), SourceProtocol.MpegTsUdp),
                },
            };
            var session = new MosaicSession(options, null, factory);

            var startTask = session.StartAsync();
            await Task.Delay(20);
            fakes[0].EmitStderr("SDP:");
            fakes[0].EmitStderr("v=0");
            fakes[0].EmitStderr("m=video 6000 RTP/AVP 96");
            fakes[0].EmitStderr("");
            fakes[0].EmitStderr("frame=  1 fps=25");
            await startTask;
            Assert.NotNull(session.OutputSdp);

            var reconfigTask = session.ReconfigureAsync(options);
            await Task.Delay(20);
            fakes[0].EmitExit(new ProcessExitInfo(0, false));
            await Task.Delay(20);
            fakes[1].EmitStderr("frame=  1 fps=25");
            await reconfigTask;

            // After reconfigure completes without a new SDP the field stays null
            Assert.Null(session.OutputSdp);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ReconfigureAsync_InvalidNewOptions_ThrowsConfigurationException_StateUnchanged()
    {
        var path = TempBinary();
        try
        {
            var (factory, fakes) = FakeFactory(1);
            var session = new MosaicSession(MakeOptions(path), null, factory);

            var startTask = session.StartAsync();
            await Task.Delay(20);
            fakes[0].EmitStderr("frame=  1 fps=25");
            await startTask;
            Assert.Equal(SessionState.Running, session.State);

            // Layout expects 4 sources but we only supply 2 — validation should fire before teardown
            var badOptions = MakeOptions(path) with { Layout = Layout.Grid(2, 2) };

            await Assert.ThrowsAsync<MosaicConfigurationException>(() =>
                session.ReconfigureAsync(badOptions));

            // Session should still be Running — no teardown happened
            Assert.Equal(SessionState.Running, session.State);

            await session.StopAsync();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ReconfigureAsync_FromStopped_ThrowsInvalidOperation()
    {
        var path = TempBinary();
        try
        {
            var session = new MosaicSession(MakeOptions(path));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                session.ReconfigureAsync(MakeOptions(path)));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ReconfigureAsync_FromFaulted_Succeeds()
    {
        var path = TempBinary();
        try
        {
            var (factory, fakes) = FakeFactory(2);
            var session = new MosaicSession(MakeOptions(path), null, factory);

            var startTask = session.StartAsync();
            await Task.Delay(20);
            fakes[0].EmitStderr("frame=  1 fps=25");
            await startTask;

            // Fault the session
            fakes[0].EmitExit(new ProcessExitInfo(139, false));
            await Task.Delay(20);
            Assert.Equal(SessionState.Faulted, session.State);

            // Reconfigure from Faulted should work
            var reconfigTask = session.ReconfigureAsync(MakeOptions(path));
            await Task.Delay(20);
            fakes[1].EmitStderr("frame=  1 fps=25");
            await reconfigTask;

            Assert.Equal(SessionState.Running, session.State);
            await session.StopAsync();
        }
        finally { File.Delete(path); }
    }

    // ── ReconfigureLayoutAsync ───────────────────────────────────────────────

    [Fact]
    public async Task ReconfigureLayoutAsync_ChangesFilterGraph()
    {
        var path = TempBinary();
        try
        {
            var (factory, fakes) = FakeFactory(2);
            var session = new MosaicSession(MakeOptions(path, rows: 1, cols: 2), null, factory);

            var startTask = session.StartAsync();
            await Task.Delay(20);
            fakes[0].EmitStderr("frame=  1 fps=25");
            await startTask;

            var reconfigTask = session.ReconfigureLayoutAsync(Layout.Grid(2, 1));
            await Task.Delay(20);
            fakes[0].EmitExit(new ProcessExitInfo(0, false));
            await Task.Delay(20);
            fakes[1].EmitStderr("frame=  1 fps=25");
            await reconfigTask;

            Assert.Equal(SessionState.Running, session.State);
            // The second process should have received new filter args
            Assert.NotNull(fakes[1].StartedArgs);
            Assert.Contains("-filter_complex", fakes[1].StartedArgs!);

            await session.StopAsync();
        }
        finally { File.Delete(path); }
    }

    // ── ReconfigureSourcesAsync ──────────────────────────────────────────────

    [Fact]
    public async Task ReconfigureSourcesAsync_ReplacesSourcesInNewProcess()
    {
        var path = TempBinary();
        try
        {
            var (factory, fakes) = FakeFactory(2);
            var session = new MosaicSession(MakeOptions(path), null, factory);

            var startTask = session.StartAsync();
            await Task.Delay(20);
            fakes[0].EmitStderr("frame=  1 fps=25");
            await startTask;

            var newSources = new[]
            {
                new VideoSource("replaced-a", new Uri("udp://127.0.0.1:7001"), SourceProtocol.MpegTsUdp),
                new VideoSource("replaced-b", new Uri("udp://127.0.0.1:7002"), SourceProtocol.MpegTsUdp),
            };

            var reconfigTask = session.ReconfigureSourcesAsync(newSources);
            await Task.Delay(20);
            fakes[0].EmitExit(new ProcessExitInfo(0, false));
            await Task.Delay(20);
            fakes[1].EmitStderr("frame=  1 fps=25");
            await reconfigTask;

            Assert.Equal(SessionState.Running, session.State);
            Assert.Equal("replaced-a", session.SourceStates[0].Name);
            Assert.Equal("replaced-b", session.SourceStates[1].Name);

            // New args must reference the new URIs (with query params appended by InputArgBuilder)
            Assert.NotNull(fakes[1].StartedArgs);
            Assert.Contains(fakes[1].StartedArgs!, a => a.StartsWith("udp://127.0.0.1:7001"));

            await session.StopAsync();
        }
        finally { File.Delete(path); }
    }
}
