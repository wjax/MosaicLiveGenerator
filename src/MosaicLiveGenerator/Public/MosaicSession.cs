using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MosaicLiveGenerator.Composition;
using MosaicLiveGenerator.Diagnostics;
using MosaicLiveGenerator.Process;

namespace MosaicLiveGenerator;

public sealed class MosaicSession : IAsyncDisposable
{
    private readonly MosaicSessionOptions _options;
    private readonly ILogger<MosaicSession> _logger;
    private readonly Func<IProcessHost> _processHostFactory;
    private readonly SessionStateMachine _state = new();
    private readonly SourceStateTable _sourceStates;

    private IProcessHost? _host;
    private StderrParser? _parser;
    private TaskCompletionSource? _runningTcs;
    private TaskCompletionSource<ProcessExitInfo>? _exitTcs;
    private string? _sdpDir;
    private Exception? _startupError;

    public MosaicSession(
        MosaicSessionOptions options,
        ILogger<MosaicSession>? logger = null)
        : this(options, logger, () => throw new NotImplementedException("Real FfmpegProcessHost arrives in Task 26"))
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
#pragma warning disable CS0067 // Raised in Task 23
    public event EventHandler<FaultedEventArgs>? Faulted;
#pragma warning restore CS0067

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (!_state.TryTransition(SessionState.Stopped, SessionState.Starting))
            throw new InvalidOperationException($"Cannot start: state={_state.Current}.");

        try
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
        catch (Exception ex)
        {
            _state.TryTransition(SessionState.Starting, SessionState.Faulted);
            LastError = ex;
            await SafeTearDownAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task StopAsync(CancellationToken ct = default)
        => throw new NotImplementedException("Task 23");

    public ValueTask DisposeAsync()
        => throw new NotImplementedException("Task 23");

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

    private void OnProcessExited(object? sender, ProcessExitInfo info) => _exitTcs?.TrySetResult(info);

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
