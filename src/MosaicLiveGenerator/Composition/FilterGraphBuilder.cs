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
        int labelFontSize)
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
            var escaped = label.Replace(@"\", @"\\").Replace("'", @"\'");
            sb.Append(",drawtext=text='").Append(escaped).Append('\'')
              .Append(":x=10:y=10:fontsize=").Append(labelFontSize)
              .Append(":fontcolor=white:box=1:boxcolor=black@0.5");
        }

        sb.Append("[v").Append(inputIndex).Append(']');
        return sb.ToString();
    }
}
