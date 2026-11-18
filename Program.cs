using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Orbit;

public static class Program
{
    private static Mutex? _mutex;
    private static EventWaitHandle? _toggle;
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--self-test")) return SelfTests.Run(args.SkipWhile(a => a != "--self-test").Skip(1).FirstOrDefault());
        _mutex = new Mutex(true, "Local\\OrbitLaunchpad.v1", out bool first);
        _toggle = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\OrbitLaunchpad.Toggle.v1");
        if (!first) { if (!args.Contains("--background")) _toggle.Set(); return 0; }
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            try { Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "data")); File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "data", "errors.log"), DateTime.Now + " " + e.Exception + Environment.NewLine); } catch { }
            System.Windows.MessageBox.Show("操作未完成，請保留 data\\errors.log 供修正。\n\n" + e.Exception.Message, "Orbit Launchpad");
            e.Handled = true;
        };
        try
        {
            var store = new Store(args.Contains("--review") ? Path.Combine(AppContext.BaseDirectory, "review-data") : null);
            var window = new LauncherWindow(store, args.Contains("--background"));
            app.MainWindow = window;
            Task.Run(() => { while (true) { _toggle.WaitOne(); app.Dispatcher.BeginInvoke(window.Toggle); } });
            window.Show();
            app.Run();
            return 0;
        }
        catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "Orbit Launchpad 啟動失敗"); return 1; }
        finally { _toggle.Dispose(); _mutex.ReleaseMutex(); _mutex.Dispose(); }
    }
}

