using Avalonia.Platform.Storage;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Cometsnap.Core.Storage;
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
        Title = T("Cometsnap settings");
        Width = 520;
        Height = 680;
        Background = SpaceTheme.NebulaBrush;
        Foreground = SpaceTheme.StarWhiteBrush;
        FontFamily = SpaceTheme.UiFont;
        Icon = new WindowIcon(SpaceTheme.Logo);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;

        var panel = new StackPanel { Margin = new Thickness(24, 18), Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = T("SETTINGS"), FontFamily = SpaceTheme.DisplayFont, FontSize = 30, Foreground = SpaceTheme.SunBrush, Margin = new Thickness(0, 0, 0, 8) });

        // Language: the desktop's, or one picked here; applies at once.
        var system = Loc.Languages.FirstOrDefault(l => l.Code == Cometsnap.Core.Localization.L.FromCulture(Loc.SystemCulture())).Name ?? "English";
        var languages = new List<(string Code, string Name)> { ("", F("Same as the system ({0})", system)) };
        languages.AddRange(Loc.Languages);
        var language = new ComboBox
        {
            ItemsSource = languages.Select(l => l.Name).ToList(),
            SelectedIndex = Math.Max(0, languages.FindIndex(l => l.Code == s.Language)),
            Width = 260,
        };
        language.SelectionChanged += (_, _) =>
        {
            var code = languages[Math.Max(0, language.SelectedIndex)].Code;
            if (code == s.Language) return;
            Close();
            _app.SetLanguage(code);
            _app.OpenSettings();
        };
        panel.Children.Add(Row(T("Language"), language));

        panel.Children.Add(Section(T("Capture")));
        panel.Children.Add(Toggle(T("Detect windows under the mouse"), s.WindowDetection, v => s.WindowDetection = v,
            WindowFinder.IsAvailable ? null : T("Wayland does not let apps see other windows: click captures the screen, drag picks an area.")));
        panel.Children.Add(Toggle(T("Show the Edit card after a capture"), s.ShowEditCard, v => s.ShowEditCard = v));
        panel.Children.Add(Toggle(T("Soft click sound"), s.PlaySounds, v => s.PlaySounds = v));
        panel.Children.Add(Toggle(T("Comet animation"), s.ShowCometAnimation, v => s.ShowCometAnimation = v,
            T("The capture rides a comet towards the tray. Needs a compositor (most desktops have one).")));
        panel.Children.Add(Toggle(T("Little celebrations (snap milestones)"), s.Celebrations, v => s.Celebrations = v));
        panel.Children.Add(Toggle(T("Seasonal looks (snowy December, Halloween pumpkin)"), s.SeasonalSkins, v =>
        {
            s.SeasonalSkins = v;
            _app.ApplySeason();
        }));
        var corner = new ComboBox { ItemsSource = new[] { T("Bottom left"), T("Bottom right"), T("Top left"), T("Top right"), T("Follow the cursor") }, SelectedIndex = (int)s.LoupeCorner, Width = 200 };
        corner.SelectionChanged += (_, _) => { s.LoupeCorner = (LoupeCorner)Math.Max(0, corner.SelectedIndex); _app.SaveSettings(); };
        panel.Children.Add(Row(T("Magnifier corner"), corner));
        panel.Children.Add(ThemePicker());

        panel.Children.Add(Section(T("Where captures go")));
        panel.Children.Add(Toggle(T("Copy captures to the clipboard"), s.CopyToClipboardOnCapture, v => s.CopyToClipboardOnCapture = v));
        panel.Children.Add(Toggle(T("Save every capture"), s.AutoSave, v => s.AutoSave = v, T("Each capture is also saved in your captures folder.")));
        panel.Children.Add(Toggle(T("Ask where to save each time"), s.AskWhereToSave, v => s.AskWhereToSave = v, T("A Save dialog after each capture. The clipboard copy happens first.")));
        panel.Children.Add(Toggle(T("Open in the editor right away"), s.OpenEditorAfterCapture, v => s.OpenEditorAfterCapture = v, T("Every capture opens in the editor, ready for arrows and text.")));
        panel.Children.Add(FolderRow());
        panel.Children.Add(NameRow());
        var format = new ComboBox { ItemsSource = new[] { "PNG", "JPG" }, SelectedIndex = SnapNaming.ExtensionFor(s.ImageFormat) == ".jpg" ? 1 : 0, Width = 120 };
        format.SelectionChanged += (_, _) =>
        {
            s.ImageFormat = format.SelectedIndex == 1 ? "jpg" : "png";
            _app.SaveSettings();
            _refreshExample?.Invoke();
        };
        panel.Children.Add(Row(T("File format"), format));
        panel.Children.Add(new TextBlock { Text = T("PNG keeps every pixel sharp. JPG files are smaller."), FontSize = 11, Foreground = SpaceTheme.DustBrush, TextWrapping = TextWrapping.Wrap });

        panel.Children.Add(Section(T("Keyboard shortcuts")));
        panel.Children.Add(new TextBlock { Text = T("Click a shortcut, then press the new keys. Esc cancels."), FontSize = 11, Foreground = SpaceTheme.DustBrush, TextWrapping = TextWrapping.Wrap });
        var defaults = new AppSettings();
        panel.Children.Add(HotkeyRow(T("Capture a window or an area"), () => s.CaptureHotkey, v => s.CaptureHotkey = v, defaults.CaptureHotkey));
        panel.Children.Add(HotkeyRow(T("Backup shortcut for capture"), () => s.CaptureHotkeyFallback, v => s.CaptureHotkeyFallback = v, defaults.CaptureHotkeyFallback));
        panel.Children.Add(HotkeyRow(T("Capture the whole screen"), () => s.FullscreenHotkey, v => s.FullscreenHotkey = v, defaults.FullscreenHotkey));
        panel.Children.Add(HotkeyRow(T("Scrolling capture"), () => s.ScrollingHotkey, v => s.ScrollingHotkey = v, defaults.ScrollingHotkey));
        panel.Children.Add(HotkeyRow(T("Copy text from the screen"), () => s.TextHotkey, v => s.TextHotkey = v, defaults.TextHotkey));
        panel.Children.Add(HotkeyRow(T("Pick a color"), () => s.ColorHotkey, v => s.ColorHotkey = v, defaults.ColorHotkey));
        panel.Children.Add(HotkeyRow(T("Record a video (press again to stop)"), () => s.RecordHotkey, v => s.RecordHotkey = v, defaults.RecordHotkey));
        panel.Children.Add(HotkeyRow(T("Record a GIF (press again to stop)"), () => s.RecordGifHotkey, v => s.RecordGifHotkey = v, defaults.RecordGifHotkey));

        panel.Children.Add(Section(T("Recording")));
        panel.Children.Add(Toggle(T("Record the computer's sound"), s.RecordSystemAudio, v => s.RecordSystemAudio = v));
        panel.Children.Add(Toggle(T("Record the microphone"), s.RecordMicrophone, v => s.RecordMicrophone = v));
        panel.Children.Add(Toggle(T("Show the mouse pointer"), s.RecordShowCursor, v => s.RecordShowCursor = v));
        panel.Children.Add(Toggle(T("Copy recordings to the clipboard"), s.RecordCopyToClipboard, v => s.RecordCopyToClipboard = v));

        panel.Children.Add(Section(T("Desktop")));
        panel.Children.Add(Toggle(T("Start Cometsnap when I log in"), s.StartAtLogin, v =>
        {
            s.StartAtLogin = v;
            DesktopIntegration.SetStartAtLogin(v);
        }));
        var keyText = s.ReplacedDesktopTool ? T("Give Print Screen back to the desktop") : T("Use Print Screen for Cometsnap…");
        panel.Children.Add(Ui.ActionButton(keyText, null, T("Your desktop's own screenshot shortcut"), () =>
        {
            Close();
            if (_app.Settings.ReplacedDesktopTool) _app.GiveBackPrintScreen();
            else _app.ShowReplaceOffer(force: true);
        }));

        panel.Children.Add(Section(T("Updates")));
        var updates = _app.Updates;
        var auto = new CheckBox { IsChecked = updates.Prefs.CheckAutomatically, Content = new TextBlock { Text = T("Check for updates once a day"), FontSize = 14, TextWrapping = TextWrapping.Wrap } };
        auto.IsCheckedChanged += (_, _) => updates.SetAutomatic(auto.IsChecked == true);
        panel.Children.Add(auto);
        panel.Children.Add(new TextBlock { Text = T("Cometsnap asks GitHub if a new version is out. Nothing installs without your click."), FontSize = 11, Foreground = SpaceTheme.DustBrush, Margin = new Thickness(28, 0, 0, 4), TextWrapping = TextWrapping.Wrap });
        var updateStatus = new TextBlock { FontSize = 12, Foreground = SpaceTheme.DustBrush, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 };
        var checking = false;
        var updateRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        updateRow.Children.Add(Ui.ActionButton(T("Check now"), null, T("Check now"), async () =>
        {
            if (checking) return;
            checking = true;
            updateStatus.Text = T("Checking…");
            var result = await updates.CheckAsync(manual: true);
            updateStatus.Text = result switch
            {
                Services.UpdateService.Result.UpToDate => F("You have the latest version ({0}).", Services.UpdateService.Current),
                Services.UpdateService.Result.Unreachable => T("Could not reach GitHub. Try again later."),
                _ => "",
            };
            checking = false;
        }));
        updateRow.Children.Add(updateStatus);
        panel.Children.Add(updateRow);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(Ui.ActionButton(T("Edit settings file"), null, T("All settings, including shortcuts, as JSON"), () => Tools.Open(_app.Paths.SettingsFile)));
        panel.Children.Add(buttons);
        panel.Children.Add(new TextBlock
        {
            Text = $"Cometsnap {typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3)} · {DesktopSession.Describe()} · " + T("MIT license"),
            Foreground = SpaceTheme.DustBrush, FontSize = 11, Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap,
        });

        Content = new ScrollViewer { Content = panel };
        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Deactivated += (_, _) => StopListening();
        Closed += (_, _) => StopListening();
    }

    // ------------------------------------------------------------------ captures folder and file name

    private Action? _refreshExample;

    private Control FolderRow()
    {
        var stack = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
        stack.Children.Add(new TextBlock { Text = T("Captures folder"), FontSize = 14 });
        var path = new TextBlock { Text = _app.CapturesFolder, FontSize = 12, Foreground = SpaceTheme.DustBrush, TextWrapping = TextWrapping.Wrap };
        stack.Children.Add(path);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(Ui.ActionButton(T("Change…"), null, T("Where should your captures land?"), () => _ = ChooseFolderAsync(path)));
        buttons.Children.Add(Ui.ActionButton(T("Open captures folder"), null, "", _app.OpenCapturesFolder));
        buttons.Children.Add(Ui.ActionButton(T("Back to default"), null, "", () =>
        {
            _app.SetCapturesFolder("");
            path.Text = _app.CapturesFolder;
        }));
        stack.Children.Add(buttons);
        return stack;
    }

    private async Task ChooseFolderAsync(TextBlock path)
    {
        try
        {
            Avalonia.Platform.Storage.IStorageFolder? start = null;
            try { start = await StorageProvider.TryGetFolderFromPathAsync(_app.CapturesFolder); }
            catch { /* the picker chooses */ }
            var picked = await StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = T("Where should your captures land?"), AllowMultiple = false, SuggestedStartLocation = start,
            });
            if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } folder)
            {
                _app.SetCapturesFolder(folder);
                path.Text = _app.CapturesFolder;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Choosing the captures folder failed", ex);
        }
    }

    /// <summary>The name pattern: a text box, tags to click in, and a live example.</summary>
    private Control NameRow()
    {
        var s = _app.Settings;
        var stack = new StackPanel { Spacing = 6, Margin = new Thickness(0, 10, 0, 0) };
        stack.Children.Add(new TextBlock { Text = T("File name"), FontSize = 14 });
        var box = new TextBox { Text = string.IsNullOrWhiteSpace(s.FileNamePattern) ? FileNamePattern.Default : s.FileNamePattern, FontFamily = new FontFamily("DejaVu Sans Mono, monospace"), FontSize = 13 };
        var example = new TextBlock { FontSize = 11, Foreground = SpaceTheme.DustBrush, TextWrapping = TextWrapping.Wrap };
        _refreshExample = () => example.Text = F("Example: {0}", FileNamePattern.Expand(box.Text, DateTime.Now, _app.PeekNameCounter()) + SnapNaming.ExtensionFor(s.ImageFormat));
        box.TextChanged += (_, _) =>
        {
            var text = (box.Text ?? "").Trim();
            s.FileNamePattern = FileNamePattern.IsUsable(text) ? text : FileNamePattern.Default;
            _refreshExample();
        };
        box.LostFocus += (_, _) => _app.SaveSettings();
        _refreshExample();
        stack.Children.Add(box);
        stack.Children.Add(example);
        stack.Children.Add(new TextBlock { Text = T("Click a tag to add it. A / makes a subfolder."), FontSize = 11, Foreground = SpaceTheme.DustBrush, TextWrapping = TextWrapping.Wrap });
        var tags = new WrapPanel();
        foreach (var token in FileNamePattern.Tokens)
        {
            var t = token;
            var chip = new Button { Content = t, FontSize = 12, Padding = new Thickness(8, 3), Margin = new Thickness(0, 0, 6, 6) };
            chip.Click += (_, _) =>
            {
                var text = box.Text ?? "";
                var at = Math.Clamp(box.CaretIndex, 0, text.Length);
                box.Text = text.Insert(at, t);
                box.CaretIndex = at + t.Length;
                box.Focus();
            };
            tags.Children.Add(chip);
        }
        var reset = new Button { Content = T("Back to default"), FontSize = 12, Padding = new Thickness(8, 3), Margin = new Thickness(0, 0, 6, 6) };
        reset.Click += (_, _) => { box.Text = FileNamePattern.Default; _app.SaveSettings(); };
        tags.Children.Add(reset);
        stack.Children.Add(tags);
        return stack;
    }

    // ------------------------------------------------------------------ shortcuts

    private (Button Button, Func<string> Get, Action<string> Set)? _listening;

    private Control HotkeyRow(string title, Func<string> get, Action<string> set, string fallback)
    {
        var button = new Button { Content = Pretty(get()), MinWidth = 170, HorizontalContentAlignment = HorizontalAlignment.Center };
        button.Click += (_, _) => StartListening(button, get, set);
        var reset = new Button { Content = "↺", Padding = new Thickness(8, 4), Margin = new Thickness(6, 0, 0, 0) };
        ToolTip.SetTip(reset, T("Back to default"));
        reset.Click += (_, _) =>
        {
            StopListening();
            set(fallback);
            button.Content = Pretty(fallback);
            _app.SaveSettings();
            _app.ApplyHotkeyChange();
        };
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(button);
        controls.Children.Add(reset);
        return Row(title, controls);
    }

    private static string Pretty(string hotkey) => HotkeyGesture.TryParse(hotkey, out var g) ? g.ToString() : hotkey;

    private void StartListening(Button button, Func<string> get, Action<string> set)
    {
        StopListening();
        _listening = (button, get, set);
        button.Content = T("Press the new keys…");
        // While listening, the app's own grabs must not swallow the keys (Print Screen above all).
        _app.Hotkeys.Stop();
    }

    private void StopListening()
    {
        if (_listening is not { } l) return;
        _listening = null;
        l.Button.Content = Pretty(l.Get());
        _app.RestartHotkeys();
    }

    private void OnKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (_listening is not { } l) return;
        e.Handled = true;
        if (e.Key == Avalonia.Input.Key.Escape) { StopListening(); return; }
        if (HotkeyText(e.Key, e.KeyModifiers) is not { } gesture) return; // a modifier alone: keep waiting
        l.Set(gesture);
        _listening = null;
        l.Button.Content = Pretty(gesture);
        _app.SaveSettings();
        _app.ApplyHotkeyChange();
    }

    /// <summary>The settings text for a key press ("Ctrl+Shift+S"), or null for a modifier alone or an unknown key.</summary>
    internal static string? HotkeyText(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers mods)
    {
        string? name = key switch
        {
            >= Avalonia.Input.Key.A and <= Avalonia.Input.Key.Z => key.ToString(),
            >= Avalonia.Input.Key.D0 and <= Avalonia.Input.Key.D9 => ((int)(key - Avalonia.Input.Key.D0)).ToString(System.Globalization.CultureInfo.InvariantCulture),
            >= Avalonia.Input.Key.NumPad0 and <= Avalonia.Input.Key.NumPad9 => ((int)(key - Avalonia.Input.Key.NumPad0)).ToString(System.Globalization.CultureInfo.InvariantCulture),
            >= Avalonia.Input.Key.F1 and <= Avalonia.Input.Key.F24 => "F" + ((int)(key - Avalonia.Input.Key.F1) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Avalonia.Input.Key.PrintScreen => "PrintScreen",
            Avalonia.Input.Key.Pause => "Pause",
            Avalonia.Input.Key.Scroll => "ScrollLock",
            Avalonia.Input.Key.Insert => "Insert",
            Avalonia.Input.Key.Home => "Home",
            Avalonia.Input.Key.End => "End",
            Avalonia.Input.Key.Space => "Space",
            Avalonia.Input.Key.Enter => "Enter",
            Avalonia.Input.Key.Tab => "Tab",
            _ => null,
        };
        if (name is null) return null;
        var parts = new List<string>(5);
        if (mods.HasFlag(Avalonia.Input.KeyModifiers.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(Avalonia.Input.KeyModifiers.Shift)) parts.Add("Shift");
        if (mods.HasFlag(Avalonia.Input.KeyModifiers.Alt)) parts.Add("Alt");
        if (mods.HasFlag(Avalonia.Input.KeyModifiers.Meta)) parts.Add("Super");
        parts.Add(name);
        return string.Join("+", parts);
    }

    private static Control Section(string text) => new TextBlock
    {
        Text = text.ToUpperInvariant(), FontFamily = SpaceTheme.HeadingFont, FontSize = 14, Foreground = SpaceTheme.DustBrush, Margin = new Thickness(0, 12, 0, 2),
    };

    /// <summary>Capture colors: a chip per theme and a live preview; applies and saves at once.</summary>
    private Control ThemePicker()
    {
        var s = _app.Settings;
        var stack = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
        stack.Children.Add(new TextBlock { Text = T("Capture colors"), FontSize = 14 });
        stack.Children.Add(new TextBlock { Text = T("The crosshair, frames, loupe and size tag, in the comet's colors."), FontSize = 11, Foreground = SpaceTheme.DustBrush, TextWrapping = TextWrapping.Wrap });
        var preview = new ThemePreview { Theme = Cometsnap.Core.Settings.CaptureThemes.Get(s.CaptureTheme) };
        var wrap = new WrapPanel();
        var chips = new List<(Border Chip, Cometsnap.Core.Settings.CaptureTheme Theme)>();
        void Paint()
        {
            foreach (var (chip, t) in chips)
            {
                var on = t.Id == Cometsnap.Core.Settings.CaptureThemes.Get(s.CaptureTheme).Id;
                chip.BorderBrush = on ? new ImmutableSolidColorBrush(CaptureLook.ToColor(t.Accent)) : SpaceTheme.NavyLightBrush;
                chip.BorderThickness = new Thickness(on ? 2 : 1);
                chip.Background = on ? SpaceTheme.NavyLightBrush : SpaceTheme.NavyBrush;
            }
        }
        foreach (var theme in Cometsnap.Core.Settings.CaptureThemes.All)
        {
            var t = theme;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
            row.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = new ImmutableSolidColorBrush(CaptureLook.ToColor(t.Accent)), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = CaptureLook.Name(t), FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            var chip = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 4, 12, 4), Margin = new Thickness(0, 0, 6, 6), Child = row, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
            chip.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                s.CaptureTheme = t.Id;
                CaptureLook.Use(t.Id);
                _app.SaveSettings();
                preview.Theme = t;
                Paint();
            };
            chips.Add((chip, t));
            wrap.Children.Add(chip);
        }
        Paint();
        stack.Children.Add(wrap);
        stack.Children.Add(preview);
        return stack;
    }

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

