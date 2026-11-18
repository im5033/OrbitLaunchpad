using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Orbit;

internal sealed record DisplayInfo(string Device, bool Primary, Rectangle Bounds);
internal sealed record DisplayOption(string Device, string Label)
{
    public override string ToString() => Label;
}
internal static class Displays
{
    public static DisplayInfo[] Connected() => System.Windows.Forms.Screen.AllScreens.Select(x => new DisplayInfo(x.DeviceName, x.Primary, x.Bounds)).ToArray();
    public static DisplayInfo Select(IReadOnlyList<DisplayInfo> screens, string? device)
        => screens.FirstOrDefault(x => !string.IsNullOrEmpty(device) && string.Equals(x.Device, device, StringComparison.OrdinalIgnoreCase))
        ?? screens.FirstOrDefault(x => x.Primary) ?? screens.First();
    public static List<DisplayOption> Options(string selected)
    {
        var options = new List<DisplayOption> { new("", "跟隨 Windows 主顯示器") };
        foreach (var screen in Connected()) options.Add(new(screen.Device, screen.Device.Replace(@"\\.\", "") + (screen.Primary ? "（主顯示器）" : "") + $" · {screen.Bounds.Width} × {screen.Bounds.Height}"));
        if (selected.Length > 0 && !options.Any(x => string.Equals(x.Device, selected, StringComparison.OrdinalIgnoreCase))) options.Add(new(selected, selected + "（未連接，暫用主顯示器）"));
        return options;
    }
}
