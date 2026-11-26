using Cometsnap.Core.Input;

namespace Cometsnap.Core.Tests;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("PrintScreen", HotkeyModifiers.None, 0x2Cu)]
    [InlineData("Ctrl+Shift+S", HotkeyModifiers.Control | HotkeyModifiers.Shift, (uint)'S')]
    [InlineData("alt + prtsc", HotkeyModifiers.Alt, 0x2Cu)]
    [InlineData("Win+F12", HotkeyModifiers.Win, 0x7Bu)]
    [InlineData("Ctrl+1", HotkeyModifiers.Control, (uint)'1')]
    public void Parses(string text, HotkeyModifiers mods, uint vk)
    {
        var g = HotkeyGesture.Parse(text);
        Assert.Equal(mods, g.Modifiers);
        Assert.Equal(vk, g.VirtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Hyper+S")]
    [InlineData("Ctrl+F25")]
    public void Rejects_garbage(string text) => Assert.False(HotkeyGesture.TryParse(text, out _));

    [Fact]
    public void Round_trips_to_text()
    {
        Assert.Equal("Ctrl+Shift+S", HotkeyGesture.Parse("shift+ctrl+s").ToString());
        Assert.Equal("PrintScreen", HotkeyGesture.Parse("PrtScn").ToString());
    }
}
