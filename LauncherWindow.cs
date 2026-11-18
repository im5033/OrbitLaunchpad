using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Orbit;

public sealed partial class LauncherWindow : Window
{
    private readonly Store _store;
    private readonly FrameMeter _meter;
    private Preferences Prefs => _store.Library.Preferences;
    private readonly Grid _surface = new();
    private readonly Grid _scene = new() { Background = Brushes.Transparent };
    private readonly Canvas _canvas = new() { Background = Brushes.Transparent };
    private readonly Canvas _folderLayer = new();
    private readonly Canvas _dragLayer = new() { IsHitTestVisible = false };
    private readonly Border _shade = new() { Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), Visibility = Visibility.Collapsed };
    private readonly TextBlock _status = Theme.Label("", 12, Theme.Muted);
    private readonly StackPanel _pager = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Dictionary<string, IconTile> _tiles = new();
    private readonly Dictionary<string, Point> _slots = new();
    private readonly Dictionary<string, Point> _rootSlots = new();
    private readonly Dictionary<string, (string Signature, IconTile Tile)> _tileCache = new();
    private string _rootViewKey = "";
    private bool _ready, _layoutDirty = true;
    private Point _pendingDragPoint, _lastDragPoint;
    private Entry? _folder;
    private Border? _folderPanel;
    private TextBox? _folderTitle;
    private Canvas? _folderItems;
    private Point _folderOrigin;
    private ScaleTransform? _folderScale;
    private TranslateTransform? _folderTranslate;
    private int _page, _folderPage;
    private bool _transitioning, _quitting, _loaded, _hiding;
    private bool _modal;
    private IconTile? _pressed, _ghost;
    private Point _pressPoint, _dragOffset;
    private bool _dragging;
    private string? _hoverId;
    private DateTime _hoverSince, _lastEdge;
    private bool _mergeArmed, _swapPreviewed;
    private ContextMenu? _openMenu;
    private readonly MenuDismissGesture _menuGesture = new();
    private readonly DispatcherTimer _dragTimer;
    private System.Windows.Forms.NotifyIcon? _tray;
    private IntPtr _hwnd;
    private bool _hotkeyRegistered;
    private HotkeyReceiver? _hotkeyReceiver;
    private ShortcutFallback? _shortcutFallback;
    private readonly ShortcutPulseGate _shortcutGate = new();
    private string _hotkeyError = "";
    private int _hotkeyId = 74;
    private uint _registeredModifiers, _registeredKey;
    private List<Entry> ActiveItems => _folder?.Children ?? _store.Library.Items;
    private int Capacity => Prefs.Columns * Prefs.Rows;

    public LauncherWindow(Store store)
    {
        _store = store;
        _meter = new FrameMeter(store.DirectoryPath);
        Title = "Orbit Launchpad"; Background = new SolidColorBrush(Color.FromRgb(10, 13, 27));
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = true;
        Topmost = true; AllowsTransparency = false;
        // Keep the same layered-window mode for the whole lifetime, including when hidden.
        // Crossing exactly 0/1 makes WPF switch native window styles during a transition.
        Opacity = .001;
        FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI"); Foreground = Theme.Text;
        Icon = Native.Icon(StartupRegistration.Executable);
        Content = _surface;
        RenderOptions.SetBitmapScalingMode(_surface, BitmapScalingMode.LowQuality);
        BuildBackground();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
        _surface.Children.Add(_scene); _scene.Children.Add(_canvas);
        BuildChrome();
        _surface.Children.Add(_shade); _surface.Children.Add(_folderLayer); _surface.Children.Add(_dragLayer);
        _shade.MouseLeftButtonUp += (_, e) => { e.Handled = true; CloseFolder(); };
        _scene.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource == _scene || e.OriginalSource == _canvas) { e.Handled = true; DismissLevel(); }
        };
        _canvas.ContextMenu = RootMenu();
        _scene.ContextMenu = RootMenu();
        AllowDrop = true;
        PreviewDragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddPaths(paths);
            e.Handled = true;
        };
        PreviewMouseMove += MouseMoved;
        PreviewMouseLeftButtonDown += (_, e) => { if (_menuGesture.AwaitingRelease) { _pressed = null; e.Handled = true; } };
        PreviewMouseLeftButtonUp += MouseReleased;
        InputManager.Current.PreProcessInput += BeforeInput;
        LostMouseCapture += (_, _) => { if (_dragging) CancelDrag(); };
        PreviewKeyDown += HandleKey;
        MouseWheel += (_, e) => { if (!_dragging && !_modal) { ChangePage(e.Delta < 0 ? 1 : -1); e.Handled = true; } };
        SizeChanged += (_, _) => { _layoutDirty = true; if (_ready && IsVisible && !_transitioning && !_hiding) { CancelDrag(); Refresh(false); } };
        SourceInitialized += (_, _) => _hwnd = new WindowInteropHelper(this).Handle;
        Closing += (_, e) => { if (!_quitting) { e.Cancel = true; HideLauncher(); } };
        Deactivated += (_, _) => { if (_dragging) CancelDrag(); };
        _dragTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(35) };
        _dragTimer.Tick += (_, _) => UpdateDragIntent();
    }

    private void BuildBackground()
    {
        var backdrop = Backdrop.Create(Native.Wallpaper());
        _backdropImage = backdrop;
        if (backdrop != null)
        {
            _surface.Children.Add(new Image { Source = backdrop, Stretch = Stretch.UniformToFill, IsHitTestVisible = false });
            return;
        }
        _surface.Children.Add(new Border
        {
            IsHitTestVisible = false,
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(170, 10, 14, 32), 0), new(Color.FromArgb(165, 27, 23, 60), .55), new(Color.FromArgb(210, 13, 19, 35), 1)
            }, new Point(0, 0), new Point(1, 1))
        });
    }
    private void BuildChrome()
    {
        _pager.VerticalAlignment = VerticalAlignment.Bottom;
        _scene.Children.Add(_pager);
        _status.HorizontalAlignment = HorizontalAlignment.Center; _status.VerticalAlignment = VerticalAlignment.Bottom;
        _status.Margin = new Thickness(20, 0, 20, 45); _status.Visibility = Visibility.Collapsed; _scene.Children.Add(_status);
    }
    internal void Start(bool background)
    {
        _hotkeyReceiver = new HotkeyReceiver();
        _hotkeyReceiver.Pressed += id => Dispatcher.BeginInvoke(() =>
        {
            if (id != _hotkeyId) return;
            ShortcutPressed("registered");
        }, DispatcherPriority.Send);
        _shortcutFallback = new ShortcutFallback();
        _shortcutFallback.Pressed += () => Dispatcher.BeginInvoke(() => ShortcutPressed("backup"), DispatcherPriority.Send);
        ActivationLog("backup receiver available=" + _shortcutFallback.Available);
        bool hotkeyOk = ConfigureHotkey(Prefs.HotkeyEnabled, Prefs.HotkeyModifiers, Prefs.HotkeyVirtualKey, out string hotkeyError);
        _tray = new System.Windows.Forms.NotifyIcon { Visible = true, Text = "Orbit Launchpad", Icon = System.Drawing.Icon.ExtractAssociatedIcon(StartupRegistration.Executable) ?? System.Drawing.SystemIcons.Application };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(Toggle);
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("開啟 / 收起", null, (_, _) => Dispatcher.Invoke(Toggle));
        menu.Items.Add("設定…", null, async (_, _) =>
        {
            if (_modal || _transitioning || _hiding) return;
            if (!IsVisible) await OpenLauncher();
            if (IsVisible && !_quitting) ShowSettings();
        });
        menu.Items.Add("結束 Orbit", null, (_, _) => Dispatcher.Invoke(Quit));
        _tray.ContextMenuStrip = menu;
        UpdateTrayText();
        if (!hotkeyOk) Dispatcher.BeginInvoke(() => { Toast(hotkeyError + " 可透過系統匣開啟設定。"); _tray.ShowBalloonTip(10000, "Orbit 快捷鍵未啟用", hotkeyError, System.Windows.Forms.ToolTipIcon.Warning); });
        if (background) ActivationLog($"background-ready visible={IsVisible} visual-handle-created={_hwnd != IntPtr.Zero}");
        else Toggle();
    }
    private bool ConfigureHotkey(bool enabled, uint modifiers, uint key, out string error)
    {
        error = "";
        if (!enabled)
        {
            if (_hotkeyRegistered) Native.UnregisterHotKey(_hotkeyReceiver!.Handle, _hotkeyId);
            _shortcutFallback?.Configure(false, modifiers, key);
            _hotkeyRegistered = false; _hotkeyError = ""; return true;
        }
        if (!Hotkeys.Valid(modifiers, key)) { error = "請選擇有效的主按鍵。"; return false; }
        if (_hotkeyRegistered && modifiers == _registeredModifiers && key == _registeredKey) return true;
        int nextId = _hotkeyId == 74 ? 75 : 74;
        if (!Native.RegisterHotKey(_hotkeyReceiver!.Handle, nextId, modifiers | 0x4000, key))
        {
            int code = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            error = "無法使用「" + Hotkeys.Describe(modifiers, key) + "」；可能已被其他程式使用，或是 Windows 保留組合（錯誤 " + code + "）。請換一組快捷鍵。";
            if (!_hotkeyRegistered) _hotkeyError = error;
            ActivationLog("registration failed " + error);
            return false;
        }
        if (_hotkeyRegistered) Native.UnregisterHotKey(_hotkeyReceiver.Handle, _hotkeyId);
        _hotkeyId = nextId; _hotkeyRegistered = true; _registeredModifiers = modifiers; _registeredKey = key; _hotkeyError = "";
        _shortcutFallback?.Configure(true, modifiers, key);
        ActivationLog("registered " + Hotkeys.Describe(modifiers, key)); return true;
    }
    private void ActivationLog(string text) { try { File.AppendAllText(Path.Combine(_store.DirectoryPath, "activation.log"), DateTime.Now.ToString("O") + " " + text + Environment.NewLine); } catch { } }
    private void ShortcutPressed(string source)
    {
        if (!_hotkeyRegistered || !Prefs.HotkeyEnabled || !_shortcutGate.Accept(Environment.TickCount64)) return;
        ActivationLog("received " + source + " " + Hotkeys.Describe(_registeredModifiers, _registeredKey) + $" visible={IsVisible} ready={_ready} modal={_modal} transition={_transitioning} hiding={_hiding}");
        Toggle();
    }
    private void UpdateTrayText()
    {
        if (_tray != null) _tray.Text = "Orbit Launchpad · " + (Prefs.HotkeyEnabled ? (_hotkeyRegistered ? Hotkeys.Describe(Prefs.HotkeyModifiers, Prefs.HotkeyVirtualKey) : "快捷鍵註冊失敗，請開啟設定") : "快捷鍵已關閉");
    }
    public async void Toggle()
    {
        if (_quitting || _modal || _transitioning || _hiding) return;
        if (IsVisible) HideLauncher();
        else await OpenLauncher();
    }
    private async Task OpenLauncher()
    {
        _transitioning = true;
        _meter.Begin(_ready ? "open" : "initial-open");
        try
        {
            if (!_ready)
            {
                if (!_store.Library.Initialized)
                {
                    var shortcuts = await Task.Run(Native.ScanShortcuts);
                    if (_quitting) return;
                    _store.Library.Items.AddRange(shortcuts.Take(Capacity));
                    _store.Library.Initialized = true;
                    Save();
                }
                await Native.WarmIconsAsync(LayoutRules.All(_store.Library.Items));
                if (_quitting) return;
                _ready = true;
            }
            Opacity = .001;
            PositionOnScreen();
            Show();
            _loaded = true;
            if (_layoutDirty) { _scene.UpdateLayout(); Refresh(false); }
            await AnimateOpen();
            Keyboard.Focus(_scene);
            if (_store.LoadWarning != null) Toast(_store.LoadWarning);
        }
        finally
        {
            _transitioning = false;
            _meter.End();
        }
    }
    private static async Task PrepareVisual(FrameworkElement visual)
    {
        // Complete layout before starting clocks. Static image and tile-object caches
        // keep shell icon decoding and control construction out of the animation.
        visual.UpdateLayout();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? next = null;
        next = (_, _) => { CompositionTarget.Rendering -= next; ready.TrySetResult(); };
        CompositionTarget.Rendering += next;
        try { await Task.WhenAny(ready.Task, Task.Delay(120)); }
        finally { CompositionTarget.Rendering -= next; }
        await visual.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
    }
    private async Task AnimateOpen()
    {
        ShowFrostWindows();
        var scale = new ScaleTransform(1.04, 1.04); _scene.RenderTransform = scale; _scene.RenderTransformOrigin = new Point(.5, .5);
        _scene.BeginAnimation(OpacityProperty, null); _scene.Opacity = 1;
        await PrepareVisual(_scene);
        if (!IsVisible || _quitting) return;
        Native.Foreground(_hwnd); Activate();
        _meter.AnimationStarted();
        await FadeWindow(true, Prefs.AnimationMs);
        _scene.InvalidateVisual(); _canvas.InvalidateVisual();
    }
    private async void HideLauncher()
    {
        if (_modal || _hiding) return;
        if (_transitioning) return;
        _meter.Begin("close"); CancelDrag(); CloseFolder(true); _hiding = true;
        await PrepareVisual(_scene);
        if (_quitting) return;
        _meter.AnimationStarted();
        await FadeWindow(false, Prefs.AnimationMs * .85);
        HideFrostWindows(); Hide(); _hiding = false; _meter.End();
    }
    private async Task FadeWindow(bool opening, double duration)
    {
        var watch = Stopwatch.StartNew();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scale = _scene.RenderTransform as ScaleTransform;
        int frames = 0;
        EventHandler frame = (_, _) =>
        {
            double progress = Math.Clamp(watch.Elapsed.TotalMilliseconds / Math.Max(100, duration), 0, 1);
            double ease = opening ? 1 - Math.Pow(1 - progress, 3) : progress * progress;
            frames++; Opacity = .001 + .998 * (opening ? ease : 1 - ease);
            FadeFrostWindows();
            if (scale != null) { double size = opening ? 1 + .025 * (1 - ease) : 1 + .025 * ease; scale.ScaleX = size; scale.ScaleY = size; }
            if (progress >= 1 || _quitting) done.TrySetResult();
        };
        CompositionTarget.Rendering += frame;
        try { await Task.WhenAny(done.Task, Task.Delay((int)duration + 200)); }
        finally { CompositionTarget.Rendering -= frame; Opacity = opening ? .999 : .001; FadeFrostWindows(); ActivationLog($"whole-window fade opening={opening} frames={frames} opacity={Opacity} elapsed_ms={watch.ElapsedMilliseconds}"); }
    }
    private void Quit()
    {
        _quitting = true; Save(); _dragTimer.Stop();
        CloseFrostWindows();
        InputManager.Current.PreProcessInput -= BeforeInput;
        if (_hotkeyRegistered) Native.UnregisterHotKey(_hotkeyReceiver!.Handle, _hotkeyId);
        _hotkeyReceiver?.Dispose();
        _shortcutFallback?.Dispose();
        _tray?.Dispose();
        System.Windows.Application.Current.Shutdown();
    }
    private void DismissLevel() { if (_dragging || _modal || _transitioning) return; if (_folder != null) CloseFolder(); else HideLauncher(); }

    private void Refresh(bool animate)
    {
        if (_folder != null) { BuildFolderContents(); return; }
        var all = _store.Library.Items;
        _page = Math.Clamp(_page, 0, Math.Max(0, (all.Count - 1) / Capacity));
        var visible = all.Skip(_page * Capacity).Take(Capacity).ToList();
        string viewKey = $"{ActualWidth:F2}:{ActualHeight:F2}:{Prefs.Columns}:{Prefs.Rows}:{Prefs.IconSize}:{_page}:" + string.Join("|", visible.Select(EntrySignature));
        if (!_layoutDirty && viewKey == _rootViewKey)
        {
            _tiles.Clear(); _slots.Clear();
            foreach (var tile in _canvas.Children.OfType<IconTile>())
            {
                _tiles[tile.Entry.Id] = tile; _slots[tile.Entry.Id] = _rootSlots[tile.Entry.Id];
                tile.Opacity = 1; tile.MergeHint(false, false); tile.MoveTo(_rootSlots[tile.Entry.Id], animate, Prefs.AnimationMs);
            }
            _status.Text = all.Count + " 個項目 · 第 " + (_page + 1) + " 頁";
            return;
        }
        _rootViewKey = viewKey; _layoutDirty = false; _rootSlots.Clear();
        _tiles.Clear(); _slots.Clear(); _canvas.Children.Clear();
        var layout = GridLayout.Reference(ActualWidth, ActualHeight, Prefs);
        _pager.Margin = new Thickness(0, 0, 0, ActualHeight * .08);
        double icon = layout.IconSize;
        for (int i = 0; i < visible.Count; i++)
        {
            var tile = CreateTile(visible[i], icon); _canvas.Children.Add(tile); _tiles[tile.Entry.Id] = tile;
            Point point = layout.TilePosition(i, Prefs.Columns, tile.Width);
            _slots[tile.Entry.Id] = point; _rootSlots[tile.Entry.Id] = point; tile.MoveTo(point, animate, Prefs.AnimationMs);
        }
        if (all.Count == 0)
        {
            var empty = Theme.Label("把程式或捷徑拖進來，開始整理你的桌面。", 18, Theme.Muted);
            Canvas.SetLeft(empty, Math.Max(40, ActualWidth / 2 - 190)); Canvas.SetTop(empty, ActualHeight / 2 - 20); _canvas.Children.Add(empty);
        }
        BuildPager(_pager, all.Count, Capacity, _page, page => { _page = page; Refresh(false); });
        _status.Text = all.Count + " 個項目 · 第 " + (_page + 1) + " 頁";
    }
    private IconTile CreateTile(Entry entry, double icon)
    {
        string cacheKey = (_folder == null ? "root:" : "folder:") + entry.Id + ":" + icon.ToString("F2");
        string signature = EntrySignature(entry);
        if (_tileCache.TryGetValue(cacheKey, out var cached) && cached.Signature == signature)
        {
            var reused = cached.Tile;
            if (reused.Parent is Panel parent) parent.Children.Remove(reused);
            reused.Opacity = 1; reused.MergeHint(false, false); reused.ContextMenu = ItemMenu(entry);
            return reused;
        }
        var tile = new IconTile(entry, icon);
        tile.MouseLeftButtonDown += (_, e) =>
        {
            if (_transitioning || _modal || _hiding) return;
            _pressed = tile; _pressPoint = e.GetPosition(_surface); _dragOffset = e.GetPosition(tile); e.Handled = true;
        };
        tile.ContextMenu = ItemMenu(entry);
        tile.ContextMenuOpening += (_, _) => { _pressed = null; if (_dragging) CancelDrag(); };
        _tileCache[cacheKey] = (signature, tile);
        return tile;
    }
    private static string EntrySignature(Entry entry) => entry.Id + ":" + entry.Name + ":" + entry.Target + ":" + entry.IconPath + ":" + entry.Children.Count + ":" + string.Join(",", entry.Children.Take(9).Select(x => x.Id + x.Name + x.IconPath + x.Target));
    private void BuildPager(StackPanel panel, int count, int capacity, int page, Action<int> change)
    {
        panel.Children.Clear(); int pages = Math.Max(1, (count + capacity - 1) / capacity);
        for (int i = Math.Max(0, page - 3); i < Math.Min(pages, page + 4); i++)
        {
            int index = i;
            var button = Theme.Button("●", (_, _) => change(index));
            button.ToolTip = "第 " + (i + 1) + " 頁"; button.Background = Brushes.Transparent;
            button.Foreground = i == page ? Brushes.White : new SolidColorBrush(Color.FromRgb(100, 106, 119));
            button.Padding = new Thickness(0); button.Margin = new Thickness(3, 0, 3, 0); button.Width = 14; button.MinHeight = 16; button.Height = 16; button.FontSize = 10;
            panel.Children.Add(button);
        }
    }
    private void ChangePage(int delta)
    {
        if (_transitioning) return;
        if (_folder != null)
        {
            int capacity = FolderCapacity();
            _folderPage = Math.Clamp(_folderPage + delta, 0, Math.Max(0, (_folder.Children.Count - 1) / capacity)); BuildFolderContents();
        }
        else { _page = Math.Clamp(_page + delta, 0, Math.Max(0, (_store.Library.Items.Count - 1) / Capacity)); Refresh(false); }
    }
    private int FolderCapacity() => Prefs.Columns * Math.Max(1, Prefs.Rows - 1);
    private async void OpenFolder(Entry folder, IconTile? origin)
    {
        if (_transitioning || _folder != null) return;
        _meter.Begin("folder-open");
        _folderOrigin = origin != null ? origin.TranslatePoint(new Point(origin.Width / 2, origin.IconSize / 2), _surface) : new Point(ActualWidth / 2, ActualHeight / 2);
        _folder = folder; _folderPage = 0; _transitioning = true;
        _shade.Visibility = Visibility.Visible; _shade.Opacity = 0; _folderLayer.Visibility = Visibility.Visible;
        BuildFolderContents();
        _folderScale = new ScaleTransform(.13, .24); _folderTranslate = new TranslateTransform();
        _folderTranslate.X = _folderOrigin.X - ActualWidth / 2;
        _folderTranslate.Y = _folderOrigin.Y - ActualHeight / 2;
        var group = new TransformGroup(); group.Children.Add(_folderScale); group.Children.Add(_folderTranslate);
        _folderPanel!.RenderTransformOrigin = new Point(.5, .5); _folderPanel.RenderTransform = group;
        _folderPanel.Opacity = .001;
        if (_folderTitle != null) _folderTitle.Opacity = .001;
        await PrepareVisual(_folderPanel);
        if (_folder != folder || _quitting) return;
        _meter.AnimationStarted();
        Theme.Animate(_canvas, OpacityProperty, .12, Prefs.AnimationMs);
        Theme.Animate(_shade, OpacityProperty, 1, Prefs.AnimationMs);
        Theme.Animate(_folderPanel, OpacityProperty, 1, Prefs.AnimationMs);
        if (_folderTitle != null) Theme.Animate(_folderTitle, OpacityProperty, 1, Prefs.AnimationMs);
        Theme.Animate(_folderScale, ScaleTransform.ScaleXProperty, 1, Prefs.AnimationMs);
        Theme.Animate(_folderScale, ScaleTransform.ScaleYProperty, 1, Prefs.AnimationMs);
        Theme.Animate(_folderTranslate, TranslateTransform.XProperty, 0, Prefs.AnimationMs);
        Theme.Animate(_folderTranslate, TranslateTransform.YProperty, 0, Prefs.AnimationMs, () => { _transitioning = false; _meter.End(); });
    }
    private void BuildFolderContents()
    {
        if (_folder == null) return;
        _folderLayer.Children.Clear(); _tiles.Clear(); _slots.Clear();
        var rootLayout = GridLayout.Reference(ActualWidth, ActualHeight, Prefs);
        double width = ActualWidth * .855, left = (ActualWidth - width) / 2;
        double cellW = rootLayout.CellWidth, cellH = rootLayout.CellHeight;
        double icon = rootLayout.IconSize * .8;
        int capacity = FolderCapacity();
        _folderPage = Math.Clamp(_folderPage, 0, Math.Max(0, (_folder.Children.Count - 1) / capacity));
        var visible = _folder.Children.Skip(_folderPage * capacity).Take(capacity).ToList();
        int rows = Math.Max(1, (visible.Count + Prefs.Columns - 1) / Prefs.Columns);
        double padding = ActualHeight / 12;
        double height = Math.Min(ActualHeight - 100, rows * cellH + padding);
        double top = (ActualHeight - height) / 2;
        _folderPanel = new Border
        {
            Width = width, Height = height, CornerRadius = new CornerRadius(22),
            Background = new SolidColorBrush(Color.FromArgb(195, 151, 148, 159)), BorderThickness = new Thickness(0)
        };
        Canvas.SetLeft(_folderPanel, left); Canvas.SetTop(_folderPanel, top);
        var contents = new Grid(); _folderPanel.Child = contents;
        var title = Theme.Input(_folder.Name); title.Width = 300; title.HorizontalContentAlignment = HorizontalAlignment.Center;
        title.Height = 34; title.Padding = new Thickness(0); title.FontSize = 20; title.BorderThickness = new Thickness(0); title.Background = Brushes.Transparent;
        Canvas.SetLeft(title, ActualWidth / 2 - title.Width / 2); Canvas.SetTop(title, top - 39);
        title.ToolTip = "點一下即可重新命名，Enter 儲存";
        var currentFolder = _folder;
        void CommitName() { string name = title.Text.Trim(); if (name.Length > 0 && name != currentFolder.Name) { currentFolder.Name = name; Save(); } title.Text = currentFolder.Name; }
        title.LostKeyboardFocus += (_, _) => CommitName();
        title.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitName(); Keyboard.ClearFocus(); e.Handled = true; } else if (e.Key == Key.Escape) { title.Text = currentFolder.Name; Keyboard.ClearFocus(); e.Handled = true; } };
        _folderTitle = title; _folderLayer.Children.Add(title);
        _folderItems = new Canvas { Background = Brushes.Transparent, Margin = new Thickness(0, padding / 2, 0, padding / 2) };
        contents.Children.Add(_folderItems);
        for (int i = 0; i < visible.Count; i++)
        {
            var tile = CreateTile(visible[i], icon); _folderItems.Children.Add(tile); _tiles[tile.Entry.Id] = tile;
            var point = new Point(rootLayout.Left - left + cellW * (i % Prefs.Columns + .5) - tile.Width / 2, cellH * (i / Prefs.Columns) + Math.Max(0, (cellH - tile.Height) / 2));
            _slots[tile.Entry.Id] = point; tile.MoveTo(point, false, 0);
        }
        if (visible.Count == 0)
        {
            var hint = Theme.Label("此資料夾目前是空的 · 可將程式拖放進來", 15, Theme.Muted);
            Canvas.SetLeft(hint, Math.Max(0, width / 2 - 210)); Canvas.SetTop(hint, 30); _folderItems.Children.Add(hint);
        }
        if (_folder.Children.Count > capacity)
        {
            var pager = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 10) };
            BuildPager(pager, _folder.Children.Count, capacity, _folderPage, page => { _folderPage = page; BuildFolderContents(); }); contents.Children.Add(pager);
        }
        _folderPanel.MouseLeftButtonUp += (_, e) => e.Handled = true;
        _folderLayer.Children.Add(_folderPanel);
        _status.Text = "點資料夾名稱可改名 · 點外側空白處返回";
    }
    private async void CloseFolder(bool immediately = false)
    {
        if (_folder == null) return;
        if (_transitioning && !immediately) return;
        void Finish()
        {
            _folder = null; _folderPanel = null; _folderItems = null; _folderTitle = null; _folderLayer.Children.Clear();
            _shade.Visibility = Visibility.Collapsed; _transitioning = false;
            _canvas.BeginAnimation(OpacityProperty, null); _canvas.Opacity = 1;
            Refresh(false);
            if (!immediately) _meter.End();
        }
        if (immediately || _folderScale == null || _folderTranslate == null) { Finish(); return; }
        _meter.Begin("folder-close"); _transitioning = true;
        await PrepareVisual(_folderPanel!);
        if (_folder == null || _quitting) return;
        if (_folderPanel!.RenderTransform is not TransformGroup)
        {
            _folderScale = new ScaleTransform(1, 1); _folderTranslate = new TranslateTransform();
            var transforms = new TransformGroup(); transforms.Children.Add(_folderScale); transforms.Children.Add(_folderTranslate);
            _folderPanel.RenderTransformOrigin = new Point(.5, .5); _folderPanel.RenderTransform = transforms;
        }
        _transitioning = true;
        _meter.AnimationStarted();
        Theme.Animate(_canvas, OpacityProperty, 1, Prefs.AnimationMs);
        Theme.Animate(_shade, OpacityProperty, 0, Prefs.AnimationMs);
        Theme.Animate(_folderPanel!, OpacityProperty, 0, Prefs.AnimationMs);
        if (_folderTitle != null) Theme.Animate(_folderTitle, OpacityProperty, 0, Prefs.AnimationMs);
        Theme.Animate(_folderScale, ScaleTransform.ScaleXProperty, .13, Prefs.AnimationMs);
        Theme.Animate(_folderScale, ScaleTransform.ScaleYProperty, .24, Prefs.AnimationMs);
        Theme.Animate(_folderTranslate, TranslateTransform.XProperty, _folderOrigin.X - ActualWidth / 2, Prefs.AnimationMs);
        Theme.Animate(_folderTranslate, TranslateTransform.YProperty, _folderOrigin.Y - ActualHeight / 2, Prefs.AnimationMs, Finish);
    }

    private void MouseMoved(object sender, MouseEventArgs e)
    {
        if (_pressed == null || e.LeftButton != MouseButtonState.Pressed || _transitioning) return;
        Point position = e.GetPosition(_surface);
        if (!_dragging)
        {
            if ((position - _pressPoint).Length < DragPolicy.StartDistance) return;
            _dragging = true;
            _ghost = new IconTile(_pressed.Entry, _pressed.IconSize) { Opacity = .94, IsHitTestVisible = false };
            _dragLayer.Children.Add(_ghost); _pressed.Opacity = .16;
            _lastDragPoint = new Point(double.NaN, double.NaN);
            _meter.Begin("drag"); _meter.AnimationStarted();
            CompositionTarget.Rendering += RenderDrag;
            Mouse.Capture(this); _dragTimer.Start();
        }
        _pendingDragPoint = position;
    }
    private void RenderDrag(object? sender, EventArgs e)
    {
        if (!_dragging || _ghost == null || _pendingDragPoint == _lastDragPoint) return;
        _lastDragPoint = _pendingDragPoint;
        _ghost.Follow(new Point(_pendingDragPoint.X - _dragOffset.X, _pendingDragPoint.Y - _dragOffset.Y));
        UpdateDragTarget(_pendingDragPoint);
    }
    private void UpdateDragTarget(Point position)
    {
        if (_pressed == null || !_dragging) return;
        Point iconCenter = DragPolicy.IconCenter(position, _dragOffset, _pressed.Width, _pressed.IconSize);
        var candidates = new List<DragTarget>();
        foreach (var tile in _tiles.Values.Where(t => t.Entry.Id != _pressed.Entry.Id))
        {
            var parent = (FrameworkElement)tile.Parent;
            Point slot = _slots[tile.Entry.Id];
            Point center = parent.TranslatePoint(new Point(slot.X + tile.Width / 2, slot.Y + tile.IconSize / 2), _surface);
            candidates.Add(new DragTarget(tile.Entry.Id, center, tile.IconSize));
        }
        var hit = DragPolicy.Hit(iconCenter, candidates, !_pressed.Entry.IsFolder && _folder == null, _mergeArmed ? _hoverId : null);
        if (hit.Action == DragAction.Group)
        {
            if (_hoverId != hit.Id || !_mergeArmed)
            {
                ClearHover(); _hoverId = hit.Id; _hoverSince = DateTime.UtcNow; _mergeArmed = true;
            }
            if (_tiles.TryGetValue(hit.Id!, out var target))
            {
                target.MergeHint(true, true);
                _status.Text = target.Entry.IsFolder ? "放開以加入「" + target.Entry.Name + "」" : "放開以建立資料夾";
            }
        }
        else if (hit.Action == DragAction.Swap)
        {
            if (_hoverId != hit.Id || _mergeArmed)
            {
                ClearHover(); _hoverId = hit.Id; _hoverSince = DateTime.UtcNow;
                _status.Text = "停留片刻後讓位 · 放開交換位置";
            }
        }
        else ClearHover();
        if ((position.X < 24 || position.X > ActualWidth - 24) && DateTime.UtcNow - _lastEdge > TimeSpan.FromMilliseconds(850))
        {
            _lastEdge = DateTime.UtcNow;
            ChangePage(position.X < 24 ? -1 : 1);
            if (_tiles.TryGetValue(_pressed.Entry.Id, out var source)) source.Opacity = .16;
        }
    }
    private void PreviewSwap(Entry target)
    {
        if (_pressed == null) return;
        if (_swapPreviewed) return;
        int from = ActiveItems.FindIndex(x => x.Id == _pressed.Entry.Id), to = ActiveItems.IndexOf(target);
        if (from < 0 || to < 0) return;
        // Preview uses the proposed slots. The model is committed only on mouse-up.
        var preview = ActiveItems.ToList(); (preview[from], preview[to]) = (preview[to], preview[from]);
        RelayoutPreview(preview);
        _swapPreviewed = true;
        _status.Text = "放開以交換位置 · Esc 取消拖曳";
    }
    private void RelayoutPreview(List<Entry> preview)
    {
        int capacity = _folder != null ? FolderCapacity() : Capacity;
        int page = _folder != null ? _folderPage : _page;
        var originalPage = ActiveItems.Skip(page * capacity).Take(capacity).ToList();
        var positions = originalPage.Select(x => _slots.TryGetValue(x.Id, out var p) ? p : new Point()).ToList();
        var visible = preview.Skip(page * capacity).Take(capacity).ToList();
        for (int i = 0; i < visible.Count && i < positions.Count; i++)
            if (_tiles.TryGetValue(visible[i].Id, out var tile) && tile != _pressed) tile.MoveTo(positions[i], true, Prefs.AnimationMs * .7);
    }
    private void UpdateDragIntent()
    {
        if (!_dragging || _hoverId == null || _pressed == null || _mergeArmed || _swapPreviewed) return;
        if (DateTime.UtcNow - _hoverSince >= TimeSpan.FromMilliseconds(Prefs.ReorderDelayMs) && _tiles.TryGetValue(_hoverId, out var target)) PreviewSwap(target.Entry);
    }
    private void ClearHover()
    {
        if (_hoverId == null) return;
        if (_tiles.TryGetValue(_hoverId, out var target)) target.MergeHint(false, false);
        RelayoutPreview(ActiveItems);
        _hoverId = null; _mergeArmed = false; _swapPreviewed = false;
    }
    private void MouseReleased(object sender, MouseButtonEventArgs e)
    {
        if (_menuGesture.ConsumeRelease()) { _pressed = null; e.Handled = true; return; }
        var tile = _pressed;
        if (tile == null) return;
        e.Handled = true;
        if (!_dragging)
        {
            _pressed = null;
            if (tile.Entry.IsFolder) OpenFolder(tile.Entry, tile);
            else Launch(tile.Entry);
            return;
        }
        Point position = e.GetPosition(_surface);
        UpdateDragTarget(position);
        string? target = _hoverId; bool merge = _mergeArmed;
        // A darkened target always means grouping; the same visible feedback and drop action agree.
        if (target != null)
        {
            if (merge && _folder == null) LayoutRules.Group(ActiveItems, tile.Entry.Id, target);
            else LayoutRules.Swap(ActiveItems, tile.Entry.Id, target);
        }
        else if (_folder != null && _folderPanel != null)
        {
            Point panelPosition = _folderPanel.TranslatePoint(new Point(), _surface);
            if (!new Rect(panelPosition, new Size(_folderPanel.Width, _folderPanel.Height)).Contains(position))
            {
                _folder.Children.Remove(tile.Entry); _store.Library.Items.Add(tile.Entry); EndDrag(); CloseFolder(true); Save(); return;
            }
        }
        else if (_folder == null)
        {
            var layout = GridLayout.Reference(ActualWidth, ActualHeight, Prefs);
            int index = layout.IndexAt(position, Prefs.Columns, _page, Capacity);
            var items = ActiveItems;
            if (layout.Contains(position, Prefs.Columns, Prefs.Rows) && index >= items.Count)
            {
                items.Remove(tile.Entry); items.Add(tile.Entry);
            }
        }
        EndDrag(); Save(); Refresh(false);
    }
    private void EndDrag()
    {
        bool wasDragging = _dragging;
        CompositionTarget.Rendering -= RenderDrag;
        _dragging = false; _dragTimer.Stop(); _pressed = null; _ghost = null;
        _hoverId = null; _mergeArmed = false; _swapPreviewed = false; _dragLayer.Children.Clear();
        if (Mouse.Captured == this) Mouse.Capture(null);
        if (wasDragging) _meter.End();
    }
    private void CancelDrag()
    {
        bool wasDragging = _dragging;
        EndDrag();
        if (wasDragging && _loaded) Refresh(false);
    }
    private void HandleKey(object sender, KeyEventArgs e)
    {
        if (_modal) return;
        if (e.Key == Key.Escape)
        {
            if (_openMenu?.IsOpen == true) _openMenu.IsOpen = false;
            else if (_dragging) CancelDrag(); else DismissLevel();
            e.Handled = true;
        }
        else if (Keyboard.FocusedElement is not TextBox && (e.Key == Key.Left || e.Key == Key.Right)) { ChangePage(e.Key == Key.Left ? -1 : 1); e.Handled = true; }
    }
    private void Launch(Entry entry, bool elevated = false)
    {
        try { Native.Open(entry, elevated); HideLauncher(); }
        catch (Exception ex) { Toast("無法啟動「" + entry.Name + "」：" + ex.Message); }
    }
    private void Save()
    {
        try { _store.Save(); }
        catch (Exception ex) { Toast("排列未能儲存：" + ex.Message); }
    }
    private async void Toast(string message)
    {
        _status.Text = message; _status.Visibility = Visibility.Visible;
        await Task.Delay(6500);
        if (!_dragging && _status.Text == message) _status.Visibility = Visibility.Collapsed;
    }
    private ContextMenu MakeMenu()
    {
        var menu = new ContextMenu { Background = Theme.Panel, Foreground = Theme.Text, BorderBrush = new SolidColorBrush(Color.FromRgb(71, 83, 111)), Padding = new Thickness(6), FontSize = 14 };
        menu.Opened += (_, _) => { _openMenu = menu; _menuGesture.MenuOpened(); _pressed = null; };
        menu.Closed += (_, _) =>
        {
            if (Mouse.LeftButton == MouseButtonState.Pressed) _menuGesture.DismissedWithLeftButton();
            if (ReferenceEquals(_openMenu, menu)) _openMenu = null;
        };
        return menu;
    }
    private void BeforeInput(object sender, PreProcessInputEventArgs e)
    {
        if (_openMenu?.IsOpen != true || e.StagingItem.Input is not MouseButtonEventArgs mouse || mouse.ChangedButton != MouseButton.Left || mouse.ButtonState != MouseButtonState.Pressed) return;
        Point point = mouse.GetPosition(_openMenu);
        if (new Rect(0, 0, _openMenu.ActualWidth, _openMenu.ActualHeight).Contains(point)) return;
        _menuGesture.DismissedWithLeftButton(); _pressed = null;
        _openMenu.IsOpen = false;
        mouse.Handled = true; e.Cancel();
    }
    private static MenuItem ActionItem(string title, Action action)
    {
        var item = new MenuItem { Header = title, Padding = new Thickness(13, 7, 18, 7) };
        item.Click += (_, _) => action(); return item;
    }
    private ContextMenu RootMenu()
    {
        var menu = MakeMenu();
        menu.Items.Add(ActionItem("添加圖示…", ShowAdd));
        menu.Items.Add(ActionItem("建立空資料夾", () =>
        {
            var folder = new Entry { IsFolder = true, Name = "未命名資料夾" };
            _store.Library.Items.Add(folder); _page = (_store.Library.Items.Count - 1) / Capacity; Save(); Refresh(false);
            OpenFolder(folder, _tiles.GetValueOrDefault(folder.Id));
        }));
        menu.Items.Add(ActionItem("按名稱排序", () => { _store.Library.Items = _store.Library.Items.OrderByDescending(x => x.IsFolder).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList(); _page = 0; Save(); Refresh(false); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(ActionItem("設定…", ShowSettings));
        menu.Items.Add(ActionItem("備份佈局…", () => BackupLayout(this)));
        menu.Items.Add(ActionItem("恢復佈局…", () => RestoreLayout(this)));
        menu.Items.Add(ActionItem("操作說明", ShowHelp));
        menu.Items.Add(new Separator());
        menu.Items.Add(ActionItem("收起啟動器", HideLauncher));
        menu.Items.Add(ActionItem("結束 Orbit", Quit));
        return menu;
    }
    private ContextMenu ItemMenu(Entry entry)
    {
        var menu = MakeMenu();
        menu.Items.Add(ActionItem("設定此圖示…", () => ShowEdit(entry)));
        if (!entry.IsFolder)
        {
            menu.Items.Add(ActionItem("以系統管理員執行", () => Launch(entry, true)));
            menu.Items.Add(ActionItem("開啟檔案所在位置", () => { try { Native.Reveal(entry); HideLauncher(); } catch (Exception ex) { Toast(ex.Message); } }));
        }
        if (_folder != null)
            menu.Items.Add(ActionItem("移出資料夾", () => { _folder.Children.Remove(entry); _store.Library.Items.Add(entry); Save(); Refresh(false); }));
        if (entry.IsFolder)
            menu.Items.Add(ActionItem("解散資料夾（保留圖示）", () =>
            {
                int index = _store.Library.Items.IndexOf(entry);
                if (index < 0) return;
                _store.Library.Items.RemoveAt(index); _store.Library.Items.InsertRange(index, entry.Children); Save(); Refresh(false);
            }));
        menu.Items.Add(new Separator());
        menu.Items.Add(ActionItem("從啟動器移除", () =>
        {
            if (Prefs.ConfirmRemoval)
            {
                _modal = true;
                var result = System.Windows.MessageBox.Show(this, "從啟動器移除「" + entry.Name + "」？\n" + (entry.IsFolder ? "資料夾內的圖示也會從啟動器移除。\n" : "") + "原本的程式、捷徑與檔案都會保留。", "移除圖示", MessageBoxButton.YesNo, MessageBoxImage.Question);
                _modal = false;
                if (result != MessageBoxResult.Yes) return;
            }
            RemoveEntry(_store.Library.Items, entry.Id); Save(); Refresh(false);
        }));
        return menu;
    }
    private static bool RemoveEntry(List<Entry> entries, string id)
    {
        int index = entries.FindIndex(x => x.Id == id);
        if (index >= 0) { entries.RemoveAt(index); return true; }
        return entries.Any(entry => RemoveEntry(entry.Children, id));
    }
    private void RunDialog(Window dialog)
    {
        CancelDrag(); _modal = true;
        try { dialog.Topmost = true; dialog.ShowDialog(); }
        finally { _modal = false; Activate(); }
    }
    private void AddPaths(IEnumerable<string> paths)
    {
        var existing = LayoutRules.All(_store.Library.Items).Select(x => x.Target).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int count = 0;
        foreach (var path in paths)
            if ((File.Exists(path) || Directory.Exists(path)) && existing.Add(path)) { ActiveItems.Add(Native.FromPath(path)); count++; }
        if (_folder == null && count > 0) _page = (_store.Library.Items.Count - 1) / Capacity;
        Save(); Refresh(false); Toast(count > 0 ? "已添加 " + count + " 個項目" : "這些項目已存在，或檔案無法讀取。");
    }
    private void ShowEdit(Entry entry)
    {
        var dialog = Theme.Dialog(this, "設定圖示 · Orbit", 610, entry.IsFolder ? 290 : 555);
        var outer = new StackPanel { Margin = new Thickness(26) };
        outer.Children.Add(Theme.Label(entry.IsFolder ? "資料夾設定" : "圖示設定", 23));
        TextBox AddField(string label, string value)
        {
            var caption = Theme.Label(label, 12, Theme.Muted); caption.Margin = new Thickness(0, 15, 0, 5); outer.Children.Add(caption);
            var box = Theme.Input(value); outer.Children.Add(box); return box;
        }
        var name = AddField("顯示名稱", entry.Name);
        TextBox? target = null, arguments = null, working = null, icon = null;
        if (!entry.IsFolder)
        {
            target = AddField("程式或捷徑路徑", entry.Target);
            arguments = AddField("啟動參數（選填）", entry.Arguments);
            working = AddField("工作目錄（選填）", entry.WorkingDirectory);
            icon = AddField("自訂圖示路徑（PNG / ICO，選填）", entry.IconPath);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(Theme.Button("取消", (_, _) => dialog.Close()));
        buttons.Children.Add(Theme.Button("儲存", (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) { name.Focus(); return; }
            if (target != null && string.IsNullOrWhiteSpace(target.Text)) { target.Focus(); return; }
            entry.Name = name.Text.Trim();
            if (!entry.IsFolder) { entry.Target = target!.Text.Trim(); entry.Arguments = arguments!.Text; entry.WorkingDirectory = working!.Text.Trim(); entry.IconPath = icon!.Text.Trim(); Native.ClearIconCache(); }
            Save(); dialog.Close(); Refresh(false);
        }, true));
        outer.Children.Add(buttons); dialog.Content = new ScrollViewer { Content = outer, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; RunDialog(dialog);
    }
    private void ShowAppearanceSettings()
    {
        var dialog = Theme.Dialog(this, "外觀與動畫 · Orbit", 550, 575);
        var outer = new StackPanel { Margin = new Thickness(26) };
        outer.Children.Add(Theme.Label("調整到順手的節奏", 23));
        Slider AddSlider(string label, double value, double minimum, double maximum, double tick, string suffix)
        {
            var caption = Theme.Label(label + "  " + value.ToString("0") + suffix, 13); caption.Margin = new Thickness(0, 20, 0, 7); outer.Children.Add(caption);
            var slider = new Slider { Minimum = minimum, Maximum = maximum, Value = value, TickFrequency = tick, IsSnapToTickEnabled = true };
            slider.ValueChanged += (_, _) => caption.Text = label + "  " + slider.Value.ToString("0") + suffix; outer.Children.Add(slider); return slider;
        }
        var columns = AddSlider("每列圖示", Prefs.Columns, 4, 10, 1, " 個");
        var rows = AddSlider("每頁列數", Prefs.Rows, 3, 6, 1, " 列");
        var size = AddSlider("圖示大小", Prefs.IconSize, 60, 160, 5, "");
        var animation = AddSlider("動畫時間", Prefs.AnimationMs, 100, 600, 20, " ms");
        var reorder = AddSlider("滑過多久後讓位", Prefs.ReorderDelayMs, 150, 700, 50, " ms");
        var explanation = Theme.Label("Ctrl+Alt+Space 喚出 · 設定與排列保存在程式旁的 data 資料夾", 11, Theme.Muted); explanation.Margin = new Thickness(0, 22, 0, 12); explanation.TextWrapping = TextWrapping.Wrap; outer.Children.Add(explanation);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Theme.Button("取消", (_, _) => dialog.Close()));
        buttons.Children.Add(Theme.Button("套用", (_, _) =>
        {
            Prefs.Columns = (int)columns.Value; Prefs.Rows = (int)rows.Value; Prefs.IconSize = size.Value; Prefs.AnimationMs = animation.Value; Prefs.ReorderDelayMs = (int)reorder.Value;
            CloseFolder(true); Save(); dialog.Close(); Refresh(false);
        }, true));
        outer.Children.Add(buttons); dialog.Content = new ScrollViewer { Content = outer, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; RunDialog(dialog);
    }
    private void ShowHelp()
    {
        var dialog = Theme.Dialog(this, "操作說明 · Orbit", 570, 445);
        var stack = new StackPanel { Margin = new Thickness(28) };
        stack.Children.Add(Theme.Label("像手機桌面一樣整理", 24));
        var text = Theme.Label("• 點一下圖示啟動程式，點資料夾展開。\n\n• 拖到另一圖示的邊緣，稍停會預覽換位；放開交換。\n\n• 圖示重疊後目標變暗，放開便會成組。\n\n• 資料夾名稱可直接編輯。把圖示拖到面板外可移出。\n\n• 右鍵選單開著時，點空白只關選單；再點才逐層返回。\n\n• 添加支援 exe、捷徑、檔案與一般資料夾。\n\n• Ctrl+Alt+Space 喚出或收起；系統匣可結束程式。", 14);
        text.TextWrapping = TextWrapping.Wrap; text.Margin = new Thickness(0, 20, 0, 18); stack.Children.Add(text);
        stack.Children.Add(Theme.Button("知道了", (_, _) => dialog.Close(), true)); dialog.Content = new ScrollViewer { Content = stack }; RunDialog(dialog);
    }
}
