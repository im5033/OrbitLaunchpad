using System;
using Rectangle = System.Drawing.Rectangle;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Orbit;

internal sealed class FrostWindow : Window
{
    private Rectangle _bounds;
    public FrostWindow(ImageSource? backdrop, Action dismiss)
    {
        Title = "Orbit · 霧玻璃"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; Opacity = .001;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(10, 13, 27));
        if (backdrop != null) Content = new Image { Source = backdrop, Stretch = Stretch.UniformToFill };
        MouseLeftButtonUp += (_, e) => { e.Handled = true; dismiss(); };
        SourceInitialized += (_, _) =>
        {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source.AddHook((IntPtr h, int message, IntPtr w, IntPtr l, ref bool handled) =>
            {
                if (message == 0x21) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
                return IntPtr.Zero;
            });
        };
        Loaded += (_, _) => Place(_bounds);
    }
    public void Place(Rectangle bounds)
    {
        _bounds = bounds;
        Native.PlaceWindow(new WindowInteropHelper(this).EnsureHandle(), bounds);
    }
    public void Reveal(Rectangle bounds, double opacity)
    {
        _bounds = bounds; Opacity = opacity;
        Show(); Place(bounds);
    }
}
