using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace Snapline.Core;

public sealed record MonitorInfo(IntPtr Handle, Int32Rect Bounds, Int32Rect WorkArea, double Scale, bool IsPrimary)
{
    public bool Contains(int x, int y) =>
        x >= Bounds.X && x < Bounds.X + Bounds.Width && y >= Bounds.Y && y < Bounds.Y + Bounds.Height;
}

public static class Monitors
{
    public static List<MonitorInfo> All()
    {
        var list = new List<MonitorInfo>();
        Native.MonitorEnumProc cb = (IntPtr h, IntPtr dc, ref Native.RECT rc, IntPtr data) =>
        {
            var mi = new Native.MONITORINFOEX { cbSize = Marshal.SizeOf<Native.MONITORINFOEX>(), szDevice = "" };
            Native.GetMonitorInfo(h, ref mi);
            uint dpi = 96;
            try { if (Native.GetDpiForMonitor(h, 0, out var dx, out _) == 0) dpi = dx; } catch { }
            list.Add(new MonitorInfo(h, ToRect(mi.rcMonitor), ToRect(mi.rcWork), dpi / 96.0, (mi.dwFlags & 1) != 0));
            return true;
        };
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);
        GC.KeepAlive(cb);
        return list;
    }

    public static MonitorInfo FromPoint(int x, int y)
    {
        var all = All();
        return all.FirstOrDefault(m => m.Contains(x, y)) ?? all.FirstOrDefault(m => m.IsPrimary) ?? all[0];
    }

    public static MonitorInfo AtCursor()
    {
        Native.GetCursorPos(out var p);
        return FromPoint(p.X, p.Y);
    }

    public static MonitorInfo Primary
    {
        get
        {
            var all = All();
            return all.FirstOrDefault(m => m.IsPrimary) ?? all[0];
        }
    }

    internal static Int32Rect ToRect(Native.RECT r) => new(r.Left, r.Top, r.Width, r.Height);
}

public sealed record WindowInfo(IntPtr Handle, Int32Rect Bounds, string Title);

public static class WindowFinder
{
    /// <summary>Visible top-level windows, top of the z-order first.</summary>
    public static List<WindowInfo> VisibleWindows()
    {
        uint self = (uint)Environment.ProcessId;
        var list = new List<WindowInfo>();
        var sb = new StringBuilder(256);
        Native.EnumWindowsProc cb = (h, _) =>
        {
            if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
            Native.GetWindowThreadProcessId(h, out uint pid);
            if (pid == self) return true;
            if (Native.DwmGetWindowAttribute(h, 14, out int cloaked, 4) == 0 && cloaked != 0) return true;
            int ex = Native.GetWindowLong(h, -20);
            if ((ex & 0x20) != 0) return true; // WS_EX_TRANSPARENT (click-through overlays)
            sb.Clear();
            Native.GetClassName(h, sb, sb.Capacity);
            var cls = sb.ToString();
            if (cls is "Progman" or "WorkerW") return true;
            if (Native.DwmGetWindowAttribute(h, 9, out Native.RECT r, Marshal.SizeOf<Native.RECT>()) != 0)
                Native.GetWindowRect(h, out r);
            if (r.Width < 24 || r.Height < 24) return true;
            sb.Clear();
            Native.GetWindowText(h, sb, sb.Capacity);
            list.Add(new WindowInfo(h, Monitors.ToRect(r), sb.ToString()));
            return true;
        };
        Native.EnumWindows(cb, IntPtr.Zero);
        GC.KeepAlive(cb);
        return list;
    }
}
