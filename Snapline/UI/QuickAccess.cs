using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Snapline.Core;

namespace Snapline.UI;

/// <summary>Floating stack of capture cards in the bottom-left corner (the "quick access overlay").</summary>
public sealed class QuickAccessHost : Window
{
    private readonly StackPanel _stack = new() { Margin = new Thickness(20, 8, 24, 22), VerticalAlignment = VerticalAlignment.Bottom };

    public QuickAccessHost()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        Foreground = Brushes.White;
        Content = _stack;
        SizeChanged += (_, _) => Reposition();
        SourceInitialized += (_, _) =>
        {
            WindowUtil.SetToolWindow(this);
            Excluded = !Dev.Active && WindowUtil.ExcludeFromCapture(this);
        };
    }

    /// <summary>True when Windows keeps this window out of screenshots, so it never needs hiding before a capture.</summary>
    public bool Excluded { get; private set; }

    private void Reposition()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Left;
        Top = wa.Bottom - ActualHeight;
    }

    public void ShowCapture(CaptureItem item) => Add(QuickCard.ForImage(this, item));
    public void ShowVideo(CaptureItem item) => Add(QuickCard.ForVideo(this, item));
    public void ShowText(string text) => Add(QuickCard.ForText(this, text));
    public void ShowInfo(string title, string body, string glyph = "") => Add(QuickCard.ForInfo(this, title, body, glyph));

    private void Add(QuickCard card)
    {
        if (!IsVisible) Show();
        Opacity = 1;
        _stack.Children.Add(card);
        var live = _stack.Children.OfType<QuickCard>().Where(c => !c.IsDismissing).ToList();
        for (int i = 0; i < live.Count - 5; i++) live[i].Dismiss();
    }

    internal void Remove(QuickCard card)
    {
        _stack.Children.Remove(card);
        if (_stack.Children.Count == 0) Hide();
    }

    public void DismissAll()
    {
        foreach (var c in _stack.Children.OfType<QuickCard>().ToList()) c.Dismiss();
    }
}

public sealed class QuickCard : Border
{
    public const double CardWidth = 256;

    private readonly QuickAccessHost _host;
    private readonly CaptureItem? _item;
    private readonly TranslateTransform _slide = new(-(CardWidth + 48), 0);
    private readonly ScaleTransform _collapse = new(1, 1);
    private readonly DispatcherTimer _timer;
    private readonly Grid _content = new();
    private readonly int _seconds;
    private Border? _hover;
    private Border? _feedback;
    private TextBlock? _feedbackText;
    private Point? _dragStart;

