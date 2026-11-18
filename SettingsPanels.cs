using System;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace Orbit;

public sealed partial class LauncherWindow
{
    private void ShowSettings()
    {
        var dialog = Theme.Dialog(this, "設定 · Orbit", 640, 650);
        var outer = new DockPanel { Margin = new Thickness(24) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(buttons, Dock.Bottom); outer.Children.Add(buttons);
        var error = Theme.Label("", 12, Theme.Accent); error.TextWrapping = TextWrapping.Wrap;
        DockPanel.SetDock(error, Dock.Bottom); outer.Children.Add(error);
        var tabs = new TabControl { Background = Theme.Panel }; outer.Children.Add(tabs);
        StackPanel Page(string title)
        {
            var panel = new StackPanel { Margin = new Thickness(18), Background = Theme.Panel };
            tabs.Items.Add(new TabItem { Header = title, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }); return panel;
        }
        CheckBox Check(StackPanel panel, string label, bool value)
        {
            var check = new CheckBox { Content = label, IsChecked = value, Foreground = Theme.Text, Margin = new Thickness(0, 10, 12, 10) }; panel.Children.Add(check); return check;
        }
        var general = Page("一般與快捷鍵");
        var enabled = Check(general, "啟用全域快捷鍵", Prefs.HotkeyEnabled);
        var modsPanel = new StackPanel { Orientation = Orientation.Horizontal }; general.Children.Add(modsPanel);
        var ctrl = Check(modsPanel, "Ctrl", (Prefs.HotkeyModifiers & 2) != 0);
        var alt = Check(modsPanel, "Alt", (Prefs.HotkeyModifiers & 1) != 0);
        var shift = Check(modsPanel, "Shift", (Prefs.HotkeyModifiers & 4) != 0);
        var win = Check(modsPanel, "Win", (Prefs.HotkeyModifiers & 8) != 0);
        var choices = Hotkeys.Choices.ToList();
        if (!choices.Any(x => x.Code == Prefs.HotkeyVirtualKey)) choices.Add(new HotkeyChoice(KeyInterop.KeyFromVirtualKey((int)Prefs.HotkeyVirtualKey).ToString(), Prefs.HotkeyVirtualKey));
        var key = new ComboBox { ItemsSource = choices, SelectedItem = choices.First(x => x.Code == Prefs.HotkeyVirtualKey), Margin = new Thickness(0, 6, 0, 12), Padding = new Thickness(8) }; general.Children.Add(key);
        var recorder = Theme.Input("點此直接按下組合鍵，也可使用上方選項"); recorder.IsReadOnly = true; general.Children.Add(recorder);
        uint Modifiers() => (ctrl.IsChecked == true ? 2u : 0) | (alt.IsChecked == true ? 1u : 0) | (shift.IsChecked == true ? 4u : 0) | (win.IsChecked == true ? 8u : 0);
        recorder.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true; var pressed = e.Key == Key.System ? e.SystemKey : e.Key; uint code = (uint)KeyInterop.VirtualKeyFromKey(pressed);
            if (!Hotkeys.Valid(0, code)) return;
            ctrl.IsChecked = Keyboard.Modifiers.HasFlag(ModifierKeys.Control); alt.IsChecked = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
            shift.IsChecked = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift); win.IsChecked = Keyboard.Modifiers.HasFlag(ModifierKeys.Windows);
            if (!choices.Any(x => x.Code == code)) { choices.Add(new HotkeyChoice(pressed.ToString(), code)); key.Items.Refresh(); }
            key.SelectedItem = choices.First(x => x.Code == code); recorder.Text = Hotkeys.Describe(Modifiers(), code);
        };
        var note = Theme.Label("支援無修飾鍵、功能鍵、數字鍵盤及多媒體鍵。若組合被系統或其他程式占用，套用時會提示，並保留原快捷鍵。", 12, Theme.Muted); note.TextWrapping = TextWrapping.Wrap; note.Margin = new Thickness(0, 12, 0, 18); general.Children.Add(note);
        string? initialStartup;
        try { initialStartup = StartupRegistration.Read(); } catch { initialStartup = null; }
        var startup = Check(general, "登入 Windows 時自動啟動（登錄表，目前使用者）", initialStartup != null);
        var startupNote = Theme.Label("自啟時在背景待命。移動可攜版位置後，重新套用此設定即可更新路徑。", 12, Theme.Muted); startupNote.TextWrapping = TextWrapping.Wrap; general.Children.Add(startupNote);
        var confirm = Check(general, "移除圖示前詢問", Prefs.ConfirmRemoval);
        var appearance = Page("佈局與動畫");
        Slider Slider(string label, double value, double min, double max, double step)
        {
            var caption = Theme.Label(label + "  " + value.ToString("0")); caption.Margin = new Thickness(0, 12, 0, 8); appearance.Children.Add(caption);
            var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = step, IsSnapToTickEnabled = true };
            slider.ValueChanged += (_, _) => caption.Text = label + "  " + slider.Value.ToString("0"); appearance.Children.Add(slider); return slider;
        }
        var columns = Slider("每列圖示", Prefs.Columns, 4, 10, 1); var rows = Slider("每頁列數", Prefs.Rows, 3, 6, 1);
        var size = Slider("圖示大小", Prefs.IconSize, 60, 160, 5); var animation = Slider("動畫時間（ms）", Prefs.AnimationMs, 100, 600, 20);
        var reorder = Slider("滑過多久後讓位（ms）", Prefs.ReorderDelayMs, 150, 700, 50);
        var backup = Page("備份與恢復");
        var explanation = Theme.Label("備份包含排列、資料夾、捷徑與自訂圖示。已安裝程式本體不包含在備份中。恢復前會自動備份目前佈局；快捷鍵與開機自啟設定會保留。", 14); explanation.TextWrapping = TextWrapping.Wrap; explanation.Margin = new Thickness(0, 0, 0, 20); backup.Children.Add(explanation);
        backup.Children.Add(Theme.Button("備份佈局…", (_, _) => BackupLayout(dialog)));
        backup.Children.Add(Theme.Button("恢復佈局…", (_, _) => { if (RestoreLayout(dialog)) dialog.Close(); }));
        buttons.Children.Add(Theme.Button("取消", (_, _) => dialog.Close()));
        buttons.Children.Add(Theme.Button("套用", (_, _) =>
        {
            var old = Prefs; var candidate = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(old))!;
            candidate.HotkeyEnabled = enabled.IsChecked == true; candidate.HotkeyModifiers = Modifiers(); candidate.HotkeyVirtualKey = ((HotkeyChoice)key.SelectedItem).Code;
            candidate.ConfirmRemoval = confirm.IsChecked == true; candidate.Columns = (int)columns.Value; candidate.Rows = (int)rows.Value; candidate.IconSize = size.Value; candidate.AnimationMs = animation.Value; candidate.ReorderDelayMs = (int)reorder.Value;
            if (!ConfigureHotkey(candidate.HotkeyEnabled, candidate.HotkeyModifiers, candidate.HotkeyVirtualKey, out string message)) { error.Text = message; return; }
            string? previousStartup = null; bool changedStartup = false;
            try
            {
                previousStartup = StartupRegistration.Read(); bool wantStartup = startup.IsChecked == true;
                if (wantStartup ? previousStartup != StartupRegistration.CommandFor(StartupRegistration.Executable) : previousStartup != null) { changedStartup = true; StartupRegistration.Write(wantStartup); }
                _store.Library.Preferences = candidate; _store.Save(); UpdateTrayText(); CloseFolder(true); dialog.Close(); Refresh(false);
            }
            catch (Exception ex)
            {
                _store.Library.Preferences = old; ConfigureHotkey(old.HotkeyEnabled, old.HotkeyModifiers, old.HotkeyVirtualKey, out _);
                try { if (changedStartup) StartupRegistration.Restore(previousStartup); } catch { }
                error.Text = "無法儲存設定：" + ex.Message;
            }
        }, true));
        dialog.Content = outer; RunDialog(dialog);
    }
    private void BackupLayout(Window owner)
    {
        var picker = new SaveFileDialog { Filter = "Orbit 佈局備份|*.orbit-layout", FileName = "Orbit佈局-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), DefaultExt = ".orbit-layout" };
        bool previousModal = _modal; _modal = true;
        try { if (picker.ShowDialog(owner) == true) { LayoutArchive.Export(_store, picker.FileName); Toast("佈局已備份"); } }
        catch (Exception ex) { MessageBox.Show(owner, ex.Message, "備份失敗"); }
        finally { _modal = previousModal; }
    }
    private bool RestoreLayout(Window owner)
    {
        var picker = new OpenFileDialog { Filter = "Orbit 佈局備份|*.orbit-layout;*.json;*.zip" };
        bool previousModal = _modal; _modal = true; var oldItems = _store.Library.Items; var oldPreferences = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(Prefs))!; bool initialized = _store.Library.Initialized;
        try
        {
            if (picker.ShowDialog(owner) != true) return false;
            var snapshot = LayoutArchive.Read(_store, picker.FileName); LayoutArchive.AutomaticBackup(_store, "恢復前");
            CloseFolder(true); LayoutArchive.Apply(_store, snapshot); _store.Save(); _tileCache.Clear(); _rootViewKey = ""; _layoutDirty = true; _page = 0; Refresh(false); Toast("佈局已恢復，原佈局已自動備份"); return true;
        }
        catch (Exception ex) { _store.Library.Items = oldItems; _store.Library.Preferences = oldPreferences; _store.Library.Initialized = initialized; MessageBox.Show(owner, ex.Message, "恢復失敗"); return false; }
        finally { _modal = previousModal; }
    }
}
