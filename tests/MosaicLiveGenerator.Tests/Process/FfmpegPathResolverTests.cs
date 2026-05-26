using MosaicLiveGenerator.Process;
using Xunit;

namespace MosaicLiveGenerator.Tests.Process;

public class FfmpegPathResolverTests
{
    [Fact]
    public void ExplicitPath_IsReturnedWhenFileExists()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var path = FfmpegPathResolver.Resolve(tmp);
            Assert.Equal(tmp, path);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void ExplicitPath_ThrowsWhenFileMissing()
    {
        var bogus = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid()}");
        var ex = Assert.Throws<MosaicConfigurationException>(() => FfmpegPathResolver.Resolve(bogus));
        Assert.Contains(bogus, ex.Message);
    }

    [Fact]
    public void NullPath_FallsBackToPathLookup()
    {
        // We don't assume ffmpeg is installed in CI. If PATH lookup yields null,
        // resolver should throw a useful error.
        var lookupResult = FfmpegPathResolver.TryFindOnPath();
        if (lookupResult is null)
        {
            var ex = Assert.Throws<MosaicConfigurationException>(() => FfmpegPathResolver.Resolve(null));
            Assert.Contains("ffmpeg", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            var path = FfmpegPathResolver.Resolve(null);
            Assert.Equal(lookupResult, path);
        }
    }

    [Fact]
    public void ExecutableSuffix_MatchesPlatform()
    {
        var expected = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        Assert.Equal(expected, FfmpegPathResolver.ExecutableFileName);
    }
}
