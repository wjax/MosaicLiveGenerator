namespace MosaicLiveGenerator;

public sealed record VideoSource(
    string Name,
    Uri Uri,
    SourceProtocol Protocol,
    TileFit Fit = TileFit.Letterbox);
