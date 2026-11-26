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
        Build();
    }

    private void Build()
    {
        _menu.Items.Clear();
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
