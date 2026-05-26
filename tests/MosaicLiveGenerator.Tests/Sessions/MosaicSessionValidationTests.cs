using Xunit;

namespace MosaicLiveGenerator.Tests.Sessions;

public class MosaicSessionValidationTests
{
    private static VideoSource U(string uri) =>
        new("s", new Uri(uri), SourceProtocol.MpegTsUdp);

    [Fact]
    public void EmptySources_Throws()
    {
        var options = new MosaicSessionOptions(
            Sources: Array.Empty<VideoSource>(),
            Layout: Layout.Grid(1, 1),
            Output: new OutputOptions(new Uri("udp://127.0.0.1:5000")));

        var ex = Assert.Throws<MosaicConfigurationException>(() => new MosaicSession(options));
        Assert.Contains("Sources", ex.Message);
    }

    [Fact]
    public void SourceCountMismatchWithLayout_Throws()
    {
        var options = new MosaicSessionOptions(
            Sources: new[] { U("udp://127.0.0.1:5001") }, // 1 source
            Layout: Layout.Grid(2, 2),                     // expects 4
            Output: new OutputOptions(new Uri("udp://127.0.0.1:5000")));

        Assert.Throws<MosaicConfigurationException>(() => new MosaicSession(options));
    }

    [Fact]
    public void OutputSchemeMismatch_Throws()
    {
        var options = new MosaicSessionOptions(
            Sources: new[] { U("udp://127.0.0.1:5001") },
            Layout: Layout.Grid(1, 1),
            Output: new OutputOptions(
                Uri: new Uri("rtp://127.0.0.1:5000"),
                Protocol: OutputProtocol.UdpMpegTs));   // mismatch

        var ex = Assert.Throws<MosaicConfigurationException>(() => new MosaicSession(options));
        Assert.Contains("Uri.Scheme", ex.Message);
    }

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, 0)]
    [InlineData(1921, 1080)]
    [InlineData(1920, 1081)]
    public void NonPositiveOrOddDimensions_Throw(int w, int h)
    {
        var options = new MosaicSessionOptions(
            Sources: new[] { U("udp://127.0.0.1:5001") },
            Layout: Layout.Grid(1, 1),
            Output: new OutputOptions(new Uri("udp://127.0.0.1:5000"), Width: w, Height: h));

        Assert.Throws<MosaicConfigurationException>(() => new MosaicSession(options));
    }

    [Fact]
    public void InitialState_IsStoppedAndSourcesAreUnknown()
    {
        var sources = new[] { U("udp://127.0.0.1:5001"), U("udp://127.0.0.1:5002") };
        var options = new MosaicSessionOptions(
            Sources: sources,
            Layout: Layout.Grid(1, 2),
            Output: new OutputOptions(new Uri("udp://127.0.0.1:5000")));

        var session = new MosaicSession(options);

        Assert.Equal(SessionState.Stopped, session.State);
        Assert.Equal(2, session.SourceStates.Count);
        Assert.All(session.SourceStates, s => Assert.Equal(SourceConnectivity.Unknown, s.Connectivity));
        Assert.Equal("s", session.SourceStates[0].Name);
    }
}
