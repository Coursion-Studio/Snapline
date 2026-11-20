using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Snapline.Core;

/// <summary>BGRA pixels (top-down, opaque) of a rectangle in global physical screen coordinates.</summary>
public sealed class PixelBuffer
{
    public PixelBuffer(int x, int y, int width, int height, byte[] pixels)
    {
        X = x; Y = y; Width = width; Height = height; Pixels = pixels;
    }

    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }
    public int Stride => Width * 4;
    public Int32Rect Bounds => new(X, Y, Width, Height);

    public BitmapSource ToBitmapSource()
    {
        var bmp = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, Pixels, Stride);
        bmp.Freeze();
        return bmp;
    }

    public PixelBuffer CropGlobal(Int32Rect r)
    {
        int x0 = Math.Max(r.X, X), y0 = Math.Max(r.Y, Y);
        int x1 = Math.Min(r.X + r.Width, X + Width), y1 = Math.Min(r.Y + r.Height, Y + Height);
        if (x1 <= x0 || y1 <= y0) throw new ArgumentException("The selected area is outside the screen.");
        int w = x1 - x0, h = y1 - y0;
        var dst = new byte[w * h * 4];
        for (int row = 0; row < h; row++)
            Buffer.BlockCopy(Pixels, ((y0 - Y + row) * Width + (x0 - X)) * 4, dst, row * w * 4, w * 4);
        return new PixelBuffer(x0, y0, w, h, dst);
    }

    public Color GetPixelGlobal(int gx, int gy)
    {
        int x = gx - X, y = gy - Y;
        if (x < 0 || y < 0 || x >= Width || y >= Height) return Colors.Transparent;
        int i = (y * Width + x) * 4;
        return Color.FromArgb(255, Pixels[i + 2], Pixels[i + 1], Pixels[i]);
    }
}

/// <summary>Reusable GDI screen grabber. Keeps one DIB section alive so repeated grabs (video) are cheap.</summary>
public sealed class GdiCapturer : IDisposable
{
    private readonly IntPtr _screenDc, _memDc, _bitmap, _oldBitmap, _bits;
    private readonly Dictionary<IntPtr, (int X, int Y)> _hotspots = new();
    private bool _disposed;

    public GdiCapturer(int width, int height)
    {
        Width = width; Height = height;
        _screenDc = Native.GetDC(IntPtr.Zero);
        _memDc = Native.CreateCompatibleDC(_screenDc);
        var bmi = new Native.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };
        _bitmap = Native.CreateDIBSection(_screenDc, ref bmi, 0, out _bits, IntPtr.Zero, 0);
        if (_bitmap == IntPtr.Zero) throw new InvalidOperationException("Could not allocate capture surface.");
        _oldBitmap = Native.SelectObject(_memDc, _bitmap);
    }

    public int Width { get; }
    public int Height { get; }
    public int ByteCount => Width * Height * 4;

    public void Grab(int x, int y, bool cursor, bool layered, byte[] dest)
    {
        Native.BitBlt(_memDc, 0, 0, Width, Height, _screenDc, x, y, Native.SRCCOPY | (layered ? Native.CAPTUREBLT : 0));
        if (cursor) DrawCursor(x, y, 1.0);
        Finish(dest);
    }

    public void GrabScaled(int x, int y, int srcWidth, int srcHeight, bool cursor, byte[] dest)
    {
        Native.SetStretchBltMode(_memDc, 4); // HALFTONE
        Native.SetBrushOrgEx(_memDc, 0, 0, IntPtr.Zero);
        Native.StretchBlt(_memDc, 0, 0, Width, Height, _screenDc, x, y, srcWidth, srcHeight, Native.SRCCOPY);
        if (cursor) DrawCursor(x, y, Width / (double)srcWidth);
        Finish(dest);
    }

    private void Finish(byte[] dest)
    {
        Native.GdiFlush();
        Marshal.Copy(_bits, dest, 0, ByteCount);
        var px = MemoryMarshal.Cast<byte, uint>(dest.AsSpan(0, ByteCount));
        for (int i = 0; i < px.Length; i++) px[i] |= 0xFF000000u;
    }

    private void DrawCursor(int originX, int originY, double scale)
    {
        var ci = new Native.CURSORINFO { cbSize = Marshal.SizeOf<Native.CURSORINFO>() };
        if (!Native.GetCursorInfo(ref ci) || (ci.flags & 1) == 0 || ci.hCursor == IntPtr.Zero) return;
        if (!_hotspots.TryGetValue(ci.hCursor, out var hs))
        {
            if (Native.GetIconInfo(ci.hCursor, out var ii))
            {
                hs = (ii.xHotspot, ii.yHotspot);
                if (ii.hbmMask != IntPtr.Zero) Native.DeleteObject(ii.hbmMask);
                if (ii.hbmColor != IntPtr.Zero) Native.DeleteObject(ii.hbmColor);
            }
            _hotspots[ci.hCursor] = hs;
        }
        int size = Math.Abs(scale - 1.0) < 0.001 ? 0 : (int)Math.Max(8, Math.Round(Native.GetSystemMetrics(13) * scale));
        int px = (int)Math.Round((ci.ptScreenPos.X - originX - hs.X) * scale);
        int py = (int)Math.Round((ci.ptScreenPos.Y - originY - hs.Y) * scale);
        Native.DrawIconEx(_memDc, px, py, ci.hCursor, size, size, 0, IntPtr.Zero, 3);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Native.SelectObject(_memDc, _oldBitmap);
        Native.DeleteObject(_bitmap);
        Native.DeleteDC(_memDc);
        Native.ReleaseDC(IntPtr.Zero, _screenDc);
    }
}

public static class ScreenCapture
{
    public static Int32Rect VirtualScreen => new(
        Native.GetSystemMetrics(76), Native.GetSystemMetrics(77),
        Native.GetSystemMetrics(78), Native.GetSystemMetrics(79));

    public static PixelBuffer Capture(Int32Rect r, bool cursor = false)
    {
        using var cap = new GdiCapturer(r.Width, r.Height);
        var buf = new byte[cap.ByteCount];
        cap.Grab(r.X, r.Y, cursor, true, buf);
        return new PixelBuffer(r.X, r.Y, r.Width, r.Height, buf);
    }
}
