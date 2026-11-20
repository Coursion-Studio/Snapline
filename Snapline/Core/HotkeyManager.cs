using System.Windows.Input;
using System.Windows.Interop;

namespace Snapline.Core;

public sealed class HotkeyManager : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 0x5000;

    public HotkeyManager()
    {
        var p = new HwndSourceParameters("SnaplineHotkeys") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    public bool Register(ModifierKeys mods, Key key, Action action)
    {
        uint m = 0x4000; // MOD_NOREPEAT
        if (mods.HasFlag(ModifierKeys.Alt)) m |= 1;
        if (mods.HasFlag(ModifierKeys.Control)) m |= 2;
        if (mods.HasFlag(ModifierKeys.Shift)) m |= 4;
        if (mods.HasFlag(ModifierKeys.Windows)) m |= 8;
        int id = _nextId++;
        if (!Native.RegisterHotKey(_source.Handle, id, m, (uint)KeyInterop.VirtualKeyFromKey(key))) return false;
        _actions[id] = action;
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 && _actions.TryGetValue(wParam.ToInt32(), out var a))
        {
            handled = true;
            try { a(); } catch (Exception ex) { Log.Error(ex); }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys) Native.UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
        _source.Dispose();
    }
}
