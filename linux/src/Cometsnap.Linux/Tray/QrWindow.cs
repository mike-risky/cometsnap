using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Cometsnap.Core.Text;
using Cometsnap.Linux.Platform;
using Cometsnap.Linux.Theme;

namespace Cometsnap.Linux.Tray;

/// <summary>
/// What the scanned QR codes (or barcodes) say, with Copy and, for web addresses, Open link.
/// The first code is already on the clipboard when this opens. Links never open by themselves.
/// </summary>
public sealed class QrWindow : Window
{
    private const string CopyIcon = "M5.5,5.5 H13.5 V13.5 H5.5 Z M2.5,10.5 V2.5 H10.5";
    private const string LinkIcon = "M9,2.5 H13.5 V7 M13.5,2.5 L7.5,8.5 M11.5,9.5 V13.5 H2.5 V4.5 H6.5";
    private readonly CometApp _app;
    private readonly TextBlock _hint;

    public QrWindow(CometApp app, IReadOnlyList<ScannedCode> codes)
    {
        _app = app;
        Title = codes.Count == 1 ? F("{0} found", T(codes[0].Format)) : F("{0} codes found", codes.Count);
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 640;
        CanResize = false;
        Topmost = true;
        Background = SpaceTheme.VoidBrush;
        Foreground = SpaceTheme.StarWhiteBrush;
        FontFamily = SpaceTheme.UiFont;
        Icon = new WindowIcon(SpaceTheme.Logo);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;

        var list = new StackPanel { Spacing = 10 };
        foreach (var code in codes.Take(8)) list.Children.Add(CodeCard(code));

        _hint = new TextBlock
        {
            Text = codes[0].Wifi is { Password: not null } ? T("The Wi-Fi password is on the clipboard.") : T("Copied to the clipboard."),
            Foreground = SpaceTheme.DustBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap,
        };
        var root = new StackPanel { Margin = new Thickness(14), Spacing = 8 };
        root.Children.Add(new ScrollViewer { Content = list, MaxHeight = 520 });
        root.Children.Add(_hint);
        Content = root;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        };
    }

    private Border CodeCard(ScannedCode code)
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new TextBlock
        {
            Text = KindLabel(code).ToUpperInvariant(), FontFamily = SpaceTheme.HeadingFont, FontSize = 12, Foreground = SpaceTheme.SunBrush,
        });
        if (code.Wifi is { } wifi)
        {
            stack.Children.Add(Field(T("Network"), wifi.Name));
            if (wifi.Password is { } password) stack.Children.Add(Field(T("Password"), password));
        }
        else
        {
            stack.Children.Add(Value(code.Text));
        }

        var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        void Add(Border b) { b.Margin = new Thickness(0, 0, 8, 4); buttons.Children.Add(b); }
        if (code.Link is { } link)
            Add(Ui.ActionButton(T("Open link"), LinkIcon, F("Opens {0} in your browser", QrCodes.LinkHost(link) ?? link), () =>
            {
                if (QrCodes.OpenableLink(link) is { } safe) Tools.Open(safe);
                Close();
            }, primary: true));
        if (code.Wifi is { Password: { } pw })
            Add(Ui.ActionButton(T("Copy password"), CopyIcon, T("Copy the Wi-Fi password"), () => _ = CopyAsync(pw, T("Password copied.")), primary: true));
        Add(Ui.ActionButton(code.Wifi is null ? T("Copy") : T("Copy all"), CopyIcon, T("Copy what the code says"),
            () => _ = CopyAsync(code.Text, T("Copied to the clipboard.")), primary: code.Link is null && code.Wifi is null));
        stack.Children.Add(buttons);

        return new Border
        {
            Background = SpaceTheme.NavyBrush, BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12), Child = stack,
        };
    }

    private static string KindLabel(ScannedCode code) => code.Kind switch
    {
        CodeKind.Link => F("{0} · link", T(code.Format)),
        CodeKind.WiFi => F("{0} · Wi-Fi", T(code.Format)),
        CodeKind.Email => F("{0} · email", T(code.Format)),
        CodeKind.Phone => F("{0} · phone", T(code.Format)),
        _ => T(code.Format),
    };

    private static Control Field(string label, string value)
    {
        var s = new StackPanel { Spacing = 2 };
        s.Children.Add(new TextBlock { Text = label, Foreground = SpaceTheme.DustBrush, FontSize = 12 });
        s.Children.Add(Value(value));
        return s;
    }

    /// <summary>Selectable: pick a part with the mouse and Ctrl+C it.</summary>
    private static SelectableTextBlock Value(string text) => new()
    {
        Text = text.Length > 600 ? text[..600] + "…" : text, TextWrapping = TextWrapping.Wrap, FontSize = 14,
        Foreground = SpaceTheme.StarWhiteBrush, SelectionBrush = SpaceTheme.CometBlueBrush,
    };

    private async Task CopyAsync(string text, string done)
    {
        await _app.Clipboard.SetTextAsync(text);
        _hint.Text = done;
    }
}
