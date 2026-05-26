namespace MosaicLiveGenerator;

public abstract class MosaicException : Exception
{
    protected MosaicException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class MosaicConfigurationException : MosaicException
{
    public MosaicConfigurationException(string message, Exception? inner = null) : base(message, inner) { }
}

public enum MosaicStartupReason
{
    Timeout,
    ImmediateExit,
    BadInputSource,
    FfmpegNotFound,
    Other
}

public sealed class MosaicStartupException : MosaicException
{
    public MosaicStartupException(string message, Exception? inner = null) : base(message, inner) { }
    public MosaicStartupReason Reason { get; init; }
    public string StderrTail { get; init; } = "";
}

public sealed class MosaicRuntimeException : MosaicException
{
    public MosaicRuntimeException(string message, Exception? inner = null) : base(message, inner) { }
    public string StderrTail { get; init; } = "";
}
