namespace MosaicLiveGenerator.Composition;

internal static class EncoderArgBuilder
{
    public static IReadOnlyList<string> Build(OutputOptions output)
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
}
