using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cometsnap.App.Theme;
using Cometsnap.Core.Styling;
using Cometsnap.Core.Input;
using Cometsnap.Core.Localization;
using Cometsnap.Core.Native;
using Cometsnap.Core.Settings;
using Cometsnap.Core.Storage;

namespace Cometsnap.App.Preferences;

/// <summary>
/// The in-app Settings window ("Mission control"). Every change applies and saves right away,
/// so there is no OK button to forget.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly CometApp _app;
    private ScrollViewer _scroll = null!;

    /// <summary>The hotkey box waiting for keys, if any.</summary>
    private HotkeyBox? _listening;

    private AppSettings S => _app.Settings;

    public SettingsWindow(CometApp app)
    {
        _app = app;
        Width = 600;
        Height = 760;
        MinWidth = 480;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
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
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        Deactivated += (_, _) => StopListening(apply: false);
        Closed += (_, _) => { StopListening(apply: false); _app.TrySaveSettings(); };
        Build();
    }

    /// <summary>Builds (or rebuilds, after a language change) the whole window.</summary>
    private void Build()
    {
        var offset = _scroll?.VerticalOffset ?? 0;
        Title = L.T("Cometsnap settings");

        var stack = new StackPanel { Margin = new Thickness(28, 22, 28, 28) };

        // ---- header
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var logo = new Image { Width = 44, Height = 44, Margin = new Thickness(0, 0, 14, 0) };
        try { logo.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/cometsnap-256.png")); }
        catch { logo.Visibility = Visibility.Collapsed; }
        header.Children.Add(logo);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = L.T("MISSION CONTROL"), FontFamily = SpaceTheme.DisplayFont, FontSize = 28, Foreground = SpaceTheme.SunBrush, TextWrapping = TextWrapping.Wrap });
        titles.Children.Add(new TextBlock { Text = L.T("Every change is saved right away."), Foreground = SpaceTheme.DustBrush, FontSize = 13 });
        header.Children.Add(titles);
        stack.Children.Add(header);

        // ---- general
        Section(stack, L.T("General"));
        stack.Children.Add(Label(L.T("Language")));
        stack.Children.Add(LanguageChips());
        stack.Children.Add(Switch(L.T("Start with Windows"), L.T("So Print Screen always works, right after you log in."), S.StartWithWindows, v =>
        {
            S.StartWithWindows = v;
            _app.ApplyStartWithWindows(userAsked: true);
        }));
        stack.Children.Add(Switch(L.T("Sound on capture"), L.T("A soft click when a capture lands."), S.PlaySounds, v => S.PlaySounds = v));
        stack.Children.Add(Switch(L.T("Little celebrations (snap milestones)"), L.T("A tiny meteor shower on round numbers, and a monthly recap."), S.Celebrations, v => S.Celebrations = v));
        stack.Children.Add(Switch(L.T("Seasonal looks (snowy December, Halloween pumpkin)"), L.T("The comet dresses up for the season."), S.SeasonalSkins, v =>
        {
            S.SeasonalSkins = v;
            _app.ApplySeason();
        }));

        // ---- capturing
        Section(stack, L.T("Capturing"));
        stack.Children.Add(Label(L.T("Capture colors")));
        stack.Children.Add(new TextBlock
        {
            Text = L.T("The crosshair, frames, loupe and size tag, in the comet's colors."),
            Foreground = SpaceTheme.DustBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, -4, 0, 10),
        });
        stack.Children.Add(ThemePicker());
        stack.Children.Add(Switch(L.T("Instant capture (no toolbar)"), L.T("The capture is taken the moment you release the mouse."), S.InstantCapture, v => S.InstantCapture = v));
        stack.Children.Add(Switch(L.T("Show the Edit card after a capture"), L.T("A small card in the corner to edit or drag the capture."), S.ShowEditCard, v => S.ShowEditCard = v));
        stack.Children.Add(Switch(L.T("Comet animation"), L.T("The comet flies your capture into the tray."), S.ShowCometAnimation, v => S.ShowCometAnimation = v));
        stack.Children.Add(Switch(L.T("Detect windows under the mouse"), L.T("Click a window to capture just that window."), S.WindowDetection, v => S.WindowDetection = v));

        // ---- share-ready style
        Section(stack, L.T("Share-ready style"));
        stack.Children.Add(new TextBlock
        {
            Text = L.T("Rounded corners, drop shadow, border, laser or neon glow and a backdrop, ready for social posts and docs. Build your own look in the Style studio."),
            Foreground = SpaceTheme.DustBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, -4, 0, 10),
        });
        stack.Children.Add(StylePicker());
        stack.Children.Add(Switch(L.T("Style on copy"), L.T("The clipboard gets the styled capture. Off: the raw capture."), S.StyleOnCopy, v => S.StyleOnCopy = v));
        stack.Children.Add(Switch(L.T("Style on export"), L.T("Saved files (and what you drag out of the tray) are styled. Off: the raw capture."), S.StyleOnExport, v => S.StyleOnExport = v));
        stack.Children.Add(Switch(L.T("Style per shot"), L.T("A ✨ Style button on the Copied card styles just that capture."), S.StylePerShot, v => S.StylePerShot = v));

        // ---- where captures go
        Section(stack, L.T("Where captures go"));
        stack.Children.Add(Switch(L.T("Copy to the clipboard"), L.T("Ready to paste anywhere right after the capture."), S.CopyToClipboardOnCapture, v => S.CopyToClipboardOnCapture = v));
        stack.Children.Add(Switch(L.T("Save every capture"), L.T("Each capture is also saved in your captures folder."), S.AutoSave, v => S.AutoSave = v));
        stack.Children.Add(Switch(L.T("Ask where to save each time"), L.T("A Save dialog after each capture. The clipboard copy happens first."), S.AskWhereToSave, v => S.AskWhereToSave = v));
        stack.Children.Add(Switch(L.T("Open in the editor right away"), L.T("Every capture opens in the editor, ready for arrows and text."), S.OpenEditorAfterCapture, v => S.OpenEditorAfterCapture = v));
        stack.Children.Add(Label(L.T("Captures folder")));
        stack.Children.Add(FolderRow());
        stack.Children.Add(Label(L.T("File name")));
        stack.Children.Add(NameRow());
        stack.Children.Add(Label(L.T("File format")));
        stack.Children.Add(new TextBlock
        {
            Text = L.T("PNG keeps every pixel sharp. JPG files are smaller."),
            Foreground = SpaceTheme.DustBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, -4, 0, 10),
        });
        stack.Children.Add(Chips(new[] { (0, "PNG"), (1, "JPG") }, SnapNaming.ExtensionFor(S.ImageFormat) == ".jpg" ? 1 : 0, v => S.ImageFormat = v == 1 ? "jpg" : "png"));

        // ---- hotkeys
        Section(stack, L.T("Keyboard shortcuts"));
        stack.Children.Add(new TextBlock
        {
            Text = L.T("Click a shortcut, then press the new keys. Esc cancels."),
            Foreground = SpaceTheme.DustBrush, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
        });
        stack.Children.Add(Switch(L.T("Own the Print Screen key"), L.T("Print Screen opens Cometsnap, even when Windows' Snipping Tool wants it."), S.TakeOverPrintScreen, v =>
        {
            S.TakeOverPrintScreen = v;
            _app.ReapplyHotkeys();
        }));
        stack.Children.Add(HotkeyRow(L.T("Capture region / window"), () => S.CaptureHotkey, v => S.CaptureHotkey = v, new AppSettings().CaptureHotkey));
        stack.Children.Add(HotkeyRow(L.T("Backup shortcut for capture"), () => S.CaptureHotkeyFallback, v => S.CaptureHotkeyFallback = v, new AppSettings().CaptureHotkeyFallback));
        stack.Children.Add(HotkeyRow(L.T("Fullscreen (monitor under the mouse)"), () => S.FullscreenHotkey, v => S.FullscreenHotkey = v, new AppSettings().FullscreenHotkey));
        stack.Children.Add(HotkeyRow(L.T("Active window"), () => S.ActiveWindowHotkey, v => S.ActiveWindowHotkey = v, new AppSettings().ActiveWindowHotkey));
        stack.Children.Add(HotkeyRow(L.T("Scrolling capture"), () => S.ScrollingHotkey, v => S.ScrollingHotkey = v, new AppSettings().ScrollingHotkey));
        stack.Children.Add(HotkeyRow(L.T("Copy text from the screen"), () => S.TextHotkey, v => S.TextHotkey = v, new AppSettings().TextHotkey));
        stack.Children.Add(HotkeyRow(L.T("Scan a QR code"), () => S.QrHotkey, v => S.QrHotkey = v, new AppSettings().QrHotkey));
        stack.Children.Add(HotkeyRow(L.T("Pick a color (copies #RRGGBB)"), () => S.ColorHotkey, v => S.ColorHotkey = v, new AppSettings().ColorHotkey));
        stack.Children.Add(HotkeyRow(L.T("Record a video (MP4)"), () => S.RecordHotkey, v => S.RecordHotkey = v, new AppSettings().RecordHotkey));
        stack.Children.Add(HotkeyRow(L.T("Record a GIF"), () => S.RecordGifHotkey, v => S.RecordGifHotkey = v, new AppSettings().RecordGifHotkey));

        // ---- recording
        Section(stack, L.T("Recording"));
        stack.Children.Add(Switch(L.T("Record PC sound"), L.T("What you hear goes into the video."), S.RecordSystemAudio, v => S.RecordSystemAudio = v));
        stack.Children.Add(Switch(L.T("Record microphone"), L.T("Narrate while you record."), S.RecordMicrophone, v => S.RecordMicrophone = v));
        stack.Children.Add(Switch(L.T("Show the mouse cursor"), null, S.RecordShowCursor, v => S.RecordShowCursor = v));
        stack.Children.Add(Switch(L.T("Copy the video to the clipboard"), L.T("Paste it straight into a chat or an email."), S.RecordCopyToClipboard, v => S.RecordCopyToClipboard = v));
        stack.Children.Add(Label(L.T("Frame rate")));
        stack.Children.Add(Chips(new[] { (15, "15 fps"), (30, "30 fps"), (60, "60 fps") }, S.RecordFramesPerSecond, v => S.RecordFramesPerSecond = v));

        // ---- footer
        var footer = new DockPanel { Margin = new Thickness(0, 26, 0, 0), LastChildFill = false };
        var version = new TextBlock { Text = $"Cometsnap {CometApp.Version}", Foreground = SpaceTheme.DustBrush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var advanced = Link(L.T("Advanced: open the settings file"), _app.OpenSettingsFile);
        DockPanel.SetDock(version, Dock.Left);
        DockPanel.SetDock(advanced, Dock.Right);
        footer.Children.Add(version);
        footer.Children.Add(advanced);
        stack.Children.Add(footer);

        _scroll = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = SpaceTheme.NebulaBrush };
        Content = _scroll;
        _scroll.ScrollToVerticalOffset(offset);
    }

    // ------------------------------------------------------------------ language

    private UIElement LanguageChips()
    {
        var current = S.Language ?? "";
        var windows = L.Languages.First(l => l.Code == L.FromCulture(CultureInfo.CurrentUICulture)).Name;
        var options = new List<(string Code, string Name)> { ("", L.F("Same as Windows ({0})", windows)) };
        options.AddRange(L.Languages);
        var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        foreach (var (code, name) in options)
        {
            var c = code;
            wrap.Children.Add(Chip(name, current == code, () =>
            {
                if ((S.Language ?? "") == c) return;
                _app.SetLanguage(c);
                Build();
            }));
        }
        return wrap;
    }

    // ------------------------------------------------------------------ capture colors

    /// <summary>English names of the themes, here so the translation check sees them.</summary>
    private static string ThemeName(CaptureTheme t) => t.Id switch
    {
        "blue" => L.T("Comet blue"),
        "yellow" => L.T("Sun yellow"),
        "terracotta" => L.T("Terracotta"),
        "orange" => L.T("Flame orange"),
        "stardust" => L.T("Stardust"),
        _ => t.Name,
    };

    /// <summary>Color chips plus a live preview of the capture screen in the chosen colors.</summary>
    private UIElement ThemePicker()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        var current = CaptureThemes.Get(S.CaptureTheme);
        var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var theme in CaptureThemes.All)
        {
            var t = theme;
            var selected = t.Id == current.Id;
            var dot = new Border
            {
                Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(CaptureLook.ToColor(t.Accent)), VerticalAlignment = VerticalAlignment.Center,
                BorderBrush = SpaceTheme.StarWhiteBrush, BorderThickness = new Thickness(selected ? 2 : 0),
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(dot);
            row.Children.Add(new TextBlock { Text = ThemeName(t), FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            var accent = new SolidColorBrush(CaptureLook.ToColor(t.Accent));
            var chip = new Border
            {
                Background = selected ? SpaceTheme.NavyLightBrush : SpaceTheme.NavyBrush,
                BorderBrush = selected ? accent : SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(selected ? 2 : 1),
                CornerRadius = new CornerRadius(16), Padding = new Thickness(10, 5, 14, 5), Margin = new Thickness(0, 0, 8, 8),
                Cursor = Cursors.Hand, Child = row,
            };
            if (!selected)
            {
                chip.MouseEnter += (_, _) => chip.BorderBrush = accent;
                chip.MouseLeave += (_, _) => chip.BorderBrush = SpaceTheme.NavyLightBrush;
            }
            chip.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (S.CaptureTheme == t.Id) return;
                S.CaptureTheme = t.Id;
                CaptureLook.Use(t.Id);
                _app.TrySaveSettings();
                Build();
            };
            wrap.Children.Add(chip);
        }
        panel.Children.Add(wrap);
        panel.Children.Add(ThemePreview(current));
        return panel;
    }

    // ------------------------------------------------------------------ share-ready style

    private Image? _stylePreview;

    /// <summary>A small preview of the current style and the button that opens the Style studio.</summary>
    private UIElement StylePicker()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        _stylePreview = new Image { Width = 300, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(_stylePreview, BitmapScalingMode.HighQuality);
        RefreshStylePreview();
        var frame = new Border
        {
            CornerRadius = new CornerRadius(10), BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1),
            Background = StyleSample.Checker(), Padding = new Thickness(16), Child = _stylePreview, Cursor = Cursors.Hand,
            ToolTip = L.T("Open the Style studio"),
        };
        frame.MouseLeftButtonUp += (_, e) => { e.Handled = true; _app.OpenStyleStudio(); };
        panel.Children.Add(frame);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        row.Children.Add(Pill("✨ " + L.T("Open the Style studio"), primary: true, _app.OpenStyleStudio));
        panel.Children.Add(row);
        return panel;
    }

    /// <summary>Redraws the small preview (the Style studio calls this after each change).</summary>
    public void RefreshStylePreview()
    {
        if (_stylePreview is null) return;
        try { _stylePreview.Source = StyledBitmaps.Apply(StyleSample.Sample, S.SnapStyle ?? new SnapStyle()); }
        catch { _stylePreview.Visibility = Visibility.Collapsed; }
    }

    /// <summary>A small fake capture screen: dimmed desktop, crosshair, selection with glow, size tag and loupe.</summary>
    private static UIElement ThemePreview(CaptureTheme theme)
    {
        var accent = CaptureLook.ToColor(theme.Accent);
        var accentBrush = new SolidColorBrush(accent);
        const double w = 520, h = 150;
        var canvas = new Canvas { Width = w, Height = h, ClipToBounds = true, Background = SpaceTheme.NebulaBrush };

        // a pretend desktop: two windows
        void Win(double x, double y, double ww, double wh, Color bar)
        {
            var r = new Border
            {
                Width = ww, Height = wh, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x3C, 0x8F)),
                BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1),
                Child = new Border { Height = 12, VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(bar), CornerRadius = new CornerRadius(4, 4, 0, 0) },
            };
            Canvas.SetLeft(r, x);
            Canvas.SetTop(r, y);
            canvas.Children.Add(r);
        }
        Win(24, 18, 190, 110, Color.FromRgb(0x5E, 0x4C, 0x4A));
        Win(150, 46, 230, 92, Color.FromRgb(0x1F, 0x25, 0x59));
        canvas.Children.Add(new Border { Width = w, Height = h, Background = SpaceTheme.WithAlpha(SpaceTheme.Void, 0x8C) });

        // selection: the second window, undimmed, with glow and frame
        var sel = new Rect(150, 46, 230, 92);
        var hole = new Border
        {
            Width = sel.Width, Height = sel.Height, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x3C, 0x8F)),
            Child = new Border { Height = 12, VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x25, 0x59)), CornerRadius = new CornerRadius(4, 4, 0, 0) },
        };
        Canvas.SetLeft(hole, sel.X);
        Canvas.SetTop(hole, sel.Y);
        canvas.Children.Add(hole);
        var glow = new System.Windows.Shapes.Rectangle { Width = sel.Width + 10, Height = sel.Height + 10, Stroke = SpaceTheme.WithAlpha(accent, 0x55), StrokeThickness = 4 };
        Canvas.SetLeft(glow, sel.X - 5);
        Canvas.SetTop(glow, sel.Y - 5);
        canvas.Children.Add(glow);
        var frame = new System.Windows.Shapes.Rectangle { Width = sel.Width + 2, Height = sel.Height + 2, Stroke = accentBrush, StrokeThickness = 1.5 };
        Canvas.SetLeft(frame, sel.X - 1);
        Canvas.SetTop(frame, sel.Y - 1);
        canvas.Children.Add(frame);

        // size tag
        var tag = new Border
        {
            Background = new SolidColorBrush(CaptureLook.ToColor(theme.Tag)), CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 1, 5, 1),
            Child = new TextBlock { Text = "1280 × 720", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(CaptureLook.ToColor(theme.TagText)) },
        };
        Canvas.SetLeft(tag, sel.X);
        Canvas.SetTop(tag, sel.Y - 20);
        canvas.Children.Add(tag);

        // crosshair
        var guide = SpaceTheme.WithAlpha(accent, 0x70);
        canvas.Children.Add(new System.Windows.Shapes.Line { X1 = 0, X2 = w, Y1 = 112.5, Y2 = 112.5, Stroke = guide, StrokeThickness = 1 });
        canvas.Children.Add(new System.Windows.Shapes.Line { X1 = 300.5, X2 = 300.5, Y1 = 0, Y2 = h, Stroke = guide, StrokeThickness = 1 });

        // loupe
        var loupe = new Border
        {
            Width = 92, Height = 112, CornerRadius = new CornerRadius(8), Background = SpaceTheme.WithAlpha(SpaceTheme.Void, 0xF0),
            BorderBrush = accentBrush, BorderThickness = new Thickness(1.5), Padding = new Thickness(5),
        };
        var lstack = new StackPanel();
        var pixels = new System.Windows.Controls.Primitives.UniformGrid { Rows = 5, Columns = 5, Width = 80, Height = 70 };
        var rng = new Random(7);
        for (var i = 0; i < 25; i++)
        {
            var v = (byte)(0x22 + rng.Next(0x30));
            pixels.Children.Add(new Border
            {
                Background = new SolidColorBrush(i == 12 ? accent : Color.FromRgb(v, (byte)(v + 0x10), (byte)(0x60 + rng.Next(0x40)))),
                BorderBrush = i == 12 ? Brushes.White : SpaceTheme.WithAlpha(Colors.Black, 0x30), BorderThickness = new Thickness(i == 12 ? 1.5 : 0.5),
            });
        }
        lstack.Children.Add(pixels);
        lstack.Children.Add(new TextBlock
        {
            Text = CaptureThemes.Hex(theme.Accent), FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11,
            Foreground = SpaceTheme.StarWhiteBrush, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center,
        });
        loupe.Child = lstack;
        Canvas.SetLeft(loupe, w - 92 - 12);
        Canvas.SetTop(loupe, h - 112 - 12);
        canvas.Children.Add(loupe);

        return new Border
        {
            CornerRadius = new CornerRadius(10), BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1),
            ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left, Child = new Viewbox { Child = canvas, Stretch = Stretch.Uniform, MaxWidth = w },
        };
    }

    // ------------------------------------------------------------------ folder

    private UIElement FolderRow()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        var path = new TextBlock
        {
            Text = _app.CapturesFolder, Foreground = SpaceTheme.StarWhiteBrush, TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 13,
        };
        panel.Children.Add(new Border
        {
            Background = SpaceTheme.WithAlpha(SpaceTheme.Void, 0xC0), BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 9), Child = path,
        });
        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(Pill(L.T("Change…"), primary: true, () =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = L.T("Where should your captures land?"), InitialDirectory = _app.CapturesFolder };
            if (dialog.ShowDialog(this) == true) _app.SetCapturesFolder(dialog.FolderName);
            path.Text = _app.CapturesFolder;
        }));
        buttons.Children.Add(Pill(L.T("Open"), primary: false, _app.OpenCapturesFolder));
        if (!string.IsNullOrWhiteSpace(S.CapturesFolder))
            buttons.Children.Add(Pill(L.T("Back to default"), primary: false, () =>
            {
                _app.SetCapturesFolder("");
                Build();
            }));
        panel.Children.Add(buttons);
        return panel;
    }

    // ------------------------------------------------------------------ file name

    /// <summary>The name pattern: a text box, tags to click in, and a live example.</summary>
    private UIElement NameRow()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var box = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(S.FileNamePattern) ? FileNamePattern.Default : S.FileNamePattern,
            Background = SpaceTheme.WithAlpha(SpaceTheme.Void, 0xC0), Foreground = SpaceTheme.StarWhiteBrush, CaretBrush = SpaceTheme.StarWhiteBrush,
            BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1), Padding = new Thickness(10, 8, 10, 8),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 13,
        };
        panel.Children.Add(new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = box });

        var example = new TextBlock { Foreground = SpaceTheme.DustBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
        void Refresh()
        {
            var name = FileNamePattern.Expand(box.Text, DateTime.Now, _app.PeekNameCounter()) + SnapNaming.ExtensionFor(S.ImageFormat);
            example.Text = L.F("Example: {0}", name);
        }
        box.TextChanged += (_, _) =>
        {
            var text = box.Text.Trim();
            S.FileNamePattern = FileNamePattern.IsUsable(text) ? text : FileNamePattern.Default;
            Refresh();
        };
        box.LostKeyboardFocus += (_, _) => _app.TrySaveSettings();
        Refresh();
        panel.Children.Add(example);

        panel.Children.Add(new TextBlock
        {
            Text = L.T("Click a tag to add it. A / makes a subfolder."),
            Foreground = SpaceTheme.DustBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
        });
        var tags = new WrapPanel();
        foreach (var token in FileNamePattern.Tokens)
        {
            var t = token;
            tags.Children.Add(Chip(t, false, () =>
            {
                var at = box.CaretIndex;
                box.Text = box.Text.Insert(at, t);
                box.CaretIndex = at + t.Length;
                box.Focus();
            }));
        }
        panel.Children.Add(tags);
        panel.Children.Add(Link(L.T("Back to default"), () =>
        {
            box.Text = FileNamePattern.Default;
            _app.TrySaveSettings();
        }));
        return panel;
    }

    // ------------------------------------------------------------------ hotkeys

    private sealed class HotkeyBox
    {
        public required Border Box { get; init; }
        public required TextBlock Text { get; init; }
        public required TextBlock Note { get; init; }
        public required Func<string> Get { get; init; }
        public required Action<string> Set { get; init; }
    }

    private UIElement HotkeyRow(string title, Func<string> get, Action<string> set, string fallback)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        var note = new TextBlock { FontSize = 12, Foreground = SpaceTheme.SunBrush, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        left.Children.Add(note);
        grid.Children.Add(left);

        var text = new TextBlock { Text = Pretty(get()), FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        var box = new Border
        {
            MinWidth = 170, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(12, 0, 6, 0), CornerRadius = new CornerRadius(8),
            Background = SpaceTheme.NavyBrush, BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand, Child = text, VerticalAlignment = VerticalAlignment.Center,
        };
        var hotkey = new HotkeyBox { Box = box, Text = text, Note = note, Get = get, Set = set };
        box.MouseLeftButtonUp += (_, e) => { e.Handled = true; StartListening(hotkey); };
        Grid.SetColumn(box, 1);
        grid.Children.Add(box);

        var reset = Link("↺", () =>
        {
            StopListening(apply: false);
            Apply(hotkey, fallback);
        });
        reset.FontSize = 16;
        reset.ToolTip = L.F("Back to default ({0})", Pretty(fallback));
        Grid.SetColumn(reset, 2);
        grid.Children.Add(reset);
        return grid;
    }

    private void StartListening(HotkeyBox hotkey)
    {
        StopListening(apply: false);
        _listening = hotkey;
        _app.PauseHotkeys(); // so Print Screen & co. reach this window instead of starting a capture
        hotkey.Box.BorderBrush = SpaceTheme.SunBrush;
        hotkey.Box.BorderThickness = new Thickness(2);
        hotkey.Text.Text = L.T("Press the keys…");
        hotkey.Text.Foreground = SpaceTheme.SunBrush;
        Focus();
    }

    private void StopListening(bool apply)
    {
        if (_listening is not { } hotkey) return;
        _listening = null;
        hotkey.Box.BorderBrush = SpaceTheme.NavyLightBrush;
        hotkey.Box.BorderThickness = new Thickness(1);
        hotkey.Text.Foreground = SpaceTheme.StarWhiteBrush;
        hotkey.Text.Text = Pretty(hotkey.Get());
        if (!apply) _app.ReapplyHotkeys();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_listening is null) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { StopListening(apply: false); return; }
        if (key == Key.Snapshot) return; // Windows only reports Print Screen on key up
        TryTake(key);
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (_listening is null) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key != Key.Snapshot) return;
        e.Handled = true;
        TryTake(key);
    }

    private void TryTake(Key key)
    {
        if (_listening is not { } hotkey) return;
        var name = KeyName(key);
        if (name is null) return; // a modifier on its own: keep waiting

        var mods = Keyboard.Modifiers;
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(name);
        var gesture = string.Join("+", parts);

        // A bare letter or digit would fire while typing anywhere.
        var plain = (mods & ~ModifierKeys.Shift) == ModifierKeys.None && name.Length == 1;
        if (plain || !HotkeyGesture.TryParse(gesture, out _))
        {
            hotkey.Text.Text = L.T("Add Ctrl, Alt or Win");
            return;
        }
        StopListening(apply: true);
        Apply(hotkey, gesture);
    }

    private void Apply(HotkeyBox hotkey, string gesture)
    {
        hotkey.Set(gesture);
        hotkey.Text.Text = Pretty(gesture);
        var ok = _app.ReapplyHotkeys(gesture);
        hotkey.Note.Text = ok ? "" : L.T("Another app already uses this shortcut.");
        hotkey.Note.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string? KeyName(Key key) => key switch
    {
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + (key - Key.NumPad0))).ToString(),
        >= Key.F1 and <= Key.F24 => key.ToString(),
        Key.Snapshot => "PrintScreen",
        Key.Space => "Space",
        Key.Insert => "Insert",
        Key.Delete => "Delete",
        Key.Home => "Home",
        Key.End => "End",
        Key.PageUp => "PageUp",
        Key.PageDown => "PageDown",
        Key.Pause => "Pause",
        Key.Scroll => "ScrollLock",
        Key.Left => "Left",
        Key.Right => "Right",
        Key.Up => "Up",
        Key.Down => "Down",
        _ => null,
    };

    /// <summary>"Ctrl+Shift+PrintScreen" → "Ctrl + Shift + Print Screen".</summary>
    private static string Pretty(string gesture) =>
        string.Join(" + ", gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Equals("PrintScreen", StringComparison.OrdinalIgnoreCase) ? L.T("Print Screen") : p));

    // ------------------------------------------------------------------ building blocks

    private static void Section(Panel stack, string title) =>
        stack.Children.Add(new TextBlock
        {
            Text = title.ToUpper(L.Culture), FontFamily = SpaceTheme.HeadingFont, FontSize = 15, Foreground = SpaceTheme.CometBlueBrush,
            Margin = new Thickness(0, 24, 0, 12),
        });

    private static TextBlock Label(string text) =>
        new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };

    /// <summary>A row with a title, a short explainer and an on/off switch. Saves on every flip.</summary>
    private UIElement Switch(string title, string? detail, bool value, Action<bool> set)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        if (detail is not null)
            text.Children.Add(new TextBlock { Text = detail, FontSize = 12, Foreground = SpaceTheme.DustBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        grid.Children.Add(text);

        var knob = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = SpaceTheme.StarWhiteBrush, Margin = new Thickness(3, 0, 3, 0) };
        var track = new Border
        {
            Width = 42, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1), Child = knob,
        };
        Grid.SetColumn(track, 1);
        grid.Children.Add(track);

        var on = value;
        void Paint()
        {
            track.Background = on ? SpaceTheme.TerracottaBrush : SpaceTheme.NavyBrush;
            track.BorderBrush = on ? SpaceTheme.TerracottaBrush : SpaceTheme.NavyLightBrush;
            knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }
        Paint();
        grid.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            on = !on;
            Paint();
            set(on);
            _app.TrySaveSettings();
        };
        return grid;
    }

    private UIElement Chips(IEnumerable<(int Value, string Text)> options, int current, Action<int> set)
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var (value, label) in options)
        {
            var v = value;
            wrap.Children.Add(Chip(label, v == current, () =>
            {
                set(v);
                _app.TrySaveSettings();
                Build();
            }));
        }
        return wrap;
    }

    private static Border Chip(string text, bool selected, Action click)
    {
        var normal = selected ? SpaceTheme.CometBlueBrush : SpaceTheme.NavyBrush;
        var b = new Border
        {
            Background = normal, BorderBrush = selected ? SpaceTheme.CometBlueBrush : SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand,
            Child = new TextBlock { Text = (selected ? "✓ " : "") + text, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal, FontSize = 13 },
        };
        if (!selected)
        {
            b.MouseEnter += (_, _) => b.Background = SpaceTheme.NavyLightBrush;
            b.MouseLeave += (_, _) => b.Background = normal;
        }
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return b;
    }

    private static Border Pill(string text, bool primary, Action click)
    {
        var normal = primary ? SpaceTheme.TerracottaBrush : SpaceTheme.NavyBrush;
        var hover = primary ? SpaceTheme.WithAlpha(SpaceTheme.Terracotta, 0xD0) : SpaceTheme.NavyLightBrush;
        var b = new Border
        {
            Background = normal, CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(0, 0, 8, 0),
            BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(primary ? 0 : 1), Cursor = Cursors.Hand,
            Child = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 13 },
        };
        b.MouseEnter += (_, _) => b.Background = hover;
        b.MouseLeave += (_, _) => b.Background = normal;
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return b;
    }

    private static TextBlock Link(string text, Action click)
    {
        var t = new TextBlock { Text = text, Foreground = SpaceTheme.DustBrush, FontSize = 12, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        t.MouseEnter += (_, _) => t.Foreground = SpaceTheme.StarWhiteBrush;
        t.MouseLeave += (_, _) => t.Foreground = SpaceTheme.DustBrush;
        t.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return t;
    }
}
