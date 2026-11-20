using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Snapline.Core;

public static class TextRecognizer
{
    public static async Task<string> RecognizeAsync(PixelBuffer px)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException("No Windows OCR language pack is installed.");

        byte[] data = px.Pixels;
        int w = px.Width, h = px.Height;

        // Small selections recognise far better when upscaled.
        double scale = 1;
        int max = Math.Max(w, h);
        if (max < 1400) scale = Math.Min(3, 1400.0 / max);
        if (max * scale > OcrEngine.MaxImageDimension) scale = OcrEngine.MaxImageDimension / (double)max;
        if (Math.Abs(scale - 1) > 0.01)
        {
            var src = px.ToBitmapSource();
            var tb = new TransformedBitmap(src, new ScaleTransform(scale, scale));
            w = tb.PixelWidth; h = tb.PixelHeight;
            data = new byte[w * h * 4];
            var conv = new FormatConvertedBitmap(tb, PixelFormats.Bgra32, null, 0);
            conv.CopyPixels(data, w * 4, 0);
        }

        using var sb = SoftwareBitmap.CreateCopyFromBuffer(data.AsBuffer(), BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(sb);
        return string.Join(Environment.NewLine, result.Lines.Select(l => l.Text)).Trim();
    }
}
