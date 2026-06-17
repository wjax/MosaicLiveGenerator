using System.Diagnostics;
using MosaicLiveGenerator.Process;

namespace MosaicLiveGenerator.Tests.Integration;

internal sealed class SyntheticSource : IAsyncDisposable
{
    private readonly System.Diagnostics.Process _proc;

    public int Port { get; }

    public SyntheticSource(int port, int width = 640, int height = 480, int rate = 25)
    {
        Port = port;
        var ffmpeg = FfmpegPathResolver.TryFindOnPath()
            ?? throw new InvalidOperationException("Integration tests require ffmpeg on PATH.");

        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Arguments = ProcessArguments.ToCommandLine(new[]
        {
            "-hide_banner", "-loglevel", "warning",
            "-re",
            "-f", "lavfi",
            "-i", $"testsrc=size={width}x{height}:rate={rate}",
            "-c:v", "libx264", "-preset", "ultrafast", "-tune", "zerolatency",
            "-g", rate.ToString(),
            "-f", "mpegts",
            $"udp://127.0.0.1:{port}?pkt_size=1316"
        });

        _proc = new System.Diagnostics.Process { StartInfo = psi };
        if (!_proc.Start()) throw new InvalidOperationException("failed to start synthetic source");
    }

    public ValueTask DisposeAsync()
    {
        try { _proc.Kill(); } catch { }
        try { _proc.WaitForExit(); } catch { }
        _proc.Dispose();
        return default;
    }
}
