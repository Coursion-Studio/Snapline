#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Media;

namespace Snapline.Updates;

// Glue between the OTA updater and the app. Set these once at startup, e.g. in Main:
//   UpdateHost.Icon = <app icon ImageSource>;
//   UpdateHost.Quit = () => <your shutdown>;
//   UpdateHost.StartupEnabled = () => <does the app start with Windows?>;
//   UpdateUI.StartAutoCheck(Application.Current.Dispatcher);
// and add "Check for updates…" to the tray / settings:  UpdateUI.CheckNow(true);
static class UpdateHost
{
    public static ImageSource Icon;
    public static Action Quit = () => System.Windows.Application.Current.Shutdown();
    public static Func<bool> StartupEnabled = () => false;
}

// Update preferences, kept in their own file so the updater doesn't depend on the app's settings code.
static class UpdateSettings
{
    public static bool AutoUpdate = true;
    public static string SkippedVersion = "";
    public static string UpdateFeed = "";      // optional override, e.g. a file:/// feed for testing
    public static long LastUpdateCheck;        // UTC ticks

    static string FilePath
    {
        get
        {
            string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Coursion", "Snapline");
            Directory.CreateDirectory(d);
            return Path.Combine(d, "update.ini");
        }
    }

    static UpdateSettings() { Load(); }

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                if (k == "AutoUpdate") AutoUpdate = v == "1";
                else if (k == "SkippedVersion") SkippedVersion = v;
                else if (k == "UpdateFeed") UpdateFeed = v;
                else if (k == "LastUpdateCheck") long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out LastUpdateCheck);
            }
        }
        catch { }
    }

    public static void Save()
    {
        try
        {
            var lines = new List<string> {
                "AutoUpdate=" + (AutoUpdate ? 1 : 0), "SkippedVersion=" + SkippedVersion,
                "LastUpdateCheck=" + LastUpdateCheck.ToString(CultureInfo.InvariantCulture) };
            if (!string.IsNullOrEmpty(UpdateFeed)) lines.Add("UpdateFeed=" + UpdateFeed);
            File.WriteAllLines(FilePath, lines);
        }
        catch { }
    }
}
