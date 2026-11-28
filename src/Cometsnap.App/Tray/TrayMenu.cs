using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Cometsnap.Video;
using Cometsnap.Core.Localization;

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
        Rebuild();
    }

    public void Rebuild()
    {
        var capture = _app.Capture;
        _menu.Items.Clear();

        var captureMenu = new MenuItem { Header = L.T("Capture") };
        captureMenu.Items.Add(Item(L.T("Region / window"), Later(capture.StartRegion), _app.RegionHotkeyText));
        captureMenu.Items.Add(Item(L.T("Fullscreen (monitor under the mouse)"), Later(capture.CaptureFullscreen), _app.Settings.FullscreenHotkey));
        captureMenu.Items.Add(Item(L.T("Scrolling capture"), Later(_app.Scrolling.Start), _app.Settings.ScrollingHotkey));
        captureMenu.Items.Add(Item(L.T("Copy text from the screen"), Later(capture.StartTextCapture), _app.Settings.TextHotkey));
        captureMenu.Items.Add(Item(L.T("Scan a QR code"), Later(capture.StartQrCapture), _app.Settings.QrHotkey));
        captureMenu.Items.Add(Item(L.T("Pick a color"), Later(capture.StartColorPicker), _app.Settings.ColorHotkey));
        var delayed = new MenuItem { Header = L.T("Delayed"), ToolTip = L.T("Capture after 3, 5 or 10 seconds: time to open a menu or hover something first") };
        foreach (var s in new[] { 3, 5, 10 })
            delayed.Items.Add(Item(L.F("{0} seconds", s), () => capture.StartDelayed(s)));
        captureMenu.Items.Add(delayed);

        var recorder = _app.Recorder;
        if (recorder.IsRecording) _menu.Items.Add(Item(L.T("Stop recording"), recorder.Stop, _app.Settings.RecordHotkey));
        _menu.Items.Add(Item(L.T("Capture region / window"), Later(capture.StartRegion), _app.RegionHotkeyText));
        _menu.Items.Add(captureMenu);
        _menu.Items.Add(RecordMenu());
        _menu.Items.Add(RecentMenu());
        _menu.Items.Add(new Separator());
        _menu.Items.Add(Item(L.T("Open captures folder"), _app.OpenCapturesFolder));
        _menu.Items.Add(OptionsMenu());
        _menu.Items.Add(Item(L.T("Replace your old screen capture…"), Later(_app.OfferReplace)));
        _menu.Items.Add(Item(L.T("Settings…"), _app.OpenSettings));
        _menu.Items.Add(Item(L.T("About Cometsnap"), ShowAbout));
        _menu.Items.Add(new Separator());
        _menu.Items.Add(Item(L.T("Quit"), _app.Quit));
    }

    private MenuItem RecordMenu()
    {
        var r = _app.Recorder;
        var s = _app.Settings;
        var record = new MenuItem { Header = L.T("Record") };
        record.Items.Add(Item(L.T("Region / window (MP4)"), Later(() => r.ToggleRegion(RecordingFormat.Mp4)), s.RecordHotkey));
        record.Items.Add(Item(L.T("Fullscreen (MP4)"), Later(() => r.ToggleFullscreen(RecordingFormat.Mp4))));
        record.Items.Add(Item(L.T("Region / window (GIF)"), Later(() => r.ToggleRegion(RecordingFormat.Gif)), s.RecordGifHotkey));
        record.Items.Add(new Separator());
        record.Items.Add(Toggle(L.T("Record PC sound"), s.RecordSystemAudio, v => s.RecordSystemAudio = v));
        record.Items.Add(Toggle(L.T("Record microphone"), s.RecordMicrophone, v => s.RecordMicrophone = v));
        record.Items.Add(Toggle(L.T("Show the mouse cursor"), s.RecordShowCursor, v => s.RecordShowCursor = v));
        var fps = new MenuItem { Header = L.T("Frame rate") };
        foreach (var f in new[] { 15, 30, 60 })
        {
            var rate = f;
            var item = new MenuItem { Header = $"{f} fps", IsCheckable = true, IsChecked = s.RecordFramesPerSecond == f };
            item.Click += (_, _) => { s.RecordFramesPerSecond = rate; _app.TrySaveSettings(); };
            fps.Items.Add(item);
        }
        record.Items.Add(fps);
        record.IsEnabled = !r.IsRecording;
        return record;
    }

    private MenuItem OptionsMenu()
    {
        var s = _app.Settings;
        var options = new MenuItem { Header = L.T("Options") };
        options.Items.Add(Toggle(L.T("Start with Windows"), s.StartWithWindows, v =>
        {
            s.StartWithWindows = v;
            _app.ApplyStartWithWindows(userAsked: true);
        }));
        options.Items.Add(LanguageMenu());
        options.Items.Add(new Separator());
        options.Items.Add(Toggle(L.T("Instant capture (no toolbar)"), s.InstantCapture, v => s.InstantCapture = v));
        options.Items.Add(Toggle(L.T("Show the Edit card after a capture"), s.ShowEditCard, v => s.ShowEditCard = v));
        options.Items.Add(Toggle(L.T("Comet animation"), s.ShowCometAnimation, v => s.ShowCometAnimation = v));
        options.Items.Add(Toggle(L.T("Little celebrations (snap milestones)"), s.Celebrations, v => s.Celebrations = v));
        options.Items.Add(Toggle(L.T("Seasonal looks (snowy December, Halloween pumpkin)"), s.SeasonalSkins, v =>
        {
            s.SeasonalSkins = v;
            _app.ApplySeason();
        }));
        options.Items.Add(Toggle(L.T("Sound on capture"), s.PlaySounds, v => s.PlaySounds = v));
        options.Items.Add(Toggle(L.T("Detect windows under the mouse"), s.WindowDetection, v => s.WindowDetection = v));
        return options;
    }

    /// <summary>Language: follow Windows, or pick one. Applies right away.</summary>
    private MenuItem LanguageMenu()
    {
        var current = _app.Settings.Language ?? "";
        var menu = new MenuItem { Header = L.T("Language") + "  ·  Language" };
        if (L.Language == "en") menu.Header = "Language";
        void Add(string header, string code)
        {
            var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = current == code };
            item.Click += (_, _) => _app.SetLanguage(code);
            menu.Items.Add(item);
        }
        Add(L.F("Same as Windows ({0})", L.Languages.First(l => l.Code == L.FromCulture(System.Globalization.CultureInfo.CurrentUICulture)).Name), "");
        menu.Items.Add(new Separator());
        foreach (var (code, name) in L.Languages) Add(name, code);
        return menu;
    }

    private MenuItem Toggle(string header, bool value, Action<bool> set)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = value };
        item.Click += (_, _) =>
        {
            set(item.IsChecked);
            _app.TrySaveSettings();
        };
        return item;
    }

    private MenuItem RecentMenu()
    {
        var recent = new MenuItem { Header = L.T("Recent") };
        var items = _app.Recent?.Snapshot().Take(10).ToList() ?? new List<string>();
        if (items.Count == 0)
        {
            recent.Items.Add(new MenuItem { Header = L.T("(no captures yet)"), IsEnabled = false });
            return recent;
        }
        foreach (var path in items)
        {
            var p = path;
            var isRecording = Cometsnap.Core.Storage.MediaKinds.IsRecording(p);
            var item = Item(System.IO.Path.GetFileNameWithoutExtension(p) + (isRecording ? $"  ({System.IO.Path.GetExtension(p).TrimStart('.').ToUpperInvariant()})" : ""),
                () =>
                {
                    if (!isRecording) { _app.OpenInEditor(p); return; }
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(p) { UseShellExecute = true }); }
                    catch (Exception ex) { Cometsnap.Core.Diagnostics.Log.Error("Could not play the recording", ex); }
                });
            item.ToolTip = isRecording ? L.T("Click to play") : L.T("Click to edit");
            recent.Items.Add(item);
        }
        return recent;
    }

    public void Open()
    {
        _menu.IsOpen = false;
        _menu.IsOpen = true;
    }

    /// <summary>Lets the menu close and repaint before the screen is grabbed.</summary>
    private Action Later(Action action) => () =>
    {
        _menu.IsOpen = false;
        _ = _menu.Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(180);
            action();
        });
    };

    private static MenuItem Item(string header, Action action, string? gesture = null)
    {
        var item = new MenuItem { Header = header, InputGestureText = gesture ?? "" };
        item.Click += (_, _) => action();
        return item;
    }

    private static void ShowAbout() =>
        MessageBox.Show(
            $"Cometsnap {CometApp.Version}\n\n" + L.T("A free, fun and snappy screenshot tool for Windows.") + "\n" + L.T("Open source (MIT).") + " github.com/mike-risky/cometsnap",
            L.T("About Cometsnap"), MessageBoxButton.OK, MessageBoxImage.Information);
}
