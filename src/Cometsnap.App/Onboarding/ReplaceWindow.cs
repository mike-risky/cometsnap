using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cometsnap.App.Theme;
using Cometsnap.Core.Native;
using Cometsnap.Core.Takeover;

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

    public ReplaceWindow(TakeoverReport report, bool startsWithWindows, string? installedBanner = null)
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
            Text = "REPLACE YOUR OLD SCREEN CAPTURE", FontFamily = SpaceTheme.DisplayFont, FontSize = 30,
            Foreground = SpaceTheme.SunBrush, TextWrapping = TextWrapping.Wrap, LineHeight = 34,
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Make Print Screen open Cometsnap: freeze, pick, annotate, done.",
            Foreground = SpaceTheme.DustBrush, Margin = new Thickness(0, 8, 0, 18), TextWrapping = TextWrapping.Wrap,
        });

        if (report.SnippingToolOwnsPrintScreen)
        {
            _snippingBox = Check("Take Print Screen back from Windows' Snipping Tool",
                "You can turn it back on in Settings > Accessibility > Keyboard.");
            stack.Children.Add(_snippingBox);
        }
        foreach (var tool in report.Tools)
        {
            var what = tool.IsRunning && tool.StartupEntries.Count > 0 ? "Close it and stop it starting with Windows"
                : tool.IsRunning ? "Close it now"
                : "Stop it starting with Windows";
            var box = Check($"Replace {tool.Name}", what + ". You can turn it back on in Task Manager > Startup apps.");
            _toolBoxes.Add((box, tool));
            stack.Children.Add(box);
        }
        if (report.NothingToReplace)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "No other screenshot tool found. Cometsnap will make sure Print Screen stays its own.",
                Foreground = SpaceTheme.StarWhiteBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
            });
        }

        _startupBox = Check("Start Cometsnap with Windows", "So Print Screen always works, right after you log in.");
        _startupBox.IsChecked = startsWithWindows;
        stack.Children.Add(_startupBox);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        buttons.Children.Add(Button("Not now", SpaceTheme.NavyBrush, () => { Choice = null; Close(); }));
        buttons.Children.Add(Button("Replace it", SpaceTheme.TerracottaBrush, Accept, primary: true));
        stack.Children.Add(buttons);

        Content = stack;
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
