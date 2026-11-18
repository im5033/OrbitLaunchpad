using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Orbit;

public sealed class Entry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "未命名";
    public string Target { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string IconPath { get; set; } = "";
    public bool IsFolder { get; set; }
    public List<Entry> Children { get; set; } = new();
}

public sealed class Preferences
{
    public int Columns { get; set; } = 7;
    public int Rows { get; set; } = 4;
    public double IconSize { get; set; } = 90;
    public double AnimationMs { get; set; } = 260;
    public int MergeDelayMs { get; set; } = 550;
}

public sealed class Library
{
    public int SchemaVersion { get; set; } = 1;
    public Preferences Preferences { get; set; } = new();
    public List<Entry> Items { get; set; } = new();
    public bool Initialized { get; set; }
}

public static class LayoutRules
{
    public static bool Swap(List<Entry> items, string a, string b)
    {
        int i = items.FindIndex(x => x.Id == a), j = items.FindIndex(x => x.Id == b);
        if (i < 0 || j < 0 || i == j) return false;
        (items[i], items[j]) = (items[j], items[i]);
        return true;
    }

    public static Entry? Group(List<Entry> items, string draggedId, string targetId)
    {
        var dragged = items.Find(x => x.Id == draggedId);
        var target = items.Find(x => x.Id == targetId);
        if (dragged == null || target == null || dragged == target || dragged.IsFolder) return null;
        if (target.IsFolder)
        {
            items.Remove(dragged);
            target.Children.Add(dragged);
            return target;
        }
        int index = items.IndexOf(target);
        var folder = new Entry { Name = "未命名資料夾", IsFolder = true, Children = new() { target, dragged } };
        items[index] = folder;
        items.Remove(dragged);
        return folder;
    }

    public static IEnumerable<Entry> All(IEnumerable<Entry> items)
    {
        foreach (var entry in items)
        {
            yield return entry;
            foreach (var child in All(entry.Children)) yield return child;
        }
    }
}

public sealed class Store
{
    public string DirectoryPath { get; }
    public string FilePath => Path.Combine(DirectoryPath, "library.json");
    public Library Library { get; private set; }
    public string? LoadWarning { get; private set; }
    public Store(string? directory = null)
    {
        DirectoryPath = directory ?? Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(DirectoryPath);
        Library = new Library();
        if (File.Exists(FilePath))
        {
            try { Library = JsonSerializer.Deserialize<Library>(File.ReadAllText(FilePath)) ?? throw new InvalidDataException(); }
            catch
            {
                string backup = FilePath + ".bak";
                try
                {
                    Library = JsonSerializer.Deserialize<Library>(File.ReadAllText(backup)) ?? throw new InvalidDataException();
                    LoadWarning = "排列檔讀取失敗，已恢復上一份備份。";
                    File.Copy(FilePath, FilePath + ".damaged-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true);
                }
                catch { throw new InvalidDataException("排列檔及備份無法讀取。原檔已保留，請先備份 data 資料夾再處理。"); }
            }
        }
        var p = Library.Preferences;
        p.Columns = Math.Clamp(p.Columns, 4, 10);
        p.Rows = Math.Clamp(p.Rows, 3, 6);
        p.IconSize = Math.Clamp(p.IconSize, 60, 120);
        p.AnimationMs = Math.Clamp(p.AnimationMs, 100, 600);
        p.MergeDelayMs = Math.Clamp(p.MergeDelayMs, 350, 1200);
    }
    public void Save()
    {
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Library, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak", true);
        else File.Move(temp, FilePath);
    }
}
