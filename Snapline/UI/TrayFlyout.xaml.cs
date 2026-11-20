using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Snapline.Core;

namespace Snapline.UI;

public partial class TrayFlyout : Window
{
    private DateTime _lastHidden = DateTime.MinValue;
    private bool _hiding;

    public TrayFlyout()
    {
        InitializeComponent();
        Deactivated += (_, _) => HideAnimated();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) HideAnimated(); };
        SourceInitialized += (_, _) => WindowUtil.SetToolWindow(this, noActivate: false);
    }

    public void Toggle()
    {
        if (IsVisible && !_hiding) { HideAnimated(); return; }
        if ((DateTime.Now - _lastHidden).TotalMilliseconds < 300) return; // the click that deactivated us
        ShowAnimated();
    }

    private void Refresh()
    {
        bool p = Snap.Settings.UsePrintScreen;
        AreaHint.Text = p ? "PrtSc · Ctrl+Shift+1" : "Ctrl+Shift+1";
        FooterHint.Text = p ? "Print Screen anywhere to capture" : "Ctrl+Shift+1 anywhere to capture";

        var recent = CaptureStore.History.Take(10).ToList();
        RecentList.ItemsSource = recent;
        RecentEmpty.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentScroll.Visibility = recent.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RecentHint.Visibility = RecentScroll.Visibility;
        RecBanner.Visibility = RecordingSession.Current != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowAnimated()
    {
        _hiding = false;
        Refresh();
        Opacity = 0;
        Show();
        UpdateLayout();
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth + 6;
        Top = wa.Bottom - ActualHeight + 6;
        Activate();
        BeginAnimation(OpacityProperty, WindowUtil.Ease(0, 1, 160));
        Slide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            WindowUtil.Ease(18, 0, 340, new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut }));
    }

    public void HideAnimated()
    {
        if (!IsVisible || _hiding) return;
        _hiding = true;
        _lastHidden = DateTime.Now;
        var a = WindowUtil.Ease(null, 0, 120);
        a.Completed += (_, _) =>
        {
            if (!_hiding) return;
            Hide();
            _hiding = false;
        };
        BeginAnimation(OpacityProperty, a);
        Slide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, WindowUtil.Ease(null, 10, 140));
    }

    public void HideNow()
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Hide();
        _hiding = false;
        _lastHidden = DateTime.Now;
    }

    private void Run(Func<Task> action)
    {
        HideNow();
        Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(140); // let DWM drop the flyout from the screen before we freeze it
            await action();
        });
    }

    private void Area_Click(object sender, RoutedEventArgs e) => Run(() => Snap.CaptureAsync(OverlayMode.Screenshot));
    private void Fullscreen_Click(object sender, RoutedEventArgs e) => Run(Snap.CaptureFullscreenAsync);
    private void Window_Click(object sender, RoutedEventArgs e) => Run(() => Snap.CaptureAsync(OverlayMode.Window));
    private void Previous_Click(object sender, RoutedEventArgs e) => Run(Snap.CapturePreviousAsync);
    private void Video_Click(object sender, RoutedEventArgs e) => Run(() => Snap.CaptureAsync(OverlayMode.RecordVideo));
    private void Gif_Click(object sender, RoutedEventArgs e) => Run(() => Snap.CaptureAsync(OverlayMode.RecordGif));
    private void Text_Click(object sender, RoutedEventArgs e) => Run(() => Snap.CaptureAsync(OverlayMode.Text));

    private void Combine_Click(object sender, RoutedEventArgs e)
    {
        HideNow();
        Snap.OpenCombine();
    }

    private async void StopRec_Click(object sender, RoutedEventArgs e)
    {
        HideNow();
        if (RecordingSession.Current is { } r) await r.StopAsync();
    }

    private void Recent_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is CaptureItem item)
        {
            HideNow();
            Snap.OpenEditor(item.Image);
        }
    }

    private void Recent_Wheel(object sender, MouseWheelEventArgs e)
    {
        RecentScroll.ScrollToHorizontalOffset(RecentScroll.HorizontalOffset - e.Delta / 2.0);
        e.Handled = true;
    }

    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        HideNow();
        Snap.OpenFolder();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => Snap.OpenSettings();

    private void Quit_Click(object sender, RoutedEventArgs e) => Snap.Quit();
}
