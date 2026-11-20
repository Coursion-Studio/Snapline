using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Snapline.Editor;

public enum Tool { Select, Arrow, Rect, Ellipse, Line, Pen, Highlighter, Text, Counter, Pixelate, Crop }

/// <summary>Draws the screenshot plus vector annotations in image-pixel coordinates, with snapshot-based undo.</summary>
public sealed class AnnotationSurface : FrameworkElement
{
    private sealed record Snapshot(BitmapSource Base, List<Annotation> Items);

    private static readonly double[] StrokeLevels = { 3, 5, 8 };
    private static readonly double[] FontLevels = { 20, 30, 46 };

    private readonly Stack<Snapshot> _undo = new(), _redo = new();
    private Annotation? _preview;
    private Point _start, _last;
    private bool _down, _movePushed, _textEditPushed;
    private Rect? _crop;

    public AnnotationSurface(BitmapSource image)
    {
        Base = image;
        SetBase(image);
        Focusable = true;
        ClipToBounds = true;
        Cursor = Cursors.Cross;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    public BitmapSource Base { get; private set; }
    public List<Annotation> Items { get; } = new();
    public Annotation? Selected { get; private set; }
    public Tool Tool { get; set; } = Tool.Arrow;
    public Color Color { get; private set; } = Color.FromRgb(0xFF, 0x3B, 0x30);
    public int SizeLevel { get; private set; } = 1;
    public double Unit { get; private set; } = 1;
    public bool Exporting { get; set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public double StrokeWidth => StrokeLevels[SizeLevel] * Unit;
    public double FontSizeForLevel => FontLevels[SizeLevel] * Unit;

    public event Action? StateChanged;
    public event Action? BaseChanged;
    public event Action<Point, TextAnnotation?>? TextRequested;

    private void SetBase(BitmapSource b)
    {
        Base = b;
        Width = b.PixelWidth;
        Height = b.PixelHeight;
        Unit = Math.Max(1, Math.Max(b.PixelWidth, b.PixelHeight) / 1400.0);
        InvalidateVisual();
    }

    private double ViewScale
    {
        get
        {
            try
            {
                var win = Window.GetWindow(this);
                if (win == null) return 1;
                var m = TransformToAncestor(win).Transform(new Point(1, 0)) - TransformToAncestor(win).Transform(new Point(0, 0));
                return Math.Max(0.02, m.Length);
            }
            catch { return 1; }
        }
    }

    // ---------- state ----------

    private void PushUndo()
    {
        _undo.Push(new Snapshot(Base, Items.Select(i => i.Clone()).ToList()));
        _redo.Clear();
    }

    private void Restore(Snapshot s)
    {
        bool resized = s.Base != Base;
        if (resized) SetBase(s.Base);
        Items.Clear();
        Items.AddRange(s.Items.Select(i => i.Clone()));
        Selected = null;
        if (resized) BaseChanged?.Invoke();
        Changed();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(new Snapshot(Base, Items.Select(i => i.Clone()).ToList()));
        Restore(_undo.Pop());
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(new Snapshot(Base, Items.Select(i => i.Clone()).ToList()));
        Restore(_redo.Pop());
    }

    private void Changed()
    {
        InvalidateVisual();
        StateChanged?.Invoke();
    }

    public void SetColor(Color c)
    {
        Color = c;
        if (Selected is not null and not PixelateAnnotation)
        {
            PushUndo();
            Selected.Color = c;
            Changed();
        }
    }

    public void SetSize(int level)
    {
        SizeLevel = Math.Clamp(level, 0, 2);
        if (Selected is not null and not PixelateAnnotation)
        {
            PushUndo();
            Selected.Stroke = StrokeWidth;
            if (Selected is TextAnnotation t) t.FontSize = FontSizeForLevel;
            Changed();
        }
    }

    public void DeleteSelected()
    {
        if (Selected == null) return;
        PushUndo();
        Items.Remove(Selected);
        Selected = null;
        Changed();
    }

    public void ClearSelection()
    {
        Selected = null;
        InvalidateVisual();
    }

    public void CommitText(TextAnnotation t)
    {
        if (!_textEditPushed) PushUndo();
        _textEditPushed = false;
        Items.Add(t);
        Changed();
    }

    public void CancelText()
    {
        _textEditPushed = false;
        Changed();
    }

    private void BeginTextEdit(Point p, TextAnnotation? existing)
    {
        if (existing != null)
        {
            PushUndo();
            _textEditPushed = true;
            Items.Remove(existing);
            Selected = null;
            InvalidateVisual();
        }
        TextRequested?.Invoke(p, existing);
    }

    private Annotation? HitTestItems(Point p)
    {
        double tol = 5 / ViewScale;
        for (int i = Items.Count - 1; i >= 0; i--)
            if (Items[i].HitTest(p, tol)) return Items[i];
        return null;
    }

    private int NextCounter => Items.OfType<CounterAnnotation>().Select(c => c.Number).DefaultIfEmpty(0).Max() + 1;

    // ---------- input ----------

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var p = e.GetPosition(this);
        _start = _last = p;
        e.Handled = true;

        switch (Tool)
        {
            case Tool.Select:
            {
                Selected = HitTestItems(p);
                if (Selected is TextAnnotation t && e.ClickCount == 2) { BeginTextEdit(t.Pos, t); return; }
                _movePushed = false;
                break;
            }
            case Tool.Text:
            {
                var hit = HitTestItems(p) as TextAnnotation;
                BeginTextEdit(hit?.Pos ?? p, hit);
                return;
            }
            case Tool.Counter:
                PushUndo();
                Items.Add(new CounterAnnotation { Center = p, Number = NextCounter, Color = Color, Stroke = StrokeWidth });
                Changed();
                return;
            case Tool.Pen:
            case Tool.Highlighter:
                _preview = new PenAnnotation { Color = Color, Stroke = StrokeWidth, Highlighter = Tool == Tool.Highlighter };
                ((PenAnnotation)_preview).Points.Add(p);
                break;
            case Tool.Crop:
                _crop = new Rect(p, p);
                break;
            case Tool.Pixelate:
                _preview = new PixelateAnnotation { Area = new Rect(p, p), Stroke = StrokeWidth };
                break;
            default:
                _preview = Tool switch
                {
                    Tool.Arrow => new ArrowAnnotation(),
                    Tool.Rect => new RectAnnotation(),
                    Tool.Ellipse => new EllipseAnnotation(),
                    _ => new LineAnnotation(),
                };
                _preview.Color = Color;
                _preview.Stroke = StrokeWidth;
                ((TwoPointAnnotation)_preview).A = p;
                ((TwoPointAnnotation)_preview).B = p;
                break;
        }
        _down = true;
        CaptureMouse();
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_down) return;
        var p = e.GetPosition(this);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        switch (Tool)
        {
            case Tool.Select when Selected != null:
                if (!_movePushed) { PushUndo(); _movePushed = true; }
                Selected.Offset(p - _last);
                break;
            case Tool.Pen:
            case Tool.Highlighter:
                if (_preview is PenAnnotation pa && (p - pa.Points[^1]).Length >= 1.2 * Unit)
                {
                    if (Tool == Tool.Highlighter && shift) { pa.Points.Clear(); pa.Points.Add(_start); pa.Points.Add(new Point(p.X, _start.Y)); }
                    else pa.Points.Add(p);
                }
                break;
            case Tool.Crop:
                _crop = Rect.Intersect(new Rect(_start, p), new Rect(0, 0, Width, Height));
                break;
            case Tool.Pixelate when _preview is PixelateAnnotation px:
                px.Area = new Rect(_start, p);
                break;
            default:
                if (_preview is TwoPointAnnotation tp)
                {
                    var b = p;
                    if (shift)
                    {
                        var v = p - _start;
                        if (tp is RectAnnotation or EllipseAnnotation)
                        {
                            double s = Math.Max(Math.Abs(v.X), Math.Abs(v.Y));
                            b = _start + new Vector(Math.Sign(v.X) * s, Math.Sign(v.Y) * s);
                        }
                        else
                        {
                            double ang = Math.Round(Math.Atan2(v.Y, v.X) / (Math.PI / 4)) * (Math.PI / 4);
                            b = _start + new Vector(Math.Cos(ang), Math.Sin(ang)) * v.Length;
                        }
                    }
                    tp.B = b;
                }
                break;
        }
        _last = p;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_down) return;
        _down = false;
        ReleaseMouseCapture();
        double min = 3 * Unit;

