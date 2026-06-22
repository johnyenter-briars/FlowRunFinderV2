using Avalonia.Controls;
using Avalonia.Platform;

namespace FlowRunFinderV2.UI;

public static class WindowIconHelper
{
    private static readonly Uri IconUri = new("avares://FlowRunFinderV2.UI/Assets/FlowRunFinderV2.ico");

    public static void ApplyAppIcon(this Window window)
    {
        using var stream = AssetLoader.Open(IconUri);
        window.Icon = new WindowIcon(stream);
    }
}