public static class SelfTests
{
    public static int Run(string? report)
    {
        var checks = new System.Collections.Generic.List<string>();
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add("PASS " + name); }
        try
        {
            Entry a = new() { Name = "A" }, b = new() { Name = "B" }, c = new() { Name = "C" };
            var items = new System.Collections.Generic.List<Entry> { a, b, c };
            Check(LayoutRules.Swap(items, a.Id, b.Id) && items[0] == b && items[1] == a, "dragging swaps exact slots without losing IDs");
            Check(!LayoutRules.Swap(items, a.Id, "missing"), "invalid drag leaves layout unchanged");
            var folder = LayoutRules.Group(items, a.Id, b.Id)!;
            Check(items.Count == 2 && items[0] == folder && folder.Children.SequenceEqual(new[] { b, a }), "grouping preserves target position and both entries");
            LayoutRules.Group(items, c.Id, folder.Id);
            Check(items.Count == 1 && folder.Children.Count == 3, "drop onto folder adds child");
            Check(LayoutRules.Group(items, folder.Id, folder.Id) == null, "self drop rejected");
            Check(LayoutRules.All(items).Select(x => x.Id).Distinct().Count() == 4, "no duplicate or lost identities after grouping");
            var parent = new Entry { IsFolder = true };
            items.Add(parent);
            Check(LayoutRules.Group(items, folder.Id, parent.Id) == null, "folder nesting rejected");
            var reference = GridLayout.Reference(2560, 1440, new Preferences { IconSize = 120, Columns = 7, Rows = 4 });
            Check(Math.Abs(reference.Left + reference.CellWidth * .5 - 380) < .01 && Math.Abs(reference.Top - 75) < .01 && Math.Abs(reference.CellWidth - 300) < .01 && Math.Abs(reference.CellHeight - 247.5) < .01 && Math.Abs(reference.IconSize - 150) < .01, "reference screenshot geometry: 7 columns, icon size, row pitch, and margins");
            var fifth = reference.TilePosition(11, 7, 204);
            Check(reference.IndexAt(new Point(fifth.X + 102, fifth.Y + 75), 7, 0, 28) == 11, "drag hit testing follows reference grid at scaled resolutions");
            var targets = new[] { new DragTarget("target", new Point(500, 200), 120) };
            var centered = DragPolicy.IconCenter(new Point(433, 160), new Point(20, 20), 174, 120);
            Check(DragPolicy.Hit(centered, targets, true, null).Action == DragAction.Group, "off-center grab groups by dragged icon center rather than cursor");
            Check(DragPolicy.Hit(new Point(565, 200), targets, true, null).Action == DragAction.Group, "visible overlap has a forgiving group target");
            Check(DragPolicy.Hit(new Point(592, 200), targets, true, "target").Action == DragAction.Group && DragPolicy.Hit(new Point(592, 200), targets, true, null).Action == DragAction.Swap, "overlap target stays selected through pointer jitter");
            Check(DragPolicy.Hit(new Point(650, 200), targets, true, "target").Action == DragAction.None, "moving away releases group target");
            Check(DragPolicy.Hit(new Point(500, 200), targets, false, null).Action == DragAction.Swap, "folder drag and folder contents cannot create nested groups");
            var gesture = new MenuDismissGesture(); gesture.MenuOpened(); gesture.DismissedWithLeftButton();
            Check(gesture.AwaitingRelease && gesture.ConsumeRelease(), "outside click is consumed when closing context menu");
            Check(!gesture.ConsumeRelease(), "next independent blank click can dismiss launcher normally");
            gesture.DismissedWithLeftButton(); gesture.MenuOpened();
            Check(!gesture.AwaitingRelease, "opening a new menu does not inherit an old click gesture");
            string testDirectory = Path.Combine(AppContext.BaseDirectory, "test-data-" + Guid.NewGuid().ToString("N"));
            var store = new Store(testDirectory);
            store.Library.Items = items; store.Library.Initialized = true; store.Save();
            folder.Name = "測試資料夾"; store.Save();
            var reloaded = new Store(testDirectory);
            Check(reloaded.Library.Items[0].Name == "測試資料夾" && reloaded.Library.Items[0].Children.Count == 3, "portable JSON round-trip persists names, ordering, and children");
            File.WriteAllText(store.FilePath, "{broken");
            var recovered = new Store(testDirectory);
            Check(recovered.LoadWarning != null && recovered.Library.Items.Count == 2, "corrupt layout recovers last valid backup");
            Check(!new Preferences().ConfirmRemoval && Hotkeys.Valid(0, 120) && Hotkeys.Valid(14, 120) && !Hotkeys.Valid(16, 120) && !Hotkeys.Valid(3, 16), "custom hotkeys and removal defaults validated");
            Check(Hotkeys.Describe(14, 120) == "Ctrl + Shift + Win + F9", "custom hotkey labels include every modifier");
            Check(StartupRegistration.CommandFor(@"C:\Portable Apps\Orbit.exe") == '"' + @"C:\Portable Apps\Orbit.exe" + '"' + " --background", "startup command quotes portable paths and launches hidden");
            string shortcut = Path.Combine(testDirectory, "test.url"); File.WriteAllText(shortcut, "[InternetShortcut]\nURL=https://example.com");
            store.Library.Items = new() { new Entry { Name = "備份資料夾", IsFolder = true, Children = new() { new Entry { Name = "捷徑", Target = shortcut } } } };
            store.Library.Preferences.HotkeyVirtualKey = 120; store.Library.Preferences.ConfirmRemoval = true;
            string archive = Path.Combine(testDirectory, "backup.orbit-layout"); LayoutArchive.Export(store, archive);
            var snapshot = LayoutArchive.Read(store, archive);
            Check(snapshot.Items[0].Name == "備份資料夾" && File.ReadAllText(PortablePaths.Resolve(snapshot.Items[0].Children[0].Target)) == File.ReadAllText(shortcut), "layout archive restores folder order and embedded shortcuts");
            snapshot.Columns = 8; LayoutArchive.Apply(store, snapshot);
            Check(store.Library.Preferences.Columns == 8 && store.Library.Preferences.HotkeyVirtualKey == 120 && store.Library.Preferences.ConfirmRemoval, "layout restore preserves activation and removal settings");
            string malformed = Path.Combine(testDirectory, "bad.orbit-layout");
            using (var zip = System.IO.Compression.ZipFile.Open(malformed, System.IO.Compression.ZipArchiveMode.Create)) { zip.CreateEntry("assets/../outside.txt"); }
            bool rejected = false; try { LayoutArchive.Read(store, malformed); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && store.Library.Items[0].Name == "備份資料夾", "invalid archive paths rejected without replacing current layout");
            var pulses = new ShortcutPulseGate();
            Check(pulses.Accept(1000) && !pulses.Accept(1001) && !pulses.Accept(1100) && pulses.Accept(1200), "registered and backup shortcut events toggle only once per press");
            string text = string.Join(Environment.NewLine, checks);
            File.WriteAllText(report ?? Path.Combine(AppContext.BaseDirectory, "self-test.txt"), text);
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(report ?? Path.Combine(AppContext.BaseDirectory, "self-test.txt"), string.Join(Environment.NewLine, checks) + "\nFAIL " + ex); return 1; }
    }
}
