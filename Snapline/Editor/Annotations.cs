using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Snapline.Editor;

public abstract class Annotation
{
    protected static readonly Brush ShadowBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x48, 0, 0, 0)));

    public Color Color { get; set; }
    public double Stroke { get; set; }
    public abstract Rect Bounds { get; }
    public abstract void Offset(Vector v);
    public abstract void Render(DrawingContext dc);

    public virtual bool HitTest(Point p, double tolerance)
    {
        var b = Bounds;
        b.Inflate(tolerance, tolerance);
        return b.Contains(p);
    }

    public virtual Annotation Clone() => (Annotation)MemberwiseClone();

    protected static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
    protected Brush Fill => Frozen(new SolidColorBrush(Color));
    protected Pen MakePen(double thickness, Color? c = null) => Frozen(new Pen(new SolidColorBrush(c ?? Color), thickness)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
        LineJoin = PenLineJoin.Round,
    });

    protected double ShadowOffset => Math.Max(1, Stroke * 0.3);

    protected static double DistanceToSegment(Point p, Point a, Point b)
    {
        var ab = b - a;
        double len2 = ab.LengthSquared;
        if (len2 < 1e-6) return (p - a).Length;
        double t = Math.Clamp(Vector.Multiply(p - a, ab) / len2, 0, 1);
        return (p - (a + t * ab)).Length;
    }

    protected static bool IsLight(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 170;
}

public abstract class TwoPointAnnotation : Annotation
{
    public Point A { get; set; }
    public Point B { get; set; }
    public override Rect Bounds => new(A, B);
    public override void Offset(Vector v) { A += v; B += v; }
}

public sealed class ArrowAnnotation : TwoPointAnnotation
{
    public override bool HitTest(Point p, double tol) => DistanceToSegment(p, A, B) <= tol + Stroke * 1.5;

    public override void Render(DrawingContext dc)
    {
        var geo = BuildGeometry();
        if (geo == null) return;
        dc.PushTransform(new TranslateTransform(0, ShadowOffset));
        dc.DrawGeometry(ShadowBrush, null, geo);
        dc.Pop();
        dc.DrawGeometry(Fill, MakePen(Math.Max(1, Stroke * 0.25)), geo);
    }

    private Geometry? BuildGeometry()
    {
        var v = B - A;
        double len = v.Length;
        if (len < 2) return null;
        var d = v / len;
        var n = new Vector(-d.Y, d.X);
        double head = Math.Min(len * 0.55, Math.Max(14, Stroke * 4.8));
        double headW = head * 0.92;
        double tail = Math.Max(1, Stroke * 0.3);
        double neck = Stroke * 1.05;
        var basePt = B - d * head * 0.82;
        var pts = new[]
        {
            A + n * tail / 2, basePt + n * neck / 2, B - d * head + n * headW / 2, B,
            B - d * head - n * headW / 2, basePt - n * neck / 2, A - n * tail / 2,
        };
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(pts[0], true, true);
            ctx.PolyLineTo(pts.Skip(1).ToList(), true, true);
        }
        g.Freeze();
        return g;
    }
}

public sealed class LineAnnotation : TwoPointAnnotation
{
    public override bool HitTest(Point p, double tol) => DistanceToSegment(p, A, B) <= tol + Stroke;

    public override void Render(DrawingContext dc)
    {
        dc.DrawLine(MakePen(Stroke, Color.FromArgb(0x48, 0, 0, 0)), A + new Vector(0, ShadowOffset), B + new Vector(0, ShadowOffset));
        dc.DrawLine(MakePen(Stroke), A, B);
    }
}

public sealed class RectAnnotation : TwoPointAnnotation
{
    public override bool HitTest(Point p, double tol)
    {
        var outer = Bounds; outer.Inflate(tol + Stroke, tol + Stroke);
        var inner = Bounds; inner.Inflate(-(tol + Stroke), -(tol + Stroke));
        return outer.Contains(p) && (inner.IsEmpty || !inner.Contains(p));
    }

