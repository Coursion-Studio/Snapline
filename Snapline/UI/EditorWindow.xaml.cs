using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Snapline.Core;
using Snapline.Editor;

namespace Snapline.UI;

public partial class EditorWindow : Window
{
    private readonly AnnotationSurface _surface;
    private readonly Dictionary<Tool, RadioButton> _toolButtons = new();
    private readonly bool _ready;
    private int _bgIndex = 1;
    private bool _bgTouched;
    private TextBox? _activeText;
    private Point _activeTextPos;
    private Color _activeTextColor;
    private double _activeTextSize;
    private Point? _dragStart;
    private Storyboard? _toastSb;

    public EditorWindow(BitmapSource image)
    {
        InitializeComponent();
        _surface = new AnnotationSurface(image);
        SurfaceHost.Children.Insert(0, _surface);
        _surface.TextRequested += BeginText;
        _surface.StateChanged += UpdateChrome;
        _surface.BaseChanged += OnBaseChanged;

        foreach (var rb in ToolPanel.Children.OfType<RadioButton>().Where(r => r.GroupName == "tool"))
            _toolButtons[Enum.Parse<Tool>((string)rb.Tag)] = rb;

        BuildSwatches();
        _ready = true;
        _toolButtons[Tool.Arrow].IsChecked = true;
        OnBaseChanged();

        WindowUtil.WireChrome(this, RootGrid);
        Loaded += (_, _) => ApplyDpi();
        DpiChanged += (_, _) => ApplyDpi();
        SizeChanged += (_, _) => _surface.InvalidateVisual();
        PreviewKeyDown += OnKey;
        DragHandle.PreviewMouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(this);
        DragHandle.PreviewMouseMove += DragHandle_MouseMove;
        DragHandle.PreviewMouseLeftButtonUp += (_, _) => _dragStart = null;
        Closing += (_, _) => CommitText();
    }

    private void ApplyDpi()
    {
        double d = WindowUtil.Dpi(this);
        DpiWrap.LayoutTransform = new ScaleTransform(1 / d, 1 / d);
    }

    private void OnBaseChanged()
    {
        TextLayer.Width = _surface.Width;
        TextLayer.Height = _surface.Height;
        double max = Math.Max(_surface.Width, _surface.Height);
        double min = Math.Min(_surface.Width, _surface.Height);
        bool wasReady = PadSlider.Tag != null;
        PadSlider.Maximum = Math.Round(max * 0.3);
        RadiusSlider.Maximum = Math.Round(Math.Min(min / 4, 80 * _surface.Unit));
        if (!wasReady)
        {
            PadSlider.Value = Math.Round(max * 0.07);
            RadiusSlider.Value = Math.Round(14 * _surface.Unit);
            PadSlider.Tag = true;
        }
        ApplyBackground();
        UpdateChrome();
    }

    private void UpdateChrome()
    {
        UndoBtn.IsEnabled = _surface.CanUndo;
        RedoBtn.IsEnabled = _surface.CanRedo;
        SizeText.Text = $"{_surface.Base.PixelWidth} × {_surface.Base.PixelHeight} px";
    }

    // ---------- background ----------

    private void BuildSwatches()
    {
        for (int i = 1; i < Backgrounds.All.Count; i++)
        {
            var opt = Backgrounds.All[i];
            int index = i;
            var rb = new RadioButton
            {
                GroupName = "bg",
                Style = (Style)FindResource("BgSwatch"),
                Background = opt.Brush,
                ToolTip = opt.Name,
                IsChecked = i == _bgIndex,
            };
            rb.Checked += (_, _) =>
            {
                _bgIndex = index;
                if (_ready && BgEnabled.IsChecked != true) BgEnabled.IsChecked = true;
                ApplyBackground();
            };
            BgSwatches.Children.Add(rb);
        }
    }

