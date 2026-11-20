// Snapline's own small UI toolkit for code-built windows: settings, tray menu, update window and the installer.
// Shared by the app (.NET 8) and the installer (.NET Framework 4.8, built with the C# 5 compiler that ships
// with Windows), so this file sticks to C# 5: no string interpolation, no ?. , no expression-bodied members.
// Colours match Themes/Theme.xaml, the palette used by the rest of Snapline.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Snapline.Shared
{
    public static class Ui
    {
        static SolidColorBrush B(uint argb)
        {
            var b = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
            b.Freeze();
            return b;
        }

        public static readonly Color AccentColor = Color.FromRgb(0x0A, 0x84, 0xFF);
        public static readonly SolidColorBrush Accent = B(0xFF0A84FF);
        public static readonly SolidColorBrush AccentHover = B(0xFF3395FF);
        public static readonly SolidColorBrush AccentSoft = B(0x330A84FF);
        public static readonly SolidColorBrush AccentText = B(0xFF4DA3FF);
        public static readonly SolidColorBrush WindowBg = B(0xFF1C1C1E);
        public static readonly SolidColorBrush CardBg = B(0xFF242427);
        public static readonly SolidColorBrush Raised = B(0xFF2C2C2E);
        public static readonly SolidColorBrush RaisedHover = B(0xFF3A3A3D);
        public static readonly SolidColorBrush Line = B(0xFF343437);
        public static readonly SolidColorBrush Hairline = B(0x26FFFFFF);
        public static readonly SolidColorBrush Text = B(0xFFF2F2F7);
        public static readonly SolidColorBrush SubText = B(0xFF98989F);
        public static readonly SolidColorBrush Faint = B(0xFF6E6E73);
        public static readonly SolidColorBrush Danger = B(0xFFFF453A);
        public static readonly SolidColorBrush DangerSoft = B(0x29FF453A);
        public static readonly SolidColorBrush DangerHover = B(0x40FF453A);
        public static readonly SolidColorBrush Track = B(0xFF48484A);

        public static readonly FontFamily Font = new FontFamily("Segoe UI Variable Text, Segoe UI");
        public static readonly FontFamily DisplayFont = new FontFamily("Segoe UI Variable Display, Segoe UI");
        public static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        public static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
        public static readonly IEasingFunction Bounce = Frozen(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 });

        static IEasingFunction Frozen(EasingFunctionBase e) { e.Freeze(); return e; }

        public static DoubleAnimation Anim(double to, int ms, IEasingFunction ease)
        {
            return new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
        }

        public static TextBlock Glyph(string glyph, double size, Brush brush)
        {
            return new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = size, Foreground = brush, VerticalAlignment = VerticalAlignment.Center };
        }

        public static TextBlock Label(string text, double size, Brush brush, bool bold)
        {
            return new TextBlock
            {
                Text = text, FontSize = size, Foreground = brush, TextWrapping = TextWrapping.Wrap,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal
            };
        }

        /// <summary>Upper-case caption above a group of rows.</summary>
        public static TextBlock Section(string text)
        {
            return new TextBlock
            {
                Text = text.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = SubText,
                Margin = new Thickness(6, 20, 0, 8)
            };
        }

        /// <summary>A rounded group of rows with thin dividers between them.</summary>
        public static Border Card(params UIElement[] rows)
        {
            var stack = new StackPanel();
            for (int i = 0; i < rows.Length; i++)
            {
                if (i > 0) stack.Children.Add(new Rectangle { Height = 1, Fill = Line, Margin = new Thickness(16, 0, 0, 0) });
                stack.Children.Add(rows[i]);
            }
            return new Border { Background = CardBg, CornerRadius = new CornerRadius(12), BorderBrush = Hairline, BorderThickness = new Thickness(1), Child = stack };
        }

        /// <summary>A settings line: optional icon, title and description on the left, a control on the right.</summary>
        public static Grid Row(string title, string description, UIElement control, UIElement icon)
        {
            var g = new Grid { Margin = new Thickness(16, 12, 14, 12), MinHeight = 24 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (icon != null)
            {
                var holder = new Border { Width = 28, Child = icon, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                g.Children.Add(holder);
            }
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            if (!string.IsNullOrEmpty(title)) words.Children.Add(new TextBlock { Text = title, FontSize = 13.5, Foreground = Text });
            if (!string.IsNullOrEmpty(description))
                words.Children.Add(new TextBlock
                {
                    Text = description, FontSize = 12, Foreground = SubText, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 12, 0)
                });
            Grid.SetColumn(words, 1);
            g.Children.Add(words);
            if (control != null)
            {
                var holder = new Border { Child = control, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
                Grid.SetColumn(holder, 2);
                g.Children.Add(holder);
            }
            return g;
        }

        public static Grid Row(string title, string description, UIElement control)
        {
            return Row(title, description, control, null);
        }

        /// <summary>Keyboard keys for a combination like "Ctrl+Shift+4".</summary>
        public static StackPanel Keys(string combo)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            string[] parts = combo.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                var cap = new Border
                {
                    MinWidth = 22, Padding = new Thickness(7, 2, 7, 3), Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0),
                    CornerRadius = new CornerRadius(6), Background = Raised, BorderBrush = Hairline, BorderThickness = new Thickness(1, 1, 1, 2),
                    Child = new TextBlock { Text = parts[i].Trim(), FontSize = 11, Foreground = Text, HorizontalAlignment = HorizontalAlignment.Center }
                };
                sp.Children.Add(cap);
            }
            return sp;
        }

        /// <summary>Renders the small subset of Markdown used by the licence and release notes.</summary>
        public static StackPanel Markdown(string md)
        {
            var sp = new StackPanel();
            var paragraph = new List<string>();
            Action flush = delegate
            {
                if (paragraph.Count == 0) return;
                sp.Children.Add(new TextBlock
                {
                    Text = string.Join(" ", paragraph.ToArray()), FontSize = 12.5, Foreground = SubText,
                    TextWrapping = TextWrapping.Wrap, LineHeight = 19, Margin = new Thickness(0, 0, 0, 10)
                });
                paragraph.Clear();
            };
            foreach (string raw in md.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line == "---") { flush(); continue; }
                if (line.StartsWith("# "))
                {
                    flush();
                    sp.Children.Add(new TextBlock { Text = line.Substring(2), FontFamily = DisplayFont, FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
                }
                else if (line.StartsWith("## "))
                {
                    flush();
                    sp.Children.Add(new TextBlock { Text = line.Substring(3), FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Text, Margin = new Thickness(0, 8, 0, 6) });
                }
                else if (line.StartsWith("- ") || line.StartsWith("* "))
                {
                    flush();
                    var bullet = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                    var dot = new Ellipse { Width = 5, Height = 5, Fill = Accent, Margin = new Thickness(2, 7, 10, 0), VerticalAlignment = VerticalAlignment.Top };
                    bullet.Children.Add(dot);
                    bullet.Children.Add(new TextBlock { Text = line.Substring(2).Replace("**", ""), FontSize = 12.5, Foreground = SubText, TextWrapping = TextWrapping.Wrap, LineHeight = 19 });
                    sp.Children.Add(bullet);
                }
                else paragraph.Add(line.Replace("**", ""));
            }
            flush();
            return sp;
        }

        static ResourceDictionary scrollbars;

        /// <summary>Thin, rounded scroll bars. Merge into a window's resources.</summary>
        public static ResourceDictionary Scrollbars
        {
            get
            {
                if (scrollbars != null) return scrollbars;
                const string xaml =
                    "<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" +
                    " <Style TargetType='ScrollBar'>" +
                    "  <Setter Property='Width' Value='8'/><Setter Property='MinWidth' Value='8'/>" +
                    "  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>" +
                    "   <Track x:Name='PART_Track' IsDirectionReversed='True'><Track.Thumb><Thumb><Thumb.Template>" +
                    "    <ControlTemplate TargetType='Thumb'><Border Margin='2,2' CornerRadius='2' Background='#55FFFFFF'/></ControlTemplate>" +
                    "   </Thumb.Template></Thumb></Track.Thumb></Track>" +
                    "  </ControlTemplate></Setter.Value></Setter>" +
                    " </Style>" +
                    "</ResourceDictionary>";
                scrollbars = (ResourceDictionary)XamlReader.Parse(xaml);
                return scrollbars;
            }
        }
    }

    /// <summary>
    /// A rounded dark panel with a soft shadow. The shadow sits on its own layer behind the panel,
    /// so the panel's text stays sharp.
    /// </summary>
    public class UiPanel : Grid
    {
        readonly Border panel;
        readonly Grid inner = new Grid();

        public UiPanel(double radius)
        {
            Children.Add(new Border
            {
                CornerRadius = new CornerRadius(radius), Background = Brushes.Black, Margin = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 8, Direction = 270, Opacity = 0.55, Color = Colors.Black }
            });
            panel = new Border
            {
                CornerRadius = new CornerRadius(radius), Background = Ui.WindowBg, BorderBrush = Ui.Hairline,
                BorderThickness = new Thickness(1), Child = inner
            };
            panel.SizeChanged += delegate
            {
                double r = Math.Max(0, radius - 1);
                inner.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, panel.ActualWidth - 2), Math.Max(0, panel.ActualHeight - 2)), r, r);
            };
            Children.Add(panel);
        }

        public UIElement Body
        {
            set { inner.Children.Clear(); if (value != null) inner.Children.Add(value); }
        }
    }

    /// <summary>A borderless rounded window with a small title bar, a close button and Esc to close.</summary>
    public class UiWindow : Window
    {
        readonly ContentControl body = new ContentControl();
        readonly Image titleIcon = new Image { Width = 16, Height = 16, Margin = new Thickness(0, 0, 10, 0) };
        readonly TranslateTransform slide = new TranslateTransform();
        const double Shadow = 24;

        public UiWindow(string title, double width)
        {
            Title = title;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = width + Shadow * 2;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = Ui.Font;
            Foreground = Ui.Text;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            Resources.MergedDictionaries.Add(Ui.Scrollbars);

            var bar = new Grid { Height = 44, Background = Brushes.Transparent };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(titleIcon);
            left.Children.Add(new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.SubText, VerticalAlignment = VerticalAlignment.Center });
            bar.Children.Add(left);

            var x = Ui.Glyph("", 10, Ui.SubText);
            x.HorizontalAlignment = HorizontalAlignment.Center;
            var close = new Border { Width = 32, Height = 28, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 8, 0), Background = Brushes.Transparent, Child = x, ToolTip = "Close" };
            close.MouseEnter += delegate { close.Background = Ui.Danger; x.Foreground = Brushes.White; };
            close.MouseLeave += delegate { close.Background = Brushes.Transparent; x.Foreground = Ui.SubText; };
            close.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
            close.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; Close(); };
            Grid.SetColumn(close, 1);
            bar.Children.Add(close);
            bar.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

            var dock = new DockPanel();
            DockPanel.SetDock(bar, Dock.Top);
            dock.Children.Add(bar);
            dock.Children.Add(body);

            var panel = new UiPanel(14) { Margin = new Thickness(Shadow, Shadow - 8, Shadow, Shadow + 8), RenderTransform = slide };
            panel.Body = dock;
            Content = panel;

            PreviewKeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
            Loaded += delegate
            {
                BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
                slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = Ui.EaseOut });
            };
        }

        public object Body
        {
            get { return body.Content; }
            set { body.Content = value; }
        }

        public ImageSource TitleIcon
        {
            set { titleIcon.Source = value; Icon = value; }
        }

        /// <summary>Shows the window, or brings it to the front if it's already open.</summary>
        public void Present()
        {
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            UiWin.ForceForeground(this);
        }
    }

    public enum UiButtonKind { Primary, Secondary, Quiet, Danger }

    public class UiButton : Border
    {
        public event Action Click;
        readonly Brush rest, hover;

        public UiButton(string text, UiButtonKind kind) : this(text, kind, null) { }

        public UiButton(string text, UiButtonKind kind, string glyph)
        {
            CornerRadius = new CornerRadius(8);
            Padding = new Thickness(14, 6, 14, 7);
            MinHeight = 32;
            Cursor = Cursors.Hand;
            Brush fg;
            switch (kind)
            {
                case UiButtonKind.Primary: rest = Ui.Accent; hover = Ui.AccentHover; fg = Brushes.White; break;
                case UiButtonKind.Quiet: rest = Brushes.Transparent; hover = Ui.Raised; fg = Ui.Text; break;
                case UiButtonKind.Danger: rest = Ui.DangerSoft; hover = Ui.DangerHover; fg = Ui.Danger; break;
                default: rest = Ui.Raised; hover = Ui.RaisedHover; fg = Ui.Text; BorderBrush = Ui.Hairline; BorderThickness = new Thickness(1); break;
            }
            Background = rest;
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (glyph != null)
            {
                var g = Ui.Glyph(glyph, 12, fg);
                g.Margin = new Thickness(0, 1, 7, 0);
                sp.Children.Add(g);
            }
            sp.Children.Add(new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = fg, VerticalAlignment = VerticalAlignment.Center });
            Child = sp;

            MouseEnter += delegate { Background = hover; };
            MouseLeave += delegate { Background = rest; Opacity = 1; };
            MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { Opacity = 0.75; e.Handled = true; };
            MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                Opacity = 1;
                e.Handled = true;
                if (!IsMouseOver) return;
                var h = Click;
                if (h != null) h();
            };
        }
    }

    /// <summary>On/off switch.</summary>
    public class UiToggle : Border
    {
        public event Action<bool> Changed;
        readonly TranslateTransform knobMove = new TranslateTransform();
        bool on;

        public UiToggle(bool initial)
        {
            Width = 40; Height = 24;
            CornerRadius = new CornerRadius(12);
            Cursor = Cursors.Hand;
            VerticalAlignment = VerticalAlignment.Center;
            Child = new Ellipse
            {
                Width = 18, Height = 18, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0), RenderTransform = knobMove,
                Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Direction = 270, Opacity = 0.3 }
            };
            on = initial;
            Update(false);
            MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
            MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; Flip(); };
        }

        public bool IsOn
        {
            get { return on; }
            set { if (on == value) return; on = value; Update(true); }
        }

        public void Flip()
        {
            on = !on;
            Update(true);
            var h = Changed;
            if (h != null) h(on);
        }

        void Update(bool animate)
        {
            Background = on ? Ui.Accent : Ui.Track;
            double x = on ? 16 : 0;
            if (animate) knobMove.BeginAnimation(TranslateTransform.XProperty, Ui.Anim(x, 200, Ui.Bounce));
            else knobMove.X = x;
        }
    }

    /// <summary>Pick one of a few short options, e.g. 24 / 30 / 60.</summary>
    public class UiChoice : Border
    {
        public event Action<int> Changed;
        readonly List<Border> options = new List<Border>();
        int selected = -1;

        public UiChoice(string[] labels, int initial)
        {
            CornerRadius = new CornerRadius(9);
            Background = Ui.Raised;
            Padding = new Thickness(3);
            VerticalAlignment = VerticalAlignment.Center;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var o = new Border
                {
                    CornerRadius = new CornerRadius(6), Padding = new Thickness(11, 3, 11, 4), Cursor = Cursors.Hand,
                    Child = new TextBlock { Text = labels[i], FontSize = 12 }
                };
                o.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
                o.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; Select(index, true); };
                options.Add(o);
                row.Children.Add(o);
            }
            Child = row;
            Select(Math.Max(0, Math.Min(labels.Length - 1, initial)), false);
        }

        public int Selected { get { return selected; } }

        public void Select(int index, bool notify)
        {
            if (index == selected) return;
            selected = index;
            for (int i = 0; i < options.Count; i++)
            {
                bool on = i == index;
                options[i].Background = on ? (Brush)Ui.Accent : Brushes.Transparent;
                var t = (TextBlock)options[i].Child;
                t.Foreground = on ? Brushes.White : Ui.SubText;
                t.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
            if (notify) { var h = Changed; if (h != null) h(index); }
        }
    }

    /// <summary>Pop-up menu for the tray icon. Arrow keys and Enter work; clicking elsewhere closes it.</summary>
    public class UiMenu : Window
    {
        readonly StackPanel list = new StackPanel { Margin = new Thickness(6) };
        readonly List<Border> rows = new List<Border>();
        readonly List<Action> actions = new List<Action>();
        readonly ScaleTransform grow = new ScaleTransform(1, 1);
        readonly UiPanel panel;
        int focused = -1;
        bool closing;

        public UiMenu(double width)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            FontFamily = Ui.Font;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            panel = new UiPanel(12) { Width = width, Margin = new Thickness(18, 12, 18, 26), RenderTransform = grow };
            panel.Body = list;
            Content = panel;
            Deactivated += delegate { CloseMenu(); };
            PreviewKeyDown += OnKey;
        }

        public void AddHeader(ImageSource icon, string title, string subtitle)
        {
            var g = new Grid { Margin = new Thickness(10, 8, 10, 10) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(new Image { Source = icon, Width = 30, Height = 30, Margin = new Thickness(0, 0, 12, 0) });
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Ui.Text });
            if (!string.IsNullOrEmpty(subtitle)) words.Children.Add(new TextBlock { Text = subtitle, FontSize = 11.5, Foreground = Ui.SubText });
            Grid.SetColumn(words, 1);
            g.Children.Add(words);
            list.Children.Add(g);
            AddSeparator();
        }

        public void AddSeparator()
        {
            list.Children.Add(new Rectangle { Height = 1, Fill = Ui.Line, Margin = new Thickness(8, 4, 8, 4) });
        }

        public void AddItem(string glyph, string text, string keys, Action action)
        {
            AddItem(glyph, text, keys, action, false);
        }

        public void AddItem(string glyph, string text, string keys, Action action, bool danger)
        {
            Brush fg = danger ? Ui.Danger : Ui.Text;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(Ui.Glyph(glyph, 14, danger ? Ui.Danger : Ui.SubText));
            var label = new TextBlock { Text = text, FontSize = 13, Foreground = fg, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(label, 1);
            g.Children.Add(label);
            if (!string.IsNullOrEmpty(keys))
            {
                var k = Ui.Keys(keys);
                k.Margin = new Thickness(12, 0, 0, 0);
                Grid.SetColumn(k, 2);
                g.Children.Add(k);
            }
            var row = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(10, 7, 8, 7), Background = Brushes.Transparent, Child = g };
            int index = rows.Count;
            row.MouseEnter += delegate { SetFocus(index); };
            row.MouseLeave += delegate { if (focused == index) SetFocus(-1); };
            row.MouseLeftButtonUp += delegate { Run(index); };
            rows.Add(row);
            actions.Add(action);
            list.Children.Add(row);
        }

        void SetFocus(int index)
        {
            focused = index;
            for (int i = 0; i < rows.Count; i++) rows[i].Background = i == index ? (Brush)Ui.Raised : Brushes.Transparent;
        }

        void Run(int index)
        {
            var a = actions[index];
            CloseMenu();
            Dispatcher.BeginInvoke(new Action(delegate { if (a != null) a(); }));
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { CloseMenu(); e.Handled = true; }
            else if (rows.Count == 0) return;
            else if (e.Key == Key.Down) { SetFocus((focused + 1) % rows.Count); e.Handled = true; }
            else if (e.Key == Key.Up) { SetFocus(focused <= 0 ? rows.Count - 1 : focused - 1); e.Handled = true; }
            else if (e.Key == Key.Enter && focused >= 0) { Run(focused); e.Handled = true; }
        }

        public void CloseMenu()
        {
            if (closing) return;
            closing = true;
            Dispatcher.BeginInvoke(new Action(Close));
        }

        /// <summary>Opens above and to the left of the mouse, like a tray menu, and keeps it on screen.</summary>
        public void ShowAtCursor()
        {
            Opacity = 0;
            Left = -32000; Top = -32000;
            Show();
            UpdateLayout();
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            int w = (int)Math.Ceiling(ActualWidth * dpi), h = (int)Math.Ceiling(ActualHeight * dpi);
            var cursor = System.Windows.Forms.Cursor.Position;
            var area = System.Windows.Forms.Screen.FromPoint(cursor).WorkingArea;
            int padR = (int)(panel.Margin.Right * dpi), padB = (int)(panel.Margin.Bottom * dpi), padL = (int)(panel.Margin.Left * dpi), padT = (int)(panel.Margin.Top * dpi);
            int x = cursor.X - w + padR, y = cursor.Y - h + padB;
            bool fromRight = true, fromBottom = true;
            if (x + padL < area.Left) { x = cursor.X - padL; fromRight = false; }
            if (y + padT < area.Top) { y = cursor.Y - padT; fromBottom = false; }
            x = Math.Max(area.Left - padL, Math.Min(x, area.Right - w + padR));
            y = Math.Max(area.Top - padT, Math.Min(y, area.Bottom - h + padB));
            UiWin.MoveTo(this, x, y);

            panel.RenderTransformOrigin = new Point(fromRight ? 1 : 0, fromBottom ? 1 : 0);
            UiWin.ForceForeground(this);
            Activate();
            Focus();
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
            var a = new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = Ui.EaseOut };
            grow.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            grow.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
    }

    /// <summary>Small Windows helpers.</summary>
    public static class UiWin
    {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);

        /// <summary>
        /// Brings a window to the front even when another app has focus. Windows only allows that for the
        /// process that received the last input, so this sends a harmless unassigned key (0xE8) first.
        /// (AttachThreadInput is avoided: it can leave modifier keys stuck.)
        /// </summary>
        public static void ForceForeground(Window w)
        {
            IntPtr h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero || GetForegroundWindow() == h) return;
            keybd_event(0xE8, 0, 0, UIntPtr.Zero);
            keybd_event(0xE8, 0, 2, UIntPtr.Zero);
            SetForegroundWindow(h);
        }

        /// <summary>Moves a window to a position in physical pixels (no DPI guessing).</summary>
        public static void MoveTo(Window w, int x, int y)
        {
            IntPtr h = new WindowInteropHelper(w).Handle;
            SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, 0x1 | 0x4 | 0x10); // NOSIZE | NOZORDER | NOACTIVATE
        }

        public static ImageSource FromBitmap(System.Drawing.Bitmap bmp)
        {
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.StreamSource = ms;
                img.EndInit();
                img.Freeze();
                return img;
            }
        }

        public static void Open(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch { }
        }
    }
}
