using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Snapline.Core;

/// <summary>
/// Low-level keyboard hook that claims the bare Print Screen key, so it opens Snapline instead of
/// the Snipping Tool. Win/Alt/Ctrl/Shift + PrtSc are left alone for Windows' own shortcuts.
/// The hook lives on its own thread with its own message loop, so a busy UI never delays typing
/// system-wide and Windows never drops the hook for being slow.
/// </summary>
public sealed class PrintScreenHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105, WM_QUIT = 0x12;
    private const int VK_SNAPSHOT = 0x2C;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint msg, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);

    private readonly HookProc _proc; // keep the delegate alive for as long as the hook exists
    private readonly Action _onPress;
    private readonly Dispatcher _dispatcher;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private IntPtr _hook;
    private uint _threadId;
    private bool _down;

    public PrintScreenHook(Action onPress)
    {
        _onPress = onPress;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _proc = Callback;
        _thread = new Thread(Run) { IsBackground = true, Name = "Snapline Print Screen hook" };
        _thread.Start();
        _ready.Wait(3000);
    }

    public bool IsActive => _hook != IntPtr.Zero;

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        using (var module = Process.GetCurrentProcess().MainModule)
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module?.ModuleName), 0);
        _ready.Set();
        if (_hook == IntPtr.Zero) return;
        while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private static bool Held(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Marshal.ReadInt32(lParam) == VK_SNAPSHOT)
        {
            bool modified = Held(0x5B) || Held(0x5C) || Held(0x12) || Held(0x11) || Held(0x10); // Win, Alt, Ctrl, Shift
            if (!modified)
            {
                int msg = wParam.ToInt32();
                if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
                {
                    if (!_down) { _down = true; _dispatcher.BeginInvoke(_onPress); }
                }
                else if (msg is WM_KEYUP or WM_SYSKEYUP)
                {
                    // Some keyboards only report the key-up for Print Screen.
                    if (!_down) _dispatcher.BeginInvoke(_onPress);
                    _down = false;
                }
                return new IntPtr(1); // swallow it so the Snipping Tool doesn't open too
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
    }
}
