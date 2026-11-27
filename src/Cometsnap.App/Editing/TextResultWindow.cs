using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cometsnap.App.Services;
using Cometsnap.App.Theme;
using Cometsnap.Core.Localization;
using Cometsnap.Core.Native;

namespace Cometsnap.App.Editing;

/// <summary>
/// The text that was read from a capture, in an editable box: fix a letter, pick a part and
/// copy it. The whole text is already on the clipboard when this opens.
/// </summary>
public sealed class TextResultWindow : Window
{
    private readonly TextBox _box;
    private readonly TextBlock _hint;

    public TextResultWindow(string text)
    {
        Title = L.T("Text from the capture");
        Width = 560;
        Height = 420;
        MinWidth = 320;
        MinHeight = 200;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = SpaceTheme.VoidBrush;
        Foreground = SpaceTheme.StarWhiteBrush;
        FontFamily = SpaceTheme.UiFont;
        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/cometsnap.ico")); }
        catch { /* fine without */ }
        SourceInitialized += (_, _) =>
        {
            var dark = 1;
            Win32.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        };

        _box = new TextBox
        {
            Text = text, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 14,
            Background = SpaceTheme.NavyBrush, Foreground = SpaceTheme.StarWhiteBrush, CaretBrush = SpaceTheme.SunBrush,
            SelectionBrush = SpaceTheme.CometBlueBrush, BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8),
        };

        _hint = new TextBlock
        {
            Text = L.T("The whole text is on the clipboard. Select a part to copy just that."),
            Foreground = SpaceTheme.DustBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
        };
        var copy = EditorUi.PillButton(EditorUi.Icon("M5.5,5.5 H13.5 V13.5 H5.5 Z M2.5,10.5 V2.5 H10.5", null, 16), L.T("Copy"),
            L.T("Copy the selection, or everything when nothing is selected (Ctrl+C)"), Copy, SpaceTheme.TerracottaBrush, bold: true);
        DockPanel.SetDock(copy, Dock.Right);
        var bar = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = true };
        bar.Children.Add(copy);
        bar.Children.Add(_hint);

        var dock = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(bar, Dock.Bottom);
        dock.Children.Add(bar);
        dock.Children.Add(_box);
        Content = dock;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        };
        Loaded += (_, _) => { _box.Focus(); _box.CaretIndex = 0; };
    }

    private void Copy()
    {
        var text = _box.SelectionLength > 0 ? _box.SelectedText : _box.Text;
        if (text.Length == 0) return;
        if (ClipboardService.SetText(text))
            _hint.Text = _box.SelectionLength > 0 ? L.T("Selection copied.") : L.T("Everything copied.");
    }
}