    private QuickCard(QuickAccessHost host, CaptureItem? item, int seconds)
    {
        _host = host;
        _item = item;
        _seconds = seconds;
        Width = CardWidth;
        Margin = new Thickness(0, 12, 0, 0);
        CornerRadius = new CornerRadius(14);
        Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1E));
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x38, 255, 255, 255));
        BorderThickness = new Thickness(1);
        Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.5 };
        RenderTransform = _slide;
        LayoutTransform = _collapse;
        Opacity = 0;
        Child = _content;
        _content.SizeChanged += (_, e) =>
            _content.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 13, 13);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(1, seconds)) };
        _timer.Tick += (_, _) => Dismiss();
        if (seconds > 0) _timer.Start();

        MouseEnter += (_, _) =>
        {
            _timer.Stop();
            if (_hover != null) WindowUtil.Fade(_hover, 1, 140);
        };
        MouseLeave += (_, _) =>
        {
            if (_seconds > 0 && !IsDismissing) _timer.Start();
            if (_hover != null) WindowUtil.Fade(_hover, 0, 200);
        };
        Loaded += (_, _) => AnimateIn();

        PreviewMouseLeftButtonDown += OnPreviewDown;
        PreviewMouseMove += OnPreviewMove;
        PreviewMouseLeftButtonUp += (_, _) => _dragStart = null;
    }

    public bool IsDismissing { get; private set; }

    // ---------- factories ----------

    public static QuickCard ForImage(QuickAccessHost host, CaptureItem item)
    {
        var card = new QuickCard(host, item, Snap.Settings.OverlaySeconds);
        var img = item.Image;
        double h = Math.Clamp(CardWidth * img.PixelHeight / (double)Math.Max(1, img.PixelWidth), 72, 220);
        card._content.Height = h;
        var image = new Image { Source = img, Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        card._content.Children.Add(image);

        var center = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        center.Children.Add(card.Pill("Copy", card.CopyImage));
        center.Children.Add(card.Pill("Save", card.SaveImage));

        var topRight = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6) };
        topRight.Children.Add(card.Icon("", "Annotate", () => { Snap.OpenEditor(item.Image); card.Dismiss(); }));
        topRight.Children.Add(card.Icon("", "Pin to screen", () => { Snap.Pin(item.Image, item.Origin); card.Dismiss(); }));

        var bottomLeft = card.Icon(CombineIcon(), "Add to Combine", () => { Snap.AddToCombine(item.Image); card.Dismiss(); });
        bottomLeft.HorizontalAlignment = HorizontalAlignment.Left;
        bottomLeft.VerticalAlignment = VerticalAlignment.Bottom;
        bottomLeft.Margin = new Thickness(6);

        var size = new TextBlock
        {
            Text = $"{img.PixelWidth} × {img.PixelHeight}",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 10, 9),
        };

        card.BuildHover(center, topRight, bottomLeft, size);
        card.ToolTip = null;
        return card;
    }

    public static QuickCard ForVideo(QuickAccessHost host, CaptureItem item)
    {
        var card = new QuickCard(host, item, Snap.Settings.OverlaySeconds);
        var img = item.Image;
        double h = Math.Clamp(CardWidth * img.PixelHeight / (double)Math.Max(1, img.PixelWidth), 90, 200);
        card._content.Height = h;
        card._content.Children.Add(new Image { Source = img, Stretch = Stretch.UniformToFill });

        bool gif = item.FilePath?.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) == true;
        var badge = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(0xC8, 0x10, 0x10, 0x12)),
            Padding = new Thickness(7, 3, 8, 3),
            Margin = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        var bp = new StackPanel { Orientation = Orientation.Horizontal };
        bp.Children.Add(new TextBlock { Text = "", FontFamily = (FontFamily)Application.Current.FindResource("Icons"), FontSize = 9, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
        bp.Children.Add(new TextBlock { Text = (gif ? "GIF  " : "") + Fmt(item.Duration), FontSize = 11, FontWeight = FontWeights.SemiBold });
        badge.Child = bp;
        card._content.Children.Add(badge);

        var center = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        center.Children.Add(card.Pill("Open", () => { Shell(item.FilePath!); card.Dismiss(); }));
        center.Children.Add(card.Pill("Show", () => { Process.Start("explorer.exe", $"/select,\"{item.FilePath}\""); card.Dismiss(); }));

        var topRight = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6) };
        topRight.Children.Add(card.Icon("", "Copy file", () => { CaptureStore.CopyFile(item.FilePath!); card.Flash("Copied"); }));

        card.BuildHover(center, topRight);
        return card;
    }

    public static QuickCard ForText(QuickAccessHost host, string text)
    {
        var card = new QuickCard(host, null, Math.Max(6, Snap.Settings.OverlaySeconds));
        var panel = new StackPanel { Margin = new Thickness(14, 12, 14, 14) };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(Glyph("", Color.FromRgb(0x30, 0xD1, 0x58)));
        head.Children.Add(new TextBlock { Text = "Text copied", FontWeight = FontWeights.SemiBold, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
        panel.Children.Add(head);
        panel.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 92,
            FontSize = 12,
            Margin = new Thickness(0, 10, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0xC4, 0xC4, 0xCA)),
        });
        card._content.Children.Add(panel);
        var close = card.Icon("", "Close", card.Dismiss);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Margin = new Thickness(6);
        close.Opacity = 0.75;
        card._content.Children.Add(close);
        card.Cursor = Cursors.Hand;
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (IsInButton(e.OriginalSource as DependencyObject, card)) return;
            CaptureStore.CopyText(text);
            card.Flash("Copied again");
        };
        card.AddFeedbackLayer();
        return card;
    }

    public static QuickCard ForInfo(QuickAccessHost host, string title, string body, string glyph)
    {
        var card = new QuickCard(host, null, 6);
        var row = new Grid { Margin = new Thickness(14, 12, 34, 13) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(Glyph(glyph, Color.FromRgb(0x0A, 0x84, 0xFF)));
        var texts = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(texts, 1);
        texts.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 13.5, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(body))
            texts.Children.Add(new TextBlock { Text = body, FontSize = 12, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0xB4, 0xB4, 0xBA)) });
        row.Children.Add(texts);
        card._content.Children.Add(row);
        var close = card.Icon("", "Close", card.Dismiss);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Margin = new Thickness(5);
        close.Opacity = 0.7;
        card._content.Children.Add(close);
        return card;
    }

    // ---------- building blocks ----------

    private static FrameworkElement Glyph(string glyph, Color color) => new Border
    {
        Width = 30,
        Height = 30,
        CornerRadius = new CornerRadius(15),
        Background = new SolidColorBrush(Color.FromArgb(0x33, color.R, color.G, color.B)),
        Child = new TextBlock
        {
            Text = glyph,
            FontFamily = (FontFamily)Application.Current.FindResource("Icons"),
            FontSize = 13,
            Foreground = new SolidColorBrush(color),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
    };

    private static Path CombineIcon() => new()
    {
        Data = Geometry.Parse("M4,4 H10.5 V10.5 H4 Z M13.5,4 H20 V10.5 H13.5 Z M4,13.5 H10.5 V20 H4 Z M13.5,13.5 H20 V20 H13.5 Z"),
        Stroke = Brushes.White,
        StrokeThickness = 2,
        StrokeLineJoin = PenLineJoin.Round,
        Width = 24,
        Height = 24,
        LayoutTransform = new ScaleTransform(0.55, 0.55),
    };

    private Button Pill(string text, Action click)
    {
        var b = new Button { Content = text, Style = (Style)Application.Current.FindResource("CardPill") };
        b.Click += (_, _) => click();
        return b;
    }

    private Button Icon(object content, string tip, Action click)
    {
        var b = new Button { Content = content, ToolTip = tip, Style = (Style)Application.Current.FindResource("CardIcon") };
        b.Click += (_, _) => click();
        return b;
    }

    private void BuildHover(params UIElement[] children)
    {
        var g = new Grid();
        _hover = new Border
        {
            Background = new LinearGradientBrush(Color.FromArgb(0xB4, 0x0C, 0x0C, 0x0E), Color.FromArgb(0x96, 0x0C, 0x0C, 0x0E), 90),
            Opacity = 0,
            Child = g,
        };
        foreach (var c in children) g.Children.Add(c);
        var close = Icon("", "Close", Dismiss);
        close.HorizontalAlignment = HorizontalAlignment.Left;
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Margin = new Thickness(6);
        g.Children.Add(close);
        _content.Children.Add(_hover);
        AddFeedbackLayer();
    }

    private void AddFeedbackLayer()
    {
        _feedbackText = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 13, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(new TextBlock { Text = "", FontFamily = (FontFamily)Application.Current.FindResource("Icons"), FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        sp.Children.Add(_feedbackText);
        _feedback = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x14, 0x14, 0x16)),
            Child = sp,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        _content.Children.Add(_feedback);
    }

    private void Flash(string text, bool thenDismiss = false)
    {
        if (_feedback == null || _feedbackText == null) return;
        _feedbackText.Text = text;
        WindowUtil.Fade(_feedback, 1, 120);
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(thenDismiss ? 650 : 1100) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            if (thenDismiss) Dismiss();
            else WindowUtil.Fade(_feedback, 0, 250);
        };
        t.Start();
    }

    private void CopyImage()
    {
        if (_item == null) return;
        CaptureStore.CopyImage(_item.Image);
        Flash("Copied", thenDismiss: true);
    }

    private void SaveImage()
    {
        if (_item == null) return;
        try
        {
            if (_item.FilePath == null || !_item.FilePath.StartsWith(Snap.Settings.SaveFolder, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(_item.FilePath))
                _item.FilePath = CaptureStore.SavePng(_item.Image);
            Flash("Saved", thenDismiss: true);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            Flash("Could not save");
        }
    }

    private static void Shell(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error(ex); }
    }

    private static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    // ---------- drag out / double click ----------

    private static bool IsInButton(DependencyObject? d, DependencyObject stop)
    {
        while (d != null && d != stop)
        {
            if (d is ButtonBase) return true;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    private void OnPreviewDown(object sender, MouseButtonEventArgs e)
    {
        if (_item == null || IsInButton(e.OriginalSource as DependencyObject, this)) return;
        if (e.ClickCount == 2 && !_item.IsVideo)
        {
            Snap.OpenEditor(_item.Image);
            Dismiss();
            e.Handled = true;
            return;
        }
        _dragStart = e.GetPosition(this);
    }

    private void OnPreviewMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed || _item == null) return;
        if ((e.GetPosition(this) - start).Length < 6) return;
        _dragStart = null;
        try
        {
            var path = _item.EnsureFile();
            var data = new DataObject(DataFormats.FileDrop, new[] { path });
            _timer.Stop();
            Opacity = 0.6;
            var result = DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Move);
            Opacity = 1;
            if (result != DragDropEffects.None) Dismiss();
            else if (_seconds > 0 && !IsMouseOver) _timer.Start();
        }
        catch (Exception ex) { Log.Error(ex); }
    }

    // ---------- animation ----------

    private void AnimateIn()
    {
        _slide.BeginAnimation(TranslateTransform.XProperty,
            WindowUtil.Ease(-(CardWidth + 48), 0, 520, new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut }));
        BeginAnimation(OpacityProperty, WindowUtil.Ease(0, 1, 220));
    }

    public void Dismiss()
    {
        if (IsDismissing) return;
        IsDismissing = true;
        _timer.Stop();
        IsHitTestVisible = false;
        var a = WindowUtil.Ease(null, -(CardWidth + 48), 260, new CubicEase { EasingMode = EasingMode.EaseIn });
        a.Completed += (_, _) =>
        {
            var c = WindowUtil.Ease(null, 0, 180, new CubicEase { EasingMode = EasingMode.EaseInOut });
            c.Completed += (_, _) => _host.Remove(this);
            _collapse.BeginAnimation(ScaleTransform.ScaleYProperty, c);
            BeginAnimation(MarginProperty, new ThicknessAnimation(new Thickness(0), TimeSpan.FromMilliseconds(180)));
        };
        _slide.BeginAnimation(TranslateTransform.XProperty, a);
        BeginAnimation(OpacityProperty, WindowUtil.Ease(null, 0, 240));
    }
}
