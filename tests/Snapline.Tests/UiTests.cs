using System.Windows.Controls;
using Snapline.Shared;

namespace Snapline.Tests;

/// <summary>WPF controls need a single-threaded apartment, so each test runs on its own STA thread.</summary>
public class UiTests
{
    private static void Sta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) throw new Exception("UI test failed", error);
    }

    [Fact]
    public void Markdown_renders_headings_bullets_and_paragraphs() => Sta(() =>
    {
        var sp = Ui.Markdown("# Title\n\nA paragraph\nthat wraps.\n\n## Section\n- **one**\n- two\n");
        Assert.Equal(5, sp.Children.Count);
        Assert.Equal("Title", ((TextBlock)sp.Children[0]).Text);
        Assert.Equal("A paragraph that wraps.", ((TextBlock)sp.Children[1]).Text);
        var bullet = (DockPanel)sp.Children[3];
        Assert.Equal("one", ((TextBlock)bullet.Children[1]).Text);
    });

    [Fact]
    public void Choice_reports_only_real_changes() => Sta(() =>
    {
        var c = new UiChoice(new[] { "24", "30", "60" }, 1);
        var seen = new List<int>();
        c.Changed += seen.Add;
        c.Select(1, true);
        c.Select(2, true);
        c.Select(0, false);
        Assert.Equal(new[] { 2 }, seen);
        Assert.Equal(0, c.Selected);
    });

    [Fact]
    public void Toggle_flips_and_notifies() => Sta(() =>
    {
        var t = new UiToggle(false);
        bool? last = null;
        t.Changed += v => last = v;
        t.Flip();
        Assert.True(t.IsOn);
        Assert.True(last);
    });

    [Fact]
    public void Keys_splits_a_shortcut() => Sta(() =>
    {
        Assert.Equal(3, Ui.Keys("Ctrl+Shift+4").Children.Count);
    });
}
