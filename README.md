# MosaicLiveGenerator

.NET 8 library that composes N live UDP/MPEG-TS or RTP/H.264 video sources into one mosaic stream via an `ffmpeg` child process. MIT-licensed.

## Features

- Inputs: UDP/MPEG-TS (H.264) and RTP/H.264 (no SDP required from caller — library generates internally).
- Outputs: UDP/MPEG-TS and RTP/H.264.
- Layouts: `Grid(rows, cols)` and `Custom(rects)` with normalized `[0,1]` coordinates.
- Latency-first defaults: ultrafast x264, intra-refresh, low-delay flags, tight VBV. ~150-250 ms typical end-to-end.
- Per-source connectivity tracking via stderr parsing.
- `ILogger` integration + events for state transitions.

## Requirements

- .NET 8 SDK
- `ffmpeg` binary on PATH (or pass `FfmpegOptions.BinaryPath`). Recommended build: ffmpeg 5.x or 6.x with libx264.

## Quick start

```csharp
using MosaicLiveGenerator;

var session = new MosaicSession(new MosaicSessionOptions(
    Sources: new[] {
        new VideoSource("cam1", new Uri("udp://239.0.0.1:5001"), SourceProtocol.MpegTsUdp),
        new VideoSource("cam2", new Uri("udp://239.0.0.1:5002"), SourceProtocol.MpegTsUdp),
        new VideoSource("cam3", new Uri("udp://239.0.0.1:5003"), SourceProtocol.MpegTsUdp),
        new VideoSource("cam4", new Uri("udp://239.0.0.1:5004"), SourceProtocol.MpegTsUdp),
    },
    Layout: Layout.Grid(2, 2),
    Output: new OutputOptions(new Uri("udp://239.0.0.2:6000"))));

await session.StartAsync();
// ...
await session.StopAsync();
```

## Layout examples

```csharp
// 1-big-top-left, 2 underneath, 3 down the right column
var layout = Layout.Custom(new[] {
    new NormRect(0,      0,       0.5,  0.5),     // big top-left
    new NormRect(0,      0.5,     0.25, 0.5),     // bottom-left a
    new NormRect(0.25,   0.5,     0.25, 0.5),     // bottom-left b
    new NormRect(0.5,    0,       0.5,  1.0/3),   // right column 1
    new NormRect(0.5,    1.0/3,   0.5,  1.0/3),   // right column 2
    new NormRect(0.5,    2.0/3,   0.5,  1.0/3),   // right column 3
});
```

## Building & testing

```bash
dotnet build
dotnet test                                 # unit tests only
dotnet test --filter Category=Integration   # integration tests (requires ffmpeg on PATH)
```

## Smoke test

```bash
dotnet run --project samples/MosaicSmoke -- --config samples/MosaicSmoke/example-config.json
```

## Out of scope (v1)

Audio, source hot-swap, codecs other than H.264, SRT/RTMP/NDI, hardware encoders, recording to disk, on-screen broadcast graphics.

## License

MIT — see `LICENSE`.
