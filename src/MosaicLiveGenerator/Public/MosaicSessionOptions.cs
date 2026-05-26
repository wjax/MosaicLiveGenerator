namespace MosaicLiveGenerator;

public sealed record MosaicSessionOptions(
    IReadOnlyList<VideoSource> Sources,
    Layout Layout,
    OutputOptions Output,
    LayoutOptions? LayoutChrome = null,
    FfmpegOptions? Ffmpeg = null);
