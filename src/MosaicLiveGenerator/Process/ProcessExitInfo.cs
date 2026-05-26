namespace MosaicLiveGenerator.Process;

internal sealed record ProcessExitInfo(int ExitCode, bool TimedOutOnGraceful);