    private void ApplyBackground()
    {
        if (!_ready) return;
        double w = _surface.Width, h = _surface.Height, max = Math.Max(w, h);
        if (BgEnabled.IsChecked != true)
        {
            CompositionRoot.Background = null;
            CompositionRoot.Padding = new Thickness(0);
            ShadowHost.Effect = null;
            SurfaceHost.Clip = null;
            SizeText.Text = $"{_surface.Base.PixelWidth} × {_surface.Base.PixelHeight} px";
            return;
        }
        double pad = Math.Round(PadSlider.Value), r = RadiusSlider.Value;
        CompositionRoot.Background = Backgrounds.All[_bgIndex].Brush;
        CompositionRoot.Padding = new Thickness(pad);
        SurfaceHost.Clip = r > 0 ? new RectangleGeometry(new Rect(0, 0, w, h), r, r) : null;
        ShadowHost.Effect = ShadowCheck.IsChecked == true
            ? new DropShadowEffect { BlurRadius = Math.Max(16, max * 0.035), ShadowDepth = Math.Max(4, max * 0.01), Direction = 270, Opacity = 0.42, RenderingBias = RenderingBias.Quality }
            : null;
        SizeText.Text = $"{(int)(w + 2 * pad)} × {(int)(h + 2 * pad)} px";
    }

    private void Bg_Changed(object sender, RoutedEventArgs e)
    {
        _bgTouched = true;
        ApplyBackground();
    }

    private void Bg_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => ApplyBackground();

    private void BgToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        bool open = BgToggle.IsChecked == true;
        if (open && !_bgTouched && BgEnabled.IsChecked != true)
        {
            BgEnabled.IsChecked = true;
            ApplyBackground();
        }
        var a = new DoubleAnimation(open ? 280 : 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        BgPanel.BeginAnimation(WidthProperty, a);
    }

    // ---------- tools ----------

