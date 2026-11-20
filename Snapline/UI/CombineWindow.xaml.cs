using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Snapline.Core;

namespace Snapline.UI;

public sealed class CombineEntry
{
    public CombineEntry(BitmapSource image) { Image = image; }
    public BitmapSource Image { get; }
}

public partial class CombineWindow : Window
{
    private enum Layout { Row, Column, Grid }

    private readonly ObservableCollection<CombineEntry> _items = new();
    private readonly bool _ready;
    private int _bgIndex = 1;

    public CombineWindow()
    {
        InitializeComponent();
        LayoutList.ItemsSource = _items;
        RecentList.ItemsSource = CaptureStore.History;
        _items.CollectionChanged += (_, _) => Rebuild();
        BuildSwatches();
        WindowUtil.WireChrome(this, RootGrid);
        Drop += OnDrop;
        _ready = true;
        Rebuild();
    }

    public void AddImage(BitmapSource img) => _items.Add(new CombineEntry(img));

    // ---------- options ----------

    private Layout CurrentLayout => LayoutColumn.IsChecked == true ? Layout.Column : LayoutGrid.IsChecked == true ? Layout.Grid : Layout.Row;
    private double AlignFactor => AlignStart.IsChecked == true ? 0 : AlignEnd.IsChecked == true ? 1 : 0.5;

    private void BuildSwatches()
    {
        for (int i = 0; i < Backgrounds.All.Count; i++)
        {
            var opt = Backgrounds.All[i];
            int index = i;
            var rb = new RadioButton
            {
                GroupName = "bg",
                Style = (Style)FindResource("BgSwatch"),
                Background = opt.Brush ?? Backgrounds.Checker(),
                ToolTip = opt.Name,
                IsChecked = i == _bgIndex,
            };
            rb.Checked += (_, _) => { _bgIndex = index; Rebuild(); };
            BgSwatches.Children.Add(rb);
        }
    }

    private void Option_Changed(object sender, RoutedEventArgs e) => Rebuild();
    private void Slider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => Rebuild();

    // ---------- layout ----------

