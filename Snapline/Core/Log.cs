using System.IO;
namespace Snapline.Core;

public static class Log
{
    private static readonly object Gate = new();
    /// <summary>%AppData%\Coursion\Snapline: settings, log and update preferences (removed by the uninstaller if asked).</summary>
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Coursion", "Snapline");

    /// <summary>Early builds kept settings in %AppData%\Snapline; carry them over once.</summary>
    public static void MigrateOldFolder()
    {
        try
        {
            string old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Snapline", "settings.json");
            string now = Path.Combine(Dir, "settings.json");
            if (File.Exists(old) && !File.Exists(now))
            {
                Directory.CreateDirectory(Dir);
                File.Copy(old, now);
            }
        }
        catch { }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Error(Exception ex) => Write("ERROR", ex.ToString());

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(Path.Combine(Dir, "log.txt"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch { /* logging must never throw */ }
    }
}