    public override void Render(DrawingContext dc)
    {
        var r = Bounds;
        double rad = Stroke * 1.4;
        var sr = r; sr.Offset(0, ShadowOffset);
        dc.DrawRoundedRectangle(null, MakePen(Stroke, Color.FromArgb(0x48, 0, 0, 0)), sr, rad, rad);
        dc.DrawRoundedRectangle(null, MakePen(Stroke), r, rad, rad);
    }
}

public sealed class EllipseAnnotation : TwoPointAnnotation
{
    public override void Render(DrawingContext dc)
    {
        var r = Bounds;
        var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        dc.DrawEllipse(null, MakePen(Stroke, Color.FromArgb(0x48, 0, 0, 0)), c + new Vector(0, ShadowOffset), r.Width / 2, r.Height / 2);
        dc.DrawEllipse(null, MakePen(Stroke), c, r.Width / 2, r.Height / 2);
    }
}

public sealed class PenAnnotation : Annotation
{
    public List<Point> Points { get; private set; } = new();
    public bool Highlighter { get; init; }

    public override Rect Bounds
    {
        get
        {
            if (Points.Count == 0) return Rect.Empty;
            double x0 = Points.Min(p => p.X), y0 = Points.Min(p => p.Y), x1 = Points.Max(p => p.X), y1 = Points.Max(p => p.Y);
            var r = new Rect(new Point(x0, y0), new Point(x1, y1));
            double w = Highlighter ? Stroke * 2.5 : Stroke / 2;
            r.Inflate(w, w);
            return r;
        }
    }

    public override bool HitTest(Point p, double tol)
    {
        double w = (Highlighter ? Stroke * 2.5 : Stroke / 2) + tol;
        for (int i = 1; i < Points.Count; i++)
            if (DistanceToSegment(p, Points[i - 1], Points[i]) <= w) return true;
        return Points.Count == 1 && (p - Points[0]).Length <= w;
    }

    public override void Offset(Vector v)
    {
        for (int i = 0; i < Points.Count; i++) Points[i] += v;
    }

    public override Annotation Clone()
    {
        var c = (PenAnnotation)MemberwiseClone();
        c.Points = new List<Point>(Points);
        return c;
    }

    public override void Render(DrawingContext dc)
    {
        if (Points.Count == 0) return;
        var color = Highlighter ? Color.FromArgb(0x70, Color.R, Color.G, Color.B) : Color;
        double thickness = Highlighter ? Stroke * 5 : Stroke;
        var pen = Frozen(new Pen(new SolidColorBrush(color), thickness)
        {
            StartLineCap = Highlighter ? PenLineCap.Square : PenLineCap.Round,
            EndLineCap = Highlighter ? PenLineCap.Square : PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        });
        if (Points.Count == 1)
        {
            dc.DrawEllipse(new SolidColorBrush(color), null, Points[0], thickness / 2, thickness / 2);
            return;
        }
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(Points[0], false, false);
            for (int i = 1; i < Points.Count; i++)
            {
                var mid = new Point((Points[i - 1].X + Points[i].X) / 2, (Points[i - 1].Y + Points[i].Y) / 2);
                ctx.QuadraticBezierTo(Points[i - 1], mid, true, true);
            }
            ctx.LineTo(Points[^1], true, true);
        }
        g.Freeze();
        if (!Highlighter)
        {
            dc.PushTransform(new TranslateTransform(0, ShadowOffset));
            dc.DrawGeometry(null, Frozen(new Pen(ShadowBrush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }), g);
            dc.Pop();
        }
        dc.DrawGeometry(null, pen, g);
    }
}

public sealed class TextAnnotation : Annotation
{
    private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    public Point Pos { get; set; }
    public string Text { get; set; } = "";
    public double FontSize { get; set; } = 28;

    private FormattedText Format() => new(Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, FontSize, Fill, 1.0);

    public override Rect Bounds
    {
        get
        {
            var ft = Format();
            return new Rect(Pos, new Size(Math.Max(ft.WidthIncludingTrailingWhitespace, 4), ft.Height));
        }
    }

