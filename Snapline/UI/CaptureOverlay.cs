using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Snapline.Core;

namespace Snapline.UI;

public enum OverlayMode { Screenshot, Window, Text, RecordVideo, RecordGif }

public sealed record OverlayResult(Int32Rect Rect, PixelBuffer Frozen, WindowInfo? Window);

/// <summary>One frozen-screen selection session spanning every monitor (one overlay window per monitor).</summary>
public sealed class OverlaySession
{
    private readonly TaskCompletionSource<OverlayResult?> _tcs = new();
    private readonly List<CaptureOverlay> _overlays = new();
    private bool _done;

    private OverlaySession(OverlayMode mode, PixelBuffer frozen, List<WindowInfo> windows)
    {
        Mode = mode; Frozen = frozen; Windows = windows;
    }

    public OverlayMode Mode { get; }
    public PixelBuffer Frozen { get; }
    public List<WindowInfo> Windows { get; }
    internal IReadOnlyList<CaptureOverlay> Overlays => _overlays;

    public static Task<OverlayResult?> RunAsync(OverlayMode mode)
    {
        var frozen = ScreenCapture.Capture(ScreenCapture.VirtualScreen);
        var session = new OverlaySession(mode, frozen, WindowFinder.VisibleWindows());
        Native.GetCursorPos(out var cur);
        foreach (var m in Monitors.All())
        {
            var o = new CaptureOverlay(session, m, frozen.CropGlobal(m.Bounds).ToBitmapSource());
            session._overlays.Add(o);
            o.Show();
        }
        var active = session._overlays.FirstOrDefault(o => o.Monitor.Contains(cur.X, cur.Y)) ?? session._overlays[0];
        WindowUtil.ForceForeground(active);
        active.Activate();
        active.Focus();
        System.Windows.Input.Keyboard.Focus(active);
        return session._tcs.Task;
    }

    public void Finish(Int32Rect? rect, WindowInfo? window = null)
    {
        if (_done) return;
        _done = true;
        foreach (var o in _overlays) o.Close();
        _tcs.TrySetResult(rect is { } r ? new OverlayResult(r, Frozen, window) : null);
    }

    internal void PointerEntered(CaptureOverlay active)
    {
        foreach (var o in _overlays) if (o != active) o.SetPointerVisuals(false);
    }
}

public sealed class CaptureOverlay : Window
{
    private static readonly Color Accent = Color.FromRgb(0x0A, 0x84, 0xFF);

    private readonly OverlaySession _s;
    private readonly Canvas _canvas = new() { IsHitTestVisible = false };
    private readonly Path _dim;
    private readonly RectangleGeometry _hole = new(Rect.Empty);
    private readonly RectangleGeometry _hlGeo = new(Rect.Empty, 6, 6);
    private readonly Path _hlPath;
    private readonly RectangleGeometry _selGeo = new(Rect.Empty);
    private readonly Path _selPath, _selShadow, _handles;
    private readonly Line _hLine, _vLine;
    private readonly Border _sizePill;
    private readonly TextBlock _sizeText;
    private readonly Border _windowPill;
    private readonly TextBlock _windowText;
    private readonly Border _hint;
    private readonly Rectangle _flash;

    private double _dpi;
    private Point _down;
    private bool _isDown, _dragging, _hoverShown, _completing;
    private Rect _sel;
    private WindowInfo? _hoverWindow;
    private Int32Rect? _hoverRect;

