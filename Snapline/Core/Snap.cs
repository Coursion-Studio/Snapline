using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Snapline.UI;

namespace Snapline.Core;

/// <summary>App controller: owns the tray, hotkeys and every capture flow.</summary>
public static class Snap
{
    private static HotkeyManager? _hotkeys;
    private static PrintScreenHook? _printScreen;
    private static TrayIcon? _tray;
    private static QuickAccessHost? _quick;
    private static TrayFlyout? _flyout;
    private static CombineWindow? _combine;
    private static bool _busy;
    private static Int32Rect? _lastArea;

    public static AppSettings Settings { get; private set; } = new();
    public static QuickAccessHost Quick => _quick ??= new QuickAccessHost();

    public static void Initialize(string[] args)
    {
        Log.MigrateOldFolder();
        Settings = AppSettings.Load();

        if (args.Length > 0 && args[0].StartsWith("--dev", StringComparison.OrdinalIgnoreCase))
        {
            _ = Dev.RunAsync(args);
            return;
        }
        if (args.Length > 1 && args[0] == "--export-icons")
        {
            TrayIcon.ExportIcons(args[1]);
            Application.Current.Shutdown();
            return;
        }
        bool background = args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));

        Updates.UpdateHost.Icon = TrayIcon.Logo;
        Updates.UpdateHost.Quit = Quit;
        Updates.UpdateHost.StartupEnabled = () => Startup.IsEnabled;
        Updates.UpdateUI.StartAutoCheck(Application.Current.Dispatcher);

        _tray = new TrayIcon(ToggleFlyout, ShowMenu);
        _hotkeys = new HotkeyManager();
        var failed = new List<string>();
        void Reg(Key key, string name, Action a)
        {
            if (!_hotkeys.Register(ModifierKeys.Control | ModifierKeys.Shift, key, a)) failed.Add($"Ctrl+Shift+{name}");
        }
        Reg(Key.D1, "1", () => _ = CaptureAsync(OverlayMode.Screenshot));
        Reg(Key.D2, "2", () => _ = CaptureFullscreenAsync());
        Reg(Key.D3, "3", () => _ = CapturePreviousAsync());
        Reg(Key.D4, "4", () => _ = CaptureAsync(OverlayMode.RecordVideo));
        Reg(Key.D5, "5", () => _ = CaptureAsync(OverlayMode.RecordGif));
        Reg(Key.D6, "6", () => _ = CaptureAsync(OverlayMode.Text));
        Reg(Key.D7, "7", OpenCombine);
        Reg(Key.W, "W", () => _ = CaptureAsync(OverlayMode.Window));
        SetPrintScreen(Settings.UsePrintScreen);

        if (background && failed.Count == 0) return; // started with Windows: stay quiet
        Quick.ShowInfo("Snapline is ready",
            failed.Count == 0
                ? (_printScreen?.IsActive == true ? "Print Screen or " : "") + "Ctrl+Shift+1 to capture · Ctrl+Shift+4 to record. Click the tray icon for everything else."
                : $"Some shortcuts are taken by another app: {string.Join(", ", failed)}. Use the tray icon instead.",
            "");
    }

    /// <summary>Claims (or releases) the bare Print Screen key for area capture.</summary>
    public static void SetPrintScreen(bool on)
    {
        _printScreen?.Dispose();
        _printScreen = null;
        if (on)
        {
            _printScreen = new PrintScreenHook(() => _ = CaptureAsync(OverlayMode.Screenshot));
            if (!_printScreen.IsActive) Log.Info("Print Screen hook could not be installed.");
        }
    }

    public static void Shutdown()
    {
        _printScreen?.Dispose();
        _printScreen = null;
        _hotkeys?.Dispose();
        _tray?.Dispose();
    }

    public static void Quit()
    {
        Shutdown();
        _hotkeys = null;
        _tray = null;
        Application.Current.Shutdown();
    }

    public static void ToggleFlyout()
    {
        _flyout ??= new TrayFlyout();
        _flyout.Toggle();
    }

    public static void OpenSettings()
    {
        _flyout?.HideNow();
        SettingsWindow.ShowSettings();
    }

    /// <summary>Right-click tray menu, in the Coursion style.</summary>
    public static void ShowMenu()
    {
        _flyout?.HideNow();
        var m = new Snapline.Shared.UiMenu(336);
        m.AddHeader(TrayIcon.Logo, "Snapline", Settings.UsePrintScreen ? "Print Screen to capture" : "Ctrl+Shift+1 to capture");
        void Later(Func<Task> a) => Application.Current.Dispatcher.InvokeAsync(async () => { await Task.Delay(160); await a(); });
        m.AddItem("", "Capture area", Settings.UsePrintScreen ? "PrtSc" : "Ctrl+Shift+1", () => Later(() => CaptureAsync(OverlayMode.Screenshot)));
        m.AddItem("", "Capture fullscreen", "Ctrl+Shift+2", () => Later(CaptureFullscreenAsync));
        m.AddItem("", "Record video", "Ctrl+Shift+4", () => Later(() => CaptureAsync(OverlayMode.RecordVideo)));
        m.AddItem("", "Copy text from screen", "Ctrl+Shift+6", () => Later(() => CaptureAsync(OverlayMode.Text)));
        m.AddItem("", "Combine screenshots", "Ctrl+Shift+7", OpenCombine);
        m.AddSeparator();
        m.AddItem("", "Open captures folder", null, OpenFolder);
        m.AddItem("", "Settings…", null, OpenSettings);
        m.AddItem("", "Check for updates…", null, () => Updates.UpdateUI.CheckNow(true));
        m.AddSeparator();
        m.AddItem("", "Quit Snapline", null, Quit, danger: true);
        m.ShowAtCursor();
    }

    // ---------- capture flows ----------

    private static async Task<bool> PrepareAsync()
    {
        bool waited = false;
        if (_flyout is { IsVisible: true })
        {
            _flyout.HideNow();
            waited = true;
        }
        if (_quick is { IsVisible: true, Excluded: false })
        {
            _quick.Opacity = 0;
            waited = true;
        }
        if (waited) await Task.Delay(140);
        return waited;
    }

    private static void Restore()
    {
        if (_quick != null) _quick.Opacity = 1;
    }

    public static async Task CaptureAsync(OverlayMode mode)
    {
        if (RecordingSession.Current is { } rec)
        {
            if (mode is OverlayMode.RecordVideo or OverlayMode.RecordGif) await rec.StopAsync();
            return;
        }
        if (_busy) return;
        _busy = true;
        try
        {
            await PrepareAsync();
            var result = await OverlaySession.RunAsync(mode);
            Restore();
            if (result == null) return;
            _lastArea = result.Rect;
            switch (mode)
            {
                case OverlayMode.Screenshot:
                case OverlayMode.Window:
                    Deliver(result.Frozen.CropGlobal(result.Rect).ToBitmapSource(), result.Rect);
                    break;
                case OverlayMode.Text:
                    await RecognizeAsync(result.Frozen.CropGlobal(result.Rect));
                    break;
                case OverlayMode.RecordVideo:
                case OverlayMode.RecordGif:
                    _busy = false;
                    await RecordingSession.StartAsync(result.Rect, mode == OverlayMode.RecordGif);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            Quick.ShowInfo("Capture failed", ex.Message, "");
        }
        finally
        {
            Restore();
            _busy = false;
        }
    }

    public static async Task CaptureFullscreenAsync()
    {
        if (_busy || RecordingSession.Current != null) return;
        _busy = true;
        try
        {
            await PrepareAsync();
            var mon = Monitors.AtCursor();
            var px = ScreenCapture.Capture(mon.Bounds);
            _lastArea = mon.Bounds;
            Restore();
            Deliver(px.ToBitmapSource(), mon.Bounds);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            Quick.ShowInfo("Capture failed", ex.Message, "");
        }
        finally
        {
            Restore();
            _busy = false;
        }
    }

    public static async Task CapturePreviousAsync()
    {
        if (_lastArea is not { } area)
        {
            await CaptureAsync(OverlayMode.Screenshot);
            return;
        }
        if (_busy || RecordingSession.Current != null) return;
        _busy = true;
        try
        {
            await PrepareAsync();
            var px = ScreenCapture.Capture(area);
            Restore();
            Deliver(px.ToBitmapSource(), area);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            Quick.ShowInfo("Capture failed", ex.Message, "");
        }
        finally
        {
            Restore();
            _busy = false;
        }
    }

    private static async Task RecognizeAsync(PixelBuffer px)
    {
        var text = await TextRecognizer.RecognizeAsync(px);
        if (string.IsNullOrWhiteSpace(text))
        {
            Quick.ShowInfo("No text found", "Try selecting a larger or sharper area.", "");
            return;
        }
        CaptureStore.CopyText(text);
        Quick.ShowText(text);
    }

    public static void Deliver(BitmapSource img, Int32Rect? origin)
    {
        var item = new CaptureItem(img) { Origin = origin };
        CaptureStore.Add(item);
        if (Settings.AutoCopy) CaptureStore.CopyImage(img);
        if (Settings.AutoSave)
        {
            try { item.FilePath = CaptureStore.SavePng(img); }
            catch (Exception ex) { Log.Error(ex); }
        }
        Quick.ShowCapture(item);
    }

    // ---------- windows ----------

    public static void OpenEditor(BitmapSource img)
    {
        var w = new EditorWindow(img);
        w.Show();
        w.Activate();
    }

    public static void Pin(BitmapSource img, Int32Rect? origin) => new PinWindow(img, origin).Show();

    private static CombineWindow EnsureCombine()
    {
        if (_combine == null)
        {
            _combine = new CombineWindow();
            _combine.Closed += (_, _) => _combine = null;
        }
        return _combine;
    }

    public static void AddToCombine(BitmapSource img)
    {
        var c = EnsureCombine();
        c.AddImage(img);
        c.Show();
        if (c.WindowState == WindowState.Minimized) c.WindowState = WindowState.Normal;
        c.Activate();
    }

    public static void OpenCombine()
    {
        bool fresh = _combine == null;
        var c = EnsureCombine();
        if (fresh)
            foreach (var item in CaptureStore.History.Take(2).Reverse()) c.AddImage(item.Image);
        c.Show();
        if (c.WindowState == WindowState.Minimized) c.WindowState = WindowState.Normal;
        c.Activate();
    }

    public static void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(Settings.SaveFolder);
            Process.Start("explorer.exe", $"\"{Settings.SaveFolder}\"");
        }
        catch (Exception ex) { Log.Error(ex); }
    }
}
