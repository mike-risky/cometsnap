using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Cometsnap.Linux.Core;
using Cometsnap.Linux.Platform;
using Cometsnap.Linux.Theme;

namespace Cometsnap.Linux.Tray;

/// <summary>A small settings page. Changes apply and save at once.</summary>
public sealed class SettingsWindow : Window
{
    private readonly CometApp _app;

    public SettingsWindow(CometApp app)
    {
        _app = app;
        var s = app.Settings;
        Title = "Cometsnap settings";
        Width = 520;
        Height = 680;
        Background = SpaceTheme.NebulaBrush;
        Foreground = SpaceTheme.StarWhiteBrush;
        FontFamily = SpaceTheme.UiFont;
        Icon = new WindowIcon(SpaceTheme.Logo);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;

        var panel = new StackPanel { Margin = new Thickness(24, 18), Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = "SETTINGS", FontFamily = SpaceTheme.DisplayFont, FontSize = 30, Foreground = SpaceTheme.SunBrush, Margin = new Thickness(0, 0, 0, 8) });

        panel.Children.Add(Section("Capture"));
        panel.Children.Add(Toggle("Detect windows under the mouse", s.WindowDetection, v => s.WindowDetection = v,
            WindowFinder.IsAvailable ? null : "Wayland does not let apps see other windows: click captures the screen, drag picks an area."));
        panel.Children.Add(Toggle("Show the Edit card after a capture", s.ShowEditCard, v => s.ShowEditCard = v));
        panel.Children.Add(Toggle("Copy captures to the clipboard", s.CopyToClipboardOnCapture, v => s.CopyToClipboardOnCapture = v));
        panel.Children.Add(Toggle("Soft click sound", s.PlaySounds, v => s.PlaySounds = v));
        panel.Children.Add(Toggle("Comet animation", s.ShowCometAnimation, v => s.ShowCometAnimation = v,
            "The capture rides a comet towards the tray. Needs a compositor (most desktops have one)."));
        panel.Children.Add(Toggle("Little celebrations (snap milestones)", s.Celebrations, v => s.Celebrations = v));
        panel.Children.Add(Toggle("Seasonal looks (snowy December, Halloween pumpkin)", s.SeasonalSkins, v =>
        {
            s.SeasonalSkins = v;
            _app.ApplySeason();
        }));
        var corner = new ComboBox { ItemsSource = new[] { "Bottom left", "Bottom right", "Top left", "Top right", "Follow the cursor" }, SelectedIndex = (int)s.LoupeCorner, Width = 200 };
        corner.SelectionChanged += (_, _) => { s.LoupeCorner = (LoupeCorner)Math.Max(0, corner.SelectedIndex); _app.SaveSettings(); };
        panel.Children.Add(Row("Magnifier corner", corner));

        panel.Children.Add(Section("Recording"));
        panel.Children.Add(Toggle("Record the computer's sound", s.RecordSystemAudio, v => s.RecordSystemAudio = v));
        panel.Children.Add(Toggle("Record the microphone", s.RecordMicrophone, v => s.RecordMicrophone = v));
        panel.Children.Add(Toggle("Show the mouse pointer", s.RecordShowCursor, v => s.RecordShowCursor = v));
        panel.Children.Add(Toggle("Copy recordings to the clipboard", s.RecordCopyToClipboard, v => s.RecordCopyToClipboard = v));

        panel.Children.Add(Section("Desktop"));
        panel.Children.Add(Toggle("Start Cometsnap when I log in", s.StartAtLogin, v =>
        {
            s.StartAtLogin = v;
            DesktopIntegration.SetStartAtLogin(v);
        }));
        var keyText = s.ReplacedDesktopTool ? "Give Print Screen back to the desktop" : "Use Print Screen for Cometsnap…";
        panel.Children.Add(Ui.ActionButton(keyText, null, "Your desktop's own screenshot shortcut", () =>
        {
            Close();
            if (_app.Settings.ReplacedDesktopTool) _app.GiveBackPrintScreen();
            else _app.ShowReplaceOffer(force: true);
        }));
        panel.Children.Add(new TextBlock
        {
            Text = $"Captures folder: {_app.CapturesFolder}", Foreground = SpaceTheme.DustBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0),
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(Ui.ActionButton("Open captures folder", null, "", _app.OpenCapturesFolder));
        buttons.Children.Add(Ui.ActionButton("Edit settings file", null, "All settings, including shortcuts, as JSON", () => Tools.Open(_app.Paths.SettingsFile)));
        panel.Children.Add(buttons);
        panel.Children.Add(new TextBlock
        {
            Text = $"Cometsnap {typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3)} · {DesktopSession.Describe()} · MIT license",
            Foreground = SpaceTheme.DustBrush, FontSize = 11, Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap,
        });

        Content = new ScrollViewer { Content = panel };
    }

    private static Control Section(string text) => new TextBlock
    {
        Text = text.ToUpperInvariant(), FontFamily = SpaceTheme.HeadingFont, FontSize = 14, Foreground = SpaceTheme.DustBrush, Margin = new Thickness(0, 12, 0, 2),
    };

    private Control Toggle(string label, bool value, Action<bool> set, string? note = null)
    {
        var box = new CheckBox { IsChecked = value, Content = new TextBlock { Text = label, FontSize = 14, TextWrapping = TextWrapping.Wrap } };
        box.IsCheckedChanged += (_, _) => { set(box.IsChecked == true); _app.SaveSettings(); };
        if (note is null) return box;
        var stack = new StackPanel();
        stack.Children.Add(box);
        stack.Children.Add(new TextBlock { Text = note, FontSize = 11, Foreground = SpaceTheme.DustBrush, Margin = new Thickness(28, 0, 0, 4), TextWrapping = TextWrapping.Wrap });
        return stack;
    }

    private static Control Row(string label, Control control)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 4) };
        DockPanel.SetDock(control, Dock.Right);
        dock.Children.Add(control);
        dock.Children.Add(new TextBlock { Text = label, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        return dock;
    }
}
