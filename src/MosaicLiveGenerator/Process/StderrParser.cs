using System.Text.RegularExpressions;

namespace MosaicLiveGenerator.Process;

internal sealed class StderrParser
{
    private static readonly Regex FrameRegex = new(@"^frame=\s*\d+", RegexOptions.Compiled);
    private static readonly Regex InputContextRegex = new(@"Input #(\d+)", RegexOptions.Compiled);

    private bool _runningEmitted;
    private int _lastInputContext = -1;
    private int _linesSinceInputContext = int.MaxValue;
    private readonly Queue<string> _tail = new();
    private readonly Dictionary<int, SourceConnectivity> _sourceStates = new();
    private readonly HashSet<int> _sourcesInReconnect = new();
    private bool _captureSdp;
    private readonly System.Text.StringBuilder _sdpBuf = new();

    public event EventHandler? Running;
    public event EventHandler<StartupErrorSignal>? StartupError;
    public event EventHandler<SourceConnectivitySignal>? SourceConnectivity;
    public event EventHandler<OutputSdpSignal>? OutputSdp;

    public void Feed(string line)
    {
        // Maintain rolling tail (last 32 non-empty lines)
        if (!string.IsNullOrEmpty(line))
        {
            _tail.Enqueue(line);
            while (_tail.Count > 32) _tail.Dequeue();
        }

        if (_captureSdp)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                var sdp = _sdpBuf.ToString();
                _sdpBuf.Clear();
                _captureSdp = false;
                if (sdp.Length > 0)
                    OutputSdp?.Invoke(this, new OutputSdpSignal(sdp));
            }
            else
            {
                _sdpBuf.Append(line).Append('\n');
            }
            return;
        }

        if (line.Trim().Equals("SDP:", StringComparison.Ordinal))
        {
            _captureSdp = true;
            return;
        }

        var inputMatch = InputContextRegex.Match(line);
        if (inputMatch.Success)
        {
            _lastInputContext = int.Parse(inputMatch.Groups[1].Value);
            _linesSinceInputContext = 0;
        }
        else
        {
            _linesSinceInputContext++;
        }

        if (FrameRegex.IsMatch(line))
        {
            // First-ever frame line: transition to Running.
            if (!_runningEmitted)
            {
                _runningEmitted = true;
                Running?.Invoke(this, EventArgs.Empty);
            }

            // Any frame line after a reconnect was logged: restore connectivity.
            if (_sourcesInReconnect.Count > 0)
            {
                foreach (var idx in _sourcesInReconnect.ToArray())
                {
                    if (_sourceStates.TryGetValue(idx, out var cur) && cur != MosaicLiveGenerator.SourceConnectivity.Connected)
                    {
                        _sourceStates[idx] = MosaicLiveGenerator.SourceConnectivity.Connected;
                        SourceConnectivity?.Invoke(this,
                            new SourceConnectivitySignal(idx, MosaicLiveGenerator.SourceConnectivity.Connected, line));
                    }
                }
                _sourcesInReconnect.Clear();
            }
            return;
        }

        if (line.Contains("Error opening input", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("No such file", StringComparison.OrdinalIgnoreCase))
        {
            StartupError?.Invoke(this, new StartupErrorSignal(MosaicStartupReason.BadInputSource, line));
            return;
        }

        // Reconnect notification ⇒ remember which source so the next frame= flips it back.
        if (line.Contains("Will reconnect at", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Reconnecting", StringComparison.OrdinalIgnoreCase))
        {
            if (_lastInputContext >= 0 && _linesSinceInputContext <= 5)
                _sourcesInReconnect.Add(_lastInputContext);
            return;
        }

        if (line.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Connection timed out", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Input/output error", StringComparison.OrdinalIgnoreCase))
        {
            var idx = _linesSinceInputContext <= 5 ? _lastInputContext : -1;
            if (idx >= 0)
                _sourceStates[idx] = MosaicLiveGenerator.SourceConnectivity.Disconnected;
            SourceConnectivity?.Invoke(this,
                new SourceConnectivitySignal(idx, MosaicLiveGenerator.SourceConnectivity.Disconnected, line));
        }
    }

    public string GetStderrTail() => string.Join('\n', _tail);
}
