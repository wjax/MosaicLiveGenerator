using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MosaicLiveGenerator.Composition;
using MosaicLiveGenerator.Diagnostics;
using MosaicLiveGenerator.Process;

namespace MosaicLiveGenerator;

public sealed class MosaicSession : IAsyncDisposable
{
    private readonly ILogger<MosaicSession> _logger;
    private readonly Func<IProcessHost> _processHostFactory;
    private readonly SessionStateMachine _state = new();

    private MosaicSessionOptions _options;
    private SourceStateTable _sourceStates;

    private IProcessHost? _host;
    private StderrParser? _parser;
    private TaskCompletionSource? _runningTcs;
    private TaskCompletionSource<ProcessExitInfo>? _exitTcs;
    private string? _sdpDir;
    private Exception? _startupError;

    public MosaicSession(
        MosaicSessionOptions options,
        ILogger<MosaicSession>? logger = null)
        : this(options, logger, () => new MosaicLiveGenerator.Process.FfmpegProcessHost())
    {
    }

    internal MosaicSession(
        MosaicSessionOptions options,
        ILogger<MosaicSession>? logger,
        Func<IProcessHost> processHostFactory)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<MosaicSession>.Instance;
        _processHostFactory = processHostFactory;

        Validate(options);

        _sourceStates = new SourceStateTable(options.Sources);
        _state.Changed += (_, e) => StateChanged?.Invoke(this, e);
    }

    public SessionState State => _state.Current;

    public IReadOnlyList<SourceState> SourceStates => _sourceStates.Snapshot();

    public Exception? LastError { get; private set; }

    public string? OutputSdp { get; private set; }

    public event EventHandler<SessionStateChangedEventArgs>? StateChanged;
    public event EventHandler<SourceConnectivityChangedEventArgs>? SourceConnectivityChanged;
    public event EventHandler<FaultedEventArgs>? Faulted;

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (!_state.TryTransition(SessionState.Stopped, SessionState.Starting))
            throw new InvalidOperationException($"Cannot start: state={_state.Current}.");

        try
        {
            await RunStartupAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _state.TryTransition(SessionState.Starting, SessionState.Faulted);
            LastError = ex;
            await SafeTearDownAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Stops the current ffmpeg process, replaces the session configuration with
    /// <paramref name="newOptions"/>, and immediately starts a new ffmpeg process.
    /// The state transitions are: Running/Faulted → Reconfiguring → Starting → Running.
    /// </summary>
    /// <remarks>
    /// The output endpoint (URI and protocol) must remain the same as the original options.
    /// All other fields — sources, layout, chrome, frame rate, bitrate — may be changed freely.
    /// </remarks>
    public async Task ReconfigureAsync(MosaicSessionOptions newOptions, CancellationToken ct = default)
    {
        if (newOptions is null) throw new ArgumentNullException(nameof(newOptions));

        // Validate the new options before touching any running state.
        Validate(newOptions);

        var current = _state.Current;
        if (current is not (SessionState.Running or SessionState.Faulted))
            throw new InvalidOperationException($"Cannot reconfigure: state={current}.");
        if (!_state.TryTransition(current, SessionState.Reconfiguring))
            throw new InvalidOperationException($"Cannot reconfigure: state={_state.Current}.");

        try
        {
            // Stop the running process and clean up its resources.
            await TearDownProcessAsync(ct).ConfigureAwait(false);

            // Swap in the new configuration.
            ResetForOptions(newOptions);

            // Proceed to startup.
            _state.TryTransition(SessionState.Reconfiguring, SessionState.Starting);
            await RunStartupAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Try Starting→Faulted first (RunStartupAsync is the last step and most likely to fail).
            if (!_state.TryTransition(SessionState.Starting, SessionState.Faulted))
                _state.TryTransition(SessionState.Reconfiguring, SessionState.Faulted);
            LastError = ex;
            await SafeTearDownAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Reconfigures the tile layout (and optionally the chrome options) without changing
    /// the sources or output endpoint. The source count must be compatible with the new layout.
    /// </summary>
    public Task ReconfigureLayoutAsync(
        Layout newLayout,
        LayoutOptions? newChrome = null,
        CancellationToken ct = default)
    {
        if (newLayout is null) throw new ArgumentNullException(nameof(newLayout));
        var newOptions = _options with
        {
            Layout = newLayout,
            LayoutChrome = newChrome ?? _options.LayoutChrome,
        };
        return ReconfigureAsync(newOptions, ct);
    }

    /// <summary>
    /// Reconfigures the video sources (and optionally the layout). When <paramref name="newLayout"/>
    /// is null the existing layout is reused, which requires the source count to remain the same.
    /// </summary>
    public Task ReconfigureSourcesAsync(
        IReadOnlyList<VideoSource> newSources,
        Layout? newLayout = null,
        CancellationToken ct = default)
    {
        if (newSources is null) throw new ArgumentNullException(nameof(newSources));
        var newOptions = _options with
        {
            Sources = newSources,
            Layout = newLayout ?? _options.Layout,
        };
        return ReconfigureAsync(newOptions, ct);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        var current = _state.Current;
        if (current == SessionState.Stopped) return;
        if (current == SessionState.Stopping) return;

        if (!_state.TryTransition(current, SessionState.Stopping))
        {
            // someone else moved us; check terminal
            if (_state.Current == SessionState.Stopped) return;
            throw new InvalidOperationException($"Cannot stop: state={_state.Current}.");
        }

        try
        {
            await TearDownProcessAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            await SafeTearDownAsync().ConfigureAwait(false);
            _state.TryTransition(SessionState.Stopping, SessionState.Stopped);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_state.Current != SessionState.Stopped)
        {
            try { await StopAsync().ConfigureAwait(false); }
            catch { /* swallow during dispose */ }
        }
    }

    // ── private helpers ──────────────────────────────────────────────────────

    /// <summary>Runs the ffmpeg startup sequence. State must already be Starting when called.</summary>
    private async Task RunStartupAsync(CancellationToken ct)
    {
        // 1. Resolve ffmpeg
        var binary = FfmpegPathResolver.Resolve(_options.Ffmpeg?.BinaryPath);

        // 2. Write SDP files for RTP inputs
        _sdpDir = CreateSdpDirectory();
        for (var i = 0; i < _options.Sources.Count; i++)
        {
            var src = _options.Sources[i];
            if (src.Protocol == SourceProtocol.RtpH264)
            {
                var sdp = MosaicLiveGenerator.Sources.SdpGenerator.BuildSdp(src, i);
                File.WriteAllText(Path.Combine(_sdpDir, $"src-{i}.sdp"), sdp);
            }
        }

        // 3. Build args
        var (args, _) = MosaicLiveGenerator.Composition.FfmpegCommandBuilder.Build(_options, _sdpDir);

        // 4. Set up parser
        _parser = new StderrParser();
        _runningTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _exitTcs = new TaskCompletionSource<ProcessExitInfo>(TaskCreationOptions.RunContinuationsAsynchronously);

        _parser.Running += (_, _) => _runningTcs?.TrySetResult();
        _parser.StartupError += (_, sig) =>
        {
            _startupError = new MosaicStartupException(sig.Detail)
            {
                Reason = sig.Reason,
                StderrTail = _parser?.GetStderrTail() ?? ""
            };
            _runningTcs?.TrySetException(_startupError);
        };
        _parser.SourceConnectivity += OnSourceConnectivitySignal;
        _parser.OutputSdp += (_, sig) => OutputSdp = sig.Sdp;

        // 5. Spawn process
        _host = _processHostFactory();
        _host.StderrLineReceived += (_, line) =>
        {
            _parser?.Feed(line);
            if (_options.Ffmpeg?.LogStderr ?? true) LogStderr(line);
        };
        _host.Exited += OnProcessExited;

        await _host.StartAsync(binary, args, ct).ConfigureAwait(false);

        // 6. Wait for Running, exit, or timeout
        var timeout = _options.Ffmpeg?.StartupTimeout ?? TimeSpan.Zero;
        if (timeout == TimeSpan.Zero) timeout = TimeSpan.FromSeconds(10);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        var timeoutTask = Task.Delay(Timeout.Infinite, timeoutCts.Token);
        var first = await Task.WhenAny(_runningTcs.Task, _exitTcs.Task, timeoutTask).ConfigureAwait(false);

        if (first == _runningTcs.Task)
        {
            await _runningTcs.Task.ConfigureAwait(false); // throws if startup error already raised
            _state.TryTransition(SessionState.Starting, SessionState.Running);
            return;
        }

        if (first == _exitTcs.Task)
        {
            var info = await _exitTcs.Task.ConfigureAwait(false);
            throw new MosaicStartupException($"ffmpeg exited during startup with code {info.ExitCode}.")
            {
                Reason = MosaicStartupReason.ImmediateExit,
                StderrTail = _parser?.GetStderrTail() ?? ""
            };
        }

        // timeoutTask: either canceled by ct or by the StartupTimeout
        ct.ThrowIfCancellationRequested();
        throw new MosaicStartupException($"ffmpeg did not produce a frame within {timeout}.")
        {
            Reason = MosaicStartupReason.Timeout,
            StderrTail = _parser?.GetStderrTail() ?? ""
        };
    }

    /// <summary>Gracefully stops the ffmpeg process and disposes host resources.</summary>
    private async Task TearDownProcessAsync(CancellationToken ct)
    {
        if (_host is { IsRunning: true })
        {
            try { await _host.SendGracefulQuitAsync(ct).ConfigureAwait(false); }
            catch { /* fall through to kill */ }

            if (_exitTcs is not null)
            {
                var graceMs = ct.IsCancellationRequested
                    ? 0
                    : (int)TimeSpan.FromSeconds(5).TotalMilliseconds;
                if (graceMs > 0)
                {
                    using var graceCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    graceCts.CancelAfter(graceMs);
                    try { await _exitTcs.Task.WaitAsync(graceCts.Token).ConfigureAwait(false); }
                    catch { /* timed out or cancelled – fall through to kill */ }
                }
                if (_host.IsRunning) _host.Kill();
            }
            else
            {
                _host.Kill();
            }
        }

        await SafeTearDownAsync().ConfigureAwait(false);
    }

    /// <summary>Replaces the active options and resets all per-session mutable fields.</summary>
    private void ResetForOptions(MosaicSessionOptions newOptions)
    {
        _options = newOptions;
        _sourceStates = new SourceStateTable(newOptions.Sources);
        OutputSdp = null;
        LastError = null;
        _parser = null;
        _runningTcs = null;
        _exitTcs = null;
        _startupError = null;
    }

    private void OnSourceConnectivitySignal(object? sender, SourceConnectivitySignal sig)
    {
        if (sig.SourceIndex < 0) return;
        if (!_sourceStates.TryUpdate(sig.SourceIndex, sig.NewState, out var before, out var after)) return;
        SourceConnectivityChanged?.Invoke(this, new SourceConnectivityChangedEventArgs
        {
            SourceIndex = after.Index,
            SourceName = after.Name,
            OldConnectivity = before.Connectivity,
            NewConnectivity = after.Connectivity,
        });
    }

    private void OnProcessExited(object? sender, ProcessExitInfo info)
    {
        _exitTcs?.TrySetResult(info);

        // Unexpected exit while Running -> Faulted
        if (_state.Current == SessionState.Running)
        {
            var ex = new MosaicRuntimeException($"ffmpeg exited unexpectedly with code {info.ExitCode}.")
            {
                StderrTail = _parser?.GetStderrTail() ?? ""
            };
            LastError = ex;
            if (_state.TryTransition(SessionState.Running, SessionState.Faulted))
                Faulted?.Invoke(this, new FaultedEventArgs { Error = ex });
        }
    }

    private void LogStderr(string line)
    {
        if (line.Contains("frame=") || line.Contains("speed="))
            _logger.LogInformation("ffmpeg: {Line}", line);
        else
            _logger.LogDebug("ffmpeg: {Line}", line);
    }

    private static string CreateSdpDirectory()
    {
        var d = Path.Combine(Path.GetTempPath(), $"mosaic-{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }

    private async Task SafeTearDownAsync()
    {
        try { if (_host is not null) await _host.DisposeAsync().ConfigureAwait(false); } catch { }
        _host = null;
        try { if (_sdpDir is not null && Directory.Exists(_sdpDir)) Directory.Delete(_sdpDir, true); } catch { }
        _sdpDir = null;
    }

    private static void Validate(MosaicSessionOptions o)
    {
        if (o.Sources is null || o.Sources.Count == 0)
            throw new MosaicConfigurationException("Sources must be non-empty.");

        IReadOnlyList<NormRect> cells;
        try
        {
            cells = o.Layout.ToCells(o.Sources.Count);
        }
        catch (ArgumentException ex)
        {
            throw new MosaicConfigurationException(ex.Message, ex);
        }

        for (var i = 0; i < cells.Count; i++)
            LayoutMath.ValidateRect(cells[i], i);

        // overlap is warning-only at runtime; logged inside StartAsync.
        var overlaps = LayoutMath.FindOverlaps(cells);

        if (!SchemeMatchesProtocol(o.Output))
            throw new MosaicConfigurationException(
                $"Output.Uri.Scheme '{o.Output.Uri.Scheme}' does not match Output.Protocol '{o.Output.Protocol}'.");

        if (o.Output.Width <= 0 || o.Output.Width % 2 != 0)
            throw new MosaicConfigurationException($"Output.Width must be positive and even, got {o.Output.Width}.");
        if (o.Output.Height <= 0 || o.Output.Height % 2 != 0)
            throw new MosaicConfigurationException($"Output.Height must be positive and even, got {o.Output.Height}.");
        if (o.Output.FrameRate is < 1 or > 60)
            throw new MosaicConfigurationException($"Output.FrameRate must be in [1,60], got {o.Output.FrameRate}.");
        if (o.Output.BitrateKbps <= 0)
            throw new MosaicConfigurationException("Output.BitrateKbps must be positive.");
        if (o.Output.GopSeconds < 1)
            throw new MosaicConfigurationException("Output.GopSeconds must be >= 1.");
    }

    private static bool SchemeMatchesProtocol(OutputOptions o) => (o.Protocol, o.Uri.Scheme) switch
    {
        (OutputProtocol.UdpMpegTs, "udp") => true,
        (OutputProtocol.RtpH264,   "rtp") => true,
        _ => false,
    };
}
