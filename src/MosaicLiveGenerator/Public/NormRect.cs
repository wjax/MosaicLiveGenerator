namespace MosaicLiveGenerator;

/// <summary>
/// A rectangle on the output canvas in normalized [0,1] coordinates.
/// X+Width must be ≤ 1, Y+Height must be ≤ 1.
/// </summary>
public readonly record struct NormRect(double X, double Y, double Width, double Height);
