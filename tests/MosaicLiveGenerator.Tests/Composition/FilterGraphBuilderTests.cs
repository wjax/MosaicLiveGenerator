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

    [Fact]
    public void Label_WithFontFile_EmitsFontfileBeforeText()
    {
        var chain = FilterGraphBuilder.BuildSourceChain(
            inputIndex: 0, slotW: 100, slotH: 100, frameRate: 25,
            fit: TileFit.Stretch, backgroundColor: "black",
            label: "A", labelFontSize: 18,
            labelFontFile: @"C:\Windows\Fonts\arial.ttf");

        // Backslashes → forward slashes; colon → escaped colon
        Assert.Contains(@"drawtext=fontfile='C\:/Windows/Fonts/arial.ttf':text='A'", chain);
    }

    [Fact]
    public void Label_WithoutFontFile_OmitsFontfileClause()
    {
        var chain = FilterGraphBuilder.BuildSourceChain(
            inputIndex: 0, slotW: 100, slotH: 100, frameRate: 25,
            fit: TileFit.Stretch, backgroundColor: "black",
            label: "A", labelFontSize: 18,
            labelFontFile: null);

        Assert.DoesNotContain("fontfile=", chain);
        Assert.Contains("drawtext=text='A'", chain);
    }

    [Fact]
    public void FullGraph_PropagatesLayoutOptionsLabelFontFile()
    {
        var graph = FilterGraphBuilder.BuildFullGraph(
            sources: new[] {
                new SourcePlacement(0, new PixelRect(0, 0, 960, 540), TileFit.Letterbox, label: "A"),
            },
            canvasW: 1920, canvasH: 1080,
            frameRate: 25,
            layoutChrome: new LayoutOptions(
                ShowLabels: true,
                LabelFontSize: 18,
                LabelFontFile: @"C:\Windows\Fonts\arial.ttf"));

        Assert.Contains(@"fontfile='C\:/Windows/Fonts/arial.ttf'", graph);
    }

    [Fact]
    public void FullGraph_TwoSources_ProducesBackgroundPlusOverlay()
    {
        var graph = FilterGraphBuilder.BuildFullGraph(
            sources: new[] {
                new SourcePlacement(0, new PixelRect(0, 0, 960, 540), TileFit.Letterbox, label: null),
                new SourcePlacement(1, new PixelRect(960, 0, 960, 540), TileFit.Letterbox, label: null),
            },
            canvasW: 1920, canvasH: 1080,
            frameRate: 25,
            layoutChrome: new LayoutOptions());

        Assert.Equal(
            "[0:v]setpts=PTS-STARTPTS,fps=25,scale=960:540:force_original_aspect_ratio=decrease,pad=960:540:(ow-iw)/2:(oh-ih)/2:color=black[v0];" +
            "[1:v]setpts=PTS-STARTPTS,fps=25,scale=960:540:force_original_aspect_ratio=decrease,pad=960:540:(ow-iw)/2:(oh-ih)/2:color=black[v1];" +
            "color=c=black:s=1920x1080:r=25[bg];" +
            "[bg][v0]overlay=x=0:y=0:shortest=0:eof_action=pass[c0];" +
            "[c0][v1]overlay=x=960:y=0:shortest=0:eof_action=pass[out]",
            graph);
    }

    [Fact]
    public void FullGraph_RespectsCustomBackgroundColor()
    {
        var graph = FilterGraphBuilder.BuildFullGraph(
            sources: new[] {
                new SourcePlacement(0, new PixelRect(0, 0, 1920, 1080), TileFit.Stretch, null),
            },
            canvasW: 1920, canvasH: 1080,
            frameRate: 25,
            layoutChrome: new LayoutOptions(BackgroundColor: "0x202020"));

        Assert.Contains("color=c=0x202020:s=1920x1080:r=25[bg]", graph);
    }

    [Fact]
    public void FullGraph_AppendsBorderDrawboxWhenBorderPxSet()
    {
        var graph = FilterGraphBuilder.BuildFullGraph(
            sources: new[] {
                new SourcePlacement(0, new PixelRect(0, 0, 960, 540), TileFit.Letterbox, null),
                new SourcePlacement(1, new PixelRect(960, 0, 960, 540), TileFit.Letterbox, null),
            },
            canvasW: 1920, canvasH: 1080,
            frameRate: 25,
            layoutChrome: new LayoutOptions(BorderPx: 2, BorderColor: "red"));

        // border = chained drawbox filters on the output, ending in [out]
        Assert.Contains("drawbox=x=0:y=0:w=960:h=540:color=red:t=2", graph);
        Assert.Contains("drawbox=x=960:y=0:w=960:h=540:color=red:t=2", graph);
        Assert.EndsWith("[out]", graph);
    }

    [Fact]
    public void FullGraph_LabelsPropagateFromPlacements()
    {
        var graph = FilterGraphBuilder.BuildFullGraph(
            sources: new[] {
                new SourcePlacement(0, new PixelRect(0, 0, 960, 540), TileFit.Letterbox, label: "A"),
            },
            canvasW: 1920, canvasH: 1080,
            frameRate: 25,
            layoutChrome: new LayoutOptions(ShowLabels: true, LabelFontSize: 24));

        Assert.Contains("drawtext=text='A':x=10:y=10:fontsize=24", graph);
    }
}
