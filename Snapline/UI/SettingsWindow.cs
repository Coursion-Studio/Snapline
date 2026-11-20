using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Snapline.Core;
using Snapline.Shared;
using Snapline.Updates;

namespace Snapline.UI;

/// <summary>Snapline settings: one scrolling window with grouped rows.</summary>
public sealed class SettingsWindow : UiWindow
{
    private static SettingsWindow? _open;

    public static void ShowSettings()
    {
        _open ??= new SettingsWindow();
        _open.Present();
    }

    /// <summary>For the self-test: a settings window that is built but not shown.</summary>
    internal static SettingsWindow CreateHidden() => new();

    private TextBlock? _folderText;

    private SettingsWindow() : base("Snapline Settings", 480)
    {
        TitleIcon = TrayIcon.Logo;
        Closed += (_, _) => _open = null;
        Body = Build();
    }

    private static AppSettings S => Snap.Settings;

    private static UiToggle Toggle(bool value, Action<bool> changed)
    {
        var t = new UiToggle(value);
        t.Changed += changed;
        return t;
    }

    private static UiChoice Choice(string[] options, int selected, Action<int> changed)
    {
        var c = new UiChoice(options, selected);
        c.Changed += changed;
        return c;
    }

    private static TextBlock RowIcon(string glyph) => Ui.Glyph(glyph, 16, Ui.AccentText);

    private UIElement Build()
    {
        var sp = new StackPanel { Margin = new Thickness(20, 0, 20, 22) };

        // ---- header
        var head = new Grid { Margin = new Thickness(4, 2, 4, 0) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.Children.Add(new Image { Source = TrayIcon.Logo, Width = 46, Height = 46, Margin = new Thickness(0, 0, 14, 0) });
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "Snapline", FontFamily = Ui.DisplayFont, FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = Ui.Text });
        titles.Children.Add(new TextBlock { Text = "Version " + Updater.CurrentText + " · Screenshots, recordings and annotations", FontSize = 12, Foreground = Ui.SubText, Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(titles, 1);
        head.Children.Add(titles);
        sp.Children.Add(head);

        // ---- general
        sp.Children.Add(Ui.Section("General"));
        sp.Children.Add(Ui.Card(
            Ui.Row("Print Screen opens Snapline", "Instead of the Snipping Tool. Win+PrtSc still works as usual.",
                Toggle(S.UsePrintScreen, on => { S.UsePrintScreen = on; S.Save(); Snap.SetPrintScreen(on); }), RowIcon("")),
            Ui.Row("Start with Windows", "Snapline waits quietly in the tray.",
                Toggle(Startup.IsEnabled, Startup.Set), RowIcon(""))));

        // ---- after a capture
        sp.Children.Add(Ui.Section("After a capture"));
        _folderText = new TextBlock { Text = S.SaveFolder, FontSize = 12, Foreground = Ui.SubText, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 12, 0), ToolTip = S.SaveFolder };
        var folderWords = new StackPanel();
        folderWords.Children.Add(new TextBlock { Text = "Save folder", FontSize = 13.5, Foreground = Ui.Text });
        folderWords.Children.Add(_folderText);
        var open = new UiButton("Open", UiButtonKind.Quiet);
        open.Click += Snap.OpenFolder;
        var change = new UiButton("Change…", UiButtonKind.Secondary) { Margin = new Thickness(6, 0, 0, 0) };
        change.Click += ChangeFolder;
        var folderButtons = new StackPanel { Orientation = Orientation.Horizontal };
        folderButtons.Children.Add(open);
        folderButtons.Children.Add(change);
        var folderRow = Ui.Row(null, null, folderButtons, RowIcon(""));
        var wordsHolder = new Border { Child = folderWords, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(wordsHolder, 1);
        folderRow.Children.Add(wordsHolder);

        int[] secs = { 6, 12, 30, 0 };
        sp.Children.Add(Ui.Card(
            Ui.Row("Copy to clipboard", "Paste it straight into a chat or document.",
                Toggle(S.AutoCopy, on => { S.AutoCopy = on; S.Save(); }), RowIcon("")),
            Ui.Row("Save every capture", "Also writes a PNG to your save folder.",
                Toggle(S.AutoSave, on => { S.AutoSave = on; S.Save(); }), RowIcon("")),
            folderRow,
            Ui.Row("Preview stays on screen", "The card in the corner after each capture.",
                Choice(new[] { "6 s", "12 s", "30 s", "Until closed" }, Math.Max(0, Array.IndexOf(secs, S.OverlaySeconds)),
                    i => { S.OverlaySeconds = secs[i]; S.Save(); }), RowIcon(""))));

        // ---- recording
        sp.Children.Add(Ui.Section("Recording"));
        int[] fps = { 24, 30, 60 };
        sp.Children.Add(Ui.Card(
            Ui.Row("Show the mouse pointer", null,
                Toggle(S.RecordCursor, on => { S.RecordCursor = on; S.Save(); }), RowIcon("")),
            Ui.Row("Countdown before recording", "3 seconds to get ready.",
                Toggle(S.Countdown, on => { S.Countdown = on; S.Save(); }), RowIcon("")),
            Ui.Row("Video frame rate", "Higher is smoother but makes bigger files.",
                Choice(new[] { "24", "30", "60" }, Math.Max(0, Array.IndexOf(fps, S.RecordFps)),
                    i => { S.RecordFps = fps[i]; S.Save(); }), RowIcon(""))));

        // ---- shortcuts
        sp.Children.Add(Ui.Section("Shortcuts"));
        sp.Children.Add(Ui.Card(
            Ui.Row("Capture area", null, Ui.Keys(S.UsePrintScreen ? "PrtSc" : "Ctrl+Shift+1")),
            Ui.Row("Capture fullscreen", null, Ui.Keys("Ctrl+Shift+2")),
            Ui.Row("Capture previous area", null, Ui.Keys("Ctrl+Shift+3")),
            Ui.Row("Capture window", null, Ui.Keys("Ctrl+Shift+W")),
            Ui.Row("Record video", null, Ui.Keys("Ctrl+Shift+4")),
            Ui.Row("Record GIF", null, Ui.Keys("Ctrl+Shift+5")),
            Ui.Row("Copy text from screen", null, Ui.Keys("Ctrl+Shift+6")),
            Ui.Row("Combine screenshots", null, Ui.Keys("Ctrl+Shift+7"))));

        // ---- updates
        sp.Children.Add(Ui.Section("Updates"));
        var check = new UiButton("Check now", UiButtonKind.Secondary, "");
        check.Click += () => UpdateUI.CheckNow(true);
        sp.Children.Add(Ui.Card(
            Ui.Row("Check for updates automatically", "Once a day. Nothing installs without asking you.",
                Toggle(UpdateSettings.AutoUpdate, on => { UpdateSettings.AutoUpdate = on; UpdateSettings.Save(); }), RowIcon("")),
            Ui.Row("Snapline " + Updater.CurrentText, LastChecked(), check, RowIcon(""))));

        // ---- about
        sp.Children.Add(Ui.Section("About"));
        var source = new UiButton("Source code", UiButtonKind.Quiet, "");
        source.Click += () => UiWin.Open("https://github.com/Coursion-Studio/Snapline");
        var support = new UiButton("Support", UiButtonKind.Quiet, "");
        support.Click += () => UiWin.Open("mailto:support@coursion.studio");
        var licence = new UiButton("Licence", UiButtonKind.Quiet, "");
        licence.Click += ShowLicence;
        var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 8, 10) };
        links.Children.Add(source);
        links.Children.Add(support);
        links.Children.Add(licence);
        var about = new StackPanel();
        about.Children.Add(Ui.Row("Free and open source", "MIT licence · © " + DateTime.Now.Year + " Coursion Studio", null, RowIcon("")));
        about.Children.Add(links);
        sp.Children.Add(Ui.Card(about));

