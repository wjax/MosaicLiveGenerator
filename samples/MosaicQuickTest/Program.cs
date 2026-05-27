using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MosaicLiveGenerator;

// Self-contained quick test: spawns N synthetic ffmpeg sources, composes them with
// MosaicLiveGenerator according to a configurable layout, and pipes the mosaic out
// on udp://127.0.0.1:6000.
//
// Supported layouts: 2x2 (4 sources), 3x3 (9 sources), 1x2x3 (6 sources — one big
// top-left, two underneath, three down the right column).
//
// View with:  ffplay -fflags nobuffer -i udp://127.0.0.1:6000
// Or VLC:     vlc udp://@127.0.0.1:6000

string? ffmpegPath = null;
var duration = TimeSpan.Zero; // 0 = run until Ctrl-C
var outputUri = new Uri("udp://127.0.0.1:6000");
var layoutMode = LayoutMode.Grid2x2;
var hwAccel = HwAccel.None;

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
        case "--layout" when i + 1 < args.Length:
            layoutMode = ParseLayout(args[++i]);
            break;
        case "--hwaccel" when i + 1 < args.Length:
            hwAccel = ParseHwAccel(args[++i]);
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
Console.WriteLine($"Layout: {LayoutName(layoutMode)}");
Console.WriteLine($"Encoder: {EncoderName(hwAccel)}");

using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
}).SetMinimumLevel(LogLevel.Information));

var logger = loggerFactory.CreateLogger<MosaicSession>();

// 1. Build the layout, pick the matching synthetic-source patterns.
var (layout, lavfiPatterns) = BuildLayout(layoutMode);
var sourceCount = lavfiPatterns.Length;
var sourcePorts = Enumerable.Range(0, sourceCount).Select(i => 17001 + i).ToArray();

// 2. Spawn the synthetic ffmpeg sources, one per tile.
var synthetic = new List<Process>();
for (var i = 0; i < sourceCount; i++)
{
    var p = StartSyntheticSource(resolvedFfmpeg, sourcePorts[i], lavfiPatterns[i], $"CAM {i + 1}");
    synthetic.Add(p);
}

Console.WriteLine($"Spawned {synthetic.Count} synthetic sources on ports {string.Join(",", sourcePorts)}.");
Console.WriteLine("Waiting 1.5s for sources to warm up...");
await Task.Delay(1500);

// 3. Build the MosaicSession.
var sources = sourcePorts.Select((port, idx) =>
    new VideoSource(
        Name: $"CAM {idx + 1}",
        Uri: new Uri($"udp://127.0.0.1:{port}"),
        Protocol: SourceProtocol.MpegTsUdp)).ToArray();

