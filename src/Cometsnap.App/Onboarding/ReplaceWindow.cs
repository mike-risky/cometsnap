using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cometsnap.App.Theme;
using Cometsnap.Core.Native;
using Cometsnap.Core.Takeover;
using Cometsnap.Core.Localization;
using Cometsnap.Core.Settings;

namespace Cometsnap.App.Onboarding;

/// <summary>What the user picked in the "Replace your old screen capture" window.</summary>
public sealed record ReplaceChoice(bool DisableSnippingTool, IReadOnlyList<DetectedCaptureTool> ToolsToStop, bool StartWithWindows);

/// <summary>
/// First-run offer: make Print Screen open Cometsnap, and switch off whatever owned it before
/// (Windows' Snipping Tool binding, Greenshot, ShareX...).
/// </summary>
public sealed class ReplaceWindow : Window
{
    private readonly TakeoverReport _report;
    private readonly List<(CheckBox Box, DetectedCaptureTool Tool)> _toolBoxes = new();
    private readonly CheckBox? _snippingBox;
    private readonly CheckBox _startupBox;

    public ReplaceChoice? Choice { get; private set; }

    public ReplaceWindow(TakeoverReport report, AppSettings settings, string? installedBanner = null)
    {
        _report = report;
        Title = "Cometsnap";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        Background = SpaceTheme.VoidBrush;
        Foreground = SpaceTheme.StarWhiteBrush;
        FontFamily = SpaceTheme.UiFont;
        FontSize = 14;
        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/cometsnap.ico")); }
        catch { /* fine without */ }
        SourceInitialized += (_, _) =>
        {
            var dark = 1;
            Win32.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        };

        var stack = new StackPanel { Margin = new Thickness(28, 24, 28, 24) };

        var logo = new Image { Width = 72, Height = 72, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
        try { logo.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/cometsnap-256.png")); }
        catch { logo.Visibility = Visibility.Collapsed; }
        stack.Children.Add(logo);

        if (installedBanner is not null)
        {
            // Shown right after Setup: say plainly what just happened.
            stack.Children.Add(new Border
            {
                Background = SpaceTheme.WithAlpha(SpaceTheme.CometBlue, 0x40), BorderBrush = SpaceTheme.CometBlueBrush,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 5, 14, 5),
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12),
                Child = new TextBlock { Text = "✓  " + installedBanner, FontWeight = FontWeights.SemiBold, Foreground = SpaceTheme.StarWhiteBrush },
            });
        }

        stack.Children.Add(new TextBlock
        {
            Text = L.T("REPLACE YOUR OLD SCREEN CAPTURE"), FontFamily = SpaceTheme.DisplayFont, FontSize = 30,
            Foreground = SpaceTheme.SunBrush, TextWrapping = TextWrapping.Wrap, LineHeight = 34,
        });
        stack.Children.Add(new TextBlock
        {
            Text = L.T("Make Print Screen open Cometsnap: freeze, pick, annotate, done."),
            Foreground = SpaceTheme.DustBrush, Margin = new Thickness(0, 8, 0, 18), TextWrapping = TextWrapping.Wrap,
        });

        if (report.SnippingToolOwnsPrintScreen)
        {
            _snippingBox = Check(L.T("Take Print Screen back from Windows' Snipping Tool"),
                L.T("You can turn it back on in Settings > Accessibility > Keyboard."));
            stack.Children.Add(_snippingBox);
        }
        foreach (var tool in report.Tools)
        {
            var what = tool.IsRunning && tool.StartupEntries.Count > 0 ? L.T("Close it and stop it starting with Windows")
                : tool.IsRunning ? L.T("Close it now")
                : L.T("Stop it starting with Windows");
            var box = Check(L.F("Replace {0}", tool.Name), what + ". " + L.T("You can turn it back on in Task Manager > Startup apps."));
            _toolBoxes.Add((box, tool));
            stack.Children.Add(box);
        }
        if (report.NothingToReplace)
        {
            stack.Children.Add(new TextBlock
            {
                Text = L.T("No other screenshot tool found. Cometsnap will make sure Print Screen stays its own."),
                Foreground = SpaceTheme.StarWhiteBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
            });
        }

        _startupBox = Check(L.T("Start Cometsnap with Windows"), L.T("So Print Screen always works, right after you log in."));
        _startupBox.IsChecked = settings.StartWithWindows;
        stack.Children.Add(_startupBox);

        stack.Children.Add(Briefing(settings));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        buttons.Children.Add(Button(L.T("Not now"), SpaceTheme.NavyBrush, () => { Choice = null; Close(); }));
        buttons.Children.Add(Button(L.T("Replace it"), SpaceTheme.TerracottaBrush, Accept, primary: true));
        stack.Children.Add(buttons);

        // Small screens: never taller than the work area, scroll instead.
        MaxHeight = SystemParameters.WorkArea.Height;
        Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Accept();
            else if (e.Key == Key.Escape) Close();
        };
    }

    private void Accept()
    {
        Choice = new ReplaceChoice(
            _snippingBox?.IsChecked == true,
            _toolBoxes.Where(t => t.Box.IsChecked == true).Select(t => t.Tool).ToList(),
            _startupBox.IsChecked == true);
        Close();
    }

    /// <summary>"Mission briefing": how Cometsnap works, in four lines and the handy shortcuts.</summary>
    private static Border Briefing(AppSettings s)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = L.T("Mission briefing").ToUpper(L.Culture), FontFamily = SpaceTheme.HeadingFont, FontSize = 14,
            Foreground = SpaceTheme.SunBrush, Margin = new Thickness(0, 0, 0, 8),
        });
        var steps = new[]
        {
            L.F("Press {0}, then click a window or drag a box.", Pretty(s.CaptureHotkey)),
            L.T("Let go: it's copied and saved. Just paste it anywhere."),
            L.T("Click the Copied card to add arrows, boxes and text."),
            L.T("Your snaps wait in the tray. Drag them into any app."),
        };
        for (var i = 0; i < steps.Length; i++)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = (i + 1).ToString(L.Culture), FontFamily = SpaceTheme.HeadingFont, Foreground = SpaceTheme.SunBrush });
            var text = new TextBlock { Text = steps[i], Foreground = SpaceTheme.StarWhiteBrush, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            panel.Children.Add(row);
        }
        panel.Children.Add(new TextBlock
        {
            Text = L.F("{0} video · {1} GIF · {2} copy text · {3} pick a color",
                Pretty(s.RecordHotkey), Pretty(s.RecordGifHotkey), Pretty(s.TextHotkey), Pretty(s.ColorHotkey)),
            FontSize = 12, Foreground = SpaceTheme.DustBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
        });
        return new Border
        {
            Background = SpaceTheme.NavyBrush, BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 6, 0, 0), Child = panel,
        };
    }

    /// <summary>"Ctrl+Shift+PrintScreen" → "Ctrl+Shift+Print Screen".</summary>
    private static string Pretty(string gesture) =>
        string.Join("+", gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Equals("PrintScreen", StringComparison.OrdinalIgnoreCase) ? L.T("Print Screen") : p));

    private static CheckBox Check(string title, string detail)
    {
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Foreground = SpaceTheme.StarWhiteBrush, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = detail, FontSize = 12, Foreground = SpaceTheme.DustBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        return new CheckBox
        {
            Content = text, IsChecked = true, Margin = new Thickness(0, 0, 0, 12),
            Foreground = SpaceTheme.StarWhiteBrush, VerticalContentAlignment = VerticalAlignment.Top, Cursor = Cursors.Hand,
        };
    }

    private static Border Button(string text, Brush background, Action click, bool primary = false)
    {
        var b = new Border
        {
            Background = background, CornerRadius = new CornerRadius(8), Padding = new Thickness(20, 9, 20, 9),
            Margin = new Thickness(10, 0, 0, 0), Cursor = Cursors.Hand,
            BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(primary ? 0 : 1),
            Child = new TextBlock
            {
                Text = text, Foreground = SpaceTheme.StarWhiteBrush, FontWeight = FontWeights.SemiBold,
                FontFamily = primary ? SpaceTheme.HeadingFont : SpaceTheme.UiFont, FontSize = primary ? 15 : 14,
            },
        };
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return b;
    }
}