        // ---- footer
        var quit = new UiButton("Quit Snapline", UiButtonKind.Danger, "");
        quit.Click += Snap.Quit;
        var done = new UiButton("Done", UiButtonKind.Primary) { Margin = new Thickness(8, 0, 0, 0) };
        done.Click += Close;
        var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        foot.Children.Add(quit);
        foot.Children.Add(done);
        sp.Children.Add(foot);

        return new ScrollViewer
        {
            Content = sp, MaxHeight = Math.Max(420, SystemParameters.WorkArea.Height - 140),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    private static string LastChecked()
    {
        if (UpdateSettings.LastUpdateCheck <= 0) return "Not checked yet.";
        var when = new DateTime(UpdateSettings.LastUpdateCheck, DateTimeKind.Utc).ToLocalTime();
        return "Last checked " + (when.Date == DateTime.Today ? "today at " + when.ToString("HH:mm") : when.ToString("d MMM yyyy"));
    }

    private void ChangeFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Choose where Snapline saves captures", InitialDirectory = S.SaveFolder };
        if (dlg.ShowDialog(this) != true) return;
        S.SaveFolder = dlg.FolderName;
        S.Save();
        if (_folderText != null) { _folderText.Text = dlg.FolderName; _folderText.ToolTip = dlg.FolderName; }
    }

    public static string LicenceText()
    {
        using var s = typeof(SettingsWindow).Assembly.GetManifestResourceStream("Snapline.LICENSE.md");
        return s == null ? "MIT licence. Copyright © Coursion Studio." : new StreamReader(s).ReadToEnd();
    }

    private void ShowLicence()
    {
        var w = new UiWindow("Snapline Licence", 520) { TitleIcon = TrayIcon.Logo, Owner = this };
        var md = Ui.Markdown(LicenceText());
        md.Margin = new Thickness(24, 4, 24, 20);
        w.Body = new ScrollViewer { Content = md, MaxHeight = 520, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        w.Present();
    }
}
