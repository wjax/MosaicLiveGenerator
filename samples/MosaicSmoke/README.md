# MosaicSmoke

Runs `MosaicLiveGenerator` from a JSON config against **external UDP streams** — anything that publishes MPEG-TS over UDP (VLC, OBS, ffmpeg, a real camera, etc).

`example-config.json` defines:

- 4 sources on multicast `udp://239.0.0.1:5001` – `5004`
- A 2×2 grid layout
- Output to `udp://239.0.0.2:6000` at 1920×1080, 25 fps, 6 Mbps, low-latency on

**Why multicast?** Multicast lets multiple consumers (the mosaic, an `ffplay` you use to debug the source, a recorder, etc.) all receive the same stream simultaneously. With unicast loopback (`127.0.0.1`) only one process can bind a given port at a time, and any sharing fight produces corrupted output. Use `239.0.0.0/8` for administratively-scoped traffic that stays on the local network.

## Run it

From the repo root:

```pwsh
dotnet run --project samples/MosaicSmoke -- `
    --config samples/MosaicSmoke/example-config.json `
    --ffmpeg ./lib/ffmpeg.exe
```

Or just hit ▶ on the **MosaicSmoke** profile in Rider — it's already wired up to pass these.

The session will start, log `[state] Stopped -> Starting`, then sit on `Starting` until at least one source starts feeding packets. The moment your first VLC stream comes up, ffmpeg sees a frame, the session flips to `Running`, and you can view the mosaic.

## Stream 4 sources from VLC

You need **four** VLC instances, one per source group/port. Easiest setup: open VLC four times, each playing a different file or input, with the appropriate `--sout` for each.

### Looping a video file

```pwsh
# Source 1 → 239.0.0.1:5001
vlc.exe my-video.mp4 --loop `
    --sout="#transcode{vcodec=h264,vb=2000,fps=25,acodec=none}:standard{access=udp,mux=ts,dst=239.0.0.1:5001}"

# Source 2 → 239.0.0.1:5002
vlc.exe another-video.mp4 --loop `
    --sout="#transcode{vcodec=h264,vb=2000,fps=25,acodec=none}:standard{access=udp,mux=ts,dst=239.0.0.1:5002}"
```

…and so on for `:5003` / `:5004`.

### Using the bundled `stream-mp4.ps1`

Easier than crafting VLC `sout` strings — `scripts/stream-mp4.ps1` uses the bundled ffmpeg:

```pwsh
.\scripts\stream-mp4.ps1 C:\videos\sample.mp4 -Destination 239.0.0.1 -Port 5001
```

### Webcam (Windows DirectShow)

```pwsh
vlc.exe dshow:// :dshow-vdev="<your-camera-name>" `
    --sout="#transcode{vcodec=h264,vb=2000,fps=25,acodec=none}:standard{access=udp,mux=ts,dst=239.0.0.1:5001}"
```

List your DirectShow devices with `vlc.exe --list-dshow-devices` if you don't know the name.

### Screen capture

```pwsh
vlc.exe screen:// :screen-fps=25 `
    --sout="#transcode{vcodec=h264,vb=2000,fps=25,acodec=none}:standard{access=udp,mux=ts,dst=239.0.0.1:5001}"
```

### Useful VLC flags

- `--loop` — loop the input forever (so the mosaic doesn't go black after a single playback).
- `--no-video` / `--no-audio` — only matters for VLC's own playback window; doesn't affect the stream.
- `--qt-start-minimized` — hide the VLC window once streaming starts.
- `vlc.exe --intf dummy …` — run VLC headlessly (no GUI), useful for spawning many sources from a script.

## View the composed output

```pwsh
.\lib\ffplay.exe -fflags nobuffer -i udp://239.0.0.2:6000
# or in VLC:
.\lib\vlc.exe udp://@239.0.0.2:6000
```

You can also `ffplay` any individual source feed while the mosaic is running — multicast means everyone gets a full copy:

```pwsh
.\lib\ffplay.exe -fflags nobuffer -i udp://239.0.0.1:5001
```

## Adjusting the config

Edit `example-config.json` to change source URIs, layout, output, etc. The shape is:

```jsonc
{
  "sources": [
    { "name": "...", "uri": "udp://239.0.0.1:5001", "protocol": "MpegTsUdp" }
  ],
  "grid":   { "rows": 2, "cols": 2 },        // either grid…
  "cells":  [ /* { x, y, width, height } */ ],// …or explicit normalized rects
  "output": {
    "uri": "udp://239.0.0.2:6000", "protocol": "UdpMpegTs",
    // width, height, frameRate, bitrateKbps, gopSeconds, lowLatency
    "hwAccel": "none"                         // "none" | "nvidia" | "intel"
  },
  "chrome": {
    "backgroundColor": "black", "borderPx": 2, "showLabels": true,
    "labelFontFile": "C:/Windows/Fonts/arial.ttf"  // required on Windows; drawtext needs an explicit font
  },
  "ffmpeg": { "binaryPath": "./lib/ffmpeg.exe" } // optional; --ffmpeg CLI flag wins
}
```

**`hwAccel`** picks the H.264 encoder:
- `"none"` (default) — software `libx264`. Works everywhere.
- `"nvidia"` — `h264_nvenc` (NVIDIA GPU + driver 570+).
- `"intel"` — `h264_qsv` (Intel iGPU or Arc).

## Troubleshooting

- **`startup failed: Timeout`** — none of the sources are producing packets yet. Check VLC streams are running and pointed at the right multicast address. The session will fault if no `frame=` line arrives within `FfmpegOptions.StartupTimeout` (default 10 s).
- **`Source 1 'cam2' Connected -> Disconnected`** — one of your VLC streams stopped. The mosaic keeps running with the other tiles; the disconnected tile shows its last frame or black.
- **No output appears** — try `ffplay udp://239.0.0.2:6000` instead of VLC. VLC can be slow to lock onto a low-latency stream.
- **`bind failed`** error on startup — the library now sets `reuse=1` on inputs, so orphan binders shouldn't block startup anymore. If you still see it, an older process is squatting on the port. `Get-Process ffmpeg,ffplay | Stop-Process -Force` clears it.
