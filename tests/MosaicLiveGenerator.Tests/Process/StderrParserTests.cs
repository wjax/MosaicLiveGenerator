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
}
