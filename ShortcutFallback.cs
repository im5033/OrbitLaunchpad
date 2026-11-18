using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Orbit;

// Dedicated message loop: callbacks only compare the configured combination and post a notification.
internal sealed class ShortcutFallback : IDisposable
{
    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int kind, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint message, IntPtr w, IntPtr l);
    private readonly Thread _thread;
    private readonly HookProc _callback;
    private readonly ManualResetEventSlim _ready = new();
    private IntPtr _hook;
    private uint _threadId;
    private int _enabled, _modifiers, _key, _held;
    public bool Available => _hook != IntPtr.Zero;
    public event Action? Pressed;
    public ShortcutFallback()
    {
        _callback = Handle;
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            using var context = new ApplicationContext();
            // Force the message queue to exist before allowing disposal/configuration.
            using var receiver = new HotkeyReceiver();
            _hook = SetWindowsHookEx(13, _callback, GetModuleHandle(null), 0);
            _ready.Set();
            try { System.Windows.Forms.Application.Run(context); }
            finally { if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        }) { IsBackground = true, Name = "Orbit shortcut input" };
        _thread.SetApartmentState(ApartmentState.STA); _thread.Start(); _ready.Wait();
    }
    public void Configure(bool enabled, uint modifiers, uint key)
    {
        Volatile.Write(ref _enabled, 0); Volatile.Write(ref _modifiers, (int)modifiers); Volatile.Write(ref _key, (int)key); Volatile.Write(ref _held, 0); Volatile.Write(ref _enabled, enabled ? 1 : 0);
    }
    private IntPtr Handle(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && Volatile.Read(ref _enabled) != 0 && Marshal.ReadInt32(data) == Volatile.Read(ref _key))
        {
            int kind = message.ToInt32();
            if (kind == 0x101 || kind == 0x105) Volatile.Write(ref _held, 0);
            else if ((kind == 0x100 || kind == 0x104) && Interlocked.Exchange(ref _held, 1) == 0)
            {
                uint modifiers = 0;
                if (Down(17)) modifiers |= 2; if (Down(18)) modifiers |= 1; if (Down(16)) modifiers |= 4; if (Down(91) || Down(92)) modifiers |= 8;
                if (modifiers == (uint)Volatile.Read(ref _modifiers)) Pressed?.Invoke();
            }
        }
        return CallNextHookEx(_hook, code, message, data);
    }
    private static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    public void Dispose() { Volatile.Write(ref _enabled, 0); if (_threadId != 0) PostThreadMessage(_threadId, 0x12, IntPtr.Zero, IntPtr.Zero); }
}

internal sealed class ShortcutPulseGate
{
    private long _previous = long.MinValue;
    public bool Accept(long milliseconds)
    {
        if (_previous != long.MinValue && milliseconds - _previous < 160) return false;
        _previous = milliseconds; return true;
    }
}
