using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Cometsnap.Video;

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

        var captureMenu = new MenuItem { Header = "Capture" };
        captureMenu.Items.Add(Item("Region / window", Later(capture.StartRegion), _app.RegionHotkeyText));
        captureMenu.Items.Add(Item("Fullscreen (monitor under the mouse)", Later(capture.CaptureFullscreen), _app.Settings.FullscreenHotkey));
        var delayed = new MenuItem { Header = "Delayed" };
        foreach (var s in new[] { 3, 5, 10 })
            delayed.Items.Add(Item($"{s} seconds", () => capture.StartDelayed(s)));
        captureMenu.Items.Add(delayed);

        var recorder = _app.Recorder;
        if (recorder.IsRecording) _menu.Items.Add(Item("Stop recording", recorder.Stop, _app.Settings.RecordHotkey));
        _menu.Items.Add(Item("Capture region / window", Later(capture.StartRegion), _app.RegionHotkeyText));
        _menu.Items.Add(captureMenu);
        _menu.Items.Add(RecordMenu());
        _menu.Items.Add(RecentMenu());
        _menu.Items.Add(new Separator());
        _menu.Items.Add(Item("Open captures folder", _app.OpenCapturesFolder));
        _menu.Items.Add(OptionsMenu());
        _menu.Items.Add(Item("Replace your old screen capture…", Later(_app.OfferReplace)));
        _menu.Items.Add(Item("Settings file…", _app.OpenSettingsFile));
        _menu.Items.Add(Item("About Cometsnap", ShowAbout));
        _menu.Items.Add(new Separator());
        _menu.Items.Add(Item("Quit", _app.Quit));
    }

    private MenuItem RecordMenu()
    {
        var r = _app.Recorder;
        var s = _app.Settings;
        var record = new MenuItem { Header = "Record" };
        record.Items.Add(Item("Region / window (MP4)", Later(() => r.ToggleRegion(RecordingFormat.Mp4)), s.RecordHotkey));
        record.Items.Add(Item("Fullscreen (MP4)", Later(() => r.ToggleFullscreen(RecordingFormat.Mp4))));
        record.Items.Add(Item("Region / window (GIF)", Later(() => r.ToggleRegion(RecordingFormat.Gif)), s.RecordGifHotkey));
        record.Items.Add(new Separator());
        record.Items.Add(Toggle("Record PC sound", s.RecordSystemAudio, v => s.RecordSystemAudio = v));
        record.Items.Add(Toggle("Record microphone", s.RecordMicrophone, v => s.RecordMicrophone = v));
        record.Items.Add(Toggle("Show the mouse cursor", s.RecordShowCursor, v => s.RecordShowCursor = v));
        var fps = new MenuItem { Header = "Frame rate" };
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
        var options = new MenuItem { Header = "Options" };
        options.Items.Add(Toggle("Instant capture (no toolbar)", s.InstantCapture, v => s.InstantCapture = v));
        options.Items.Add(Toggle("Show the Edit card after a capture", s.ShowEditCard, v => s.ShowEditCard = v));
        options.Items.Add(Toggle("Comet animation", s.ShowCometAnimation, v => s.ShowCometAnimation = v));
        options.Items.Add(Toggle("Sound on capture", s.PlaySounds, v => s.PlaySounds = v));
        options.Items.Add(Toggle("Detect windows under the mouse", s.WindowDetection, v => s.WindowDetection = v));
        return options;
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
        var recent = new MenuItem { Header = "Recent" };
        var items = _app.Recent?.Snapshot().Take(10).ToList() ?? new List<string>();
        if (items.Count == 0)
        {
            recent.Items.Add(new MenuItem { Header = "(no captures yet)", IsEnabled = false });
            return recent;
        }
        foreach (var path in items)
        {
            var p = path;
            var item = Item(System.IO.Path.GetFileNameWithoutExtension(p), () => _app.OpenInEditor(p));
            item.ToolTip = "Click to edit";
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
            $"Cometsnap {CometApp.Version}\n\nA free, fun and snappy screenshot tool for Windows.\nMIT licensed. github.com/mike-risky/cometsnap",
            "About Cometsnap", MessageBoxButton.OK, MessageBoxImage.Information);
}
