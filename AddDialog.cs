using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Orbit;

public sealed partial class LauncherWindow
{
    private async void ShowAdd()
    {
        if (_modal || _transitioning || _hiding) return;
        CancelDrag(); _modal = true;
        // Keep the destination folder even though the fullscreen window is hidden.
        var destination = ActiveItems;
        var dialog = Theme.Dialog(this, "添加圖示 · Orbit", 650, 330);
        dialog.ResizeMode = ResizeMode.NoResize; dialog.ShowInTaskbar = true;
        dialog.Topmost = true;
        dialog.WindowStartupLocation = WindowStartupLocation.Manual;
        dialog.Left = Left + (ActualWidth - dialog.Width) / 2;
        dialog.Top = Top + (ActualHeight - dialog.Height) / 2;
        var outer = new StackPanel { Margin = new Thickness(24) };
        outer.Children.Add(Theme.Label("把常用程式放進 Orbit", 23));
        var subtitle = Theme.Label("拖入程式、捷徑、檔案或資料夾。可一次添加多個項目。", 13, Theme.Muted);
        subtitle.Margin = new Thickness(0, 8, 0, 17); outer.Children.Add(subtitle);
        var pending = new List<Entry>();
        var existing = LayoutRules.All(_store.Library.Items).Select(x => PortablePaths.Resolve(x.Target)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dropText = Theme.Label("＋  將檔案或資料夾拖放到這裡", 17, Theme.Accent);
        dropText.HorizontalAlignment = HorizontalAlignment.Center;
        var drop = new Border { Height = 92, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), BorderBrush = Theme.Accent, Background = new SolidColorBrush(Color.FromRgb(34, 49, 73)), Child = dropText, Margin = new Thickness(0, 0, 0, 8) };
        outer.Children.Add(drop);
        var status = Theme.Label("尚未選擇項目", 12, Theme.Muted);
        status.TextTrimming = TextTrimming.CharacterEllipsis; status.MaxWidth = 590;
        outer.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        Button? finish = null;
        buttons.Children.Add(Theme.Button("選擇檔案…", (_, _) =>
        {
            var picker = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "選擇程式、捷徑或檔案", Filter = "所有檔案|*.*|程式與捷徑|*.exe;*.lnk;*.url;*.appref-ms" };
            if (picker.ShowDialog(dialog) == true) Queue(picker.FileNames);
        }));
        buttons.Children.Add(Theme.Button("選擇資料夾…", (_, _) =>
        {
            var picker = new Microsoft.Win32.OpenFolderDialog { Multiselect = true, Title = "選擇資料夾" };
            if (picker.ShowDialog(dialog) == true) Queue(picker.FolderNames);
        }));
        buttons.Children.Add(Theme.Button("取消", (_, _) => dialog.Close()));
        finish = Theme.Button("添加", (_, _) =>
        {
            destination.AddRange(pending);
            if (_folder == null && pending.Count > 0) _page = (_store.Library.Items.Count - 1) / Capacity;
            Save(); dialog.Close();
        }, true);
        finish.IsEnabled = false; buttons.Children.Add(finish); outer.Children.Add(buttons);
        dialog.Content = outer; dialog.AllowDrop = true;
        dialog.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        dialog.Drop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) Queue(paths); e.Handled = true; };
        void Queue(IEnumerable<string> paths)
        {
            int added = 0;
            foreach (string path in paths)
            {
                string resolved = PortablePaths.Resolve(path);
                if ((!File.Exists(resolved) && !Directory.Exists(resolved)) || existing.Contains(resolved) || pending.Any(x => string.Equals(PortablePaths.Resolve(x.Target), resolved, StringComparison.OrdinalIgnoreCase))) continue;
                pending.Add(Native.FromPath(resolved)); added++;
            }
            finish!.IsEnabled = pending.Count > 0;
            dropText.Text = pending.Count > 0 ? "已接收 · 可繼續拖放" : "＋  將檔案或資料夾拖放到這裡";
            status.Text = added == 0 ? "沒有新項目可添加（已存在或無法讀取）" : "已選擇 " + pending.Count + " 個：" + string.Join("、", pending.Select(x => x.Name));
            status.ToolTip = string.Join(Environment.NewLine, pending.Select(x => x.Target));
        }
        try
        {
            _hiding = true;
            await FadeWindow(false, Prefs.AnimationMs * .85);
            Hide(); _hiding = false;
            if (!_quitting) dialog.ShowDialog();
        }
        finally
        {
            _hiding = false; _modal = false;
            if (!_quitting) { Refresh(false); Toggle(); }
        }
    }
}
