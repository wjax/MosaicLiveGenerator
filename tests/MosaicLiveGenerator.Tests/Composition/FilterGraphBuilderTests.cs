using MosaicLiveGenerator.Composition;
using Xunit;

namespace MosaicLiveGenerator.Tests.Composition;

public class FilterGraphBuilderTests
{
    [Fact]
    public void Letterbox_ProducesScaleThenPad()
    {
        var chain = FilterGraphBuilder.BuildSourceChain(
            inputIndex: 0,
            slotW: 960, slotH: 540,
            frameRate: 25,
            fit: TileFit.Letterbox,
            backgroundColor: "black",
            label: null,
            labelFontSize: 18);

        Assert.Equal(
            "[0:v]setpts=PTS-STARTPTS,fps=25,scale=960:540:force_original_aspect_ratio=decrease,pad=960:540:(ow-iw)/2:(oh-ih)/2:color=black[v0]",
            chain);
    }

    [Fact]
    public void Crop_ProducesScaleThenCrop()
    {
        var chain = FilterGraphBuilder.BuildSourceChain(
            inputIndex: 1,
            slotW: 640, slotH: 360,
            frameRate: 30,
            fit: TileFit.Crop,
            backgroundColor: "black",
            label: null,
            labelFontSize: 18);

        Assert.Equal(
            "[1:v]setpts=PTS-STARTPTS,fps=30,scale=640:360:force_original_aspect_ratio=increase,crop=640:360[v1]",
            chain);
    }

    [Fact]
    public void Stretch_ProducesBareScale()
    {
        var chain = FilterGraphBuilder.BuildSourceChain(
            inputIndex: 2,
            slotW: 320, slotH: 180,
            frameRate: 25,
            fit: TileFit.Stretch,
            backgroundColor: "black",
            label: null,
            labelFontSize: 18);

        Assert.Equal(
            "[2:v]setpts=PTS-STARTPTS,fps=25,scale=320:180[v2]",
            chain);
    }

    [Fact]
    public void Label_AppendsDrawtext()
    {
        var chain = FilterGraphBuilder.BuildSourceChain(
            inputIndex: 0,
            slotW: 960, slotH: 540,
            frameRate: 25,
            fit: TileFit.Letterbox,
            backgroundColor: "black",
            label: "CAM 1",
            labelFontSize: 18);

        Assert.Contains("drawtext=text='CAM 1':x=10:y=10:fontsize=18", chain);
        Assert.EndsWith("[v0]", chain);
    }

    [Fact]
    public void Label_EscapesSingleQuotes()
    {
        var chain = FilterGraphBuilder.BuildSourceChain(
            inputIndex: 0, slotW: 100, slotH: 100, frameRate: 25,
            fit: TileFit.Stretch, backgroundColor: "black",
            label: "O'Hara", labelFontSize: 18);

        // ffmpeg drawtext escapes ' as \\'  ; inside C# raw becomes \'
        Assert.Contains(@"text='O\'Hara'", chain);
    }
}
