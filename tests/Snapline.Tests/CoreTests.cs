using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapline.Core;

namespace Snapline.Tests;

public class PixelBufferTests
{
    // A 4x3 buffer placed at (100, 50) on the virtual screen; pixel (x, y) has B = x, G = y, R = 200.
    private static PixelBuffer Buffer()
    {
        var px = new byte[4 * 3 * 4];
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 4; x++)
            {
                int i = (y * 4 + x) * 4;
                px[i] = (byte)x; px[i + 1] = (byte)y; px[i + 2] = 200; px[i + 3] = 255;
            }
        return new PixelBuffer(100, 50, 4, 3, px);
    }

    [Fact]
    public void Reads_pixels_in_screen_coordinates()
    {
        var c = Buffer().GetPixelGlobal(102, 51);
        Assert.Equal(Color.FromArgb(255, 200, 1, 2), c);
        Assert.Equal(Colors.Transparent, Buffer().GetPixelGlobal(99, 51));
    }

    [Fact]
    public void Crop_keeps_the_right_pixels_and_clips_to_the_buffer()
    {
        var crop = Buffer().CropGlobal(new Int32Rect(101, 49, 10, 2)); // runs off the top and the right
        Assert.Equal(new Int32Rect(101, 50, 3, 1), crop.Bounds);
        Assert.Equal(Color.FromArgb(255, 200, 0, 1), crop.GetPixelGlobal(101, 50));
    }

    [Fact]
    public void Crop_outside_the_buffer_fails_clearly()
    {
        Assert.Throws<ArgumentException>(() => Buffer().CropGlobal(new Int32Rect(0, 0, 10, 10)));
    }
}

public class GifWriterTests
{
    [Fact]
    public void Writes_a_valid_animated_gif()
    {
        const int w = 48, h = 32;
        string path = Path.Combine(Path.GetTempPath(), "snapline-test-" + Guid.NewGuid() + ".gif");
        try
        {
            var colours = new[] { Colors.Red, Colors.Lime, Colors.Blue };
            using (var gif = new GifWriter(path, w, h))
                foreach (var c in colours)
                {
                    var bgra = new byte[w * h * 4];
                    for (int i = 0; i < bgra.Length; i += 4) { bgra[i] = c.B; bgra[i + 1] = c.G; bgra[i + 2] = c.R; bgra[i + 3] = 255; }
                    gif.AddFrame(bgra, 10);
                }

            using var fs = File.OpenRead(path);
            var decoder = new GifBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert.Equal(3, decoder.Frames.Count);
            for (int f = 0; f < 3; f++)
            {
                var frame = new FormatConvertedBitmap(decoder.Frames[f], PixelFormats.Bgra32, null, 0);
                Assert.Equal(w, frame.PixelWidth);
                var px = new byte[4];
                frame.CopyPixels(new Int32Rect(w / 2, h / 2, 1, 1), px, 4, 0);
                Assert.Equal(colours[f], Color.FromRgb(px[2], px[1], px[0]));
            }
        }
        finally { File.Delete(path); }
    }
}

public class SettingsTests
{
    [Fact]
    public void Defaults_survive_a_save_and_load_round_trip()
    {
        var s = new AppSettings { RecordFps = 60, AutoSave = true, OverlaySeconds = 0 };
        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s))!;
        Assert.Equal(60, back.RecordFps);
        Assert.True(back.AutoSave);
        Assert.Equal(0, back.OverlaySeconds);
        Assert.True(new AppSettings().UsePrintScreen);
        Assert.True(new AppSettings().AutoCopy);
    }
}

/// <summary>The app, the installer and the changelog must agree on the version.</summary>
public class VersionTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "CHANGELOG.md"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("Repository root not found");
    }

    [Fact]
    public void Versions_match()
    {
        string root = Root();
        string app = Regex.Match(File.ReadAllText(Path.Combine(root, "Snapline", "Snapline.csproj")), "<Version>([^<]+)</Version>").Groups[1].Value;
        string setup = Regex.Match(File.ReadAllText(Path.Combine(root, "setup", "Installer.cs")), "Version = \"([^\"]+)\"").Groups[1].Value;
        string changelog = Regex.Match(File.ReadAllText(Path.Combine(root, "CHANGELOG.md")), @"^## (\d+\.\d+\.\d+)", RegexOptions.Multiline).Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(app));
        Assert.Equal(app, setup);
        Assert.Equal(app, changelog);
        Assert.Equal(Updates.Updater.CurrentText, app);
    }
}
