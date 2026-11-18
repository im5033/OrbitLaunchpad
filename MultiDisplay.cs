using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Orbit;

public sealed partial class LauncherWindow
{
    private readonly Dictionary<string, FrostWindow> _frostWindows = new(StringComparer.OrdinalIgnoreCase);
    private DisplayInfo[] _connectedDisplays = Array.Empty<DisplayInfo>();
    private DisplayInfo? _targetDisplay;
    private ImageSource? _backdropImage;

    private void PositionOnScreen()
    {
        _connectedDisplays = Displays.Connected();
        _targetDisplay = Displays.Select(_connectedDisplays, Prefs.DisplayDevice);
        var screen = _targetDisplay.Bounds;
        var dpi = VisualTreeHelper.GetDpi(this);
        if (WindowState != System.Windows.WindowState.Normal) WindowState = System.Windows.WindowState.Normal;
        Left = screen.Left / dpi.DpiScaleX; Top = screen.Top / dpi.DpiScaleY;
        Width = screen.Width / dpi.DpiScaleX; Height = screen.Height / dpi.DpiScaleY;
        Native.PlaceWindow(new WindowInteropHelper(this).EnsureHandle(), screen);
        foreach (string device in _frostWindows.Keys.Where(x => !_connectedDisplays.Any(s => s.Device == x) || x == _targetDisplay.Device).ToArray())
        { _frostWindows[device].Close(); _frostWindows.Remove(device); }
        foreach (var other in _connectedDisplays.Where(x => x.Device != _targetDisplay.Device))
        {
            if (!_frostWindows.TryGetValue(other.Device, out var frost))
            {
                frost = new FrostWindow(_backdropImage, DismissLevel) { Owner = this };
                _frostWindows.Add(other.Device, frost);
            }
            frost.Place(other.Bounds);
        }
        _layoutDirty = true;
    }
    private void ShowFrostWindows()
    {
        foreach (var other in _connectedDisplays.Where(x => _targetDisplay != null && x.Device != _targetDisplay.Device))
            if (_frostWindows.TryGetValue(other.Device, out var frost)) frost.Reveal(other.Bounds, Opacity);
    }
    private void HideFrostWindows() { foreach (var frost in _frostWindows.Values) frost.Hide(); }
    private void FadeFrostWindows() { foreach (var frost in _frostWindows.Values) frost.Opacity = Opacity; }
    private void DisplaySettingsChanged(object? sender, EventArgs args)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_quitting || !_ready || _modal || _transitioning || _hiding || !IsVisible) return;
            PositionOnScreen(); Refresh(false); ShowFrostWindows(); Activate();
        });
    }
    private void CloseFrostWindows()
    {
        SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
        foreach (var frost in _frostWindows.Values) frost.Close();
        _frostWindows.Clear();
    }
}
