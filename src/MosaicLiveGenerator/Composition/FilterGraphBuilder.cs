using System.Text;

namespace MosaicLiveGenerator.Composition;

internal static class FilterGraphBuilder
{
    public static string BuildSourceChain(
        int inputIndex,
        int slotW,
        int slotH,
        int frameRate,
        TileFit fit,
        string backgroundColor,
        string? label,
        int labelFontSize,
        string? labelFontFile = null)
    {
        var sb = new StringBuilder();
        sb.Append('[').Append(inputIndex).Append(":v]");
        sb.Append("setpts=PTS-STARTPTS,");
        sb.Append("fps=").Append(frameRate).Append(',');

        switch (fit)
        {
            case TileFit.Letterbox:
                sb.Append("scale=").Append(slotW).Append(':').Append(slotH)
                  .Append(":force_original_aspect_ratio=decrease,");
                sb.Append("pad=").Append(slotW).Append(':').Append(slotH)
                  .Append(":(ow-iw)/2:(oh-ih)/2:color=").Append(backgroundColor);
                break;
            case TileFit.Crop:
                sb.Append("scale=").Append(slotW).Append(':').Append(slotH)
                  .Append(":force_original_aspect_ratio=increase,");
                sb.Append("crop=").Append(slotW).Append(':').Append(slotH);
                break;
            case TileFit.Stretch:
                sb.Append("scale=").Append(slotW).Append(':').Append(slotH);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fit));
        }

        if (!string.IsNullOrEmpty(label))
        {
            var escapedLabel = label.Replace(@"\", @"\\").Replace("'", @"\'");
            sb.Append(",drawtext=");
            if (!string.IsNullOrEmpty(labelFontFile))
            {
                sb.Append("fontfile='").Append(EscapeFilterPath(labelFontFile)).Append("':");
            }
            sb.Append("text='").Append(escapedLabel).Append('\'')
              .Append(":x=10:y=10:fontsize=").Append(labelFontSize)
              .Append(":fontcolor=white:box=1:boxcolor=black@0.5");
        }

        sb.Append("[v").Append(inputIndex).Append(']');
        return sb.ToString();
    }

    public static string BuildFullGraph(
        IReadOnlyList<SourcePlacement> sources,
        int canvasW,
        int canvasH,
        int frameRate,
        LayoutOptions layoutChrome)
    {
        if (sources.Count == 0)
            throw new ArgumentException("at least one source required", nameof(sources));

        var sb = new StringBuilder();

        // Per-source chains
        foreach (var s in sources)
        {
            var chain = BuildSourceChain(
                s.InputIndex, s.Rect.Width, s.Rect.Height,
                frameRate, s.Fit,
                layoutChrome.BackgroundColor,
                layoutChrome.ShowLabels ? s.label : null,
                layoutChrome.LabelFontSize,
                layoutChrome.LabelFontFile);
            sb.Append(chain).Append(';');
        }

        // Background canvas
        sb.Append("color=c=").Append(layoutChrome.BackgroundColor)
          .Append(":s=").Append(canvasW).Append('x').Append(canvasH)
          .Append(":r=").Append(frameRate).Append("[bg]");

        // Overlay chain
        var prev = "bg";
        for (var i = 0; i < sources.Count; i++)
        {
            var s = sources[i];
            var isLast = i == sources.Count - 1 && layoutChrome.BorderPx == 0;
            var next = isLast ? "out" : $"c{i}";
            sb.Append(';')
              .Append('[').Append(prev).Append(']')
              .Append("[v").Append(s.InputIndex).Append(']')
              .Append("overlay=x=").Append(s.Rect.X)
              .Append(":y=").Append(s.Rect.Y)
              .Append(":shortest=0:eof_action=pass")
              .Append('[').Append(next).Append(']');
            prev = next;
        }

        // Borders: chain drawbox per source rect, ending in [out]
        if (layoutChrome.BorderPx > 0)
        {
            for (var i = 0; i < sources.Count; i++)
            {
                var s = sources[i];
                var isLast = i == sources.Count - 1;
                var next = isLast ? "out" : $"b{i}";
                sb.Append(';')
                  .Append('[').Append(prev).Append(']')
                  .Append("drawbox=x=").Append(s.Rect.X)
                  .Append(":y=").Append(s.Rect.Y)
                  .Append(":w=").Append(s.Rect.Width)
                  .Append(":h=").Append(s.Rect.Height)
                  .Append(":color=").Append(layoutChrome.BorderColor)
                  .Append(":t=").Append(layoutChrome.BorderPx)
                  .Append('[').Append(next).Append(']');
                prev = next;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Escape a Windows/Unix path for inclusion inside an ffmpeg filter argument.
    /// Backslashes become forward slashes (ffmpeg accepts either on Windows), and
    /// colons get escaped so they aren't read as filter-argument separators.
    /// </summary>
    private static string EscapeFilterPath(string path) =>
        path.Replace('\\', '/').Replace(":", @"\:");
}

internal sealed record SourcePlacement(int InputIndex, PixelRect Rect, TileFit Fit, string? label);
