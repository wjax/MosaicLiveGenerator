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

    public event EventHandler? Running;
    public event EventHandler<StartupErrorSignal>? StartupError;
    public event EventHandler<SourceConnectivitySignal>? SourceConnectivity;

    public void Feed(string line)
    {
        // Maintain rolling tail (last 32 non-empty lines)
        if (!string.IsNullOrEmpty(line))
        {
            _tail.Enqueue(line);
            while (_tail.Count > 32) _tail.Dequeue();
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

        if (!_runningEmitted && FrameRegex.IsMatch(line))
        {
            _runningEmitted = true;
            Running?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (line.Contains("Error opening input", StringComparison.OrdinalIgnoreCase))
        {
            StartupError?.Invoke(this, new StartupErrorSignal(MosaicStartupReason.BadInputSource, line));
            return;
        }

        if (line.Contains("No such file", StringComparison.OrdinalIgnoreCase))
        {
            StartupError?.Invoke(this, new StartupErrorSignal(MosaicStartupReason.BadInputSource, line));
            return;
        }

        // Connection errors (Task 17 expands this; the hook is here)
        if (line.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Connection timed out", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Input/output error", StringComparison.OrdinalIgnoreCase))
        {
            var idx = _linesSinceInputContext <= 5 ? _lastInputContext : -1;
            SourceConnectivity?.Invoke(this,
                new SourceConnectivitySignal(idx, global::MosaicLiveGenerator.SourceConnectivity.Disconnected, line));
        }
    }

    public string GetStderrTail() => string.Join('\n', _tail);
}
