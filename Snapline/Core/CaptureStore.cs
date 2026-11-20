using System.IO;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Snapline.Core;

public sealed class CaptureItem
{
    public CaptureItem(BitmapSource image) { Image = image; }

    public BitmapSource Image { get; set; }
    public DateTime Time { get; } = DateTime.Now;
    public string? FilePath { get; set; }
    public Int32Rect? Origin { get; set; }
    public bool IsVideo { get; init; }
    public TimeSpan Duration { get; init; }

    /// <summary>Returns a file on disk for this capture (saved file, or a temp PNG for drag &amp; drop).</summary>
    public string EnsureFile()
    {
        if (FilePath != null && File.Exists(FilePath)) return FilePath;
        FilePath = CaptureStore.SaveTemp(Image);
        return FilePath;
    }
}

public static class CaptureStore
{
    public static ObservableCollection<CaptureItem> History { get; } = new();

    public static void Add(CaptureItem item)
    {
        History.Insert(0, item);
        while (History.Count > 24) History.RemoveAt(History.Count - 1);
    }

    public static string NewFileName(string ext) => $"Snapline {DateTime.Now:yyyy-MM-dd 'at' HH.mm.ss}{ext}";

    public static string UniquePath(string folder, string ext)
    {
        Directory.CreateDirectory(folder);
        var name = NewFileName(ext);
        var path = Path.Combine(folder, name);
        int i = 2;
        while (File.Exists(path))
            path = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)} ({i++}){ext}");
        return path;
    }

    public static string SavePng(BitmapSource img, string? path = null)
    {
        path ??= UniquePath(Snap.Settings.SaveFolder, ".png");
        BitmapEncoder enc = path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
            ? new JpegBitmapEncoder { QualityLevel = 92 }
            : new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(img));
        using var fs = File.Create(path);
        enc.Save(fs);
        return path;
    }

    public static string SaveTemp(BitmapSource img) =>
        SavePng(img, UniquePath(Path.Combine(Path.GetTempPath(), "Snapline"), ".png"));

    public static void CopyImage(BitmapSource img)
    {
        var data = new DataObject();
        data.SetImage(img);
        var ms = new MemoryStream();
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(img));
        enc.Save(ms);
        data.SetData("PNG", ms, false);
        SetClipboard(data);
    }

    public static void CopyText(string text) => SetClipboard(new DataObject(DataFormats.UnicodeText, text));

    public static void CopyFile(string path)
    {
        var data = new DataObject();
        data.SetFileDropList(new StringCollection { path });
        SetClipboard(data);
    }

    private static void SetClipboard(DataObject data)
    {
        for (int i = 0; i < 8; i++)
        {
            try { Clipboard.SetDataObject(data, true); return; }
            catch (System.Runtime.InteropServices.COMException) { Thread.Sleep(40); }
        }
    }
}
