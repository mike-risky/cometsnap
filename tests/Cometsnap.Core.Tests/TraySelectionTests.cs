using Cometsnap.Core.Storage;

namespace Cometsnap.Core.Tests;

public class TraySelectionTests
{
    private static readonly string[] Items = { "a.png", "b.png", "c.png", "d.png", "e.png" };

    private static TraySelection Make()
    {
        var s = new TraySelection();
        s.SetItems(Items);
        return s;
    }

    [Fact]
    public void Plain_click_picks_only_that_one()
    {
        var s = Make();
        s.Click("b.png");
        s.Click("d.png");
        Assert.Equal(new[] { "d.png" }, s.Selected);
    }

    [Fact]
    public void Ctrl_click_toggles_and_keeps_display_order()
    {
        var s = Make();
        s.Click("d.png");
        s.Click("a.png", toggle: true);
        s.Click("c.png", toggle: true);
        Assert.Equal(new[] { "a.png", "c.png", "d.png" }, s.Selected);
        s.Click("c.png", toggle: true);
        Assert.Equal(new[] { "a.png", "d.png" }, s.Selected);
    }

    [Fact]
    public void Shift_click_picks_a_range_from_the_anchor_both_ways()
    {
        var s = Make();
        s.Click("b.png");
        s.Click("d.png", range: true);
        Assert.Equal(new[] { "b.png", "c.png", "d.png" }, s.Selected);
        // The anchor stays on b: going the other way replaces the range.
        s.Click("a.png", range: true);
        Assert.Equal(new[] { "a.png", "b.png" }, s.Selected);
    }

    [Fact]
    public void Ctrl_shift_click_adds_the_range()
    {
        var s = Make();
        s.Click("a.png");
        s.Click("d.png", toggle: true);
        s.Click("e.png", toggle: true, range: true);
        Assert.Equal(new[] { "a.png", "d.png", "e.png" }, s.Selected);
    }

    [Fact]
    public void Shift_click_without_anchor_picks_just_that_one()
    {
        var s = Make();
        s.Click("c.png", range: true);
        Assert.Equal(new[] { "c.png" }, s.Selected);
    }

    [Fact]
    public void Select_all_and_clear()
    {
        var s = Make();
        var changes = 0;
        s.Changed += () => changes++;
        s.SelectAll();
        Assert.Equal(Items, s.Selected);
        s.SelectAll(); // nothing new: no event
        s.Clear();
        Assert.Equal(0, s.Count);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Box_replaces_or_adds_with_ctrl()
    {
        var s = Make();
        s.Click("a.png");
        s.BeginBox(add: false);
        s.UpdateBox(new[] { "c.png", "d.png" });
        Assert.Equal(new[] { "c.png", "d.png" }, s.Selected);
        s.UpdateBox(new[] { "c.png" }); // the box shrank
        Assert.Equal(new[] { "c.png" }, s.Selected);
        s.EndBox();

        s.BeginBox(add: true);
        s.UpdateBox(new[] { "e.png" });
        Assert.Equal(new[] { "c.png", "e.png" }, s.Selected);
        s.EndBox();
    }

    [Fact]
    public void Actions_work_on_the_whole_selection_only_from_a_selected_capture()
    {
        var s = Make();
        s.Click("a.png");
        s.Click("c.png", toggle: true);
        Assert.Equal(new[] { "a.png", "c.png" }, s.For("c.png"));
        Assert.Equal(new[] { "b.png" }, s.For("b.png"));
        s.Click("a.png");
        Assert.Equal(new[] { "a.png" }, s.For("a.png"));
    }

    [Fact]
    public void A_search_drops_hidden_captures_from_the_selection()
    {
        var s = Make();
        s.SelectAll();
        s.SetItems(new[] { "b.png", "c.png" });
        Assert.Equal(new[] { "b.png", "c.png" }, s.Selected);
        s.Click("z.png"); // not shown: ignored
        Assert.Equal(2, s.Count);
    }

    [Fact]
    public void Only_keeps_a_single_selection_quiet()
    {
        var s = Make();
        s.Click("b.png");
        var changes = 0;
        s.Changed += () => changes++;
        s.Only("b.png");
        Assert.Equal(0, changes);
        s.Click("d.png", toggle: true);
        s.Only("d.png");
        Assert.Equal(new[] { "d.png" }, s.Selected);
    }
}
