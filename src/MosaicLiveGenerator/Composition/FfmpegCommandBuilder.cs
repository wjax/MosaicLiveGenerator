using MosaicLiveGenerator.Sources;

namespace MosaicLiveGenerator.Composition;

internal static class FfmpegCommandBuilder
{
    public static (IReadOnlyList<string> Args, IReadOnlyList<SourcePlacement> Placements)
        Build(MosaicSessionOptions options, string sdpDirectory)
    {
        var chrome = options.LayoutChrome ?? new LayoutOptions();
        var rects = options.Layout.ToCells(options.Sources.Count);
        var canvasW = options.Output.Width;
        var canvasH = options.Output.Height;

        var placements = new List<SourcePlacement>(rects.Count);
        for (var i = 0; i < rects.Count; i++)
        {
            var px = LayoutMath.ToPixelRect(rects[i], canvasW, canvasH);
            placements.Add(new SourcePlacement(i, px, options.Sources[i].Fit, options.Sources[i].Name));
        }

        var args = new List<string>
        {
            "-hide_banner",
            "-y",
        };

        for (var i = 0; i < options.Sources.Count; i++)
            args.AddRange(InputArgBuilder.Build(options.Sources[i], i, sdpDirectory));

        var graph = FilterGraphBuilder.BuildFullGraph(
            placements, canvasW, canvasH, options.Output.FrameRate, chrome);

        args.Add("-filter_complex");
        args.Add(graph);
        args.Add("-map");
        args.Add("[out]");

        args.AddRange(EncoderArgBuilder.Build(options.Output));
        args.AddRange(OutputArgBuilder.Build(options.Output));

        return (args, placements);
    }
}
