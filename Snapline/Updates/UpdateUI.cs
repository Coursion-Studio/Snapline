#nullable disable
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Snapline.Shared;

namespace Snapline.Updates;

// "Update available" window, manual checks, and the quiet daily check.
static class UpdateUI
{
    static UiWindow window;
    static string shownThisRun;
    static DispatcherTimer timer;
    static bool checking;

    /// Starts the background check: 20 s after launch, then hourly; it only asks the feed once a day.
    public static void StartAutoCheck(Dispatcher d)
    {
        timer = new DispatcherTimer(DispatcherPriority.Background, d) { Interval = TimeSpan.FromSeconds(20) };
        timer.Tick += (s, e) =>
        {
            timer.Interval = TimeSpan.FromHours(1);
            if (!UpdateSettings.AutoUpdate) return;
            if (DateTime.UtcNow - new DateTime(UpdateSettings.LastUpdateCheck, DateTimeKind.Utc) < TimeSpan.FromHours(24)) return;
            CheckNow(false);
        };
        timer.Start();
    }

    /// manual = the user asked: always report the result. Automatic checks stay silent unless there's an update.
    public static void CheckNow(bool manual)
    {
        if (checking) return;
        checking = true;
        var d = Application.Current.Dispatcher;
        Task.Run(() =>
        {
            UpdateInfo u = null; Exception err = null;
            try { u = Updater.Check(); } catch (Exception ex) { err = ex; }
            d.BeginInvoke(new Action(() =>
            {
                checking = false;
                if (err != null) { if (manual) ShowMessage("Couldn't check for updates", Friendly(err), false); return; }
                if (u == null) { if (manual) ShowMessage("You're up to date", "Snapline " + Updater.CurrentText + " is the newest version.", true); return; }
                string v = u.Version.ToString();
                if (!manual && (v == UpdateSettings.SkippedVersion || v == shownThisRun)) return;
                shownThisRun = v;
                ShowUpdate(u);
            }));
        });
    }

    static string Friendly(Exception ex)
    {
        var inner = ex is AggregateException ? ex.GetBaseException() : ex;
        if (inner is System.Net.WebException) return "Snapline couldn't reach the update server. Check your internet connection and try again.";
        return inner.Message;
    }

    static void Present(UiWindow w)
    {
        if (window != null) window.Close();
        window = w;
        w.Closed += (s, e) => { if (window == w) window = null; };
        w.Present();
    }

    static Image Hero(double size)
    {
        return new Image
        {
            Source = UpdateHost.Icon, Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new DropShadowEffect { Color = Ui.AccentColor, BlurRadius = 30, ShadowDepth = 0, Opacity = 0.5 }
        };
    }

    static TextBlock Heading(string text)
    {
        return new TextBlock
        {
            Text = text, FontFamily = Ui.DisplayFont, FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = Ui.Text,
            HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0)
        };
    }

    static TextBlock Sub(string text)
    {
        return new TextBlock
        {
            Text = text, FontSize = 12.5, Foreground = Ui.SubText, HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
        };
    }

    static StackPanel Buttons(params UiButton[] b)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        for (int i = 0; i < b.Length; i++) { if (i > 0) b[i].Margin = new Thickness(8, 0, 0, 0); sp.Children.Add(b[i]); }
        return sp;
    }

    static void ShowMessage(string title, string text, bool ok)
    {
        var w = new UiWindow("Snapline Updates", 380) { TitleIcon = UpdateHost.Icon };
        var sp = new StackPanel { Margin = new Thickness(26, 2, 26, 22) };
        sp.Children.Add(Hero(56));
        sp.Children.Add(Heading(title));
        sp.Children.Add(Sub(text));
        var close = new UiButton("OK", ok ? UiButtonKind.Primary : UiButtonKind.Secondary);
        close.Click += w.Close;
        sp.Children.Add(Buttons(close));
        w.Body = sp;
        Present(w);
    }

    public static UiWindow BuildUpdateWindow(UpdateInfo u)
    {
        var w = new UiWindow("Snapline Update", 440) { TitleIcon = UpdateHost.Icon };
        var sp = new StackPanel { Margin = new Thickness(26, 2, 26, 22) };
        sp.Children.Add(Hero(64));
        sp.Children.Add(Heading("Snapline " + u.Version + " is available"));
        sp.Children.Add(Sub("You have " + Updater.CurrentText + (u.Published != null ? " · released " + u.Published : "")));

        if (u.Notes.Count > 0)
        {
            var md = "## What's new\n" + string.Join("\n", u.Notes.ConvertAll(n => "- " + n));
            var notes = Ui.Markdown(md);
            notes.Margin = new Thickness(14, 4, 14, 8);
            var card = Ui.Card(new ScrollViewer
            {
                Content = notes, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            });
            card.Margin = new Thickness(0, 18, 0, 0);
            sp.Children.Add(card);
        }

        // progress (hidden until Install)
        var status = new TextBlock { FontSize = 12, Foreground = Ui.SubText, Margin = new Thickness(2, 16, 0, 6), Visibility = Visibility.Collapsed };
        var track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Ui.Raised, Visibility = Visibility.Collapsed };
        var fill = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Ui.Accent, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        track.Child = fill;
        sp.Children.Add(status);
        sp.Children.Add(track);

        var skip = new UiButton("Skip this version", UiButtonKind.Quiet);
        var later = new UiButton("Later", UiButtonKind.Secondary);
        var install = new UiButton("Install update", UiButtonKind.Primary, "");
        var buttons = Buttons(skip, later, install);
        sp.Children.Add(buttons);

        skip.Click += () => { UpdateSettings.SkippedVersion = u.Version.ToString(); UpdateSettings.Save(); w.Close(); };
        later.Click += w.Close;
        install.Click += () =>
        {
            buttons.IsEnabled = false; buttons.Opacity = 0.5;
            status.Visibility = Visibility.Visible; track.Visibility = Visibility.Visible;
            status.Text = "Downloading…";
            Action<double> setBar = p => fill.BeginAnimation(FrameworkElement.WidthProperty,
                new DoubleAnimation(Math.Max(0, track.ActualWidth * p), TimeSpan.FromMilliseconds(200)) { EasingFunction = Ui.EaseOut });
            Task.Run(() =>
            {
                try
                {
                    string path = Updater.Download(u, pct => w.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        status.Text = "Downloading… " + pct + "%";
                        setBar(pct / 100.0 * 0.85);
                    })));
                    w.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        status.Text = "Verified. Installing — Snapline will reopen in a moment…";
                        setBar(1);
                    }));
                    System.Threading.Thread.Sleep(700);
                    w.Dispatcher.BeginInvoke(new Action(() => Updater.Install(path)));
                }
                catch (Exception ex)
                {
                    w.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        status.Text = Friendly(ex);
                        status.Foreground = Ui.Danger;
                        fill.BeginAnimation(FrameworkElement.WidthProperty, null); fill.Width = 0;
                        buttons.IsEnabled = true; buttons.Opacity = 1;
                    }));
                }
            });
        };
        w.Body = sp;
        return w;
    }

    public static void ShowUpdate(UpdateInfo u)
    {
        Present(BuildUpdateWindow(u));
    }
}
