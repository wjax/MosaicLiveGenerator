namespace MosaicLiveGenerator.Composition;

internal static class EncoderArgBuilder
{
    public static IReadOnlyList<string> Build(OutputOptions output) => output.HwAccel switch
    {
        HwAccel.None   => BuildLibx264(output),
        HwAccel.Nvidia => BuildNvenc(output),
        HwAccel.Intel  => BuildQsv(output),
        _ => throw new ArgumentOutOfRangeException(nameof(output), $"Unknown HwAccel {output.HwAccel}."),
    };

    private static IReadOnlyList<string> BuildLibx264(OutputOptions output)
    {
        var keyint = output.FrameRate * output.GopSeconds;
        var bitrate = $"{output.BitrateKbps}k";

        var args = new List<string>
        {
            "-c:v", "libx264",
            "-preset", output.LowLatency ? "ultrafast" : "veryfast",
            "-tune", "zerolatency",
            "-pix_fmt", "yuv420p",
            "-r", output.FrameRate.ToString(),
            "-g", keyint.ToString(),
            "-keyint_min", keyint.ToString(),
            "-bf", "0",
            "-refs", "1",
            "-flags", "+low_delay",
            "-flush_packets", "1",
            "-b:v", bitrate,
            "-maxrate", bitrate,
            "-bufsize", bitrate,
        };

        if (output.LowLatency)
        {
            var x264 =
                $"nal-hrd=cbr:force-cfr=1:scenecut=0:rc-lookahead=0:sync-lookahead=0" +
                $":bframes=0:b-adapt=0:intra-refresh=1:keyint={keyint}:min-keyint={keyint}" +
                $":no-mbtree=1:sliced-threads=1";
            args.Add("-x264-params");
            args.Add(x264);
        }

        args.Add("-an");
        return args;
    }

    private static IReadOnlyList<string> BuildNvenc(OutputOptions output)
    {
        var keyint = output.FrameRate * output.GopSeconds;
        var bitrate = $"{output.BitrateKbps}k";

        var args = new List<string>
        {
            "-c:v", "h264_nvenc",
            // Modern NVENC preset/tune (SDK 11+). p1 = fastest, p4 = balanced.
            "-preset", output.LowLatency ? "p1" : "p4",
            "-tune",   output.LowLatency ? "ll" : "hq",
            "-rc", "cbr",
            "-profile:v", "main",
            "-pix_fmt", "yuv420p",
            "-r", output.FrameRate.ToString(),
            "-g", keyint.ToString(),
            "-keyint_min", keyint.ToString(),
            "-bf", output.LowLatency ? "0" : "2",
            "-no-scenecut", "1",
            "-b:v", bitrate,
            "-maxrate", bitrate,
            "-bufsize", bitrate,
            "-an",
        };

        if (output.LowLatency)
        {
            args.Add("-zerolatency");
            args.Add("1");
        }

        return args;
    }

    private static IReadOnlyList<string> BuildQsv(OutputOptions output)
    {
        var keyint = output.FrameRate * output.GopSeconds;
        var bitrate = $"{output.BitrateKbps}k";

        var args = new List<string>
        {
            "-c:v", "h264_qsv",
            "-preset", output.LowLatency ? "veryfast" : "medium",
            "-profile:v", "main",
            "-pix_fmt", "nv12",
            "-r", output.FrameRate.ToString(),
            "-g", keyint.ToString(),
            "-bf", output.LowLatency ? "0" : "2",
            // Strict CBR via b:v + maxrate + bufsize; QSV picks CBR mode automatically.
            "-b:v", bitrate,
            "-maxrate", bitrate,
            "-bufsize", bitrate,
            "-an",
        };

        if (output.LowLatency)
        {
            // Disable look-ahead so frames don't sit in a buffer waiting for future context.
            args.Add("-look_ahead");
            args.Add("0");
        }

        return args;
    }
}
