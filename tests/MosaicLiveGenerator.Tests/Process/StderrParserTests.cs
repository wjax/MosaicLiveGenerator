using MosaicLiveGenerator.Process;
using Xunit;

namespace MosaicLiveGenerator.Tests.Process;

public class StderrParserTests
{
    [Fact]
    public void FrameLine_EmitsRunningSignalOnce()
    {
        var parser = new StderrParser();
        var running = 0;
        parser.Running += (_, _) => running++;

        parser.Feed("frame=    1 fps=0.0 q=24.0 size=...");
        parser.Feed("frame=   25 fps=25 q=24.0 size=...");
        parser.Feed("frame=   50 fps=25 q=24.0 size=...");

        Assert.Equal(1, running);
    }

    [Fact]
    public void ErrorOpeningInput_EmitsStartupErrorSignal()
    {
        var parser = new StderrParser();
        StartupErrorSignal? captured = null;
        parser.StartupError += (_, s) => captured = s;

        parser.Feed("[udp @ 0x55...] Error opening input file udp://1.2.3.4:5000");

        Assert.NotNull(captured);
        Assert.Equal(MosaicStartupReason.BadInputSource, captured!.Reason);
    }

    [Fact]
    public void NoSuchFile_EmitsStartupError()
    {
        var parser = new StderrParser();
        StartupErrorSignal? captured = null;
        parser.StartupError += (_, s) => captured = s;

        parser.Feed("No such file or directory");

        Assert.NotNull(captured);
    }

    [Fact]
    public void StderrTail_KeepsLast32Lines()
    {
        var parser = new StderrParser();

        for (var i = 0; i < 100; i++) parser.Feed($"line {i}");

        var tail = parser.GetStderrTail();
        var lines = tail.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(32, lines.Length);
        Assert.Equal("line 99", lines[^1]);
        Assert.Equal("line 68", lines[0]);
    }

    [Fact]
    public void ConnectionRefused_NearInputContext_AttributesToThatSource()
    {
        var parser = new StderrParser();
        SourceConnectivitySignal? sig = null;
        parser.SourceConnectivity += (_, s) => sig = s;

        parser.Feed("Input #2, mpegts, from 'udp://127.0.0.1:5006':");
        parser.Feed("[udp @ 0x55] Connection refused");

        Assert.NotNull(sig);
        Assert.Equal(2, sig!.SourceIndex);
        Assert.Equal(SourceConnectivity.Disconnected, sig.NewState);
    }

    [Fact]
    public void ConnectionRefused_FarFromInputContext_IsAmbiguous()
    {
        var parser = new StderrParser();
        SourceConnectivitySignal? sig = null;
        parser.SourceConnectivity += (_, s) => sig = s;

        parser.Feed("Input #0, mpegts, from 'udp://...':");
        for (var i = 0; i < 10; i++) parser.Feed("unrelated line");
        parser.Feed("[tcp @ 0x55] Connection refused");

        Assert.NotNull(sig);
        Assert.Equal(-1, sig!.SourceIndex);
    }

    [Fact]
    public void Reconnect_FollowedByFrames_RestoresConnectedState()
    {
        var parser = new StderrParser();
        var signals = new List<SourceConnectivitySignal>();
        parser.SourceConnectivity += (_, s) => signals.Add(s);

        parser.Feed("Input #1, mpegts, from 'udp://...':");
        parser.Feed("Connection refused");                 // -> Disconnected for source 1
        parser.Feed("[udp @ x] Will reconnect at ...");    // reconnect notice
        parser.Feed("frame=  100 fps=25");                  // first frame after reconnect

        // 1 disconnect + 1 reconnect signal
        Assert.Equal(2, signals.Count);
        Assert.Equal(SourceConnectivity.Disconnected, signals[0].NewState);
        Assert.Equal(SourceConnectivity.Connected, signals[1].NewState);
        Assert.Equal(1, signals[1].SourceIndex);
    }
}
