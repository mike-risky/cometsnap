using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Cometsnap.App.Tray;

/// <summary>The classic right-click menu of the tray icon.</summary>
public sealed class TrayMenu
{
    private readonly CometApp _app;
    private readonly ContextMenu _menu;

    public TrayMenu(CometApp app)
    {
        _app = app;
        _menu = new ContextMenu { Placement = PlacementMode.MousePoint, StaysOpen = false };
        Rebuild();
    }

    public void Rebuild()
    {
        var capture = _app.Capture;
        _menu.Items.Clear();

        var captureMenu = new MenuItem { Header = "Capture" };
        captureMenu.Items.Add(Item("Region / window", Later(capture.StartRegion), _app.RegionHotkeyText));
        captureMenu.Items.Add(Item("Fullscreen (monitor under the mouse)", Later(capture.CaptureFullscreen), _app.Settings.FullscreenHotkey));
        var delayed = new MenuItem { Header = "Delayed" };
        foreach (var s in new[] { 3, 5, 10 })
            delayed.Items.Add(Item($"{s} seconds", () => capture.StartDelayed(s)));
        captureMenu.Items.Add(delayed);

        _menu.Items.Add(Item("Capture region / window", Later(capture.StartRegion), _app.RegionHotkeyText));
        _menu.Items.Add(captureMenu);
        _menu.Items.Add(new Separator());
        _menu.Items.Add(Item("Open captures folder", _app.OpenCapturesFolder));
        _menu.Items.Add(Item("Settings…", _app.OpenSettingsFile));
        _menu.Items.Add(Item("About Cometsnap", ShowAbout));
        _menu.Items.Add(new Separator());
        _menu.Items.Add(Item("Quit", _app.Quit));
    }

    public void Open()
    {
        _menu.IsOpen = false;
        _menu.IsOpen = true;
    }

    /// <summary>Lets the menu close and repaint before the screen is grabbed.</summary>
    private Action Later(Action action) => () =>
    {
        _menu.IsOpen = false;
        _ = _menu.Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(180);
            action();
        });
    };

    private static MenuItem Item(string header, Action action, string? gesture = null)
    {
        var item = new MenuItem { Header = header, InputGestureText = gesture ?? "" };
        item.Click += (_, _) => action();
        return item;
    }

    private static void ShowAbout() =>
        MessageBox.Show(
            $"Cometsnap {CometApp.Version}\n\nA free, fun and snappy screenshot tool for Windows.\nMIT licensed. github.com/mike-risky/cometsnap",
            "About Cometsnap", MessageBoxButton.OK, MessageBoxImage.Information);
}
