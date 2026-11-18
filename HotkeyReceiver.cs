using System;
using System.Windows.Forms;

namespace Orbit;

// This message-only window remains alive while the visible WPF window is hidden.
internal sealed class HotkeyReceiver : NativeWindow, IDisposable
{
    public event Action<int>? Pressed;
    public HotkeyReceiver() => CreateHandle(new CreateParams { Caption = "Orbit Hotkey Receiver", Parent = new IntPtr(-3) });
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0312) { Pressed?.Invoke(message.WParam.ToInt32()); return; }
        base.WndProc(ref message);
    }
    public void Dispose() => DestroyHandle();
}
