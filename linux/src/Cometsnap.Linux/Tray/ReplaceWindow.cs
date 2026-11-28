using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Cometsnap.Linux.Core;
using Cometsnap.Linux.Platform;
using Cometsnap.Linux.Theme;

namespace Cometsnap.Linux.Tray;

/// <summary>
/// First run: "Replace your old screenshot tool". On GNOME, Ubuntu, Cinnamon and Xfce one click
/// hands Print Screen over to Cometsnap; elsewhere it shows the shortcut commands to paste.
/// </summary>
public sealed class ReplaceWindow : Window
{
    public ReplaceWindow(CometApp app)
    {
        Title = "Cometsnap";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        Background = SpaceTheme.NebulaBrush;
        Foreground = SpaceTheme.StarWhiteBrush;
        FontFamily = SpaceTheme.UiFont;
        Icon = new WindowIcon(SpaceTheme.Logo);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;

        var panel = new StackPanel { Margin = new Thickness(28, 24), Spacing = 12 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        head.Children.Add(new Image { Source = SpaceTheme.Logo, Width = 56, Height = 56 });
        var title = new TextBlock { FontFamily = SpaceTheme.DisplayFont, FontSize = 30, VerticalAlignment = VerticalAlignment.Center };
        title.Inlines!.Add(new Avalonia.Controls.Documents.Run("COMET") { Foreground = SpaceTheme.StarWhiteBrush });
        title.Inlines.Add(new Avalonia.Controls.Documents.Run("SNAP") { Foreground = SpaceTheme.SunBrush });
        head.Children.Add(title);
        panel.Children.Add(head);

        panel.Children.Add(new TextBlock
        {
            Text = T("Replace your old screenshot tool?"), FontFamily = SpaceTheme.HeadingFont, FontSize = 18, Foreground = SpaceTheme.StarWhiteBrush,
        });

        var auto = DesktopIntegration.CanTakeOverPrintScreen;
        panel.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = SpaceTheme.DustBrush,
            Text = auto
                ? T("Print Screen will open Cometsnap instead of the desktop's screenshot tool. Ctrl+Print Screen grabs the whole screen, Shift+Print Screen records a video and Ctrl+Shift+Print Screen a GIF. You can give the keys back anytime in Settings.")
                : DesktopSession.IsWayland
                    ? T("Your desktop decides which app gets Print Screen. Open its keyboard shortcut settings and add these commands (Cometsnap also asked the desktop for its shortcuts, so a confirmation window may have appeared):")
                    : T("Another program already uses Print Screen. Open your desktop's keyboard shortcut settings and point these keys at Cometsnap:"),
        });

        if (!auto)
        {
            foreach (var b in DesktopIntegration.Bindings(app.Settings))
            {
                var box = new TextBox { Text = b.Command, IsReadOnly = true, FontSize = 11, FontFamily = new FontFamily("DejaVu Sans Mono, monospace"), TextWrapping = TextWrapping.Wrap };
                var row = new StackPanel { Spacing = 2 };
                row.Children.Add(new TextBlock { Text = $"{b.Label}:  {b.Name}", FontSize = 13, FontWeight = FontWeight.SemiBold });
                row.Children.Add(box);
                panel.Children.Add(row);
            }
        }

        panel.Children.Add(Briefing(app.Settings));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        if (auto)
        {
            buttons.Children.Add(Ui.ActionButton(T("Not now"), null, T("Keep the desktop's tool"), () => { app.AnswerReplaceOffer(false); Close(); }));
            buttons.Children.Add(Ui.ActionButton(T("Yes, use Cometsnap"), null, T("Hand Print Screen over to Cometsnap"), () => { app.AnswerReplaceOffer(true); Close(); }, primary: true));
        }
        else
        {
            if (DesktopSession.IsKde && DesktopSession.Which("systemsettings") is not null)
                buttons.Children.Add(Ui.ActionButton(T("Open shortcut settings"), null, "", () => Tools.Launch("systemsettings", "kcm_keys")));
            buttons.Children.Add(Ui.ActionButton(T("Done"), null, "", () => { app.AnswerReplaceOffer(false); Close(); }, primary: true));
        }
        panel.Children.Add(buttons);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Opened += (_, _) =>
        {
            // Small screens: never taller than the screen, scroll instead.
            if (Screens.Primary is { } screen) MaxHeight = screen.WorkingArea.Height / screen.Scaling;
        };
    }

    /// <summary>"Mission briefing": how Cometsnap works, in four lines and the handy shortcuts.</summary>
    private static Border Briefing(AppSettings s)
    {
        var panel = new StackPanel { Spacing = 5 };
        panel.Children.Add(new TextBlock
        {
            Text = T("Mission briefing").ToUpper(Loc.Culture), FontFamily = SpaceTheme.HeadingFont, FontSize = 14,
            Foreground = SpaceTheme.SunBrush, Margin = new Thickness(0, 0, 0, 3),
        });
        var steps = new[]
        {
            F("Press {0}, then click a window or drag a box.", Pretty(s.CaptureHotkey)),
            T("Let go: it's copied and saved. Just paste it anywhere."),
            T("Click the Copied card to add arrows, boxes and text."),
            T("Your snaps wait in the tray. Drag them into any app."),
        };
        for (var i = 0; i < steps.Length; i++)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*") };
            row.Children.Add(new TextBlock { Text = (i + 1).ToString(Loc.Culture), FontFamily = SpaceTheme.HeadingFont, Foreground = SpaceTheme.SunBrush });
            var text = new TextBlock { Text = steps[i], Foreground = SpaceTheme.StarWhiteBrush, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            panel.Children.Add(row);
        }
        panel.Children.Add(new TextBlock
        {
            Text = F("{0} video · {1} GIF · {2} copy text · {3} pick a color",
                Pretty(s.RecordHotkey), Pretty(s.RecordGifHotkey), Pretty(s.TextHotkey), Pretty(s.ColorHotkey)),
            FontSize = 12, Foreground = SpaceTheme.DustBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0),
        });
        return new Border
        {
            Background = SpaceTheme.NavyBrush, BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12), Child = panel,
        };
    }

    private static string Pretty(string hotkey) =>
        HotkeyGesture.TryParse(hotkey, out var g) ? g.ToString().Replace("Print Screen", T("Print Screen")) : hotkey;
}