    public CaptureOverlay(OverlaySession session, MonitorInfo monitor, System.Windows.Media.Imaging.BitmapSource background)
    {
        _s = session;
        Monitor = monitor;
        _dpi = monitor.Scale;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;
        UseLayoutRounding = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = monitor.Bounds.X / monitor.Scale;
        Top = monitor.Bounds.Y / monitor.Scale;
        Width = monitor.Bounds.Width / monitor.Scale;
        Height = monitor.Bounds.Height / monitor.Scale;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        var root = new Grid();
        var img = new Image { Source = background, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
        root.Children.Add(img);

        var gg = new GeometryGroup { FillRule = FillRule.EvenOdd };
        gg.Children.Add(new RectangleGeometry(new Rect(0, 0, 20000, 20000)));
        gg.Children.Add(_hole);
        _dim = new Path { Data = gg, Fill = new SolidColorBrush(Color.FromArgb(0x78, 0, 0, 0)), Opacity = 0, IsHitTestVisible = false };
        root.Children.Add(_dim);
        root.Children.Add(_canvas);

        // Hovered-window highlight
        _hlPath = new Path
        {
            Data = _hlGeo,
            Stroke = new SolidColorBrush(Accent),
            StrokeThickness = 2.5,
            Fill = new SolidColorBrush(Color.FromArgb(0x16, Accent.R, Accent.G, Accent.B)),
            Opacity = 0,
        };
        _canvas.Children.Add(_hlPath);

        // Selection rectangle
        _selShadow = new Path { Data = _selGeo, Stroke = new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)), StrokeThickness = 3 };
        _selPath = new Path { Data = _selGeo, Stroke = Brushes.White, StrokeThickness = 1.25 };
        _handles = new Path { Fill = Brushes.White, Stroke = new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)), StrokeThickness = 1 };
        _canvas.Children.Add(_selShadow);
        _canvas.Children.Add(_selPath);
        _canvas.Children.Add(_handles);

        // Crosshair
        var lineBrush = new SolidColorBrush(Color.FromArgb(0x8C, 255, 255, 255));
        _hLine = new Line { Stroke = lineBrush, StrokeThickness = 1, X1 = 0, X2 = 20000 };
        _vLine = new Line { Stroke = lineBrush, StrokeThickness = 1, Y1 = 0, Y2 = 20000 };
        _canvas.Children.Add(_hLine);
        _canvas.Children.Add(_vLine);

        // Window title pill
        _windowText = new TextBlock { Foreground = Brushes.White, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 360 };
        _windowPill = Pill(_windowText);
        _windowPill.Opacity = 0;
        _canvas.Children.Add(_windowPill);

        // Size pill
        _sizeText = new TextBlock { Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold };
        _sizePill = Pill(_sizeText);
        _sizePill.Visibility = Visibility.Collapsed;
        _canvas.Children.Add(_sizePill);

        // Flash used when the capture fires
        _flash = new Rectangle { Fill = Brushes.White, Opacity = 0 };
        _canvas.Children.Add(_flash);

        // Hint
        var hintText = new TextBlock { Foreground = Brushes.White, FontSize = 13 };
        hintText.Inlines.Add(new System.Windows.Documents.Run(ModeTitle(session.Mode)) { FontWeight = FontWeights.SemiBold });
        hintText.Inlines.Add(new System.Windows.Documents.Run("   " + ModeHint(session.Mode)) { Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC6)) });
        _hint = new Border
        {
            Child = hintText,
            Padding = new Thickness(16, 9, 16, 9),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1C, 0x1C, 0x1E)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 28, 0, 0),
            IsHitTestVisible = false,
            Opacity = 0,
            RenderTransform = new TranslateTransform(0, -10),
            Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 4, Direction = 270, Opacity = 0.45 },
        };
        root.Children.Add(_hint);

        Content = root;
        SetPointerVisuals(false);

        SourceInitialized += (_, _) => Place();
        Loaded += (_, _) =>
        {
            _dpi = WindowUtil.Dpi(this);
            Place();
            _dim.BeginAnimation(OpacityProperty, WindowUtil.Ease(0, 1, 170));
            if (monitor.Contains(CursorX, CursorY))
            {
                _hint.BeginAnimation(OpacityProperty, WindowUtil.Ease(0, 1, 260));
                ((TranslateTransform)_hint.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                    WindowUtil.Ease(-10, 0, 380, new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut }));
                var p = PointFromScreenPx(CursorX, CursorY);
                OnPointer(p);
            }
        };
        DpiChanged += (_, e) =>
        {
            _dpi = e.NewDpi.DpiScaleX;
            Dispatcher.BeginInvoke(Place);
        };

        MouseMove += (_, e) => OnPointer(e.GetPosition(this));
        MouseEnter += (_, _) => { SetPointerVisuals(true); _s.PointerEntered(this); };
        MouseLeave += (_, _) => { if (!_isDown) SetPointerVisuals(false); };
        MouseLeftButtonDown += OnDown;
        MouseLeftButtonUp += OnUp;
        MouseRightButtonUp += (_, _) => _s.Finish(null);
        KeyDown += OnKey;
    }

    public MonitorInfo Monitor { get; }

    private static int CursorX { get { Native.GetCursorPos(out var p); return p.X; } }
    private static int CursorY { get { Native.GetCursorPos(out var p); return p.Y; } }

    private bool AllowDrag => _s.Mode != OverlayMode.Window;

    private static string ModeTitle(OverlayMode m) => m switch
    {
        OverlayMode.Window => "Capture window",
        OverlayMode.Text => "Capture text",
        OverlayMode.RecordVideo => "Record video",
        OverlayMode.RecordGif => "Record GIF",
        _ => "Capture",
    };

    private static string ModeHint(OverlayMode m) => m switch
    {
        OverlayMode.Window => "Click a window  ·  Esc to cancel",
        OverlayMode.Text => "Drag over the text to copy  ·  Esc to cancel",
        OverlayMode.RecordVideo or OverlayMode.RecordGif => "Drag an area or click a window  ·  Esc to cancel",
        _ => "Drag an area  ·  Click a window  ·  Shift = square  ·  Esc to cancel",
    };

    private static Border Pill(UIElement child) => new()
    {
        Child = child,
        Padding = new Thickness(9, 4, 9, 4),
        CornerRadius = new CornerRadius(7),
        Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x1C, 0x1C, 0x1E)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)),
        BorderThickness = new Thickness(1),
    };

    private void Place()
    {
        WindowUtil.PlacePhysical(this, Monitor.Bounds);
    }

    internal void SetPointerVisuals(bool visible)
    {
        var v = visible ? Visibility.Visible : Visibility.Collapsed;
        _hLine.Visibility = _dragging ? Visibility.Collapsed : v;
        _vLine.Visibility = _dragging ? Visibility.Collapsed : v;
        if (!visible && !_dragging)
        {
            _hlPath.Opacity = 0;
            _windowPill.Opacity = 0;
            _hoverRect = null;
            _hoverWindow = null;
            SetHole(Rect.Empty, false);
        }
    }

    private Point PointFromScreenPx(int gx, int gy) => new((gx - Monitor.Bounds.X) / _dpi, (gy - Monitor.Bounds.Y) / _dpi);

    private Rect ToLocal(Int32Rect g) => new((g.X - Monitor.Bounds.X) / _dpi, (g.Y - Monitor.Bounds.Y) / _dpi, g.Width / _dpi, g.Height / _dpi);

    private Int32Rect ToGlobal(Rect local)
    {
        int x0 = (int)Math.Round(local.X * _dpi), y0 = (int)Math.Round(local.Y * _dpi);
        int x1 = (int)Math.Round(local.Right * _dpi), y1 = (int)Math.Round(local.Bottom * _dpi);
        return new Int32Rect(Monitor.Bounds.X + x0, Monitor.Bounds.Y + y0, x1 - x0, y1 - y0);
    }

    private static Int32Rect Intersect(Int32Rect a, Int32Rect b)
    {
        int x0 = Math.Max(a.X, b.X), y0 = Math.Max(a.Y, b.Y);
        int x1 = Math.Min(a.X + a.Width, b.X + b.Width), y1 = Math.Min(a.Y + a.Height, b.Y + b.Height);
        return x1 <= x0 || y1 <= y0 ? Int32Rect.Empty : new Int32Rect(x0, y0, x1 - x0, y1 - y0);
    }

    private void OnPointer(Point p)
    {
        if (_completing) return;
        SetPointerVisuals(true);
        int gx = Monitor.Bounds.X + (int)Math.Floor(p.X * _dpi);
        int gy = Monitor.Bounds.Y + (int)Math.Floor(p.Y * _dpi);

        _hLine.Y1 = _hLine.Y2 = Math.Round(p.Y) + 0.5;
        _vLine.X1 = _vLine.X2 = Math.Round(p.X) + 0.5;

        if (_isDown && AllowDrag)
        {
            if (!_dragging && (p - _down).Length > 4)
            {
                _dragging = true;
                _hLine.Visibility = _vLine.Visibility = Visibility.Collapsed;
                Fade(_hlPath, 0, 100);
                Fade(_windowPill, 0, 100);
                Fade(_hint, 0, 200);
                _sizePill.Visibility = Visibility.Visible;
            }
            if (_dragging) UpdateSelection(p);
            return;
        }
        if (!_isDown) UpdateHover(gx, gy);
    }

    private void UpdateHover(int gx, int gy)
    {
        var w = _s.Windows.FirstOrDefault(w =>
            gx >= w.Bounds.X && gx < w.Bounds.X + w.Bounds.Width && gy >= w.Bounds.Y && gy < w.Bounds.Y + w.Bounds.Height);
        var target = Intersect(w?.Bounds ?? Monitor.Bounds, Monitor.Bounds);
        if (target.IsEmpty) return;
        if (_hoverRect is { } cur && cur.Equals(target)) return;
        _hoverRect = target;
        _hoverWindow = w;

        var r = ToLocal(target);
        bool animate = _hoverShown;
        _hoverShown = true;
        if (animate)
        {
            var ra = new RectAnimation(r, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            _hlGeo.BeginAnimation(RectangleGeometry.RectProperty, ra);
        }
        else
        {
            _hlGeo.BeginAnimation(RectangleGeometry.RectProperty, null);
            _hlGeo.Rect = r;
        }
        Fade(_hlPath, 1, 120);
        SetHole(w == null ? Rect.Empty : r, animate);

        if (w != null && !string.IsNullOrWhiteSpace(w.Title))
        {
            _windowText.Text = $"{w.Title}   {target.Width} × {target.Height}";
            _windowPill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double top = r.Top + 10;
            Canvas.SetLeft(_windowPill, Math.Max(4, r.Left + (r.Width - _windowPill.DesiredSize.Width) / 2));
            Canvas.SetTop(_windowPill, Math.Max(4, top));
            Fade(_windowPill, 1, 120);
        }
        else Fade(_windowPill, 0, 100);
    }

    private void SetHole(Rect r, bool animate)
    {
        if (animate && !r.IsEmpty && !_hole.Rect.IsEmpty)
        {
            var ra = new RectAnimation(r, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            _hole.BeginAnimation(RectangleGeometry.RectProperty, ra);
        }
        else
        {
            _hole.BeginAnimation(RectangleGeometry.RectProperty, null);
            _hole.Rect = r;
        }
    }

    private void UpdateSelection(Point p)
    {
        var a = _down;
        var b = new Point(Math.Clamp(p.X, 0, ActualWidth), Math.Clamp(p.Y, 0, ActualHeight));
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            double s = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
            b = new Point(a.X + Math.Sign(b.X - a.X) * s, a.Y + Math.Sign(b.Y - a.Y) * s);
        }
        _sel = new Rect(a, b);
        SetHole(_sel, false);
        _selGeo.Rect = _sel;

        double hr = 3.5;
        var hg = new GeometryGroup();
        foreach (var c in new[] { _sel.TopLeft, _sel.TopRight, _sel.BottomLeft, _sel.BottomRight })
            hg.Children.Add(new EllipseGeometry(c, hr, hr));
        _handles.Data = hg;

        var g = ToGlobal(_sel);
        _sizeText.Text = $"{g.Width} × {g.Height}";
        _sizePill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var ps = _sizePill.DesiredSize;
        double x = _sel.Right - ps.Width, y = _sel.Bottom + 8;
        if (y + ps.Height > ActualHeight - 4) y = _sel.Bottom - ps.Height - 8;
        if (x < 4) x = 4;
        Canvas.SetLeft(_sizePill, x);
        Canvas.SetTop(_sizePill, y);
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (_completing) return;
        _down = e.GetPosition(this);
        _isDown = true;
        CaptureMouse();
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDown || _completing) return;
        _isDown = false;
        ReleaseMouseCapture();
        if (_dragging)
        {
            _dragging = false;
            var g = ToGlobal(_sel);
            if (g.Width >= 4 && g.Height >= 4)
            {
                Complete(g, _sel, null);
                return;
            }
            _selGeo.Rect = Rect.Empty;
            _handles.Data = null;
            _sizePill.Visibility = Visibility.Collapsed;
        }
        if (_hoverRect is { } hr) Complete(hr, ToLocal(hr), _hoverWindow);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) _s.Finish(null);
        else if (e.Key == Key.Enter && _hoverRect is { } hr) Complete(hr, ToLocal(hr), _hoverWindow);
    }

    private async void Complete(Int32Rect global, Rect local, WindowInfo? window)
    {
        if (_completing) return;
        _completing = true;
        bool still = _s.Mode is OverlayMode.Screenshot or OverlayMode.Window or OverlayMode.Text;
        if (still)
        {
            _hLine.Visibility = _vLine.Visibility = Visibility.Collapsed;
            Canvas.SetLeft(_flash, local.X);
            Canvas.SetTop(_flash, local.Y);
            _flash.Width = local.Width;
            _flash.Height = local.Height;
            _flash.BeginAnimation(OpacityProperty, WindowUtil.Ease(0.55, 0, 150));
            await Task.Delay(110);
        }
        _s.Finish(global, window);
    }

    private static void Fade(UIElement e, double to, int ms) => e.BeginAnimation(OpacityProperty, WindowUtil.Ease(null, to, ms));
}
