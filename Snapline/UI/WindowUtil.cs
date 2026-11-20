using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Snapline.Core;

namespace Snapline.UI;

public static class WindowUtil
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_LAYERED = 0x80000;
    private const uint SWP_NOSIZE = 0x1, SWP_NOACTIVATE = 0x10;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    public static IntPtr Hwnd(Window w) => new WindowInteropHelper(w).EnsureHandle();

    public static void SetToolWindow(Window w, bool noActivate = true, bool clickThrough = false)
    {
        var h = Hwnd(w);
        int ex = Native.GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TOOLWINDOW;
        if (noActivate) ex |= WS_EX_NOACTIVATE;
        if (clickThrough) ex |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
        Native.SetWindowLong(h, GWL_EXSTYLE, ex);
    }

    /// <summary>Hides the window from every screen capture (including our own). Win10 2004+.</summary>
    public static bool ExcludeFromCapture(Window w) => Native.SetWindowDisplayAffinity(Hwnd(w), 0x11);

    public static void PlacePhysical(Window w, Int32Rect r) =>
        Native.SetWindowPos(Hwnd(w), HWND_TOPMOST, r.X, r.Y, r.Width, r.Height, SWP_NOACTIVATE);

    public static void MovePhysical(Window w, int x, int y) =>
        Native.SetWindowPos(Hwnd(w), HWND_TOPMOST, x, y, 0, 0, SWP_NOACTIVATE | SWP_NOSIZE);

    /// <summary>
    /// Takes keyboard focus even when we were triggered by a keyboard hook (which, unlike a registered
    /// hotkey, doesn't grant foreground rights). Uses the safe method in UiWin, not AttachThreadInput,
    /// which can leave keys such as Win stuck.
    /// </summary>
    public static void ForceForeground(Window w) => Snapline.Shared.UiWin.ForceForeground(w);

    public static void RoundCorners(Window w)
    {
        int pref = 2; // DWMWCP_ROUND
        try { Native.DwmSetWindowAttribute(Hwnd(w), 33, ref pref, 4); } catch { }
    }

    public static double Dpi(Visual v) => VisualTreeHelper.GetDpi(v).DpiScaleX;

    public static void Fade(UIElement e, double to, int ms = 160, Action? done = null)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        if (done != null) a.Completed += (_, _) => done();
        e.BeginAnimation(UIElement.OpacityProperty, a);
    }

    public static DoubleAnimation Ease(double? from, double to, int ms, IEasingFunction? ease = null)
    {
        var a = new DoubleAnimation { To = to, Duration = TimeSpan.FromMilliseconds(ms), EasingFunction = ease ?? new CubicEase { EasingMode = EasingMode.EaseOut } };
        if (from.HasValue) a.From = from.Value;
        return a;
    }

    /// <summary>Hooks up maximise/minimise/close buttons and fixes the WindowChrome maximised margin.</summary>
    public static void WireChrome(Window w, FrameworkElement root)
    {
        w.StateChanged += (_, _) => root.Margin = w.WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        w.SourceInitialized += (_, _) => RoundCorners(w);
    }
}

public static class Backgrounds
{
    public sealed record Option(string Name, Brush? Brush);

    public static readonly IReadOnlyList<Option> All = new List<Option>
    {
        new("None", null),
        new("Ocean", Grad("#2193B0", "#6DD5ED")),
        new("Sunset", Grad("#FF5F6D", "#FFC371")),
        new("Grape", Grad("#8E2DE2", "#4A00E0")),
        new("Mint", Grad("#11998E", "#38EF7D")),
        new("Peach", Grad("#FDA085", "#F6D365")),
        new("Berry", Grad("#C471F5", "#FA71CD")),
        new("Sky", Grad("#A1C4FD", "#C2E9FB")),
        new("Dusk", Grad("#0F2027", "#2C5364")),
        new("Night", Grad("#232526", "#414345")),
        new("Paper", Solid("#F4F4F5")),
        new("Ink", Solid("#111113")),
    };

    private static Brush Grad(string a, string b)
    {
        var g = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(a), (Color)ColorConverter.ConvertFromString(b), new Point(0, 0), new Point(1, 1));
        g.Freeze();
        return g;
    }

    private static Brush Solid(string c)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(c));
        b.Freeze();
        return b;
    }

    public static Brush Checker()
    {
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3D)), null, new RectangleGeometry(new Rect(0, 0, 2, 2))));
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5E)), null,
            new GeometryGroup { Children = { new RectangleGeometry(new Rect(0, 0, 1, 1)), new RectangleGeometry(new Rect(1, 1, 1, 1)) } }));
        var b = new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 12, 12), ViewportUnits = BrushMappingMode.Absolute };
        b.Freeze();
        return b;
    }
}
