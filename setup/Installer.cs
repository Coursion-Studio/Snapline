using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using Microsoft.Win32;

[assembly: AssemblyTitle("Snapline Setup")]
[assembly: AssemblyDescription("Installs Snapline for Windows")]
[assembly: AssemblyProduct("Snapline for Windows")]
[assembly: AssemblyCompany("Coursion Studio")]
[assembly: AssemblyCopyright("Copyright © 2026 Coursion Studio. MIT licence.")]
[assembly: AssemblyVersion("1.0.1.0")]
[assembly: AssemblyFileVersion("1.0.1.0")]

// Per-user install: no admin rights, nothing outside the user's profile.
// Payload files are embedded as resources named "payload.<file name>" (see build-setup.ps1).
static class Installer
{
    public const string AppName = "Snapline";
    public const string Version = "1.0.1";
    const string ProcessName = "Snapline";                 // running app to close before installing
    const string ExeName = "Snapline.exe";
    static readonly string[] Files = { "Snapline.exe" };
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CoursionStudio.Snapline";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "Snapline";

    public static string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Snapline");
    public static string ExePath { get { return Path.Combine(Dir, ExeName); } }
    static string UninstallerPath { get { return Path.Combine(Dir, "Uninstall.exe"); } }
    static string ShortcutPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"); } }
    static string DataDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Coursion", "Snapline"); } }

    public static bool IsInstalled { get { return File.Exists(ExePath); } }

    public static string InstalledVersion
    {
        get
        {
            using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                return k == null ? null : k.GetValue("DisplayVersion") as string;
        }
    }

    public static bool StartupEnabled
    {
        get
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(RunValue) != null;
        }
    }

    // ------------------------------------------------------------ install / update
    public static void Install(bool startup, Action<double, string> progress)
    {
        progress(0.08, "Closing " + AppName + "…");
        StopApp();

        progress(0.25, "Copying files…");
        Directory.CreateDirectory(Dir);
        foreach (var f in Files) WriteWithRetry(Path.Combine(Dir, f), Payload(f));
        string self = Assembly.GetExecutingAssembly().Location;
        if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(UninstallerPath), StringComparison.OrdinalIgnoreCase))
            WriteWithRetry(UninstallerPath, File.ReadAllBytes(self));

        progress(0.6, "Adding " + AppName + " to the Start menu…");
        CreateShortcut();

        progress(0.8, "Registering with Windows…");
        Register();
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            // The app itself may write the same value; keep the format identical: "<exe>" --background
            if (startup) k.SetValue(RunValue, "\"" + ExePath + "\" --background");
            else if (PointsHere(k.GetValue(RunValue) as string)) k.DeleteValue(RunValue, false);
        }
        progress(1, "Done");
    }

    public static void Launch()
    {
        Process.Start(new ProcessStartInfo(ExePath) { UseShellExecute = true, WorkingDirectory = Dir });
    }

    // ------------------------------------------------------------ uninstall
    public static void Uninstall(bool removeData, Action<double, string> progress)
    {
        progress(0.1, "Closing " + AppName + "…");
        StopApp();

        progress(0.35, "Removing shortcuts and startup entry…");
        TryDelete(ShortcutPath);
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
            if (k != null && PointsHere(k.GetValue(RunValue) as string)) k.DeleteValue(RunValue, false);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);

        progress(0.65, "Removing files…");
        foreach (var f in Files) TryDelete(Path.Combine(Dir, f));
        if (removeData) { try { if (Directory.Exists(DataDir)) Directory.Delete(DataDir, true); } catch { } }

        // The uninstaller may be running from the install folder; let cmd remove it once we exit.
        string self = Assembly.GetExecutingAssembly().Location;
        if (self.StartsWith(Dir, StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + Dir + "\"")
            { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        }
        else
        {
            try { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); } catch { }
        }
        progress(1, "Done");
    }

    // ------------------------------------------------------------ helpers
    static bool PointsHere(string runValue)
    {
        return runValue != null && runValue.IndexOf(Dir, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static void StopApp()
    {
        foreach (var p in Process.GetProcessesByName(ProcessName))
        {
            try { p.Kill(); p.WaitForExit(4000); } catch { }
        }
    }

    public static byte[] Payload(string name)
    {
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload." + name))
        {
            if (s == null) throw new InvalidOperationException("Setup is missing " + name + ".");
            var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
    }

    static void WriteWithRetry(string path, byte[] data)
    {
        for (int i = 0; ; i++)
        {
            try { File.WriteAllBytes(path, data); return; }
            catch (IOException) { if (i >= 15) throw; Thread.Sleep(200); }
            catch (UnauthorizedAccessException) { if (i >= 15) throw; Thread.Sleep(200); }
        }
    }

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    static void CreateShortcut()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ShortcutPath));
        Type t = Type.GetTypeFromProgID("WScript.Shell");
        dynamic shell = Activator.CreateInstance(t);
        dynamic lnk = shell.CreateShortcut(ShortcutPath);
        lnk.TargetPath = ExePath;
        lnk.WorkingDirectory = Dir;
        lnk.Description = AppName + " — screenshots, recordings and annotations";
        lnk.IconLocation = ExePath + ",0";
        lnk.Save();
    }

    static void Register()
    {
        long bytes = 0;
        foreach (var f in Directory.GetFiles(Dir)) bytes += new FileInfo(f).Length;
        using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            k.SetValue("DisplayName", AppName);
            k.SetValue("DisplayVersion", Version);
            k.SetValue("Publisher", "Coursion Studio");
            k.SetValue("DisplayIcon", ExePath + ",0");
            k.SetValue("InstallLocation", Dir);
            k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            k.SetValue("UninstallString", "\"" + UninstallerPath + "\" /uninstall");
            k.SetValue("QuietUninstallString", "\"" + UninstallerPath + "\" /uninstall /S");
            k.SetValue("URLInfoAbout", "https://coursion.studio");
            k.SetValue("HelpLink", "mailto:support@coursion.studio");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            k.SetValue("EstimatedSize", (int)(bytes / 1024), RegistryValueKind.DWord);
        }
    }
}