var options = new MosaicSessionOptions(
    Sources: sources,
    Layout: layout,
    Output: new OutputOptions(
        Uri: outputUri,
        Width: 1280, Height: 720,
        FrameRate: 25,
        BitrateKbps: 3000,
        HwAccel: hwAccel),
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

// ---- layout selection ---------------------------------------------------

static LayoutMode ParseLayout(string s) => s.ToLowerInvariant() switch
{
    "2x2" => LayoutMode.Grid2x2,
    "3x3" => LayoutMode.Grid3x3,
    "1x2x3" or "1+2+3" => LayoutMode.OneBigTwoBottomThreeRight,
    _ => throw new ArgumentException($"unknown layout '{s}' (expected: 2x2 | 3x3 | 1x2x3)"),
};

static HwAccel ParseHwAccel(string s) => s.ToLowerInvariant() switch
{
    "none" => HwAccel.None,
    "nvidia" or "nvenc" => HwAccel.Nvidia,
    "intel" or "qsv" => HwAccel.Intel,
    _ => throw new ArgumentException($"unknown hwaccel '{s}' (expected: none | nvidia | intel)"),
};

static string EncoderName(HwAccel a) => a switch
{
    HwAccel.None => "libx264 (software)",
    HwAccel.Nvidia => "h264_nvenc (NVIDIA)",
    HwAccel.Intel => "h264_qsv (Intel Quick Sync)",
    _ => a.ToString(),
};

static string LayoutName(LayoutMode m) => m switch
{
    LayoutMode.Grid2x2 => "2x2 grid (4 tiles)",
    LayoutMode.Grid3x3 => "3x3 grid (9 tiles)",
    LayoutMode.OneBigTwoBottomThreeRight => "1x2x3 (1 big top-left, 2 underneath, 3 down the right column)",
    _ => "unknown",
};

static (Layout layout, string[] patterns) BuildLayout(LayoutMode mode)
{
    // 9 distinct lavfi sources to pick from. All are animated/varied so each tile is
    // visually identifiable on the mosaic.
    var all = new[]
    {
        "testsrc=size=640x480:rate=25",
        "testsrc2=size=640x480:rate=25",
        "smptebars=size=640x480:rate=25",
        "smptehdbars=size=640x480:rate=25",
        "mandelbrot=size=640x480:rate=25:maxiter=100",
        "life=size=640x480:rate=25:mold=10",
        "cellauto=size=640x480:rate=25:rule=110",
        "yuvtestsrc=size=640x480:rate=25",
        "rgbtestsrc=size=640x480:rate=25",
    };

    switch (mode)
    {
        case LayoutMode.Grid2x2:
            return (Layout.Grid(2, 2), all.Take(4).ToArray());

        case LayoutMode.Grid3x3:
            return (Layout.Grid(3, 3), all.Take(9).ToArray());

        case LayoutMode.OneBigTwoBottomThreeRight:
            // canvas split:
            //   +------------+-------+
            //   |            |   4   |
            //   |     1      +-------+
            //   |            |   5   |
            //   +------+-----+-------+
            //   |  2   |  3  |   6   |
            //   +------+-----+-------+
            var third = 1.0 / 3.0;
            var layout = Layout.Custom(new[]
            {
                new NormRect(0,    0,     0.5,  0.5),    // 1: big top-left
                new NormRect(0,    0.5,   0.25, 0.5),    // 2: bottom-left a
                new NormRect(0.25, 0.5,   0.25, 0.5),    // 3: bottom-left b
                new NormRect(0.5,  0,     0.5,  third),  // 4: right column top
                new NormRect(0.5,  third, 0.5,  third),  // 5: right column middle
                new NormRect(0.5,  2*third, 0.5, third), // 6: right column bottom
            });
            return (layout, all.Take(6).ToArray());

        default:
            throw new ArgumentOutOfRangeException(nameof(mode));
    }
}

// ---- helpers ------------------------------------------------------------

static Process StartSyntheticSource(string ffmpeg, int port, string lavfiPattern, string label)
{
    // lavfi pattern + drawtext overlay so each tile is visually identifiable,
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
    Console.WriteLine("MosaicQuickTest — runs MosaicLiveGenerator against synthetic sources.");
    Console.WriteLine();
    Console.WriteLine("Usage: MosaicQuickTest [--layout <name>] [--hwaccel <vendor>]");
    Console.WriteLine("                       [--ffmpeg <path>] [--output <uri>] [--duration <secs>]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --layout <name>     Mosaic layout. One of:");
    Console.WriteLine("                        2x2     — 4 tiles, 2 rows × 2 cols (default)");
    Console.WriteLine("                        3x3     — 9 tiles, 3 rows × 3 cols");
    Console.WriteLine("                        1x2x3   — 6 tiles: 1 big top-left,");
    Console.WriteLine("                                  2 underneath, 3 down the right column");
    Console.WriteLine("  --hwaccel <vendor>  Output H.264 encoder. One of:");
    Console.WriteLine("                        none    — libx264 (software, default)");
    Console.WriteLine("                        nvidia  — h264_nvenc (NVIDIA GPU)");
    Console.WriteLine("                        intel   — h264_qsv (Intel iGPU / Arc)");
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

internal enum LayoutMode
{
    Grid2x2,
    Grid3x3,
    OneBigTwoBottomThreeRight,
}
