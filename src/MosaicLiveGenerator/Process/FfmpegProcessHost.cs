using System.Diagnostics;

namespace MosaicLiveGenerator.Process;

internal sealed class FfmpegProcessHost : IProcessHost
{
    private System.Diagnostics.Process? _process;
    private Task? _stderrPump;
    private CancellationTokenSource? _pumpCts;
    private int _running;

    public bool IsRunning => Volatile.Read(ref _running) == 1;

    public event EventHandler<string>? StderrLineReceived;
    public event EventHandler<ProcessExitInfo>? Exited;

    public Task StartAsync(string executable, IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var p = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true };
        p.Exited += OnExited;

        if (!p.Start())
            throw new MosaicStartupException($"failed to start '{executable}'")
            {
                Reason = MosaicStartupReason.Other
            };

        _process = p;
        Interlocked.Exchange(ref _running, 1);

        _pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _stderrPump = Task.Run(() => PumpStderrAsync(p, _pumpCts.Token));

        return Task.CompletedTask;
    }

    private async Task PumpStderrAsync(System.Diagnostics.Process p, CancellationToken ct)
    {
        try
        {
            string? line;
            while ((line = await p.StandardError.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
            {
                StderrLineReceived?.Invoke(this, line);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* process gone; Exited will fire */ }
    }

    private void OnExited(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _running, 0) == 0) return;
        var info = new ProcessExitInfo(_process?.ExitCode ?? -1, TimedOutOnGraceful: false);
        Exited?.Invoke(this, info);
    }

    public async Task SendGracefulQuitAsync(CancellationToken ct)
    {
        if (_process is null || _process.HasExited) return;
        try
        {
            await _process.StandardInput.WriteLineAsync(new ReadOnlyMemory<char>("q".ToCharArray()), ct).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception) { /* fall through to kill */ }
    }

    public void Kill()
    {
        try { _process?.Kill(entireProcessTree: true); }
        catch { }
    }

    public async ValueTask DisposeAsync()
    {
        try { _pumpCts?.Cancel(); } catch { }
        try { if (_stderrPump is not null) await _stderrPump.ConfigureAwait(false); } catch { }
        try { _process?.Dispose(); } catch { }
        _process = null;
    }
}
