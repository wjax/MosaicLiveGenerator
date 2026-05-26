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
        foreach (var a in new[]
        {
            "-hide_banner", "-loglevel", "warning",
            "-re",
            "-f", "lavfi",
            "-i", $"testsrc=size={width}x{height}:rate={rate}",
            "-c:v", "libx264", "-preset", "ultrafast", "-tune", "zerolatency",
            "-g", rate.ToString(),
            "-f", "mpegts",
            $"udp://127.0.0.1:{port}?pkt_size=1316"
        }) psi.ArgumentList.Add(a);

        _proc = new System.Diagnostics.Process { StartInfo = psi };
        if (!_proc.Start()) throw new InvalidOperationException("failed to start synthetic source");
    }

    public async ValueTask DisposeAsync()
    {
        try { _proc.Kill(entireProcessTree: true); } catch { }
        try { await _proc.WaitForExitAsync(); } catch { }
        _proc.Dispose();
    }
}
