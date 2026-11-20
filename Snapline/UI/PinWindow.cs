using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Snapline.Core;

namespace Snapline.UI;

/// <summary>Always-on-top floating screenshot. Drag to move, wheel to zoom, Ctrl+wheel for opacity, double-click / Esc to close.</summary>
public sealed class PinWindow : Window
{
    private const double Pad = 18;
    private readonly BitmapSource _img;
    private readonly Border _card;
    private readonly ScaleTransform _pop = new(0.92, 0.92);
    private readonly Int32Rect? _origin;
    private double _zoom = 1;

    public PinWindow(BitmapSource img, Int32Rect? origin)
    {
        _img = img;
        _origin = origin;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "Pinned screenshot";

        var brush = new ImageBrush(img) { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
        _card = new Border
        {
            Margin = new Thickness(Pad),
            CornerRadius = new CornerRadius(8),
            Background = brush,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 5, Direction = 270, Opacity = 0.45 },
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _pop,
            Cursor = Cursors.SizeAll,
            ToolTip = "Drag to move · Scroll to zoom · Ctrl+Scroll for opacity · Double-click to close",
        };
        Content = _card;

        var menu = new ContextMenu();
        menu.Items.Add(Item("Copy", () => CaptureStore.CopyImage(_img)));
        menu.Items.Add(Item("Save", () => CaptureStore.SavePng(_img)));
        menu.Items.Add(Item("Annotate", () => { Snap.OpenEditor(_img); Close(); }));
        menu.Items.Add(Item("Reset size", () => SetZoom(1)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Close", Close));
        _card.ContextMenu = menu;

        SourceInitialized += (_, _) => WindowUtil.SetToolWindow(this, noActivate: false);
        Loaded += (_, _) =>
        {
            SetZoom(1);
            UpdateLayout();
            double dpi = WindowUtil.Dpi(this);
            if (_origin is { } o)
                WindowUtil.MovePhysical(this, o.X - (int)Math.Round(Pad * dpi), o.Y - (int)Math.Round(Pad * dpi));
            else
            {
                var m = Monitors.AtCursor();
                int w = (int)(ActualWidth * m.Scale), h = (int)(ActualHeight * m.Scale);
                WindowUtil.MovePhysical(this, m.WorkArea.X + (m.WorkArea.Width - w) / 2, m.WorkArea.Y + (m.WorkArea.Height - h) / 2);
            }
            var ease = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut };
            _pop.BeginAnimation(ScaleTransform.ScaleXProperty, WindowUtil.Ease(0.92, 1, 280, ease));
            _pop.BeginAnimation(ScaleTransform.ScaleYProperty, WindowUtil.Ease(0.92, 1, 280, ease));
            BeginAnimation(OpacityProperty, WindowUtil.Ease(0, 1, 160));
        };
        DpiChanged += (_, _) => SetZoom(_zoom);

        _card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) Close();
            else DragMove();
        };
        MouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                Opacity = Math.Clamp(Opacity + (e.Delta > 0 ? 0.1 : -0.1), 0.2, 1);
            else
                SetZoom(Math.Clamp(_zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), 0.1, 6));
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    private static MenuItem Item(string header, Action a)
    {
        var mi = new MenuItem { Header = header };
        mi.Click += (_, _) => a();
        return mi;
    }

    private void SetZoom(double z)
    {
        _zoom = z;
        double dpi = WindowUtil.Dpi(this);
        _card.Width = _img.PixelWidth / dpi * z;
        _card.Height = _img.PixelHeight / dpi * z;
    }
}
