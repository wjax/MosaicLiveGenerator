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
#pragma warning disable CS0067 // Events raised in future tasks (StartAsync, StopAsync)
    public event EventHandler<SourceConnectivityChangedEventArgs>? SourceConnectivityChanged;
    public event EventHandler<FaultedEventArgs>? Faulted;
#pragma warning restore CS0067

    public Task StartAsync(CancellationToken ct = default)
        => throw new NotImplementedException("Task 21");

    public Task StopAsync(CancellationToken ct = default)
        => throw new NotImplementedException("Task 23");

    public ValueTask DisposeAsync()
        => throw new NotImplementedException("Task 23");

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