/// <summary>A small fake capture screen in the chosen colors: dimmed desktop, selection with glow, size tag and loupe.</summary>
internal sealed class ThemePreview : Control
{
    private Cometsnap.Core.Settings.CaptureTheme _theme = Cometsnap.Core.Settings.CaptureThemes.Default;

    public Cometsnap.Core.Settings.CaptureTheme Theme
    {
        get => _theme;
        set { _theme = value; InvalidateVisual(); }
    }

    public ThemePreview()
    {
        Width = 460;
        Height = 132;
        HorizontalAlignment = HorizontalAlignment.Left;
    }

    public override void Render(DrawingContext dc)
    {
        var accent = CaptureLook.ToColor(_theme.Accent);
        var accentBrush = new ImmutableSolidColorBrush(accent);
        var b = new Rect(Bounds.Size);
        using var clip = dc.PushClip(new RoundedRect(b, 10));
        dc.FillRectangle(SpaceTheme.NebulaBrush, b);
        void Win(Rect r, Color bar)
        {
            dc.DrawRectangle(new ImmutableSolidColorBrush(Color.FromRgb(0x2A, 0x3C, 0x8F)), null, r, 4, 4);
            dc.DrawRectangle(new ImmutableSolidColorBrush(bar), null, new Rect(r.X, r.Y, r.Width, 12), 4, 4);
        }
        Win(new Rect(22, 18, 180, 96), Color.FromRgb(0x5E, 0x4C, 0x4A));
        dc.FillRectangle(SpaceTheme.WithAlpha(SpaceTheme.Void, 0x8C), b);
        var sel = new Rect(140, 34, 210, 84);
        Win(sel, Color.FromRgb(0x1F, 0x25, 0x59));
        dc.DrawRectangle(new ImmutablePen(SpaceTheme.WithAlpha(accent, 0x55), 6), sel.Inflate(3));
        dc.DrawRectangle(new ImmutablePen(accentBrush, 2), sel.Inflate(1));
        dc.DrawLine(new ImmutablePen(SpaceTheme.WithAlpha(accent, 0x70), 1), new Point(0, 100.5), new Point(b.Width, 100.5));
        dc.DrawLine(new ImmutablePen(SpaceTheme.WithAlpha(accent, 0x70), 1), new Point(300.5, 0), new Point(300.5, b.Height));
        var tagText = new FormattedText("1280 × 720", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(SpaceTheme.UiFont, FontStyle.Normal, FontWeight.SemiBold), 11, new ImmutableSolidColorBrush(CaptureLook.ToColor(_theme.TagText)));
        var tag = new Rect(sel.X, sel.Y - tagText.Height - 10, tagText.Width + 12, tagText.Height + 4);
        dc.DrawRectangle(new ImmutableSolidColorBrush(CaptureLook.ToColor(_theme.Tag)), null, tag, 4, 4);
        dc.DrawText(tagText, new Point(tag.X + 6, tag.Y + 2));
        var loupe = new Rect(b.Width - 100, 12, 88, 108);
        dc.DrawRectangle(SpaceTheme.WithAlpha(SpaceTheme.Void, 0xF0), new ImmutablePen(accentBrush, 2), loupe, 8, 8);
        const double cell = 15;
        for (var i = 0; i < 5; i++)
        for (var j = 0; j < 5; j++)
        {
            var v = (byte)((i * 37 + j * 53) % 40);
            var c = i == 2 && j == 2 ? accent : Color.FromRgb((byte)(0x22 + v), (byte)(0x30 + v), (byte)(0x72 + v));
            dc.FillRectangle(new ImmutableSolidColorBrush(c), new Rect(loupe.X + 6.5 + i * cell, loupe.Y + 6.5 + j * cell, cell, cell));
        }
        dc.DrawRectangle(new ImmutablePen(SpaceTheme.SunBrush, 1.5), new Rect(loupe.X + 6.5 + 2 * cell, loupe.Y + 6.5 + 2 * cell, cell, cell));
        var hex = new FormattedText(Cometsnap.Core.Settings.CaptureThemes.Hex(_theme.Accent), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("DejaVu Sans Mono, monospace")), 11, SpaceTheme.StarWhiteBrush);
        dc.DrawText(hex, new Point(loupe.X + 14, loupe.Bottom - 20));
    }
}
