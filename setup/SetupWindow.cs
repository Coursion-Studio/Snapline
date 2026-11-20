using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Snapline.Shared;

class SetupWindow : UiWindow
{
    static ImageSource icon;
    public static ImageSource AppIcon
    {
        get
        {
            if (icon == null)
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.StreamSource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Setup.icon.png");
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();
                icon = img;
            }
            return icon;
        }
    }

    readonly bool uninstall;
    bool busy;

    public SetupWindow(bool uninstall) : base(uninstall ? "Uninstall Snapline" : "Snapline Setup", 440)
    {
        this.uninstall = uninstall;
        TitleIcon = AppIcon;
        Closing += (s, e) => { if (busy) e.Cancel = true; };
        if (uninstall) ShowUninstallPage(); else ShowWelcomePage();
    }

    // ------------------------------------------------------------ shared pieces
    static StackPanel Page() { return new StackPanel { Margin = new Thickness(28, 0, 28, 24) }; }

    static Image Hero(double size)
    {
        return new Image
        {
            Source = AppIcon, Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new DropShadowEffect { Color = Ui.AccentColor, BlurRadius = 34, ShadowDepth = 0, Opacity = 0.5 }
        };
    }

    static TextBlock Heading(string text)
    {
        return new TextBlock
        {
            Text = text, FontFamily = Ui.DisplayFont, FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Ui.Text,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0)
        };
    }

    static TextBlock Sub(string text, double top = 4)
    {
        return new TextBlock
        {
            Text = text, FontSize = 12.5, Foreground = Ui.SubText, TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0)
        };
    }

    static StackPanel Buttons(params UiButton[] buttons)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        for (int i = 0; i < buttons.Length; i++)
        {
            if (i > 0) buttons[i].Margin = new Thickness(8, 0, 0, 0);
            sp.Children.Add(buttons[i]);
        }
        return sp;
    }

    // ------------------------------------------------------------ install flow
    void ShowWelcomePage()
    {
        string existing = Installer.IsInstalled ? Installer.InstalledVersion : null;
        bool update = Installer.IsInstalled;

        var p = Page();
        p.Children.Add(Hero(84));
        p.Children.Add(Heading(update ? "Update Snapline" : "Install Snapline"));
        p.Children.Add(Sub(update && existing != null
            ? "Version " + existing + " → " + Installer.Version + " · Coursion Studio"
            : "Version " + Installer.Version + " · Coursion Studio"));
        p.Children.Add(new TextBlock
        {
            Text = "Screenshots, recordings and annotations.", FontSize = 13, Foreground = Ui.SubText,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0)
        });

        var startup = new UiToggle(update ? Installer.StartupEnabled : true);
        var open = new UiToggle(true);
        var card = Ui.Card(
            Ui.Row("Start with Windows", "Snapline waits in the tray, ready when you need it.", startup),
            Ui.Row("Open Snapline when done", null, open));
        card.Margin = new Thickness(0, 22, 0, 0);
        p.Children.Add(card);

        var where = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 14, 0, 0) };
        var lockGlyph = Ui.Glyph("", 12, Ui.SubText);
        lockGlyph.Margin = new Thickness(0, 1, 8, 0);
        where.Children.Add(lockGlyph);
        where.Children.Add(new TextBlock
        {
            Text = "Installs just for you — no admin rights needed.", FontSize = 11.5, Foreground = Ui.SubText,
            TextWrapping = TextWrapping.Wrap
        });
        p.Children.Add(where);

        var lic = new TextBlock { FontSize = 11.5, Foreground = Ui.SubText, Margin = new Thickness(4, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        lic.Inlines.Add(new Run("Free and open source under the "));
        var link = new Hyperlink(new Run("MIT licence")) { Foreground = Ui.AccentText, TextDecorations = null, Cursor = System.Windows.Input.Cursors.Hand };
        link.Click += (s, e) => ShowLicence();
        lic.Inlines.Add(link);
        lic.Inlines.Add(new Run("."));
        p.Children.Add(lic);

        var cancel = new UiButton("Cancel", UiButtonKind.Quiet);
        cancel.Click += Close;
        var go = new UiButton(update ? "Update" : "Install", UiButtonKind.Primary, "");
        go.Click += () => RunInstall(startup.IsOn, open.IsOn);
        p.Children.Add(Buttons(cancel, go));
        Body = p;
    }

    async void RunInstall(bool startup, bool openAfter)
    {
        var bar = ShowProgressPage("Installing Snapline…");
        busy = true;
        Exception error = null;
        try
        {
            await Task.Run(() => Installer.Install(startup, (v, s) => Dispatcher.BeginInvoke(new Action(() => bar(v, s)))));
            await Task.Delay(350);
            if (openAfter) Installer.Launch();
        }
        catch (Exception ex) { error = ex; }
        busy = false;
        if (error != null) ShowErrorPage("Snapline couldn't be installed.", error.Message);
        else ShowDonePage(openAfter);
    }

    void ShowDonePage(bool opened)
    {
        var p = Page();
        p.Children.Add(Check());
        p.Children.Add(Heading("Snapline is ready"));
        p.Children.Add(Sub("Snapline is in the tray. Press Print Screen to capture.", 6));
        var done = new UiButton(opened ? "Done" : "Close", opened ? UiButtonKind.Primary : UiButtonKind.Quiet);
        done.Click += Close;
        if (opened) p.Children.Add(Buttons(done));
        else
        {
            var open = new UiButton("Open Snapline", UiButtonKind.Primary, "");
            open.Click += () => { Installer.Launch(); Close(); };
            p.Children.Add(Buttons(done, open));
        }
        Body = p;
    }

    // ------------------------------------------------------------ uninstall flow
    void ShowUninstallPage()
    {
        var p = Page();
        p.Children.Add(Hero(72));
        p.Children.Add(Heading("Uninstall Snapline?"));
        p.Children.Add(Sub("Snapline, its Start menu shortcut and its startup entry will be removed from this PC.", 6));
        var purge = new UiToggle(false);
        var card = Ui.Card(Ui.Row("Also remove settings", "Your Snapline preferences. Saved screenshots and recordings are never deleted.", purge));
        card.Margin = new Thickness(0, 22, 0, 0);
        p.Children.Add(card);
        var cancel = new UiButton("Cancel", UiButtonKind.Quiet);
        cancel.Click += Close;
        var go = new UiButton("Uninstall", UiButtonKind.Danger, "");
        go.Click += () => RunUninstall(purge.IsOn);
        p.Children.Add(Buttons(cancel, go));
        Body = p;
    }

    async void RunUninstall(bool purge)
    {
        var bar = ShowProgressPage("Removing Snapline…");
        busy = true;
        Exception error = null;
        try { await Task.Run(() => Installer.Uninstall(purge, (v, s) => Dispatcher.BeginInvoke(new Action(() => bar(v, s))))); await Task.Delay(300); }
        catch (Exception ex) { error = ex; }
        busy = false;
        if (error != null) { ShowErrorPage("Snapline couldn't be fully removed.", error.Message); return; }
        var p = Page();
        p.Children.Add(Check());
        p.Children.Add(Heading("Snapline was removed"));
        p.Children.Add(Sub("Thanks for trying it. You can reinstall any time.", 6));
        var done = new UiButton("Close", UiButtonKind.Primary);
        done.Click += Close;
        p.Children.Add(Buttons(done));
        Body = p;
    }

    // ------------------------------------------------------------ progress / done / error
    Action<double, string> ShowProgressPage(string title)
    {
        var p = Page();
        p.Children.Add(Hero(64));
        p.Children.Add(Heading(title));
        var status = Sub("Starting…", 6);
        p.Children.Add(status);

        var track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Ui.Raised, Margin = new Thickness(0, 24, 0, 8) };
        var fill = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Ui.Accent, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        track.Child = fill;
        p.Children.Add(track);
        Body = p;

        return (v, s) =>
        {
            status.Text = s;
            double w = Math.Max(0, track.ActualWidth * Math.Min(1, v));
            fill.BeginAnimation(WidthProperty, new DoubleAnimation(w, TimeSpan.FromMilliseconds(260)) { EasingFunction = Ui.EaseOut });
        };
    }

    static FrameworkElement Check()
    {
        var circle = new Border
        {
            Width = 64, Height = 64, CornerRadius = new CornerRadius(32), Background = Ui.Accent,
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new DropShadowEffect { Color = Ui.AccentColor, BlurRadius = 30, ShadowDepth = 0, Opacity = 0.55 },
            Child = new TextBlock
            {
                Text = "", FontFamily = Ui.IconFont, FontSize = 26, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };
        return circle;
    }

    void ShowErrorPage(string title, string detail)
    {
        var p = Page();
        p.Children.Add(new Border
        {
            Width = 64, Height = 64, CornerRadius = new CornerRadius(32), Background = Ui.DangerSoft, HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = 26, Foreground = Ui.Danger, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        });
        p.Children.Add(Heading(title));
        p.Children.Add(Sub(detail, 6));
        p.Children.Add(Sub("If this keeps happening, contact support@coursion.studio.", 10));
        var close = new UiButton("Close", UiButtonKind.Secondary);
        close.Click += Close;
        p.Children.Add(Buttons(close));
        Body = p;
    }

    static void ShowLicence()
    {
        string text = "Licence text unavailable.";
        try
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Setup.LICENSE.md"))
            using (var r = new StreamReader(s)) text = r.ReadToEnd();
        }
        catch { }
        var w = new UiWindow("Licence", 560) { TitleIcon = AppIcon };
        w.Body = new ScrollViewer
        {
            MaxHeight = 480, Margin = new Thickness(0, 0, 6, 16), Padding = new Thickness(24, 0, 14, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = Ui.Markdown(text)
        };
        w.Present();
    }

    // Exposed for screenshot tests.
    public void DebugShowDone() { ShowDonePage(true); }
    public void DebugShowProgress() { ShowProgressPage("Installing Snapline…")(0.6, "Adding Snapline to the Start menu…"); }
}

static class SetupProgram
{
    [STAThread]
    static int Main(string[] args)
    {
        bool uninstall = args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));
        bool silent = args.Any(a => a == "/S" || a.Equals("/silent", StringComparison.OrdinalIgnoreCase));
        var dirArg = args.FirstOrDefault(a => a.StartsWith("/dir=", StringComparison.OrdinalIgnoreCase));
        if (dirArg != null) Installer.Dir = Path.GetFullPath(dirArg.Substring(5).Trim('"'));

        if (silent)
        {
            try
            {
                if (uninstall) Installer.Uninstall(args.Contains("/purge"), (v, s) => { });
                else
                {
                    Installer.Install(args.Contains("/startup"), (v, s) => { });
                    if (args.Contains("/launch")) Installer.Launch();
                }
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        }

        var app = new Application();
        return app.Run(new SetupWindow(uninstall));
    }
}
