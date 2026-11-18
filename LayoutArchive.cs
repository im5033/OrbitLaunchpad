using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace Orbit;

public sealed class LayoutSnapshot
{
    public int FormatVersion { get; set; } = 1;
    public int Columns { get; set; } = 7;
    public int Rows { get; set; } = 4;
    public double IconSize { get; set; } = 120;
    public List<Entry> Items { get; set; } = new();
}

internal static class LayoutArchive
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private const long MaxTotalBytes = 128L * 1024 * 1024;
    public static void Export(Store store, string destination)
    {
        var snapshot = new LayoutSnapshot { Columns = store.Library.Preferences.Columns, Rows = store.Library.Preferences.Rows, IconSize = store.Library.Preferences.IconSize, Items = JsonSerializer.Deserialize<List<Entry>>(JsonSerializer.Serialize(store.Library.Items))! };
        string temp = destination + ".writing-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                long total = 0;
                var copied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in LayoutRules.All(snapshot.Items))
                {
                    entry.Target = AddAsset(entry.Target, false);
                    entry.IconPath = AddAsset(entry.IconPath, true);
                }
                using var writer = new StreamWriter(archive.CreateEntry("layout.json").Open());
                writer.Write(JsonSerializer.Serialize(snapshot, JsonOptions));
                string AddAsset(string value, bool icon)
                {
                    string resolved = PortablePaths.Resolve(value);
                    string extension = Path.GetExtension(resolved).ToLowerInvariant();
                    bool supported = icon ? new[] { ".png", ".jpg", ".jpeg", ".ico", ".bmp" }.Contains(extension) : new[] { ".lnk", ".url", ".appref-ms" }.Contains(extension);
                    if (!supported || !File.Exists(resolved)) return value;
                    if (copied.TryGetValue(resolved, out string? previous)) return previous;
                    if (new FileInfo(resolved).Length > 32L * 1024 * 1024) throw new InvalidDataException("佈局素材超過 32 MB：" + Path.GetFileName(resolved));
                    total += new FileInfo(resolved).Length;
                    if (total > MaxTotalBytes - 8L * 1024 * 1024) throw new InvalidDataException("備份素材總大小超過限制。");
                    string asset = "assets/" + Guid.NewGuid().ToString("N") + extension;
                    archive.CreateEntryFromFile(resolved, asset, CompressionLevel.Optimal); copied[resolved] = asset; return asset;
                }
            }
            File.Move(temp, destination, true);
        }
        catch { if (File.Exists(temp)) File.Delete(temp); throw; }
    }

    public static LayoutSnapshot Read(Store store, string source)
    {
        if (string.Equals(Path.GetExtension(source), ".json", StringComparison.OrdinalIgnoreCase))
        {
            if (new FileInfo(source).Length > 8L * 1024 * 1024) throw new InvalidDataException("佈局檔太大。");
            string json = File.ReadAllText(source);
            using var document = JsonDocument.Parse(json);
            LayoutSnapshot snapshot;
            if (document.RootElement.TryGetProperty("Preferences", out _))
            {
                var library = JsonSerializer.Deserialize<Library>(json) ?? throw new InvalidDataException("無效的佈局檔。");
                snapshot = new LayoutSnapshot { Columns = library.Preferences.Columns, Rows = library.Preferences.Rows, IconSize = library.Preferences.IconSize, Items = library.Items };
            }
            else snapshot = JsonSerializer.Deserialize<LayoutSnapshot>(json) ?? throw new InvalidDataException("無效的佈局檔。");
            Validate(snapshot); return snapshot;
        }
        using var archive = ZipFile.OpenRead(source);
        if (archive.Entries.Count > 5000 || archive.Entries.Sum(x => x.Length) > MaxTotalBytes) throw new InvalidDataException("備份超過允許的大小。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in archive.Entries)
        {
            string name = asset.FullName.Replace('\\', '/');
            if (!names.Add(name) || name.StartsWith('/') || name.Contains(':') || name.Split('/').Any(x => x == ".." || x == ".") || (name != "layout.json" && !name.StartsWith("assets/", StringComparison.Ordinal))) throw new InvalidDataException("備份含不合法的素材路徑。");
            if (asset.Length > 32L * 1024 * 1024) throw new InvalidDataException("備份素材太大。");
            if (name != "layout.json" && (name.Split('/').Length != 2 || string.IsNullOrEmpty(Path.GetFileName(name)))) throw new InvalidDataException("不合法的素材名稱。");
        }
        var layout = archive.GetEntry("layout.json") ?? throw new InvalidDataException("備份內找不到 layout.json。");
        if (layout.Length > 8L * 1024 * 1024) throw new InvalidDataException("佈局檔太大。");
        LayoutSnapshot result;
        using (var reader = new StreamReader(layout.Open())) result = JsonSerializer.Deserialize<LayoutSnapshot>(reader.ReadToEnd()) ?? throw new InvalidDataException("無效的佈局檔。");
        Validate(result);
        // Check every referenced asset before writing any files or modifying current state.
        foreach (var entry in LayoutRules.All(result.Items)) { CheckReference(entry.Target); CheckReference(entry.IconPath); }
        string directory = Path.Combine(store.DirectoryPath, "imports", Guid.NewGuid().ToString("N"));
        foreach (var entry in LayoutRules.All(result.Items))
        {
            entry.Target = Extract(entry.Target); entry.IconPath = Extract(entry.IconPath);
        }
        return result;
        void CheckReference(string value)
        {
            if (value.StartsWith("assets/", StringComparison.Ordinal) && archive.GetEntry(value) == null) throw new InvalidDataException("備份缺少素材。");
        }
        string Extract(string value)
        {
            if (!value.StartsWith("assets/", StringComparison.Ordinal)) return value;
            var asset = archive.GetEntry(value)!;
            Directory.CreateDirectory(directory);
            string destination = Path.Combine(directory, Path.GetFileName(value));
            if (!File.Exists(destination)) asset.ExtractToFile(destination);
            return Path.GetRelativePath(AppContext.BaseDirectory, destination);
        }
    }
    public static void Apply(Store store, LayoutSnapshot snapshot)
    {
        Validate(snapshot);
        store.Library.Items = snapshot.Items;
        store.Library.Initialized = true;
        var preferences = store.Library.Preferences;
        preferences.Columns = snapshot.Columns; preferences.Rows = snapshot.Rows; preferences.IconSize = snapshot.IconSize; preferences.LayoutRevision = 2;
    }
    public static string AutomaticBackup(Store store, string prefix)
    {
        string directory = Path.Combine(store.DirectoryPath, "backups"); Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, prefix + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6] + ".orbit-layout");
        Export(store, path); return path;
    }
    private static void Validate(LayoutSnapshot snapshot)
    {
        if (snapshot.FormatVersion != 1 || snapshot.Columns < 4 || snapshot.Columns > 10 || snapshot.Rows < 3 || snapshot.Rows > 6 || !double.IsFinite(snapshot.IconSize) || snapshot.IconSize < 60 || snapshot.IconSize > 160 || snapshot.Items == null) throw new InvalidDataException("佈局格式或版面尺寸不正確。");
        var ids = new HashSet<string>(); int count = 0;
        void Walk(IEnumerable<Entry> entries, int depth)
        {
            foreach (var entry in entries)
            {
                if (entry == null || ++count > 5000 || string.IsNullOrEmpty(entry.Id) || !ids.Add(entry.Id) || string.IsNullOrWhiteSpace(entry.Name) || entry.Children == null || entry.Target == null || entry.IconPath == null || entry.WorkingDirectory == null || entry.Arguments == null) throw new InvalidDataException("佈局包含無效或重複的圖示。");
                if (entry.IsFolder && depth > 0) throw new InvalidDataException("目前不支援資料夾內再放分類資料夾。");
                if (!entry.IsFolder && entry.Children.Count > 0) throw new InvalidDataException("程式圖示不應含子項目。");
                Walk(entry.Children, depth + 1);
            }
        }
        Walk(snapshot.Items, 0);
    }
}
