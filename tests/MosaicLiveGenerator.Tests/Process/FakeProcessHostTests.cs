using MosaicLiveGenerator.Process;
using Xunit;

namespace MosaicLiveGenerator.Tests.Process;

public class FakeProcessHostTests
{
    [Fact]
    public async Task StartAsync_FlipsIsRunning_AndRecordsArgs()
    {
        var fake = new FakeProcessHost();
        await fake.StartAsync("/usr/bin/ffmpeg", new[] { "-i", "x" }, CancellationToken.None);

        Assert.True(fake.IsRunning);
        Assert.Equal("/usr/bin/ffmpeg", fake.StartedExecutable);
        Assert.Equal(new[] { "-i", "x" }, fake.StartedArgs);
    }

    [Fact]
    public void EmitStderr_FiresEventInOrder()
    {
        var fake = new FakeProcessHost();
        var lines = new List<string>();
        fake.StderrLineReceived += (_, l) => lines.Add(l);

        fake.EmitStderr("a", "b", "c");

        Assert.Equal(new[] { "a", "b", "c" }, lines);
    }

    [Fact]
    public async Task Kill_FiresExitWithSentinel()
    {
        var fake = new FakeProcessHost();
        ProcessExitInfo? captured = null;
        fake.Exited += (_, e) => captured = e;
        await fake.StartAsync("x", Array.Empty<string>(), default);

        fake.Kill();

        Assert.NotNull(captured);
        Assert.True(captured!.TimedOutOnGraceful);
        Assert.False(fake.IsRunning);
    }
}
