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
using Cometsnap.Core.Text;

namespace Cometsnap.App.Editing;

/// <summary>
/// What a scanned QR code (or barcode) says, with Copy and, for web addresses, Open link.
/// The first code is already on the clipboard when this opens. Links never open by themselves.
/// </summary>
public sealed class QrResultWindow : Window
{
    private const string CopyIcon = "M5.5,5.5 H13.5 V13.5 H5.5 Z M2.5,10.5 V2.5 H10.5";
    private const string LinkIcon = "M9,2.5 H13.5 V7 M13.5,2.5 L7.5,8.5 M11.5,9.5 V13.5 H2.5 V4.5 H6.5";
    private readonly TextBlock _hint;

    public QrResultWindow(IReadOnlyList<ScannedCode> codes)
    {
        Title = codes.Count == 1 ? L.F("{0} found", L.T(codes[0].Format)) : L.F("{0} codes found", codes.Count);
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MinWidth = 340;
        MaxHeight = 640;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = SpaceTheme.VoidBrush;
        Foreground = SpaceTheme.StarWhiteBrush;
        FontFamily = SpaceTheme.UiFont;
        Topmost = true;
        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/cometsnap.ico")); }
        catch { /* fine without */ }
        SourceInitialized += (_, _) =>
        {
            var dark = 1;
            Win32.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        };

        var list = new StackPanel();
        foreach (var code in codes.Take(8)) list.Children.Add(CodeCard(code));

        _hint = new TextBlock
        {
            Text = codes[0].Wifi is { Password: not null } ? L.T("The Wi-Fi password is on the clipboard.") : L.T("Copied to the clipboard."),
            Foreground = SpaceTheme.DustBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 8, 0, 0),
        };

        var root = new StackPanel { Margin = new Thickness(14) };
        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 520 });
        root.Children.Add(_hint);
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        };
    }

    private Border CodeCard(ScannedCode code)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = KindLabel(code).ToUpper(L.Culture), FontFamily = SpaceTheme.HeadingFont, FontSize = 12,
            Foreground = SpaceTheme.SunBrush, Margin = new Thickness(0, 0, 0, 6),
        });

        if (code.Wifi is { } wifi)
        {
            stack.Children.Add(Field(L.T("Network"), wifi.Name));
            if (wifi.Password is { } password) stack.Children.Add(Field(L.T("Password"), password));
        }
        else
        {
            stack.Children.Add(Value(code.Text, maxHeight: 180));
        }

        var buttons = new WrapPanel { Margin = new Thickness(-4, 10, 0, 0) };
        if (code.Link is { } link)
        {
            buttons.Children.Add(EditorUi.PillButton(EditorUi.Icon(LinkIcon, null, 16), L.T("Open link"),
                L.F("Opens {0} in your browser", QrCodes.LinkHost(link) ?? link), () => { QrService.OpenLink(link); Close(); },
                SpaceTheme.TerracottaBrush, bold: true));
        }
        if (code.Wifi is { Password: { } pw })
        {
            buttons.Children.Add(EditorUi.PillButton(EditorUi.Icon(CopyIcon, null, 16), L.T("Copy password"), L.T("Copy the Wi-Fi password"),
                () => CopyNow(pw, L.T("Password copied.")), SpaceTheme.TerracottaBrush, bold: true));
        }
        buttons.Children.Add(EditorUi.PillButton(EditorUi.Icon(CopyIcon, null, 16), code.Wifi is null ? L.T("Copy") : L.T("Copy all"),
            L.T("Copy what the code says"), () => CopyNow(code.Text, L.T("Copied to the clipboard.")),
            code.Link is null && code.Wifi is null ? SpaceTheme.TerracottaBrush : null, bold: code.Link is null && code.Wifi is null));
        stack.Children.Add(buttons);

        return new Border
        {
            Background = SpaceTheme.NavyBrush, BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 10), Child = stack,
        };
    }

    private static string KindLabel(ScannedCode code) => code.Kind switch
    {
        CodeKind.Link => L.F("{0} · link", L.T(code.Format)),
        CodeKind.WiFi => L.F("{0} · Wi-Fi", L.T(code.Format)),
        CodeKind.Email => L.F("{0} · email", L.T(code.Format)),
        CodeKind.Phone => L.F("{0} · phone", L.T(code.Format)),
        _ => L.T(code.Format),
    };

    private static UIElement Field(string label, string value)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        stack.Children.Add(new TextBlock { Text = label, Foreground = SpaceTheme.DustBrush, FontSize = 12, Margin = new Thickness(0, 0, 0, 2) });
        stack.Children.Add(Value(value, maxHeight: 60));
        return stack;
    }

    /// <summary>Selectable, read-only: pick a part with the mouse and Ctrl+C it.</summary>
    private static TextBox Value(string text, double maxHeight) => new()
    {
        Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontSize = 14, MaxHeight = maxHeight,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brushes.Transparent,
        Foreground = SpaceTheme.StarWhiteBrush, CaretBrush = SpaceTheme.SunBrush, SelectionBrush = SpaceTheme.CometBlueBrush,
        BorderThickness = new Thickness(0), Padding = new Thickness(0),
    };

    private void CopyNow(string text, string done)
    {
        if (ClipboardService.SetText(text)) _hint.Text = done;
    }
}
