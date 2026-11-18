using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Orbit;

internal static class Native
{
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SystemParametersInfo(uint action, uint parameter, StringBuilder value, uint flags);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path, uint attributes, out SHFILEINFO info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int Width; public int Height; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct SHFILEINFO
    {
        public IntPtr Icon; public int IconIndex; public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, uint flags, out IntPtr bitmap); }

    private static readonly ConcurrentDictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);
    public static Task WarmIconsAsync(IEnumerable<Entry> entries)
    {
        var paths = entries.Where(x => !x.IsFolder).Select(x => string.IsNullOrEmpty(x.IconPath) ? x.Target : x.IconPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { foreach (string path in paths) Icon(path); completion.SetResult(); }
            catch (Exception error) { completion.SetException(error); }
        }) { IsBackground = true, Name = "Orbit icon loader" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    public static ImageSource? Icon(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (IconCache.TryGetValue(path, out var cached)) return cached;
        if (string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            object? shell = null, shortcut = null;
            try
            {
                var type = Type.GetTypeFromProgID("WScript.Shell");
                if (type != null)
                {
                    shell = Activator.CreateInstance(type);
                    shortcut = ((dynamic)shell!).CreateShortcut(path);
                    string location = ((string)((dynamic)shortcut).IconLocation).Trim();
                    int comma = location.LastIndexOf(',');
                    if (comma > 0) location = location.Substring(0, comma);
                    location = Environment.ExpandEnvironmentVariables(location.Trim('"'));
                    string target = (string)((dynamic)shortcut).TargetPath;
                    string resolved = File.Exists(location) ? location : target;
                    if (File.Exists(resolved) && !string.Equals(path, resolved, StringComparison.OrdinalIgnoreCase) && !string.Equals(Path.GetExtension(resolved), ".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        var icon = Icon(resolved);
                        if (icon != null) { IconCache[path] = icon; return icon; }
                    }
                }
            }
            catch { }
            finally { if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.ReleaseComObject(shortcut); if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell); }
        }
        ImageSource? result = null;
        if (new[] { ".png", ".jpg", ".jpeg", ".ico", ".bmp" }.Contains(Path.GetExtension(path).ToLowerInvariant()) && File.Exists(path))
        {
            try
            {
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.UriSource = new Uri(path); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 192; bitmap.EndInit(); bitmap.Freeze(); result = bitmap;
            }
            catch { }
        }
        if (result == null)
        {
            IShellItemImageFactory? factory = null;
            IntPtr handle = IntPtr.Zero;
            try
            {
                var guid = typeof(IShellItemImageFactory).GUID;
                SHCreateItemFromParsingName(path, IntPtr.Zero, ref guid, out factory);
                if (factory.GetImage(new SIZE { Width = 192, Height = 192 }, 0x1 | 0x4, out handle) >= 0 && handle != IntPtr.Zero)
                {
                    var source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze(); result = source;
                }
            }
            catch { }
            finally { if (handle != IntPtr.Zero) DeleteObject(handle); if (factory != null) Marshal.ReleaseComObject(factory); }
        }
        if (result == null)
        {
            try
            {
                if (SHGetFileInfo(path, 0, out var info, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x100) != IntPtr.Zero && info.Icon != IntPtr.Zero)
                {
                    try { var source = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); source.Freeze(); result = source; }
                    finally { DestroyIcon(info.Icon); }
                }
            }
            catch { }
        }
        if (result is BitmapSource bitmapResult) result = CropIcon(bitmapResult);
        IconCache[path] = result;
        return result;
    }
    public static void ClearIconCache() => IconCache.Clear();
    private static BitmapSource CropIcon(BitmapSource source)
    {
        try
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int w = converted.PixelWidth, h = converted.PixelHeight, stride = w * 4;
            var pixels = new byte[stride * h]; converted.CopyPixels(pixels, stride, 0);
            int left = w, top = h, right = -1, bottom = -1;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                if (pixels[y * stride + x * 4 + 3] < 175) continue;
                left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            }
            if (right >= left && bottom >= top && (right - left < w * .88 || bottom - top < h * .88))
            {
                left = Math.Max(0, left - 1); top = Math.Max(0, top - 1); right = Math.Min(w - 1, right + 1); bottom = Math.Min(h - 1, bottom + 1);
                var cropped = new CroppedBitmap(source, new Int32Rect(left, top, right - left + 1, bottom - top + 1)); cropped.Freeze(); return cropped;
            }
        }
        catch { }
        return source;
    }
    public static string Wallpaper()
    {
        var path = new StringBuilder(2048);
        return SystemParametersInfo(0x0073, (uint)path.Capacity, path, 0) ? path.ToString() : "";
    }
    public static System.Windows.Forms.Screen CurrentScreen()
    {
        GetCursorPos(out var point);
        return System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(point.X, point.Y));
    }
    public static void Open(Entry entry, bool elevated = false)
    {
        Process.Start(new ProcessStartInfo(entry.Target)
        {
            UseShellExecute = true, Arguments = entry.Arguments,
            WorkingDirectory = string.IsNullOrEmpty(entry.WorkingDirectory) ? "" : entry.WorkingDirectory,
            Verb = elevated ? "runas" : ""
        });
    }
    public static void Reveal(Entry entry)
    {
        if (File.Exists(entry.Target)) Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + entry.Target + "\"") { UseShellExecute = true });
        else if (Directory.Exists(entry.Target)) Process.Start(new ProcessStartInfo(entry.Target) { UseShellExecute = true });
        else throw new FileNotFoundException("找不到此項目的檔案位置。", entry.Target);
    }
    public static List<Entry> ScanShortcuts()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        };
        var result = new List<Entry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots.Where(Directory.Exists)) Walk(root);
        string[] preferred = { "Brave", "Discord", "Chrome", "Microsoft Edge", "LINE", "Notepad++", "OBS Studio", "Bambu Studio", "SolidCraft", "Word", "Excel", "PowerPoint", "Joplin", "7-Zip File Manager", "Steam", "OpenSpeedy" };
        return result.OrderBy(x => { int i = Array.FindIndex(preferred, name => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)); return i < 0 ? 999 : i; }).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        void Walk(string directory)
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext != ".lnk" && ext != ".url" && ext != ".appref-ms") continue;
                    string name = Path.GetFileNameWithoutExtension(file);
                    string[] excluded = { "uninstall", "解除安裝", "卸载", "卸載", "移除", "使用指南", "使用說明", "取得說明", "帮助", "Help", "manual", "readme", "參照文件", "錄製內容管理員", "重啟", "WebATM", "官方網站", "造訪 " };
                    if (excluded.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase))) continue;
                    if (seen.Add(name)) result.Add(FromPath(file));
                }
                foreach (string child in Directory.EnumerateDirectories(directory))
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) Walk(child);
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
    }
    public static Entry FromPath(string path) => new()
    {
        Name = Directory.Exists(path) ? new DirectoryInfo(path).Name : Path.GetFileNameWithoutExtension(path),
        Target = path,
        WorkingDirectory = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? ""
    };
}
