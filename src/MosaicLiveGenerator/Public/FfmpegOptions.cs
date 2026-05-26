namespace MosaicLiveGenerator;

public sealed record FfmpegOptions(
    string? BinaryPath = null,
    bool LogStderr = true,
    TimeSpan StartupTimeout = default);
