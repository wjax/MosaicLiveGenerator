namespace MosaicLiveGenerator;

public sealed record LayoutOptions(
    string BackgroundColor = "black",
    int BorderPx = 0,
    string BorderColor = "white",
    bool ShowLabels = false,
    int LabelFontSize = 18);
