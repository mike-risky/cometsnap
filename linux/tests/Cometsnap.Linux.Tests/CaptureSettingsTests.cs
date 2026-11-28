using Avalonia.Input;
using Cometsnap.Core.Storage;
using Cometsnap.Linux.Core;
using Cometsnap.Linux.Services;
using Cometsnap.Linux.Tray;

namespace Cometsnap.Linux.Tests;

public class CaptureSettingsTests
{
    private static readonly DateTime When = new(2026, 11, 26, 8, 56, 12);

    [Fact]
    public void Name_pattern_shapes_the_saved_path()
    {
        var path = SnapNaming.NextFreePath("/pics", When, "{year}/{month}/Shot {time}", ".jpg", 1, _ => false);
        Assert.Equal(Path.Combine("/pics", "2026", "11", "Shot 08-56-12.jpg"), path);
        Assert.Equal(Path.Combine("/pics", "2026-11", "snap_2026-11-26_08-56-12.png"), SnapNaming.NextFreePath("/pics", When, FileNamePattern.Default, ".png", 1, _ => false));
    }

    [Fact]
    public void Jpeg_paths_get_jpeg_bytes()
    {
        using var bitmap = new SkiaSharp.SKBitmap(4, 3);
        bitmap.Erase(SkiaSharp.SKColors.OrangeRed);
        using var data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        var png = data.ToArray();
        Assert.Same(png, CaptureStore.ForPath(png, "/x/a.png"));
        var jpg = CaptureStore.ForPath(png, "/x/a.jpg");
        Assert.Equal(new byte[] { 0xFF, 0xD8 }, jpg[..2]);
        Assert.True(MediaKinds.IsListed("/x/a.jpg"));
    }

    [Theory]
    [InlineData(Key.S, KeyModifiers.Control | KeyModifiers.Shift, "Ctrl+Shift+S")]
    [InlineData(Key.PrintScreen, KeyModifiers.None, "PrintScreen")]
    [InlineData(Key.D4, KeyModifiers.Meta | KeyModifiers.Shift, "Shift+Super+4")]
    [InlineData(Key.F13, KeyModifiers.Alt, "Alt+F13")]
    [InlineData(Key.LeftCtrl, KeyModifiers.Control, null)]
    public void Key_presses_become_shortcuts(Key key, KeyModifiers mods, string? expected)
    {
        var text = SettingsWindow.HotkeyText(key, mods);
        Assert.Equal(expected, text);
        if (text is not null) Assert.True(HotkeyGesture.TryParse(text, out _));
    }
}
