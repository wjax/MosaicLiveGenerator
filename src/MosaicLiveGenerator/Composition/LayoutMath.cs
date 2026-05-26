namespace MosaicLiveGenerator.Composition;

internal readonly record struct PixelRect(int X, int Y, int Width, int Height);

internal static class LayoutMath
{
    public static void ValidateRect(NormRect r, int index)
    {
        if (r.X < 0 || r.X >= 1) throw new ArgumentException($"Rect[{index}].X={r.X} must be in [0,1).");
        if (r.Y < 0 || r.Y >= 1) throw new ArgumentException($"Rect[{index}].Y={r.Y} must be in [0,1).");
        if (r.Width <= 0 || r.Width > 1) throw new ArgumentException($"Rect[{index}].Width={r.Width} must be in (0,1].");
        if (r.Height <= 0 || r.Height > 1) throw new ArgumentException($"Rect[{index}].Height={r.Height} must be in (0,1].");
        if (r.X + r.Width > 1 + 1e-9) throw new ArgumentException($"Rect[{index}] X+W={r.X + r.Width} exceeds 1.");
        if (r.Y + r.Height > 1 + 1e-9) throw new ArgumentException($"Rect[{index}] Y+H={r.Y + r.Height} exceeds 1.");
    }

    public static IReadOnlyList<(int a, int b)> FindOverlaps(IReadOnlyList<NormRect> rects)
    {
        var result = new List<(int, int)>();
        for (var i = 0; i < rects.Count; i++)
        for (var j = i + 1; j < rects.Count; j++)
            if (Overlap(rects[i], rects[j])) result.Add((i, j));
        return result;
    }

    private static bool Overlap(NormRect a, NormRect b)
    {
        // edge contact (e.g. a.right == b.left) is NOT overlap
        const double eps = 1e-9;
        var noOverlap =
            a.X + a.Width <= b.X + eps ||
            b.X + b.Width <= a.X + eps ||
            a.Y + a.Height <= b.Y + eps ||
            b.Y + b.Height <= a.Y + eps;
        return !noOverlap;
    }

    public static PixelRect ToPixelRect(NormRect r, int canvasW, int canvasH)
    {
        var x = RoundEven(r.X * canvasW);
        var y = RoundEven(r.Y * canvasH);
        var w = RoundEven(r.Width * canvasW);
        var h = RoundEven(r.Height * canvasH);
        // ensure we stay inside canvas
        if (x + w > canvasW) w = canvasW - x;
        if (y + h > canvasH) h = canvasH - y;
        if (w % 2 != 0) w -= 1;
        if (h % 2 != 0) h -= 1;
        return new PixelRect(x, y, w, h);
    }

    private static int RoundEven(double v)
    {
        var n = (int)Math.Round(v, MidpointRounding.AwayFromZero);
        if (n % 2 != 0) n -= 1;
        return Math.Max(0, n);
    }
}
