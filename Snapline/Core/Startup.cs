using Microsoft.Win32;

namespace Snapline.Core;

/// <summary>
/// "Start with Windows". Same value name and format as the installer writes
/// (HKCU\...\Run\Snapline = "&lt;exe&gt;" --background), so the two never undo each other.
/// </summary>
public static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Snapline";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) != null;
            }
            catch { return false; }
        }
    }

    public static void Set(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --background");
            else key.DeleteValue(ValueName, false);
        }
        catch (Exception ex) { Log.Error(ex); }
    }
}
