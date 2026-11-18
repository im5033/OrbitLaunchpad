using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Orbit;

internal static class Theme
{
    public static readonly Brush Text = new SolidColorBrush(Color.FromRgb(239, 242, 255));
    public static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(167, 179, 205));
    public static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(127, 181, 255));
    public static readonly Brush Panel = new SolidColorBrush(Color.FromRgb(30, 36, 54));
    public static TextBlock Label(string text, double size = 14, Brush? color = null) => new()
    {
        Text = text, FontSize = size, Foreground = color ?? Text, FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI"),
        VerticalAlignment = VerticalAlignment.Center
    };
    public static Button Button(string text, RoutedEventHandler action, bool primary = false)
    {
        var button = new Button
        {
            Content = text, Padding = new Thickness(17, 9, 17, 9), Margin = new Thickness(4), Cursor = System.Windows.Input.Cursors.Hand,
            Foreground = primary ? new SolidColorBrush(Color.FromRgb(13, 26, 49)) : Text,
            Background = primary ? Accent : new SolidColorBrush(Color.FromRgb(47, 57, 79)), BorderThickness = new Thickness(0),
            FontSize = 14, MinHeight = 38
        };
        var factory = new FrameworkElementFactory(typeof(Border));
        factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        factory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        factory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        factory.AppendChild(content);
        button.Template = new ControlTemplate(typeof(Button)) { VisualTree = factory };
        button.MouseEnter += (_, _) => button.Opacity = .8;
        button.MouseLeave += (_, _) => button.Opacity = 1;
        button.Click += action;
        return button;
    }
    public static TextBox Input(string text = "") => new()
    {
        Text = text, Foreground = Text, Background = new SolidColorBrush(Color.FromRgb(22, 28, 44)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(71, 86, 117)), BorderThickness = new Thickness(1),
        Padding = new Thickness(12, 9, 12, 9), FontSize = 14, CaretBrush = Text, SelectionBrush = new SolidColorBrush(Color.FromRgb(59, 103, 165)),
        VerticalContentAlignment = VerticalAlignment.Center
    };
    public static void Animate(Animatable target, DependencyProperty property, double value, double ms = 240, Action? done = null)
    {
        var animation = new DoubleAnimation(value, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        if (done != null) animation.Completed += (_, _) => done();
        target.BeginAnimation(property, animation);
    }
    public static void Animate(UIElement target, DependencyProperty property, double value, double ms = 240, Action? done = null)
    {
        var animation = new DoubleAnimation(value, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        if (done != null) animation.Completed += (_, _) => done();
        target.BeginAnimation(property, animation);
    }
    public static Window Dialog(Window owner, string title, double width = 560, double height = 460) => new()
    {
        Owner = owner, Title = title, Width = width, Height = height, MinWidth = width, MinHeight = Math.Min(height, 400),
        WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Panel, Foreground = Text,
        FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI"), ShowInTaskbar = false, ResizeMode = ResizeMode.CanResizeWithGrip
    };
}

internal sealed class IconTile : Border
{
    public Entry Entry { get; }
    public Point Position { get; set; }
    private readonly TranslateTransform _move = new();
    private readonly Border _face;
    private readonly ScaleTransform _scale = new(1, 1);
    public double IconSize { get; }
    public IconTile(Entry entry, double iconSize)
    {
        Entry = entry; IconSize = iconSize;
        Width = iconSize + 54; Height = iconSize + 58; Background = Brushes.Transparent;
        RenderTransform = _move; Cursor = System.Windows.Input.Cursors.Hand; ToolTip = entry.IsFolder ? entry.Name + " · " + entry.Children.Count + " 個項目" : entry.Name + "\n" + entry.Target;
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        _face = new Border { Width = iconSize, Height = iconSize, CornerRadius = new CornerRadius(iconSize * .23), RenderTransform = _scale, RenderTransformOrigin = new Point(.5, .5), BorderThickness = new Thickness(2) };
        if (entry.IsFolder)
        {
            _face.Background = new SolidColorBrush(Color.FromArgb(155, 164, 177, 207));
            var preview = new System.Windows.Controls.Primitives.UniformGrid { Rows = 3, Columns = 3, Margin = new Thickness(9) };
            foreach (var child in entry.Children.Take(9)) preview.Children.Add(Visual(child, (iconSize - 24) / 3));
            for (int i = preview.Children.Count; i < 9; i++) preview.Children.Add(new Border());
            _face.Child = preview;
        }
        else _face.Child = Visual(entry, iconSize - 4);
        stack.Children.Add(_face);
        var label = Theme.Label(entry.Name, 12.5); label.TextAlignment = TextAlignment.Center;
        label.TextWrapping = TextWrapping.Wrap; label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.MaxHeight = 36; label.MaxWidth = Width - 8; label.Margin = new Thickness(0, 9, 0, 0);
        label.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 5, ShadowDepth = 1, Opacity = .8 };
        stack.Children.Add(label); Child = stack;
        MouseEnter += (_, _) => { if (System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Released) { Theme.Animate(_scale, ScaleTransform.ScaleXProperty, 1.045, 130); Theme.Animate(_scale, ScaleTransform.ScaleYProperty, 1.045, 130); } };
        MouseLeave += (_, _) => { Theme.Animate(_scale, ScaleTransform.ScaleXProperty, 1, 130); Theme.Animate(_scale, ScaleTransform.ScaleYProperty, 1, 130); };
    }
    private static UIElement Visual(Entry entry, double size)
    {
        var image = Native.Icon(string.IsNullOrEmpty(entry.IconPath) ? entry.Target : entry.IconPath);
        if (image != null) return new Image { Source = image, Width = size, Height = size, Stretch = Stretch.Uniform, IsHitTestVisible = false, SnapsToDevicePixels = true };
        int hash = entry.Name.Aggregate(0, (n, c) => unchecked(n * 31 + c));
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size * .23),
            Background = new SolidColorBrush(Color.FromRgb((byte)(65 + (hash & 63)), (byte)(90 + ((hash >> 6) & 63)), (byte)(145 + ((hash >> 12) & 63)))),
            Child = new TextBlock { Text = entry.Name.Length > 0 ? entry.Name.Substring(0, 1).ToUpperInvariant() : "?", FontSize = size * .48, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
    }
    public void MoveTo(Point point, bool animate, double ms)
    {
        double x = Canvas.GetLeft(this), y = Canvas.GetTop(this);
        if (double.IsNaN(x)) x = point.X; if (double.IsNaN(y)) y = point.Y;
        double oldX = x + _move.X, oldY = y + _move.Y;
        _move.BeginAnimation(TranslateTransform.XProperty, null); _move.BeginAnimation(TranslateTransform.YProperty, null);
        Position = point; Canvas.SetLeft(this, point.X); Canvas.SetTop(this, point.Y);
        _move.X = animate ? oldX - point.X : 0; _move.Y = animate ? oldY - point.Y : 0;
        if (animate) { Theme.Animate(_move, TranslateTransform.XProperty, 0, ms); Theme.Animate(_move, TranslateTransform.YProperty, 0, ms); }
    }
    public void MergeHint(bool hovering, bool armed)
    {
        _face.Opacity = hovering ? .57 : 1;
        _face.BorderBrush = armed ? Theme.Accent : Brushes.Transparent;
    }
}
