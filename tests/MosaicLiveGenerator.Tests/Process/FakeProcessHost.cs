using MosaicLiveGenerator.Process;

namespace MosaicLiveGenerator.Tests.Process;

internal sealed class FakeProcessHost : IProcessHost
{
    private bool _running;
    public bool IsRunning => _running;

    public string? StartedExecutable { get; private set; }
    public IReadOnlyList<string>? StartedArgs { get; private set; }
    public int GracefulQuitCount { get; private set; }
    public int KillCount { get; private set; }
    public bool Disposed { get; private set; }

    public event EventHandler<string>? StderrLineReceived;
    public event EventHandler<ProcessExitInfo>? Exited;

    public Task StartAsync(string executable, IReadOnlyList<string> args, CancellationToken ct)
    {
        StartedExecutable = executable;
        StartedArgs = args;
        _running = true;
        return Task.CompletedTask;
    }

    public Task SendGracefulQuitAsync(CancellationToken ct)
    {
        GracefulQuitCount++;
        return Task.CompletedTask;
    }

    public void Kill()
    {
        KillCount++;
        if (_running) EmitExit(new ProcessExitInfo(ExitCode: -1, TimedOutOnGraceful: true));
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    // Test driving methods:
    public void EmitStderr(string line) => StderrLineReceived?.Invoke(this, line);
    public void EmitStderr(params string[] lines) { foreach (var l in lines) EmitStderr(l); }

    public void EmitExit(ProcessExitInfo info)
    {
        if (!_running) return;
        _running = false;
        Exited?.Invoke(this, info);
    }
}