    private void Tool_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        CommitText();
        _surface.Tool = Enum.Parse<Tool>((string)((FrameworkElement)sender).Tag);
        _surface.Cursor = _surface.Tool switch
        {
            Tool.Select => Cursors.Arrow,
            Tool.Text => Cursors.IBeam,
            _ => Cursors.Cross,
        };
        if (_surface.Tool != Tool.Select) _surface.ClearSelection();
    }

    private void Color_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        var c = ((SolidColorBrush)((Control)sender).Background).Color;
        _surface.SetColor(c);
        if (_activeText != null)
        {
            _activeTextColor = c;
            _activeText.Foreground = new SolidColorBrush(c);
        }
    }

    private void Size_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _surface.SetSize(int.Parse((string)((FrameworkElement)sender).Tag));
        if (_activeText != null)
        {
            _activeTextSize = _surface.FontSizeForLevel;
            _activeText.FontSize = _activeTextSize;
        }
    }

    private void SelectTool(Tool t) => _toolButtons[t].IsChecked = true;

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.Z when shift: _surface.Redo(); break;
                case Key.Z: _surface.Undo(); break;
                case Key.Y: _surface.Redo(); break;
                case Key.C: Copy_Click(this, e); break;
                case Key.S when shift: SaveAs_Click(this, e); break;
                case Key.S: Save_Click(this, e); break;
                default: return;
            }
            e.Handled = true;
            return;
        }
        switch (e.Key)
        {
            case Key.V: SelectTool(Tool.Select); break;
            case Key.A: SelectTool(Tool.Arrow); break;
            case Key.R: SelectTool(Tool.Rect); break;
            case Key.E: SelectTool(Tool.Ellipse); break;
            case Key.L: SelectTool(Tool.Line); break;
            case Key.P: SelectTool(Tool.Pen); break;
            case Key.H: SelectTool(Tool.Highlighter); break;
            case Key.T: SelectTool(Tool.Text); break;
            case Key.N: SelectTool(Tool.Counter); break;
            case Key.B: SelectTool(Tool.Pixelate); break;
            case Key.C: SelectTool(Tool.Crop); break;
            case Key.Delete:
            case Key.Back: _surface.DeleteSelected(); break;
            case Key.Escape: _surface.ClearSelection(); break;
            default: return;
        }
        e.Handled = true;
    }

    // ---------- text ----------

    private void BeginText(Point p, TextAnnotation? existing)
    {
        CommitText();
        _activeTextSize = existing?.FontSize ?? _surface.FontSizeForLevel;
        _activeTextColor = existing?.Color ?? _surface.Color;
        _activeTextPos = p;
        double border = Math.Max(1, _surface.Unit);
        var tb = new TextBox
        {
            Text = existing?.Text ?? "",
            FontSize = _activeTextSize,
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(_activeTextColor),
            CaretBrush = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xB0, 0x0A, 0x84, 0xFF)),
            BorderThickness = new Thickness(border),
            Padding = new Thickness(0),
            MinWidth = _activeTextSize * 1.5,
            AcceptsReturn = true,
        };
        Canvas.SetLeft(tb, p.X - border - 2);
        Canvas.SetTop(tb, p.Y - border);
        TextLayer.Children.Add(tb);
        _activeText = tb;
        tb.LostKeyboardFocus += (_, _) => CommitText();
        tb.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape || (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
            {
                CommitText();
                e.Handled = true;
            }
        };
        Dispatcher.BeginInvoke(() =>
        {
            tb.Focus();
            Keyboard.Focus(tb);
            tb.CaretIndex = tb.Text.Length;
        }, DispatcherPriority.Input);
    }

    private void CommitText()
    {
        if (_activeText == null) return;
        var tb = _activeText;
        _activeText = null;
        TextLayer.Children.Remove(tb);
        var text = tb.Text.TrimEnd();
        if (text.Length == 0) { _surface.CancelText(); return; }
        _surface.CommitText(new TextAnnotation
        {
            Pos = _activeTextPos,
            Text = text,
            FontSize = _activeTextSize,
            Color = _activeTextColor,
            Stroke = _surface.StrokeWidth,
        });
    }

    // ---------- export ----------

    public BitmapSource Render()
    {
        CommitText();
        _surface.Exporting = true;
        _surface.InvalidateVisual();
        CompositionRoot.UpdateLayout();
        int w = (int)Math.Round(CompositionRoot.ActualWidth), h = (int)Math.Round(CompositionRoot.ActualHeight);
        var rtb = new RenderTargetBitmap(Math.Max(1, w), Math.Max(1, h), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(CompositionRoot);
        rtb.Freeze();
        _surface.Exporting = false;
        _surface.InvalidateVisual();
        return rtb;
    }

    private void ShowToast(string text)
    {
        ToastText.Text = text;
        _toastSb?.Stop();
        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1600))));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1900))));
        Storyboard.SetTarget(fade, Toast);
        Storyboard.SetTargetProperty(fade, new PropertyPath(OpacityProperty));
        var slide = new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(slide, Toast);
        Storyboard.SetTargetProperty(slide, new PropertyPath("RenderTransform.Y"));
        _toastSb = new Storyboard { Children = { fade, slide } };
        _toastSb.Begin();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        CaptureStore.CopyImage(Render());
        ShowToast("Copied to clipboard");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = CaptureStore.SavePng(Render());
            ShowToast($"Saved to {System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path))}");
        }
        catch (Exception ex) { Log.Error(ex); ShowToast("Could not save: " + ex.Message); }
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            FileName = CaptureStore.NewFileName(".png"),
            Filter = "PNG image|*.png|JPEG image|*.jpg",
            InitialDirectory = Snap.Settings.SaveFolder,
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            CaptureStore.SavePng(Render(), dlg.FileName);
            ShowToast("Saved");
        }
        catch (Exception ex) { Log.Error(ex); ShowToast("Could not save: " + ex.Message); }
    }

    private void Pin_Click(object sender, RoutedEventArgs e) => Snap.Pin(Render(), null);

    private void DragHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } s || e.LeftButton != MouseButtonState.Pressed) return;
        if ((e.GetPosition(this) - s).Length < 5) return;
        _dragStart = null;
        try
        {
            var path = CaptureStore.SaveTemp(Render());
            DragDrop.DoDragDrop(DragHandle, new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy);
        }
        catch (Exception ex) { Log.Error(ex); }
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => _surface.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => _surface.Redo();
    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Max_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
