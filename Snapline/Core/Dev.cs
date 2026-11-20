using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapline.Editor;
using Snapline.UI;

namespace Snapline.Core;

/// <summary>Self-test harness: `Snapline.exe --dev &lt;outdir&gt;` drives every surface and writes screenshots/files for inspection.</summary>
internal static class Dev
{
    public static bool Active { get; private set; }
    private static string _out = "";
    private static readonly List<string> Report = new();

    public static async Task RunAsync(string[] args)
    {
        Active = true;
        _out = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "snapline-dev");
        Directory.CreateDirectory(_out);
        Snap.Settings.SaveFolder = Path.Combine(_out, "saved");
        Snap.Settings.AutoCopy = false;
        string only = args.Length > 2 ? args[2] : "all";
        try
        {
            if (only is "all" or "overlay") await Step("overlay", Overlay);
            if (only is "all" or "quick") await Step("quick", Quick);
            if (only is "all" or "editor") await Step("editor", EditorTest);
            if (only is "all" or "combine") await Step("combine", Combine);
            if (only is "all" or "flyout") await Step("flyout", Flyout);
            if (only is "all" or "mp4") await Step("mp4", () => Record(false));
            if (only is "all" or "gif") await Step("gif", () => Record(true));
            if (only is "all" or "ocr") await Step("ocr", Ocr);
            // Not part of "all": these never put a window on screen.
            if (only is "ui") await Step("ui", RenderUi);
            if (only is "docs") await Step("docs", RenderDocs);
            if (only is "ota") await Step("ota", () => Ota(args.Length > 3 ? args[3] : ""));
        }
        finally
        {
            File.WriteAllLines(Path.Combine(_out, "report.txt"), Report);
            Application.Current.Shutdown();
        }
    }

    private static async Task Step(string name, Func<Task> f)
    {
        try { await f(); Report.Add($"{name}: OK"); }
        catch (Exception ex) { Report.Add($"{name}: FAIL {ex}"); }
    }

    private static void Shot(string name, Int32Rect? area = null)
    {
        var px = ScreenCapture.Capture(area ?? Monitors.Primary.Bounds);
        CaptureStore.SavePng(px.ToBitmapSource(), Path.Combine(_out, name + ".png"));
    }

    private static Int32Rect PhysicalBounds(Window w)
    {
        Native.GetWindowRect(WindowUtil.Hwnd(w), out var r);
        return new Int32Rect(r.Left, r.Top, r.Width, r.Height);
    }

    private static BitmapSource Sample(int w = 1100, int h = 680)
    {
        var m = Monitors.Primary.Bounds;
        return ScreenCapture.Capture(new Int32Rect(m.X + 40, m.Y + 40, Math.Min(w, m.Width - 80), Math.Min(h, m.Height - 80))).ToBitmapSource();
    }

    private static async Task Overlay()
    {
        var task = OverlaySession.RunAsync(OverlayMode.Screenshot);
        await Task.Delay(700);
        Shot("01-overlay");
        foreach (Window w in Application.Current.Windows)
            if (w is CaptureOverlay) { w.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(w)!, 0, System.Windows.Input.Key.Escape) { RoutedEvent = UIElement.KeyDownEvent }); break; }
        var r = await task;
        if (r != null) throw new Exception("Expected cancel");
    }

    private static async Task Quick()
    {
        Snap.Deliver(Sample(900, 520), null);
        await Task.Delay(300);
        Snap.Quick.ShowText("Hello from OCR\nSecond line of recognised text");
        Snap.Quick.ShowInfo("Snapline is ready", "Ctrl+Shift+1 to capture · Ctrl+Shift+4 to record.", "");
        await Task.Delay(900);
        Shot("02-quick");
        Snap.Quick.DismissAll();
        await Task.Delay(800);
    }

    private static async Task EditorTest()
    {
        var img = Sample();
        var ed = new EditorWindow(img);
        ed.Show();
        await Task.Delay(400);
        var surface = FindSurface(ed)!;
        double u = surface.StrokeWidth;
        surface.Items.Add(new ArrowAnnotation { A = new Point(120, 520), B = new Point(430, 260), Color = Color.FromRgb(0xFF, 0x3B, 0x30), Stroke = u });
        surface.Items.Add(new RectAnnotation { A = new Point(460, 160), B = new Point(760, 330), Color = Color.FromRgb(0xFF, 0xD6, 0x0A), Stroke = u });
        surface.Items.Add(new CounterAnnotation { Center = new Point(460, 160), Number = 1, Color = Color.FromRgb(0x0A, 0x84, 0xFF), Stroke = u });
        surface.Items.Add(new TextAnnotation { Pos = new Point(140, 560), Text = "Look here!", FontSize = surface.FontSizeForLevel, Color = Color.FromRgb(0xFF, 0x3B, 0x30), Stroke = u });
        var px = new PixelateAnnotation { Area = new Rect(820, 380, 220, 120), Stroke = u };
        px.Rebuild(surface.Base, surface.Unit);
        surface.Items.Add(px);
        surface.InvalidateVisual();
        ed.BgToggle.IsChecked = true;
        await Task.Delay(900);
        Shot("03-editor", PhysicalBounds(ed));
        var outImg = ed.Render();
        CaptureStore.SavePng(outImg, Path.Combine(_out, "03-editor-export.png"));
        ed.Close();
    }

    private static AnnotationSurface? FindSurface(DependencyObject d)
    {
        if (d is AnnotationSurface s) return s;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            if (FindSurface(VisualTreeHelper.GetChild(d, i)) is { } f) return f;
        return null;
    }

    private static async Task Combine()
    {
        CaptureStore.Add(new CaptureItem(Sample(700, 460)));
        CaptureStore.Add(new CaptureItem(Sample(520, 640)));
        Snap.OpenCombine();
        await Task.Delay(900);
        var w = Application.Current.Windows.OfType<CombineWindow>().First();
        Shot("04-combine", PhysicalBounds(w));
        w.Close();
    }

    /// <summary>Renders a window's content off-screen at 2x on a transparent background.</summary>
    private static void RenderOffscreen(Window w, string name, double width)
    {
        var content = (FrameworkElement)w.Content;
        w.Content = null;
        var host = new System.Windows.Controls.Border { Child = content, Width = width };
        host.Resources.MergedDictionaries.Add(Snapline.Shared.Ui.Scrollbars);
        System.Windows.Documents.TextElement.SetFontFamily(host, Snapline.Shared.Ui.Font);
        host.Measure(new Size(width, double.PositiveInfinity));
        host.Arrange(new Rect(host.DesiredSize));
        host.UpdateLayout();
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth * 2), (int)Math.Ceiling(host.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        rtb.Render(host);
        CaptureStore.SavePng(rtb, Path.Combine(_out, name + ".png"));
        Report.Add($"  rendered {name}: {host.ActualWidth:0}x{host.ActualHeight:0}");
    }

    private static async Task RenderUi()
    {
        var s = SettingsWindow.CreateHidden();
        if (((FrameworkElement)s.Content) is { } root)
            foreach (var sv in FindAll<System.Windows.Controls.ScrollViewer>(root)) sv.MaxHeight = double.PositiveInfinity;
        RenderOffscreen(s, "ui-settings", s.Width);

        Updates.UpdateHost.Icon = TrayIcon.Logo;
        var info = new Updates.UpdateInfo
        {
            Version = new Version(1, 1, 0), Published = "2026-12-01",
            Notes = { "Print Screen opens Snapline anywhere.", "New settings window.", "Updates install themselves after you say yes." },
        };
        var u = Updates.UpdateUI.BuildUpdateWindow(info);
        RenderOffscreen(u, "ui-update", u.Width);
        await Task.CompletedTask;
    }

    // ---------- README images: rendered off-screen from the real UI, with made-up content only ----------

    private static void RenderElement(FrameworkElement e, string name, double width, double height = double.NaN, double scale = 2)
    {
        e.Width = width;
        if (!double.IsNaN(height)) e.Height = height;
        e.Measure(new Size(width, double.IsNaN(height) ? double.PositiveInfinity : height));
        e.Arrange(new Rect(e.DesiredSize));
        e.UpdateLayout();
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(e.ActualWidth * scale), (int)Math.Ceiling(e.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(e);
        CaptureStore.SavePng(rtb, Path.Combine(_out, name + ".png"));
        Report.Add($"  rendered {name}: {e.ActualWidth:0}x{e.ActualHeight:0}");
    }

    /// <summary>Renders an element at its natural size, margins (and the shadows drawn in them) included.</summary>
    private static void RenderNatural(FrameworkElement e, string name, double scale = 2)
    {
        var host = new System.Windows.Controls.Border { Child = e };
        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        host.Arrange(new Rect(host.DesiredSize));
        host.UpdateLayout();
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth * scale), (int)Math.Ceiling(host.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(host);
        CaptureStore.SavePng(rtb, Path.Combine(_out, name + ".png"));
        Report.Add($"  rendered {name}: {host.ActualWidth:0}x{host.ActualHeight:0}");
    }

    private static FrameworkElement Detach(Window w)
    {
        var c = (FrameworkElement)w.Content;
        w.Content = null;
        return c;
    }

    private static SolidColorBrush B(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    /// <summary>A made-up dashboard to annotate in the README shots (no real screen content).</summary>
    private static BitmapSource FakeApp(int w, int h, string title, string accent, int seed)
    {
        var rnd = new Random(seed);
        var g = new System.Windows.Controls.Grid { Width = w, Height = h, Background = B("#F6F7FB") };
        var c = new System.Windows.Controls.Canvas();
        g.Children.Add(c);
        void Box(double x, double y, double bw, double bh, string fill, double r = 10)
        {
            var b = new System.Windows.Controls.Border { Width = bw, Height = bh, Background = B(fill), CornerRadius = new CornerRadius(r) };
            System.Windows.Controls.Canvas.SetLeft(b, x); System.Windows.Controls.Canvas.SetTop(b, y); c.Children.Add(b);
        }
        void Text(double x, double y, string t, double size, string fg, bool bold = false)
        {
            var tb = new System.Windows.Controls.TextBlock { Text = t, FontSize = size, Foreground = B(fg), FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
            System.Windows.Controls.Canvas.SetLeft(tb, x); System.Windows.Controls.Canvas.SetTop(tb, y); c.Children.Add(tb);
        }
        Box(0, 0, w, 56, "#FFFFFF", 0);
        Box(20, 16, 24, 24, accent, 7);
        Text(56, 14, title, 18, "#1B1E2B", true);
        Box(w - 160, 14, 136, 28, "#EEF0F6", 14);
        Box(0, 56, 190, h - 56, "#FFFFFF", 0);
        string[] nav = { "Overview", "Projects", "Reports", "Team", "Settings" };
        for (int i = 0; i < nav.Length; i++)
        {
            if (i == 0) Box(12, 76, 166, 34, "#EEF0F6", 8);
            Text(28, 82 + i * 44, nav[i], 14, i == 0 ? "#1B1E2B" : "#6B7184", i == 0);
        }
        double cx = 214, cw = w - cx - 24;
        string[] kpi = { "Active users", "Sessions", "Conversion" };
        string[] val = { "12,480", "48.2k", "3.9%" };
        double kw = (cw - 32) / 3;
        for (int i = 0; i < 3; i++)
        {
            Box(cx + i * (kw + 16), 80, kw, 92, "#FFFFFF", 12);
            Text(cx + i * (kw + 16) + 18, 96, kpi[i], 13, "#6B7184");
            Text(cx + i * (kw + 16) + 18, 118, val[i], 26, "#1B1E2B", true);
        }
        double top = 192, ch = h - top - 24;
        Box(cx, top, cw * 0.62, ch, "#FFFFFF", 12);
        Text(cx + 18, top + 14, "Weekly signups", 14, "#1B1E2B", true);
        int bars = 12;
        double bw2 = (cw * 0.62 - 60) / bars;
        for (int i = 0; i < bars; i++)
        {
            double bh = 40 + rnd.NextDouble() * (ch - 110);
            Box(cx + 24 + i * bw2, top + ch - 24 - bh, bw2 - 10, bh, i == bars - 3 ? accent : "#C9CEE0", 5);
        }
        double rx = cx + cw * 0.62 + 16, rw = cw * 0.38 - 16;
        Box(rx, top, rw, ch, "#FFFFFF", 12);
        Text(rx + 18, top + 14, "Recent activity", 14, "#1B1E2B", true);
        for (int i = 0; i < 6 && 52 + i * 46 < ch - 30; i++)
        {
            Box(rx + 18, top + 52 + i * 46, 28, 28, i % 2 == 0 ? accent : "#FFB547", 14);
            Box(rx + 56, top + 56 + i * 46, rw * (0.4 + rnd.NextDouble() * 0.35), 9, "#D9DCE7", 4);
            Box(rx + 56, top + 71 + i * 46, rw * 0.3, 7, "#ECEEF4", 3);
        }
        g.Measure(new Size(w, h)); g.Arrange(new Rect(0, 0, w, h)); g.UpdateLayout();
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(g);
        rtb.Freeze();
        return rtb;
    }

    private static async Task RenderDocs()
    {
        // Neutral values so nothing personal appears in the images.
        Snap.Settings.SaveFolder = @"C:\Users\You\Pictures\Snapline";
        Snap.Settings.AutoCopy = true;
        Snap.Settings.AutoSave = false;
        Updates.UpdateHost.Icon = TrayIcon.Logo;

        // Settings
        var s = SettingsWindow.CreateHidden();
        foreach (var sv in FindAll<System.Windows.Controls.ScrollViewer>((FrameworkElement)s.Content)) sv.MaxHeight = double.PositiveInfinity;
        RenderOffscreen(s, "shot-settings", s.Width);

        // Update window
        var info = new Updates.UpdateInfo
        {
            Version = new Version(1, 1, 0), Published = "2026-12-01",
            Notes = { "Scrolling capture for long pages.", "Faster GIF export.", "Small fixes and polish." },
        };
        var u = Updates.UpdateUI.BuildUpdateWindow(info);
        RenderOffscreen(u, "shot-update", u.Width);

        // Editor with annotations and a background
        var img = FakeApp(1100, 680, "Northwind Analytics", "#5E5CE6", 7);
        var ed = new EditorWindow(img);
        var edRoot = Detach(ed);
        var surface = FindSurfaceLogical(edRoot) ?? throw new Exception("no surface");
        double unit = surface.StrokeWidth;
        surface.Items.Add(new ArrowAnnotation { A = new Point(560, 600), B = new Point(760, 430), Color = Color.FromRgb(0xFF, 0x3B, 0x30), Stroke = unit });
        surface.Items.Add(new RectAnnotation { A = new Point(214, 80), B = new Point(560, 176), Color = Color.FromRgb(0xFF, 0xD6, 0x0A), Stroke = unit });
        surface.Items.Add(new CounterAnnotation { Center = new Point(214, 80), Number = 1, Color = Color.FromRgb(0x0A, 0x84, 0xFF), Stroke = unit });
        surface.Items.Add(new TextAnnotation { Pos = new Point(330, 612), Text = "Best week so far", FontSize = surface.FontSizeForLevel, Color = Color.FromRgb(0xFF, 0x3B, 0x30), Stroke = unit });
        var px = new PixelateAnnotation { Area = new Rect(884, 236, 190, 150), Stroke = unit };
        px.Rebuild(surface.Base, surface.Unit);
        surface.Items.Add(px);
        ed.BgToggle.IsChecked = true;
        RenderElement(edRoot, "shot-editor", 1320, 900, 1.5);
        var exported = ed.Render();
        CaptureStore.SavePng(exported, Path.Combine(_out, "shot-export.png"));
        Report.Add("  rendered shot-export");

        // Combine
        CaptureStore.Add(new CaptureItem(FakeApp(760, 500, "Roadmap", "#11998E", 3)));
        CaptureStore.Add(new CaptureItem(FakeApp(560, 680, "Inbox", "#FF5F6D", 5)));
        var cw = new CombineWindow();
        foreach (var it in CaptureStore.History.Take(2).Reverse()) cw.AddImage(it.Image);
        RenderElement(Detach(cw), "shot-combine", 1320, 860, 1.5);

        // Tray panel
        var fly = new TrayFlyout();
        var flyRoot = Detach(fly);
        var refresh = typeof(TrayFlyout).GetMethod("Refresh", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        // Refresh needs the named elements, which stay with the detached content.
        refresh?.Invoke(fly, null);
        RenderNatural(flyRoot, "shot-panel");

        // Tray menu
        var m = new Snapline.Shared.UiMenu(336);
        m.AddHeader(TrayIcon.Logo, "Snapline", "Print Screen to capture");
        m.AddItem("\uE722", "Capture area", "PrtSc", () => { });
        m.AddItem("\uE7F4", "Capture fullscreen", "Ctrl+Shift+2", () => { });
        m.AddItem("\uE714", "Record video", "Ctrl+Shift+4", () => { });
        m.AddItem("\uE8C1", "Copy text from screen", "Ctrl+Shift+6", () => { });
        m.AddItem("\uF0E2", "Combine screenshots", "Ctrl+Shift+7", () => { });
        m.AddSeparator();
        m.AddItem("\uE838", "Open captures folder", null, () => { });
        m.AddItem("\uE713", "Settings…", null, () => { });
        m.AddItem("\uE895", "Check for updates…", null, () => { });
        m.AddSeparator();
        m.AddItem("\uE7E8", "Quit Snapline", null, () => { }, danger: true);
        var mr = Detach(m);
        mr.Resources.MergedDictionaries.Add(Snapline.Shared.Ui.Scrollbars);
        System.Windows.Documents.TextElement.SetFontFamily(mr, Snapline.Shared.Ui.Font);
        RenderNatural(mr, "shot-menu");

        RenderNatural(Banner(), "banner", 1);

        await Task.CompletedTask;
    }

    private static FrameworkElement Banner()
    {
        var g = new System.Windows.Controls.Grid { Width = 1600, Height = 420, ClipToBounds = true };
        g.Background = new LinearGradientBrush(Color.FromRgb(0x14, 0x0F, 0x22), Color.FromRgb(0x07, 0x05, 0x0F), 90);
        var glow = new System.Windows.Shapes.Ellipse { Width = 1100, Height = 700, Margin = new Thickness(0, -420, 0, 0), VerticalAlignment = VerticalAlignment.Top,
            Fill = new RadialGradientBrush(Color.FromArgb(0x70, 0x5E, 0x5C, 0xE6), Color.FromArgb(0, 0x0A, 0x84, 0xFF)) };
        g.Children.Add(glow);
        var sp = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(new System.Windows.Controls.Image { Source = TrayIcon.Logo, Width = 150, Height = 150, Margin = new Thickness(0, 0, 44, 0),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0x0A, 0x84, 0xFF), BlurRadius = 60, ShadowDepth = 0, Opacity = 0.6 } });
        var texts = new System.Windows.Controls.StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new System.Windows.Controls.TextBlock { Text = "Snapline", FontSize = 96, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI") });
        texts.Children.Add(new System.Windows.Controls.TextBlock { Text = "Screenshots, recordings and annotations for Windows", FontSize = 30, Foreground = B("#C9C9D8"), Margin = new Thickness(4, 0, 0, 0), FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI") });
        sp.Children.Add(texts);
        g.Children.Add(sp);
        return g;
    }

    private static AnnotationSurface? FindSurfaceLogical(DependencyObject d)
    {
        if (d is AnnotationSurface s) return s;
        foreach (var child in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>())
            if (FindSurfaceLogical(child) is { } f) return f;
        return null;
    }

    private static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T t) yield return t;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var x in FindAll<T>(child)) yield return x;
    }

    /// <summary>
    /// Update check against a local feed (file:///...update.json): the genuine installer must pass,
    /// and a changed byte, a changed file with matching checksum, and a junk signature must all be refused.
    /// The user's update.ini is restored afterwards.
    /// </summary>
    private static async Task Ota(string feed)
    {
        string ini = Path.Combine(Log.Dir, "update.ini");
        byte[]? backup = File.Exists(ini) ? File.ReadAllBytes(ini) : null;
        try
        {
            Updates.UpdateSettings.UpdateFeed = feed;
            var u = await Task.Run(Updates.Updater.Check) ?? throw new Exception("feed says no update (version not newer?)");
            Report.Add($"  found {u.Version} (running {Updates.Updater.CurrentText}), {u.Notes.Count} notes");
            string path = await Task.Run(() => Updates.Updater.Download(u, null!));
            var data = File.ReadAllBytes(path);
            Report.Add($"  genuine accepted: {path} ({data.Length} bytes)");

            void MustFail(string label, byte[] bytes, Updates.UpdateInfo info)
            {
                try { Updates.Updater.Verify(bytes, info); Report.Add($"  {label}: ACCEPTED (BAD)"); throw new Exception(label + " was accepted"); }
                catch (System.Security.Cryptography.CryptographicException ex) { Report.Add($"  {label}: rejected ({ex.Message})"); }
            }
            var changed = (byte[])data.Clone();
            changed[changed.Length / 2] ^= 0xFF;
            MustFail("one changed byte", changed, u);
            string changedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(changed)).ToLowerInvariant();
            MustFail("changed file + matching checksum", changed, new Updates.UpdateInfo { Sha256 = changedHash, Signature = u.Signature, Url = u.Url, Version = u.Version });
            MustFail("junk signature", data, new Updates.UpdateInfo { Sha256 = u.Sha256, Signature = Convert.ToBase64String(new byte[384]), Url = u.Url, Version = u.Version });
            File.Delete(path);
        }
        finally
        {
            Updates.UpdateSettings.UpdateFeed = "";
            if (backup != null) File.WriteAllBytes(ini, backup); else if (File.Exists(ini)) File.Delete(ini);
        }
    }

    private static async Task Flyout()
    {
        Snap.ToggleFlyout();
        await Task.Delay(700);
        Shot("05-flyout");
        Application.Current.Windows.OfType<TrayFlyout>().First().HideNow();
    }

    private static async Task Record(bool gif)
    {
        var m = Monitors.Primary.Bounds;
        var region = new Int32Rect(m.X + 100, m.Y + 100, 640, 360);
        Snap.Settings.Countdown = false;
        var start = RecordingSession.StartAsync(region, gif);
        await Task.Delay(900);
        Shot(gif ? "07-gif-hud" : "06-mp4-hud");
        await Task.Delay(1200);
        await RecordingSession.Current!.StopAsync();
        await start;
        await Task.Delay(300);
        var file = Directory.GetFiles(Snap.Settings.SaveFolder, gif ? "*.gif" : "*.mp4").OrderByDescending(File.GetCreationTime).First();
        Report.Add($"{(gif ? "gif" : "mp4")} file: {file} ({new FileInfo(file).Length} bytes)");
        if (gif)
        {
            var dec = new GifBitmapDecoder(new Uri(file), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Report.Add($"gif frames: {dec.Frames.Count}, size {dec.Frames[0].PixelWidth}x{dec.Frames[0].PixelHeight}");
            CaptureStore.SavePng(dec.Frames[0], Path.Combine(_out, "07-gif-frame0.png"));
        }
        else
        {
            var sf = await global::Windows.Storage.StorageFile.GetFileFromPathAsync(file);
            var clip = await global::Windows.Media.Editing.MediaClip.CreateFromFileAsync(sf);
            Report.Add($"mp4 duration: {clip.OriginalDuration}");
            var comp = new global::Windows.Media.Editing.MediaComposition();
            comp.Clips.Add(clip);
            var thumb = await comp.GetThumbnailAsync(TimeSpan.FromMilliseconds(500), 640, 360, global::Windows.Media.Editing.VideoFramePrecision.NearestFrame);
            using var outStream = File.Create(Path.Combine(_out, "06-mp4-frame.jpg"));
            using var s = thumb.AsStreamForRead();
            await s.CopyToAsync(outStream);
        }
        Snap.Quick.DismissAll();
        await Task.Delay(600);
    }

    private static async Task Ocr()
    {
        var ed = new EditorWindow(Sample(600, 300));
        ed.Show();
        await Task.Delay(500);
        var b = PhysicalBounds(ed);
        var px = ScreenCapture.Capture(new Int32Rect(b.X, b.Y + b.Height - 70, b.Width, 70));
        var text = await TextRecognizer.RecognizeAsync(px);
        Report.Add("ocr text: " + text.Replace(Environment.NewLine, " | "));
        ed.Close();
    }
}