        switch (Tool)
        {
            case Tool.Select:
                if (_movePushed && Selected is PixelateAnnotation movedPx) movedPx.Rebuild(Base, Unit);
                break;
            case Tool.Crop:
                if (_crop is { } c && c.Width > min && c.Height > min) ApplyCrop(c);
                _crop = null;
                break;
            case Tool.Pixelate:
                if (_preview is PixelateAnnotation px && px.Area.Width > min && px.Area.Height > min)
                {
                    px.Rebuild(Base, Unit);
                    PushUndo();
                    Items.Add(px);
                }
                break;
            default:
                if (_preview != null && (_preview is PenAnnotation || _preview.Bounds.Width > min || _preview.Bounds.Height > min))
                {
                    PushUndo();
                    Items.Add(_preview);
                }
                break;
        }
        _preview = null;
        Changed();
    }

    private void ApplyCrop(Rect r)
    {
        int x = (int)Math.Max(0, Math.Round(r.X)), y = (int)Math.Max(0, Math.Round(r.Y));
        int w = (int)Math.Min(Base.PixelWidth - x, Math.Round(r.Width)), h = (int)Math.Min(Base.PixelHeight - y, Math.Round(r.Height));
        if (w < 2 || h < 2) return;
        PushUndo();
        var cropped = new CroppedBitmap(Base, new Int32Rect(x, y, w, h));
        cropped.Freeze();
        foreach (var a in Items) a.Offset(new Vector(-x, -y));
        Selected = null;
        SetBase(cropped);
        BaseChanged?.Invoke();
    }

    // ---------- rendering ----------

    protected override void OnRender(DrawingContext dc)
    {
        var full = new Rect(0, 0, Base.PixelWidth, Base.PixelHeight);
        dc.DrawImage(Base, full);
        foreach (var a in Items) a.Render(dc);
        _preview?.Render(dc);
        if (Exporting) return;

        double px = 1 / ViewScale;
        if (Selected != null && Items.Contains(Selected))
        {
            var r = Selected.Bounds;
            r.Inflate(6 * px, 6 * px);
            dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)), 3 * px), r, 4 * px, 4 * px);
            dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF)), 1.5 * px) { DashStyle = new DashStyle(new double[] { 3, 2 }, 0) }, r, 4 * px, 4 * px);
        }
        if (_crop is { } c && !c.IsEmpty)
        {
            var dim = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(full), new RectangleGeometry(c));
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), null, dim);
            var thin = new Pen(new SolidColorBrush(Color.FromArgb(0x70, 255, 255, 255)), 1 * px);
            for (int i = 1; i <= 2; i++)
            {
                dc.DrawLine(thin, new Point(c.X + c.Width * i / 3, c.Y), new Point(c.X + c.Width * i / 3, c.Bottom));
                dc.DrawLine(thin, new Point(c.X, c.Y + c.Height * i / 3), new Point(c.Right, c.Y + c.Height * i / 3));
            }
            dc.DrawRectangle(null, new Pen(Brushes.White, 1.5 * px), c);
            var label = new FormattedText($"{Math.Round(c.Width)} × {Math.Round(c.Height)}", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12 * px, Brushes.White, 1.0);
            var lp = new Point(c.X + 6 * px, c.Y + 4 * px);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0xC0, 0x1C, 0x1C, 0x1E)), null,
                new Rect(lp.X - 4 * px, lp.Y - 2 * px, label.Width + 8 * px, label.Height + 4 * px), 4 * px, 4 * px);
            dc.DrawText(label, lp);
        }
    }
}
