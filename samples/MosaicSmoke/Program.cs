using System.Text.Json;
using Microsoft.Extensions.Logging;
using MosaicLiveGenerator;

string? configPath = null;
string? ffmpegPath = null;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--config" when i + 1 < args.Length:
            configPath = args[++i];
            break;
        case "--ffmpeg" when i + 1 < args.Length:
            ffmpegPath = args[++i];
            break;
        case "--help" or "-h":
            PrintUsage();
            return 0;
    }
}

if (configPath is null)
{
    PrintUsage();
    return 2;
}

var json = await File.ReadAllTextAsync(configPath);
var config = JsonSerializer.Deserialize<SmokeConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
             ?? throw new InvalidOperationException("Could not parse config.");

using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
}).SetMinimumLevel(LogLevel.Debug));

var logger = loggerFactory.CreateLogger<MosaicSession>();

var sources = config.Sources.Select(s => new VideoSource(
    Name: s.Name,
    Uri: new Uri(s.Uri),
    Protocol: Enum.Parse<SourceProtocol>(s.Protocol, ignoreCase: true),
    Fit: Enum.Parse<TileFit>(s.Fit ?? "Letterbox", ignoreCase: true))).ToArray();

Layout layout = config.Grid is { Rows: > 0, Cols: > 0 }
    ? Layout.Grid(config.Grid.Rows, config.Grid.Cols)
    : Layout.Custom(config.Cells!.Select(c => new NormRect(c.X, c.Y, c.Width, c.Height)).ToArray());

// --ffmpeg CLI overrides config.ffmpeg.binaryPath, which overrides PATH lookup.
var resolvedFfmpegPath = ffmpegPath ?? config.Ffmpeg?.BinaryPath;

var hwAccel = config.Output.HwAccel is null
    ? HwAccel.None
    : Enum.Parse<HwAccel>(config.Output.HwAccel, ignoreCase: true);

var options = new MosaicSessionOptions(
    Sources: sources,
    Layout: layout,
    Output: new OutputOptions(
        Uri: new Uri(config.Output.Uri),
        Protocol: Enum.Parse<OutputProtocol>(config.Output.Protocol, ignoreCase: true),
        Width: config.Output.Width ?? 1920,
        Height: config.Output.Height ?? 1080,
        FrameRate: config.Output.FrameRate ?? 25,
        BitrateKbps: config.Output.BitrateKbps ?? 6000,
        GopSeconds: config.Output.GopSeconds ?? 1,
        LowLatency: config.Output.LowLatency ?? true,
        HwAccel: hwAccel),
    LayoutChrome: new LayoutOptions(
        BackgroundColor: config.Chrome?.BackgroundColor ?? "black",
        BorderPx: config.Chrome?.BorderPx ?? 0,
        ShowLabels: config.Chrome?.ShowLabels ?? false,
        LabelFontFile: config.Chrome?.LabelFontFile),
    Ffmpeg: resolvedFfmpegPath is not null
        ? new FfmpegOptions(BinaryPath: resolvedFfmpegPath)
        : null);

await using var session = new MosaicSession(options, logger);
session.StateChanged += (_, e) => Console.WriteLine($"[state] {e.OldState} -> {e.NewState}");
session.SourceConnectivityChanged += (_, e) =>
    Console.WriteLine($"[src {e.SourceIndex} '{e.SourceName}'] {e.OldConnectivity} -> {e.NewConnectivity}");
session.Faulted += (_, e) => Console.WriteLine($"[fault] {e.Error.Message}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    await session.StartAsync(cts.Token);
    if (session.OutputSdp is not null)
    {
        Console.WriteLine("---- output SDP ----");
        Console.WriteLine(session.OutputSdp);
        Console.WriteLine("--------------------");
    }
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException) { }
catch (MosaicStartupException ex)
{
    Console.Error.WriteLine($"startup failed: {ex.Reason}: {ex.Message}");
    Console.Error.WriteLine(ex.StderrTail);
    return 3;
}
finally
{
    await session.StopAsync();
}
return 0;

static void PrintUsage()
{
    Console.WriteLine("MosaicSmoke — runs MosaicLiveGenerator from a JSON config against external streams.");
    Console.WriteLine();
    Console.WriteLine("Usage: MosaicSmoke --config <path-to-json> [--ffmpeg <path>]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --config <path>     Required. JSON file describing sources, layout, output.");
    Console.WriteLine("  --ffmpeg <path>     Optional. Path to ffmpeg binary.");
    Console.WriteLine("                      Precedence: --ffmpeg > config.ffmpeg.binaryPath > PATH lookup.");
    Console.WriteLine("  --help, -h          Show this help.");
}

internal record SmokeConfig(
    SmokeSource[] Sources,
    SmokeGrid? Grid,
    SmokeCell[]? Cells,
    SmokeOutput Output,
    SmokeChrome? Chrome,
    SmokeFfmpeg? Ffmpeg);

internal record SmokeSource(string Name, string Uri, string Protocol, string? Fit);
internal record SmokeGrid(int Rows, int Cols);
internal record SmokeCell(double X, double Y, double Width, double Height);
internal record SmokeOutput(string Uri, string Protocol, int? Width, int? Height, int? FrameRate, int? BitrateKbps, int? GopSeconds, bool? LowLatency, string? HwAccel = null);
internal record SmokeChrome(string? BackgroundColor, int? BorderPx, bool? ShowLabels, string? LabelFontFile = null);
internal record SmokeFfmpeg(string? BinaryPath);