    private FrameworkElement? BuildVisual(out int width, out int height)
    {
        width = height = 0;
        if (_items.Count == 0) return null;
        var imgs = _items.Select(i => i.Image).ToList();
        var layout = CurrentLayout;
        double gap = Math.Round(SpacingSlider.Value), pad = Math.Round(PaddingSlider.Value), radius = RadiusSlider.Value;
        double align = AlignFactor;
        bool match = MatchSize.IsChecked == true;

        var sizes = imgs.Select(i => new Size(i.PixelWidth, i.PixelHeight)).ToList();
        if (match)
        {
            if (layout == Layout.Row)
            {
                double th = sizes.Max(s => s.Height);
                sizes = sizes.Select(s => new Size(Math.Round(s.Width * th / s.Height), th)).ToList();
            }
            else
            {
                double tw = sizes.Max(s => s.Width);
                sizes = sizes.Select(s => new Size(tw, Math.Round(s.Height * tw / s.Width))).ToList();
            }
        }

        var rects = new List<Rect>();
        double W, H;
        switch (layout)
        {
            case Layout.Row:
            {
                double ch = sizes.Max(s => s.Height), x = pad;
                foreach (var s in sizes)
                {
                    rects.Add(new Rect(x, pad + Math.Round((ch - s.Height) * align), s.Width, s.Height));
                    x += s.Width + gap;
                }
                W = x - gap + pad; H = ch + 2 * pad;
                break;
            }
            case Layout.Column:
            {
                double cw = sizes.Max(s => s.Width), y = pad;
                foreach (var s in sizes)
                {
                    rects.Add(new Rect(pad + Math.Round((cw - s.Width) * align), y, s.Width, s.Height));
                    y += s.Height + gap;
                }
                W = cw + 2 * pad; H = y - gap + pad;
                break;
            }
            default:
            {
                int cols = Math.Min((int)ColumnsSlider.Value, sizes.Count);
                int rows = (int)Math.Ceiling(sizes.Count / (double)cols);
                double cellW = sizes.Max(s => s.Width);
                var rowH = new double[rows];
                for (int i = 0; i < sizes.Count; i++) rowH[i / cols] = Math.Max(rowH[i / cols], sizes[i].Height);
                double y = pad;
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        int i = r * cols + c;
                        if (i >= sizes.Count) break;
                        var s = sizes[i];
                        double x = pad + c * (cellW + gap) + Math.Round((cellW - s.Width) * align);
                        rects.Add(new Rect(x, y + Math.Round((rowH[r] - s.Height) * align), s.Width, s.Height));
                    }
                    y += rowH[r] + gap;
                }
                W = 2 * pad + cols * cellW + (cols - 1) * gap;
                H = y - gap + pad;
                break;
            }
        }

        width = (int)Math.Ceiling(W);
        height = (int)Math.Ceiling(H);
        var canvas = new Canvas { Width = width, Height = height, Background = Backgrounds.All[_bgIndex].Brush, ClipToBounds = true };
        double max = Math.Max(width, height);
        for (int i = 0; i < imgs.Count; i++)
        {
            var brush = new ImageBrush(imgs[i]) { Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
            var b = new Border
            {
                Width = rects[i].Width,
                Height = rects[i].Height,
                CornerRadius = new CornerRadius(radius),
                Background = brush,
                Effect = ShadowCheck.IsChecked == true
                    ? new DropShadowEffect { BlurRadius = Math.Max(14, max * 0.025), ShadowDepth = Math.Max(3, max * 0.007), Direction = 270, Opacity = 0.4, RenderingBias = RenderingBias.Quality }
                    : null,
            };
            RenderOptions.SetBitmapScalingMode(b, BitmapScalingMode.HighQuality);
            Canvas.SetLeft(b, rects[i].X);
            Canvas.SetTop(b, rects[i].Y);
            canvas.Children.Add(b);
        }
        return canvas;
    }

    private void Rebuild()
    {
        if (!_ready) return;
        ColumnsRow.Visibility = CurrentLayout == Layout.Grid ? Visibility.Visible : Visibility.Collapsed;
        ColumnsLabel.Text = ((int)ColumnsSlider.Value).ToString();
        SpacingLabel.Text = $"{SpacingSlider.Value:0} px";
        PaddingLabel.Text = $"{PaddingSlider.Value:0} px";
        RadiusLabel.Text = $"{RadiusSlider.Value:0} px";
        LayoutCaption.Text = $"IN LAYOUT  ·  {_items.Count}";

        var v = BuildVisual(out int w, out int h);
        bool had = PreviewHost.Child != null;
        PreviewHost.Child = v;
        bool any = v != null;
        EmptyState.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        CopyBtn.IsEnabled = SaveBtn.IsEnabled = EditBtn.IsEnabled = any;
        SizeText.Text = any ? $"{w} × {h} px" : "";
        if (any && !had)
        {
            Zoom.BeginAnimation(OpacityProperty, WindowUtil.Ease(0, 1, 220));
        }
    }

    private BitmapSource? Export()
    {
        var v = BuildVisual(out int w, out int h);
        if (v == null) return null;
        v.Measure(new Size(w, h));
        v.Arrange(new Rect(0, 0, w, h));
        v.UpdateLayout();
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(v);
        rtb.Freeze();
        return rtb;
    }

    // ---------- item management ----------

    private void Recent_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is CaptureItem item) AddImage(item.Image);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is CombineEntry entry) _items.Remove(entry);
    }

    private void MoveLeft_Click(object sender, RoutedEventArgs e) => Move(sender, -1);
    private void MoveRight_Click(object sender, RoutedEventArgs e) => Move(sender, 1);

    private void Move(object sender, int delta)
    {
        if ((sender as FrameworkElement)?.Tag is not CombineEntry entry) return;
        int i = _items.IndexOf(entry), j = i + delta;
        if (i < 0 || j < 0 || j >= _items.Count) return;
        _items.Move(i, j);
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Multiselect = true, Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff" };
        if (dlg.ShowDialog(this) == true)
            foreach (var f in dlg.FileNames) AddFile(f);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            foreach (var f in files) AddFile(f);
    }

    private void AddFile(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            BitmapSource src = bmp;
            if (Math.Abs(bmp.DpiX - 96) > 0.5)
            {
                var px = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
                var buf = new byte[px.PixelWidth * px.PixelHeight * 4];
                px.CopyPixels(buf, px.PixelWidth * 4, 0);
                src = BitmapSource.Create(px.PixelWidth, px.PixelHeight, 96, 96, PixelFormats.Bgra32, null, buf, px.PixelWidth * 4);
                src.Freeze();
            }
            AddImage(src);
        }
        catch (Exception ex) { Log.Error(ex); }
    }

    private void Strip_Wheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer sv)
        {
            sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta / 2.0);
            e.Handled = true;
        }
    }

    // ---------- actions ----------

    private void Toast(string text)
    {
        ToastText.Text = text;
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1500))));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1800))));
        ToastText.BeginAnimation(OpacityProperty, a);
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (Export() is { } img) { CaptureStore.CopyImage(img); Toast("Copied to clipboard"); }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Export() is not { } img) return;
        try { CaptureStore.SavePng(img); Toast("Saved"); }
        catch (Exception ex) { Log.Error(ex); Toast("Could not save"); }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Export() is { } img) Snap.OpenEditor(img);
    }

    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Max_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
