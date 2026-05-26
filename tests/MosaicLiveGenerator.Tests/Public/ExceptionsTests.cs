using Xunit;

namespace MosaicLiveGenerator.Tests.Public;

public class ExceptionsTests
{
    [Fact]
    public void MosaicConfigurationException_IsAMosaicException()
    {
        var ex = new MosaicConfigurationException("bad option");
        Assert.IsAssignableFrom<MosaicException>(ex);
        Assert.Equal("bad option", ex.Message);
    }

    [Fact]
    public void MosaicStartupException_CarriesReasonAndStderrTail()
    {
        var ex = new MosaicStartupException("startup failed")
        {
            Reason = MosaicStartupReason.Timeout,
            StderrTail = "line1\nline2"
        };
        Assert.Equal(MosaicStartupReason.Timeout, ex.Reason);
        Assert.Equal("line1\nline2", ex.StderrTail);
    }

    [Fact]
    public void MosaicRuntimeException_CarriesStderrTail()
    {
        var ex = new MosaicRuntimeException("ffmpeg crashed")
        {
            StderrTail = "Segmentation fault"
        };
        Assert.Contains("Segmentation", ex.StderrTail);
    }

    [Fact]
    public void EventArgs_ExposeExpectedFields()
    {
        var sc = new SessionStateChangedEventArgs { OldState = SessionState.Starting, NewState = SessionState.Running };
        Assert.Equal(SessionState.Starting, sc.OldState);
        Assert.Equal(SessionState.Running, sc.NewState);

        var src = new SourceConnectivityChangedEventArgs
        {
            SourceIndex = 2,
            SourceName = "cam3",
            OldConnectivity = SourceConnectivity.Connected,
            NewConnectivity = SourceConnectivity.Disconnected
        };
        Assert.Equal(2, src.SourceIndex);
        Assert.Equal("cam3", src.SourceName);

        var f = new FaultedEventArgs { Error = new InvalidOperationException("boom") };
        Assert.Equal("boom", f.Error.Message);
    }
}
