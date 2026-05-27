namespace MosaicLiveGenerator;

public sealed record OutputOptions(
    Uri Uri,
    OutputProtocol Protocol = OutputProtocol.UdpMpegTs,
    int Width = 1920,
    int Height = 1080,
    int FrameRate = 25,
    int BitrateKbps = 6000,
    int GopSeconds = 1,
    bool LowLatency = true,
    HwAccel HwAccel = HwAccel.None);
