using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MosaicLiveGenerator;

// Self-contained quick test: spawns 4 synthetic ffmpeg sources, composes them with
// MosaicLiveGenerator, and pipes the mosaic out on udp://127.0.0.1:6000.
// View with:  ffplay -fflags nobuffer -i udp://127.0.0.1:6000
// Or VLC:     vlc udp://@127.0.0.1:6000

string? ffmpegPath = null;
var duration = TimeSpan.Zero; // 0 = run until Ctrl-C
var outputUri = new Uri("udp://127.0.0.1:6000");

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--ffmpeg" when i + 1 < args.Length:
            ffmpegPath = args[++i];
            break;
        case "--duration" when i + 1 < args.Length:
            duration = TimeSpan.FromSeconds(int.Parse(args[++i]));
            break;
        case "--output" when i + 1 < args.Length:
            outputUri = new Uri(args[++i]);
            break;
        case "--help" or "-h":
            PrintUsage();
            return 0;
    }
}

// Resolve ffmpeg path: explicit > PATH lookup
var resolvedFfmpeg = ffmpegPath ?? TryFindFfmpegOnPath();
if (resolvedFfmpeg is null || !File.Exists(resolvedFfmpeg))
{
    Console.Error.WriteLine("ERROR: ffmpeg not found.");
    Console.Error.WriteLine("  - Put ffmpeg on PATH, OR");
    Console.Error.WriteLine("  - Pass --ffmpeg <path-to-ffmpeg.exe>");
    Console.Error.WriteLine();
    PrintUsage();
    return 2;
}

Console.WriteLine($"Using ffmpeg: {resolvedFfmpeg}");

using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
}).SetMinimumLevel(LogLevel.Information));

var logger = loggerFactory.CreateLogger<MosaicSession>();

// 1. Spawn 4 synthetic ffmpeg sources, one per tile.
//    Each produces a distinct lavfi pattern over UDP/MPEG-TS so the mosaic is visually distinguishable.
var sourcePorts = new[] { 17001, 17002, 17003, 17004 };
var sourcePatterns = new[]
{
    "testsrc=size=640x480:rate=25",                   // color bars + sweep
    "testsrc2=size=640x480:rate=25",                  // newer test pattern
    "smptebars=size=640x480:rate=25",                 // SMPTE bars
    "mandelbrot=size=640x480:rate=25:maxiter=100",    // fractal animation
};
var synthetic = new List<Process>();
for (var i = 0; i < 4; i++)
{
    var p = StartSyntheticSource(resolvedFfmpeg, sourcePorts[i], sourcePatterns[i], $"CAM {i + 1}");
    synthetic.Add(p);
}

Console.WriteLine($"Spawned {synthetic.Count} synthetic sources on ports {string.Join(",", sourcePorts)}.");
Console.WriteLine("Waiting 1.5s for sources to warm up...");
await Task.Delay(1500);

// 2. Build the MosaicSession.
var sources = sourcePorts.Select((port, idx) =>
    new VideoSource(
        Name: $"CAM {idx + 1}",
        Uri: new Uri($"udp://127.0.0.1:{port}"),
        Protocol: SourceProtocol.MpegTsUdp)).ToArray();

var options = new MosaicSessionOptions(
    Sources: sources,
    Layout: Layout.Grid(2, 2),
    Output: new OutputOptions(
        Uri: outputUri,
        Width: 1280, Height: 720,
        FrameRate: 25,
        BitrateKbps: 3000),
    LayoutChrome: new LayoutOptions(
        BackgroundColor: "black",
        BorderPx: 2,
        BorderColor: "white",
        ShowLabels: true,
        LabelFontSize: 24),
    Ffmpeg: new FfmpegOptions(
        BinaryPath: resolvedFfmpeg,
        StartupTimeout: TimeSpan.FromSeconds(15)));

await using var session = new MosaicSession(options, logger);

session.StateChanged += (_, e) =>
    Console.WriteLine($"[state] {e.OldState} -> {e.NewState}");
session.SourceConnectivityChanged += (_, e) =>
    Console.WriteLine($"[src {e.SourceIndex} '{e.SourceName}'] {e.OldConnectivity} -> {e.NewConnectivity}");
