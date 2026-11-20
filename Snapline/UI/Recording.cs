using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Snapline.Core;

namespace Snapline.UI;

public sealed class RecordingSession
{
    private readonly Int32Rect _region;
    private readonly bool _gif;
    private readonly MonitorInfo _mon;
    private readonly DispatcherTimer _tick;
    private IFrameRecorder? _rec;
    private RecordingFrame? _frame;
    private RecordingHud? _hud;
    private bool _stopping, _cancelled;

    private RecordingSession(Int32Rect region, bool gif)
    {
        _region = region;
        _gif = gif;
        _mon = Monitors.FromPoint(region.X + region.Width / 2, region.Y + region.Height / 2);
        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _tick.Tick += (_, _) => { if (_rec != null) _hud?.SetTime(_rec.Elapsed); };
    }

    public static RecordingSession? Current { get; private set; }
    public bool IsGif => _gif;

    public static async Task StartAsync(Int32Rect region, bool gif)
    {
        if (Current != null) return;
        int w = Math.Max(16, region.Width & ~1), h = Math.Max(16, region.Height & ~1);
        var s = new RecordingSession(new Int32Rect(region.X, region.Y, w, h), gif);
        Current = s;
        try
        {
            await s.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            s.Cleanup();
            Current = null;
            Snap.Quick.ShowInfo("Recording failed", ex.Message, "");
        }
    }

    private async Task RunAsync()
    {
        _frame = new RecordingFrame(_region);
        _frame.Show();
        if (Snap.Settings.Countdown) await _frame.CountdownAsync(() => _cancelled);
        if (_cancelled) { Cleanup(); Current = null; return; }

        _hud = new RecordingHud(this);
        _hud.ShowNear(_region, _mon);

        var path = CaptureStore.UniquePath(Snap.Settings.SaveFolder, _gif ? ".gif" : ".mp4");
        _rec = _gif
            ? new GifRecorder(_region, 15, Snap.Settings.RecordCursor, path)
            : new Mp4Recorder(_region, Snap.Settings.RecordFps, Snap.Settings.RecordCursor, path);
        await _rec.StartAsync();
        _frame.SetRecording();
        _tick.Start();
    }

    public void TogglePause()
    {
        if (_rec == null || _stopping) return;
        _rec.Paused = !_rec.Paused;
        _hud?.SetPaused(_rec.Paused);
        _frame?.SetPaused(_rec.Paused);
    }

    public async Task StopAsync(bool cancel = false)
    {
        if (_rec == null) { _cancelled = true; return; } // still counting down
        if (_stopping) return;
        _stopping = true;
        _tick.Stop();
        _hud?.SetBusy(cancel ? "Discarding…" : _gif ? "Encoding GIF…" : "Saving video…");
        _frame?.Close();
        _frame = null;

        string? path = null;
        Exception? error = null;
        var duration = _rec.Elapsed;
        try { path = await _rec.StopAsync(); }
        catch (Exception ex) { error = ex; Log.Error(ex); }

        Cleanup();
        Current = null;

        if (error != null) { Snap.Quick.ShowInfo("Recording failed", error.Message, ""); return; }
        if (cancel)
        {
            try { if (path != null) System.IO.File.Delete(path); } catch { }
            return;
        }
        var thumb = _rec.Thumbnail ?? Placeholder();
        Snap.Quick.ShowVideo(new CaptureItem(thumb) { FilePath = path, IsVideo = true, Duration = duration });
    }

    private void Cleanup()
    {
        _tick.Stop();
        _frame?.Close();
        _frame = null;
        _hud?.Close();
        _hud = null;
    }

    private static BitmapSource Placeholder()
    {
        var px = new byte[16 * 9 * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = 0x24; px[i + 1] = 0x22; px[i + 2] = 0x22; px[i + 3] = 255; }
        var b = BitmapSource.Create(16, 9, 96, 96, PixelFormats.Bgra32, null, px, 64);
        b.Freeze();
        return b;
    }
}

/// <summary>Click-through dashed frame drawn just outside the recorded region (also hosts the countdown).</summary>
public sealed class RecordingFrame : Window
{
    private const int FrameMargin = 10;
    private readonly Int32Rect _r;
    private readonly Rectangle _border, _shade;
    private readonly Border _count;
    private readonly TextBlock _countText;
    private readonly ScaleTransform _countScale = new(1, 1);

    public RecordingFrame(Int32Rect region)
    {
        _r = region;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = 10; Height = 10;

        _shade = new Rectangle { Stroke = new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)), StrokeThickness = 4, RadiusX = 3, RadiusY = 3 };
        _border = new Rectangle
        {
            Stroke = Brushes.White,
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            RadiusX = 3,
            RadiusY = 3,
        };
        _countText = new TextBlock
        {
            FontSize = 60,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
        };
        _count = new Border
        {
            Width = 124,
            Height = 124,
            CornerRadius = new CornerRadius(62),
            Background = new SolidColorBrush(Color.FromArgb(0xC8, 0x1C, 0x1C, 0x1E)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = _countText,
            Opacity = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _countScale,
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Direction = 270, Opacity = 0.5 },
        };
        var root = new Grid();
        root.Children.Add(_shade);
        root.Children.Add(_border);
        root.Children.Add(_count);
        Content = root;

        SourceInitialized += (_, _) =>
        {
            WindowUtil.SetToolWindow(this, noActivate: true, clickThrough: true);
            if (!Dev.Active) WindowUtil.ExcludeFromCapture(this);
            Place();
        };
        Loaded += (_, _) => Place();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Place);
    }

    private void Place()
    {
        WindowUtil.PlacePhysical(this, new Int32Rect(_r.X - FrameMargin, _r.Y - FrameMargin, _r.Width + 2 * FrameMargin, _r.Height + 2 * FrameMargin));
        double m = FrameMargin / WindowUtil.Dpi(this);
        _border.Margin = new Thickness(Math.Max(0, m - 3));
        _shade.Margin = new Thickness(Math.Max(0, m - 4));
    }

    public async Task CountdownAsync(Func<bool> cancelled)
    {
        var ease = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut };
        for (int i = 3; i >= 1; i--)
        {
            if (cancelled()) return;
            _countText.Text = i.ToString();
            _countScale.BeginAnimation(ScaleTransform.ScaleXProperty, WindowUtil.Ease(1.35, 1, 320, ease));
            _countScale.BeginAnimation(ScaleTransform.ScaleYProperty, WindowUtil.Ease(1.35, 1, 320, ease));
            _count.BeginAnimation(OpacityProperty, WindowUtil.Ease(i == 3 ? 0 : null, 1, 160));
            await Task.Delay(780);
        }
        _count.BeginAnimation(OpacityProperty, WindowUtil.Ease(null, 0, 140));
        await Task.Delay(220);
    }

    public void SetRecording()
    {
        _border.Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
        _border.StrokeDashArray = null;
    }

    public void SetPaused(bool paused) =>
        _border.Stroke = paused ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD6, 0x0A)) : new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
}

