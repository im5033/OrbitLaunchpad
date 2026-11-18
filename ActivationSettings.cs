using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Microsoft.Win32;

namespace Orbit;

internal static class PortablePaths
{
    public static string Resolve(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        string path = Environment.ExpandEnvironmentVariables(value);
        if (Path.IsPathRooted(path) || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || Uri.TryCreate(path, UriKind.Absolute, out _)) return path;
        return Path.GetFullPath(path, AppContext.BaseDirectory);
    }
}

internal readonly record struct HotkeyChoice(string Name, uint Code)
{
    public override string ToString() => Name;
}

internal static class Hotkeys
{
    public const uint AllowedModifiers = 0x000F;
    public static bool Valid(uint modifiers, uint key) => (modifiers & ~AllowedModifiers) == 0 && key >= 8 && key <= 254 && key is not (16 or 17 or 18 or 91 or 92 or 160 or 161 or 162 or 163 or 164 or 165);
    public static string Describe(uint modifiers, uint key)
    {
        var parts = new List<string>();
        if ((modifiers & 2) != 0) parts.Add("Ctrl"); if ((modifiers & 1) != 0) parts.Add("Alt");
        if ((modifiers & 4) != 0) parts.Add("Shift"); if ((modifiers & 8) != 0) parts.Add("Win");
        var choice = Choices.FirstOrDefault(x => x.Code == key);
        parts.Add(choice.Name ?? KeyInterop.KeyFromVirtualKey((int)key).ToString());
        return string.Join(" + ", parts);
    }
    public static readonly IReadOnlyList<HotkeyChoice> Choices = Build();
    private static List<HotkeyChoice> Build()
    {
        var keys = new List<HotkeyChoice> { new("Space", 32), new("Tab", 9), new("Enter", 13), new("Escape", 27), new("Backspace", 8) };
        for (uint i = 112; i <= 135; i++) keys.Add(new("F" + (i - 111), i));
        for (uint i = 65; i <= 90; i++) keys.Add(new(((char)i).ToString(), i));
        for (uint i = 48; i <= 57; i++) keys.Add(new(((char)i).ToString(), i));
        for (uint i = 96; i <= 105; i++) keys.Add(new("Numpad " + (i - 96), i));
        keys.AddRange(new[] { new HotkeyChoice("←",37), new("↑",38), new("→",39), new("↓",40), new("Page Up",33), new("Page Down",34), new("Home",36), new("End",35), new("Insert",45), new("Delete",46), new("Print Screen",44), new("Pause",19), new("Scroll Lock",145), new("Caps Lock",20), new("Num Lock",144), new("Numpad +",107), new("Numpad -",109), new("Numpad *",106), new("Numpad /",111), new("Numpad .",110), new(";",186), new("=",187), new(",",188), new("-",189), new(".",190), new("/",191), new("`",192), new("[",219), new("\\",220), new("]",221), new("'",222), new("Volume Mute",173), new("Volume Down",174), new("Volume Up",175), new("Media Next",176), new("Media Previous",177), new("Media Stop",178), new("Media Play/Pause",179) });
        return keys;
    }
}

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OrbitLaunchpad";
    public static string Executable => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "OrbitLaunchpad.exe");
    public static string CommandFor(string executable) => "\"" + Path.GetFullPath(executable) + "\" --background";
    public static string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) as string;
    }
    public static void Write(bool enabled)
    {
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            key.SetValue(ValueName, CommandFor(Executable), RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            key?.DeleteValue(ValueName, false);
        }
    }
    public static void Restore(string? previous)
    {
        if (previous == null) { Write(false); return; }
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        key.SetValue(ValueName, previous, RegistryValueKind.String);
    }
}
