<#
.SYNOPSIS
    Stream an MP4 file to a UDP/MPEG-TS endpoint using the bundled ffmpeg.

.DESCRIPTION
    Loops the file forever (-stream_loop -1) and paces output at wall-clock
    realtime (-re). By default re-encodes to H.264 Main @ ~3.5 Mbps CBR with
    a 2-second closed GOP, which is a "normal" distribution-grade profile.
    Pass -Copy to skip re-encoding and just repackage MP4 -> MPEG-TS/UDP
    (much less CPU, but you inherit the file's existing GOP / bitrate).

.PARAMETER File
    Path to the source .mp4 (or any container ffmpeg can demux).

.PARAMETER Port
    Destination UDP port. Default 5001 (matches samples/MosaicSmoke/example-config.json source 1).

.PARAMETER Destination
    Destination host. Default 127.0.0.1 (loopback).

.PARAMETER BitrateKbps
    Target bitrate when transcoding. Default 3500. Ignored when -Copy.

.PARAMETER FrameRate
    Output frame rate when transcoding. Default 25. Ignored when -Copy.

.PARAMETER Copy
    Skip re-encoding. Stream-copies the H.264 elementary stream from MP4
    straight into MPEG-TS. Source must be H.264.

.PARAMETER IncludeAudio
    Include the source's audio track (transcoded to AAC 128 kbps, or copied
    in -Copy mode). Default is video-only.

.EXAMPLE
    # Stream a video to udp://127.0.0.1:5001 with default transcode params
    .\scripts\stream-mp4.ps1 C:\videos\sample.mp4

.EXAMPLE
    # Stream to source 2 of the smoke-test mosaic
    .\scripts\stream-mp4.ps1 C:\videos\sample.mp4 -Port 5002

.EXAMPLE
    # Pure remux, no transcode (lowest CPU, fastest startup)
    .\scripts\stream-mp4.ps1 C:\videos\sample.mp4 -Copy

.EXAMPLE
    # Fire 4 sources at once for a 2x2 mosaic
    1..4 | ForEach-Object {
        Start-Process powershell -ArgumentList "-File scripts/stream-mp4.ps1 C:\videos\file$_.mp4 -Port $(5000+$_)"
    }
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [string]$File,

    [string]$Destination = '127.0.0.1',
    [int]$Port = 5001,
    [int]$BitrateKbps = 3500,
    [int]$FrameRate = 25,
    [switch]$Copy,
    [switch]$IncludeAudio
)

$ErrorActionPreference = 'Stop'

# Locate ffmpeg: prefer bundled lib/ffmpeg.exe, fall back to PATH
$repoRoot = Split-Path -Parent $PSScriptRoot
$bundled  = Join-Path $repoRoot 'lib\ffmpeg.exe'
$ffmpeg   = if (Test-Path $bundled) { $bundled } else { (Get-Command ffmpeg -ErrorAction SilentlyContinue)?.Source }
if (-not $ffmpeg) {
    Write-Error "ffmpeg not found. Looked at '$bundled' and on PATH."
}

if (-not (Test-Path -LiteralPath $File)) {
    Write-Error "Input file not found: $File"
}
$resolvedFile = (Resolve-Path -LiteralPath $File).Path
$endpoint     = "udp://${Destination}:${Port}?pkt_size=1316"

# Assemble ffmpeg arguments
$ffArgs = @(
    '-hide_banner',
    '-loglevel', 'warning',
    '-re',
    '-stream_loop', '-1',
    '-i', $resolvedFile,
    '-map', '0:v:0'
)

if ($Copy) {
    $ffArgs += @(
        '-c:v', 'copy',
        '-bsf:v', 'h264_mp4toannexb'   # MP4 AVCC framing -> MPEG-TS Annex-B
    )
    $mode = 'stream copy (no transcode)'
} else {
    $keyint = $FrameRate * 2            # 2-second GOP
    $br     = "${BitrateKbps}k"
    $ffArgs += @(
        '-c:v', 'libx264',
        '-profile:v', 'main',
        '-level:v', '4.0',
        '-pix_fmt', 'yuv420p',
        '-preset', 'medium',
        '-tune', 'film',
        '-r', "$FrameRate",
        '-b:v', $br,
        '-minrate', $br,
        '-maxrate', $br,
        '-bufsize', $br,
        '-g', "$keyint",
        '-keyint_min', "$keyint",
        '-sc_threshold', '0',
        '-bf', '2',
        '-x264-params', 'nal-hrd=cbr:force-cfr=1:scenecut=0'
    )
    $mode = "transcode H.264 Main @ ${BitrateKbps} kbps CBR, GOP $keyint"
}

if ($IncludeAudio) {
    $ffArgs += '-map', '0:a:0?'        # '?' = optional, no error if file has no audio
    if ($Copy) {
        $ffArgs += '-c:a', 'copy'
    } else {
        $ffArgs += '-c:a', 'aac', '-b:a', '128k', '-ac', '2', '-ar', '48000'
    }
} else {
    $ffArgs += '-an'
}

$ffArgs += @(
    '-muxdelay', '0.5',
    '-muxpreload', '0.5',
    '-mpegts_flags', '+resend_headers+pat_pmt_at_frames',
    '-f', 'mpegts',
    $endpoint
)

Write-Host ""
Write-Host "ffmpeg:  $ffmpeg"
Write-Host "Input:   $resolvedFile"
Write-Host "Output:  $endpoint"
Write-Host "Mode:    $mode"
Write-Host "Audio:   $(if ($IncludeAudio) { 'on' } else { 'off' })"
Write-Host ""
Write-Host "Press Ctrl-C to stop."
Write-Host ""

& $ffmpeg @ffArgs
exit $LASTEXITCODE
