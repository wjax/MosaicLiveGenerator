namespace MosaicLiveGenerator.Process;

internal interface IProcessHost : IAsyncDisposable
{
    /// <summary>
    /// Starts the child process. Returns once the OS has acknowledged the spawn.
    /// </summary>
    Task StartAsync(string executable, IReadOnlyList<string> args, CancellationToken ct);

    /// <summary>
    /// Fires for every line of stderr (and stdout, if relevant) the process emits.
    /// </summary>
    event EventHandler<string>? StderrLineReceived;

    /// <summary>
    /// Fires exactly once when the process exits, with exit info.
    /// </summary>
    event EventHandler<ProcessExitInfo>? Exited;

    /// <summary>
    /// True after StartAsync completes successfully and before Exited fires.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Sends a graceful-quit signal (ffmpeg-specific: "q\n" on stdin).
    /// </summary>
    Task SendGracefulQuitAsync(CancellationToken ct);

    /// <summary>
    /// Force-kills the process and its descendants.
    /// </summary>
    void Kill();
}