/// <summary>Small floating control bar: timer, pause, stop, discard.</summary>
public sealed class RecordingHud : Window
{
    private readonly RecordingSession _s;
    private readonly TextBlock _time;
    private readonly Ellipse _dot;
    private readonly Button _pause;
    private readonly StackPanel _controls;
    private readonly TextBlock _busy;
    private readonly TranslateTransform _slide = new(0, 10);

    public RecordingHud(RecordingSession session)
    {
        _s = session;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        Opacity = 0;

        _dot = new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var pulse = new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(800)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
        _dot.BeginAnimation(OpacityProperty, pulse);

        _time = new TextBlock
        {
            Text = "0:00",
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(9, 0, 12, 0),
            MinWidth = 44,
        };
        var iconStyle = (Style)Application.Current.FindResource("IconButton");
        _pause = new Button { Content = "", Style = iconStyle, ToolTip = "Pause", Foreground = Brushes.White };
        _pause.Click += (_, _) => _s.TogglePause();

        var stopBox = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = Brushes.White };
        var stopContent = new StackPanel { Orientation = Orientation.Horizontal };
        stopContent.Children.Add(stopBox);
        stopContent.Children.Add(new TextBlock { Text = "Stop", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0), FontFamily = FontFamily, FontSize = 13 });
        var stop = new Button { Content = stopContent, Style = (Style)Application.Current.FindResource("PrimaryButton"), Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)), Padding = new Thickness(12, 6, 14, 6), Margin = new Thickness(4, 0, 2, 0) };
        stop.Click += async (_, _) => await _s.StopAsync();

        var discard = new Button { Content = "", Style = iconStyle, ToolTip = "Discard recording", Foreground = Brushes.White };
        discard.Click += async (_, _) => await _s.StopAsync(cancel: true);

        _controls = new StackPanel { Orientation = Orientation.Horizontal };
        _controls.Children.Add(_pause);
        _controls.Children.Add(discard);
        _controls.Children.Add(stop);

        _busy = new TextBlock { Foreground = Brushes.White, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Visibility = Visibility.Collapsed };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_dot);
        row.Children.Add(_time);
        row.Children.Add(new Border { Width = 1, Height = 20, Background = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)), Margin = new Thickness(0, 0, 6, 0) });
        row.Children.Add(_controls);
        row.Children.Add(_busy);

        var pill = new Border
        {
            Child = row,
            Margin = new Thickness(16),
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x1C, 0x1C, 0x1E)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 5, Direction = 270, Opacity = 0.5 },
            RenderTransform = _slide,
            Cursor = Cursors.SizeAll,
        };
        pill.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is not Button) DragMove(); };
        Content = pill;

        SourceInitialized += (_, _) =>
        {
            WindowUtil.SetToolWindow(this);
            if (!Dev.Active) WindowUtil.ExcludeFromCapture(this);
        };
    }

    public void ShowNear(Int32Rect r, MonitorInfo mon)
    {
        Show();
        UpdateLayout();
        int w = (int)Math.Ceiling(ActualWidth * mon.Scale), h = (int)Math.Ceiling(ActualHeight * mon.Scale);
        int x = r.X + r.Width / 2 - w / 2;
        int y = r.Y + r.Height + 2;
        var b = mon.WorkArea;
        if (y + h > b.Y + b.Height) y = r.Y - h - 2;
        if (y < b.Y) y = r.Y + r.Height - h - 12;
        x = Math.Clamp(x, b.X, b.X + b.Width - w);
        WindowUtil.MovePhysical(this, x, y);
        BeginAnimation(OpacityProperty, WindowUtil.Ease(0, 1, 200));
        _slide.BeginAnimation(TranslateTransform.YProperty, WindowUtil.Ease(10, 0, 380, new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut }));
    }

    public void SetTime(TimeSpan t) => _time.Text = t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    public void SetPaused(bool paused)
    {
        _pause.Content = paused ? "" : "";
        _pause.ToolTip = paused ? "Resume" : "Pause";
        _dot.BeginAnimation(OpacityProperty, paused ? null : new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(800)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        _dot.Fill = new SolidColorBrush(paused ? Color.FromRgb(0xFF, 0xD6, 0x0A) : Color.FromRgb(0xFF, 0x45, 0x3A));
    }

    public void SetBusy(string text)
    {
        _controls.Visibility = Visibility.Collapsed;
        _busy.Text = text;
        _busy.Visibility = Visibility.Visible;
        _dot.Fill = new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF));
    }
}