session.Faulted += (_, e) =>
    Console.WriteLine($"[fault] {e.Error.Message}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
if (duration > TimeSpan.Zero) cts.CancelAfter(duration);

var exitCode = 0;
try
{
    Console.WriteLine();
    Console.WriteLine("Starting mosaic session...");
    await session.StartAsync(cts.Token);

    Console.WriteLine();
    Console.WriteLine("====================================================================");
    Console.WriteLine($"  Mosaic running on {outputUri}");
    Console.WriteLine("  View with:");
    Console.WriteLine($"    ffplay -fflags nobuffer -i {outputUri}");
    Console.WriteLine($"    vlc {outputUri.ToString().Replace("udp://", "udp://@")}");
    Console.WriteLine();
    Console.WriteLine("  Press Ctrl-C to stop.");
    Console.WriteLine("====================================================================");
    Console.WriteLine();

    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    // expected on Ctrl-C or duration timeout
}
catch (MosaicStartupException ex)
{
    Console.Error.WriteLine($"startup failed: {ex.Reason}: {ex.Message}");
    Console.Error.WriteLine(ex.StderrTail);
    exitCode = 3;
}
finally
{
    Console.WriteLine();
    Console.WriteLine("Stopping mosaic session...");
    try { await session.StopAsync(); } catch { }

    Console.WriteLine("Tearing down synthetic sources...");
    foreach (var p in synthetic)
    {
        try { p.Kill(entireProcessTree: true); } catch { }
        try { await p.WaitForExitAsync(); } catch { }
        p.Dispose();
    }
}

Console.WriteLine("Done.");
return exitCode;

static Process StartSyntheticSource(string ffmpeg, int port, string lavfiPattern, string label)
{
    // testsrc/smptebars/etc + drawtext overlay so each tile is visually identifiable,
    // re-encoded with low-latency H.264 and shipped over UDP/MPEG-TS.
    var psi = new ProcessStartInfo
    {
        FileName = ffmpeg,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardError = true,
    };
    foreach (var a in new[]
    {
        "-hide_banner", "-loglevel", "warning",
        "-re",
        "-f", "lavfi",
        "-i", lavfiPattern,
        "-vf", $"drawtext=text='{label}':x=20:y=20:fontsize=36:fontcolor=white:box=1:boxcolor=black@0.6",
        "-c:v", "libx264",
        "-preset", "ultrafast",
        "-tune", "zerolatency",
        "-pix_fmt", "yuv420p",
        "-g", "25",
        "-f", "mpegts",
        $"udp://127.0.0.1:{port}?pkt_size=1316",
    })
    {
        psi.ArgumentList.Add(a);
    }

    var p = new Process { StartInfo = psi };
    if (!p.Start())
        throw new InvalidOperationException($"failed to start synthetic source on port {port}");

    // Drain stderr so the OS pipe buffer doesn't fill and block the child.
    _ = Task.Run(async () =>
    {
        try
        {
            string? line;
            while ((line = await p.StandardError.ReadLineAsync()) is not null) { /* swallow */ }
        }
        catch { /* process exiting */ }
    });

    return p;
}

static string? TryFindFfmpegOnPath()
{
    var pathEnv = Environment.GetEnvironmentVariable("PATH");
    if (string.IsNullOrEmpty(pathEnv)) return null;

    var exeName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
    var separator = OperatingSystem.IsWindows() ? ';' : ':';

    foreach (var dir in pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries))
    {
        string candidate;
        try { candidate = Path.Combine(dir.Trim(), exeName); }
        catch { continue; }
        if (File.Exists(candidate)) return candidate;
    }
    return null;
}

static void PrintUsage()
{
    Console.WriteLine("MosaicQuickTest — runs MosaicLiveGenerator against 4 synthetic sources.");
    Console.WriteLine();
    Console.WriteLine("Usage: MosaicQuickTest [--ffmpeg <path>] [--output <uri>] [--duration <secs>]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --ffmpeg <path>     Explicit path to ffmpeg binary.");
    Console.WriteLine("                      Default: looks on PATH.");
    Console.WriteLine("  --output <uri>      Output URI (udp:// or rtp://).");
    Console.WriteLine("                      Default: udp://127.0.0.1:6000");
    Console.WriteLine("  --duration <secs>   Auto-stop after N seconds.");
    Console.WriteLine("                      Default: run until Ctrl-C.");
    Console.WriteLine("  --help, -h          Show this help.");
    Console.WriteLine();
    Console.WriteLine("View the mosaic with:  ffplay -fflags nobuffer -i udp://127.0.0.1:6000");
}
