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
        if (!first) { _toggle.Set(); return 0; }
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
            var window = new LauncherWindow(store);
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
            string testDirectory = Path.Combine(AppContext.BaseDirectory, "test-data-" + Guid.NewGuid().ToString("N"));
            var store = new Store(testDirectory);
            store.Library.Items = items; store.Library.Initialized = true; store.Save();
            folder.Name = "測試資料夾"; store.Save();
            var reloaded = new Store(testDirectory);
            Check(reloaded.Library.Items[0].Name == "測試資料夾" && reloaded.Library.Items[0].Children.Count == 3, "portable JSON round-trip persists names, ordering, and children");
            File.WriteAllText(store.FilePath, "{broken");
            var recovered = new Store(testDirectory);
            Check(recovered.LoadWarning != null && recovered.Library.Items.Count == 2, "corrupt layout recovers last valid backup");
            string text = string.Join(Environment.NewLine, checks);
            File.WriteAllText(report ?? Path.Combine(AppContext.BaseDirectory, "self-test.txt"), text);
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(report ?? Path.Combine(AppContext.BaseDirectory, "self-test.txt"), string.Join(Environment.NewLine, checks) + "\nFAIL " + ex); return 1; }
    }
}
