using System.IO;
using System.Text.Json;

namespace Snapline.Core;

public sealed class AppSettings
{
    public bool AutoCopy { get; set; } = true;
    public bool AutoSave { get; set; }
    public bool UsePrintScreen { get; set; } = true;
    public string SaveFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Snapline");
    public bool RecordCursor { get; set; } = true;
    public int RecordFps { get; set; } = 30;
    public bool Countdown { get; set; } = true;
    public int OverlaySeconds { get; set; } = 12;

    private static string FilePath => Path.Combine(Log.Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex) { Log.Error(ex); }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Log.Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Log.Error(ex); }
    }
}
