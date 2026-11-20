using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using D = System.Drawing;
using F = System.Windows.Forms;

namespace Snapline.UI;

public sealed class TrayIcon : IDisposable
{
    private readonly F.NotifyIcon _icon;

    /// <param name="onClick">Left click: the quick panel.</param>
    /// <param name="onMenu">Right click: the menu.</param>
    public TrayIcon(Action onClick, Action onMenu)
    {
        _icon = new F.NotifyIcon { Icon = CreateIcon(), Text = "Snapline — screenshots & recordings", Visible = true };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == F.MouseButtons.Right) onMenu();
            else onClick();
        };
    }

    public static D.Icon CreateIcon()
    {
        using var bmp = DrawLogo(32);
        return D.Icon.FromHandle(bmp.GetHicon());
    }

    /// <summary>The Snapline mark: a gradient squircle with a white viewfinder. Drawn on a 32-unit grid, scaled to size.</summary>
    public static D.Bitmap DrawLogo(int size)
    {
        var bmp = new D.Bitmap(size, size, D.Imaging.PixelFormat.Format32bppArgb);
        using var g = D.Graphics.FromImage(bmp);
        g.SmoothingMode = D.Drawing2D.SmoothingMode.AntiAlias;
        g.PixelOffsetMode = D.Drawing2D.PixelOffsetMode.HighQuality;
        g.ScaleTransform(size / 32f, size / 32f);
        using var path = RoundRect(new D.RectangleF(1, 1, 30, 30), 8);
        using var br = new D.Drawing2D.LinearGradientBrush(new D.Rectangle(0, 0, 32, 32), D.Color.FromArgb(0x5E, 0x5C, 0xE6), D.Color.FromArgb(0x0A, 0x84, 0xFF), 45f);
        g.FillPath(br, path);
        using var pen = new D.Pen(D.Color.White, 2.6f) { StartCap = D.Drawing2D.LineCap.Round, EndCap = D.Drawing2D.LineCap.Round, LineJoin = D.Drawing2D.LineJoin.Round };
        g.DrawLines(pen, new[] { new D.PointF(8, 13), new D.PointF(8, 8), new D.PointF(13, 8) });
        g.DrawLines(pen, new[] { new D.PointF(19, 8), new D.PointF(24, 8), new D.PointF(24, 13) });
        g.DrawLines(pen, new[] { new D.PointF(24, 19), new D.PointF(24, 24), new D.PointF(19, 24) });
        g.DrawLines(pen, new[] { new D.PointF(13, 24), new D.PointF(8, 24), new D.PointF(8, 19) });
        g.FillEllipse(D.Brushes.White, 13.2f, 13.2f, 5.6f, 5.6f);
        return bmp;
    }

    private static ImageSource? _logo;

    /// <summary>The logo as a WPF image, for window title bars, menus and the update window.</summary>
    public static ImageSource Logo
    {
        get
        {
            if (_logo != null) return _logo;
            using var bmp = DrawLogo(128);
            _logo = Snapline.Shared.UiWin.FromBitmap(bmp);
            return _logo;
        }
    }

    /// <summary>Writes icon.png (256 px) and a multi-size app.ico. Used once to make the exe and installer icons.</summary>
    public static void ExportIcons(string dir)
    {
        Directory.CreateDirectory(dir);
        using (var big = DrawLogo(256)) big.Save(Path.Combine(dir, "icon.png"), D.Imaging.ImageFormat.Png);
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var pngs = new List<byte[]>();
        foreach (int s in sizes)
        {
            using var b = DrawLogo(s);
            using var ms = new MemoryStream();
            b.Save(ms, D.Imaging.ImageFormat.Png);
            pngs.Add(ms.ToArray());
        }
        using var f = File.Create(Path.Combine(dir, "app.ico"));
        using var w = new BinaryWriter(f);
        w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(pngs[i].Length); w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var p in pngs) w.Write(p);
    }

    private static D.Drawing2D.GraphicsPath RoundRect(D.RectangleF r, float radius)
    {
        var p = new D.Drawing2D.GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