    public override void Offset(Vector v) => Pos += v;

    public override void Render(DrawingContext dc)
    {
        if (string.IsNullOrEmpty(Text)) return;
        var geo = Format().BuildGeometry(Pos);
        var outline = IsLight(Color) ? Color.FromArgb(0xE6, 0x14, 0x14, 0x16) : Color.FromArgb(0xF2, 255, 255, 255);
        dc.DrawGeometry(null, Frozen(new Pen(new SolidColorBrush(outline), FontSize * 0.16) { LineJoin = PenLineJoin.Round }), geo);
        dc.DrawGeometry(Fill, null, geo);
    }
}

public sealed class CounterAnnotation : Annotation
{
    private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    public Point Center { get; set; }
    public int Number { get; set; }
    private double Radius => Math.Max(11, Stroke * 3.4);

    public override Rect Bounds => new(Center.X - Radius, Center.Y - Radius, Radius * 2, Radius * 2);
    public override void Offset(Vector v) => Center += v;

    public override void Render(DrawingContext dc)
    {
        double r = Radius;
        dc.DrawEllipse(ShadowBrush, null, Center + new Vector(0, ShadowOffset), r + 1, r + 1);
        dc.DrawEllipse(Fill, Frozen(new Pen(Brushes.White, r * 0.13)), Center, r, r);
        var fg = IsLight(Color) ? Brushes.Black : Brushes.White;
        var ft = new FormattedText(Number.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, r * 1.1, fg, 1.0);
        dc.DrawText(ft, new Point(Center.X - ft.Width / 2, Center.Y - ft.Height / 2));
    }
}

public sealed class PixelateAnnotation : Annotation
{
    public Rect Area { get; set; }
    public BitmapSource? Image { get; private set; }

    public override Rect Bounds => Area;
    public override void Offset(Vector v) { var r = Area; r.Offset(v); Area = r; }

    public void Rebuild(BitmapSource src, double unit)
    {
        int x0 = (int)Math.Max(0, Math.Floor(Area.X)), y0 = (int)Math.Max(0, Math.Floor(Area.Y));
        int x1 = (int)Math.Min(src.PixelWidth, Math.Ceiling(Area.Right)), y1 = (int)Math.Min(src.PixelHeight, Math.Ceiling(Area.Bottom));
        if (x1 - x0 < 1 || y1 - y0 < 1) { Image = null; return; }
        int w = x1 - x0, h = y1 - y0;
        Area = new Rect(x0, y0, w, h);
        BitmapSource bgra = src.Format == PixelFormats.Bgra32 || src.Format == PixelFormats.Pbgra32 ? src : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        var px = new byte[w * h * 4];
        bgra.CopyPixels(new Int32Rect(x0, y0, w, h), px, w * 4, 0);
        int block = (int)Math.Max(6, Math.Round(10 * unit));
        for (int by = 0; by < h; by += block)
        for (int bx = 0; bx < w; bx += block)
        {
            int bw = Math.Min(block, w - bx), bh = Math.Min(block, h - by);
            long b = 0, g = 0, r = 0; int n = bw * bh;
            for (int y = by; y < by + bh; y++)
            for (int x = bx; x < bx + bw; x++)
            {
                int i = (y * w + x) * 4;
                b += px[i]; g += px[i + 1]; r += px[i + 2];
            }
            byte ab = (byte)(b / n), ag = (byte)(g / n), ar = (byte)(r / n);
            for (int y = by; y < by + bh; y++)
            for (int x = bx; x < bx + bw; x++)
            {
                int i = (y * w + x) * 4;
                px[i] = ab; px[i + 1] = ag; px[i + 2] = ar; px[i + 3] = 255;
            }
        }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        bmp.Freeze();
        Image = bmp;
    }

    public override void Render(DrawingContext dc)
    {
        if (Image != null) dc.DrawImage(Image, Area);
        else dc.DrawRectangle(Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80))), null, Area);
    }
}
