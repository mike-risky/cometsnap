using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Cometsnap.Linux.Core;
using Cometsnap.Linux.Platform;
using Cometsnap.Linux.Theme;
using PixelRect = Cometsnap.Linux.Core.PixelRect;

namespace Cometsnap.Linux.Trimming;

/// <summary>
/// The small video editor: trim the start and end of a recording and cut parts out of the middle.
/// Works on MP4 and GIF. Save writes over the recording and keeps the untouched original in the
/// hidden .cometsnap folder, so the trimmer can always re-open with every cut still adjustable.
/// </summary>
public sealed class TrimWindow : Window
{
    private const string ScissorsIcon =
        "M5,4.5 m-2,0 a2,2 0 1 0 4,0 a2,2 0 1 0 -4,0 M5,11.5 m-2,0 a2,2 0 1 0 4,0 a2,2 0 1 0 -4,0 M6.6,5.6 L14,12 M6.6,10.4 L14,4";
    private const string PlayIcon = "M5,3 L13,8 L5,13 Z";
    private const string PauseIcon = "M4.5,3 V13 M11.5,3 V13";

    private readonly CometApp _app;
    private readonly string _path;      // the recording (what Save writes)
    private readonly string _source;    // what is shown and trimmed (the original, once trimmed before)
    private readonly bool _gif;
    private readonly IClipPreview _preview;
    private readonly TrimTimeline _timeline;
    private readonly TextBlock _time;
    private readonly TextBlock _status;
    private readonly TextBlock _hint;
    private readonly DispatcherTimer _hintTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _tick = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly Border _playButton;
    private readonly Border _cutButton;
    private readonly Border _busy;
    private readonly ProgressBar _busyBar;
    private readonly Border _confirm;
    private readonly List<CutList> _undo = new();
    private readonly List<CutList> _redo = new();
    private CutList _saved;
    private CutList _cuts;
    private bool _working;
    private bool _closingAfterSave;
    private bool _previewGone;
    private bool _playButtonShowsPause;
    private readonly Avalonia.Media.Imaging.Bitmap? _poster;

    private TrimWindow(CometApp app, string path, string source, CutList cuts, IClipPreview preview, int width, int height)
    {
        _app = app;
        _path = path;
        _source = source;
        _gif = MediaKinds.IsGif(path);
        _cuts = cuts;
        _saved = cuts.Clone();
        _preview = preview;
        _poster = app.Thumbs.TryGet(path);

        Title = TitleText();
        Background = SpaceTheme.VoidBrush;
        Foreground = SpaceTheme.StarWhiteBrush;
        FontFamily = SpaceTheme.UiFont;
        Icon = new WindowIcon(SpaceTheme.Logo);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        MinWidth = 640;
        MinHeight = 420;

        // ---- top bar
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock
        {
            Text = "COMETSNAP", FontFamily = SpaceTheme.HeadingFont, FontSize = 16, Foreground = SpaceTheme.SunBrush,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 2, 0),
        });
        left.Children.Add(new TextBlock
        {
            Text = T("TRIM"), FontFamily = SpaceTheme.HeadingFont, FontSize = 12, Foreground = SpaceTheme.DustBrush,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 12, 0),
        });
        left.Children.Add(Ui.ActionButton(T("Undo"), "M5.5,4 L2.5,7 L5.5,10 M2.5,7 H10 A3.5,3.5 0 0 1 10,14 H7", T("Undo (Ctrl+Z)"), Undo));
        left.Children.Add(Ui.ActionButton(T("Redo"), "M10.5,4 L13.5,7 L10.5,10 M13.5,7 H6 A3.5,3.5 0 0 0 6,14 H9", T("Redo (Ctrl+Y)"), Redo));
        left.Children.Add(Ui.Divider(vertical: true));
        left.Children.Add(Ui.ActionButton(T("Reset"), "M3,8 A5,5 0 1 0 5,4 M3,2.5 V5.5 H6", T("Keep the whole recording again"), ResetAll));

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(Ui.ActionButton(T("Save as…"), null, T("Save the trimmed copy somewhere else (Ctrl+Shift+S)"), () => _ = SaveAsAsync()));
        right.Children.Add(Ui.ActionButton(T("Save"), "M8,2.5 V10 M4.5,6.5 L8,10 L11.5,6.5 M2.5,13.5 H13.5", T("Save and close (Ctrl+S)"), () => _ = SaveAndCloseAsync()));
        right.Children.Add(Ui.ActionButton(T("Copy & close"), "M5.5,5.5 H13.5 V13.5 H5.5 Z M2.5,10.5 V2.5 H10.5",
            T("Save, copy the file to the clipboard, and close (Ctrl+C or Ctrl+Enter)"), () => _ = CopyAndCloseAsync(), primary: true));

        var top = new DockPanel { Margin = new Thickness(12, 8, 12, 8) };
        DockPanel.SetDock(right, Dock.Right);
        top.Children.Add(right);
        top.Children.Add(new ScrollViewer { Content = left, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        var topBorder = new Border { Child = top, Background = SpaceTheme.VoidBrush, BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(0, 0, 0, 1) };

        // ---- preview
        var view = preview.View;
        view.HorizontalAlignment = HorizontalAlignment.Center;
        view.VerticalAlignment = VerticalAlignment.Center;
        var screen = new Border
        {
            Child = view, Background = Brushes.Black, Margin = new Thickness(24, 20, 24, 10),
            BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1),
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 28, Color = Color.FromArgb(0xA0, 0, 0, 0) }),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(screen, T("Click to play or pause (Space)"));
        screen.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) TogglePlay(); };
        preview.Failed += message =>
        {
            Log.Error("Trim preview failed: " + message);
            Flash(T("This video can’t be previewed here, but trimming still works."));
        };

        // ---- transport: play, time, start/end, cut
        _playButton = Ui.ActionButton(T("Play"), PlayIcon, T("Play or pause (Space)"), TogglePlay);
        _time = new TextBlock
        {
            FontSize = 13, Foreground = SpaceTheme.StarWhiteBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
            FontFamily = new FontFamily("DejaVu Sans Mono, Liberation Mono, Noto Sans Mono, monospace"),
        };
        _cutButton = Ui.ActionButton(T("Cut out"), ScissorsIcon, T("Remove the selected part (Del)"), CutSelection, primary: true);
        _cutButton.Opacity = 0.45;
        var setIn = Ui.ActionButton(T("Start here"), "M4,3 V13 M7,8 H13 M10,5 L13,8 L10,11", T("Trim the start to the playhead (I)"), () => SetTrim(start: true));
        var setOut = Ui.ActionButton(T("End here"), "M12,3 V13 M9,8 H3 M6,5 L3,8 L6,11", T("Trim the end to the playhead (O)"), () => SetTrim(start: false));
        var transportRight = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        transportRight.Children.Add(setIn);
        transportRight.Children.Add(setOut);
        transportRight.Children.Add(_cutButton);
        var transport = new DockPanel { Margin = new Thickness(24, 0, 24, 4) };
        DockPanel.SetDock(transportRight, Dock.Right);
        transport.Children.Add(transportRight);
        var transportLeft = new StackPanel { Orientation = Orientation.Horizontal };
        transportLeft.Children.Add(_playButton);
        transportLeft.Children.Add(_time);
        transport.Children.Add(transportLeft);

        _timeline = new TrimTimeline { Cuts = _cuts, Margin = new Thickness(10, 6, 10, 8) };
        _timeline.SeekRequested += t => { _preview.Position = t; ShowPosition(t); };
        _timeline.CutsChanged += Changed;
        _timeline.SelectionChanged += s => { _cutButton.Opacity = s is null ? 0.45 : 1; UpdateStatus(); };

        var center = new Grid { RowDefinitions = new RowDefinitions("*,Auto,Auto") };
        Grid.SetRow(screen, 0);
        Grid.SetRow(transport, 1);
        Grid.SetRow(_timeline, 2);
        center.Children.Add(screen);
        center.Children.Add(transport);
        center.Children.Add(_timeline);

        // ---- footer
        _status = new TextBlock { Foreground = SpaceTheme.DustBrush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _hint = new TextBlock { Foreground = SpaceTheme.SunBrush, FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Background = SpaceTheme.VoidBrush, Padding = new Thickness(10, 0, 0, 0) };
        var footer = new Grid { Margin = new Thickness(14, 6, 14, 8) };
        footer.Children.Add(_status);
        footer.Children.Add(_hint);
        _hintTimer.Tick += (_, _) => { _hintTimer.Stop(); _hint.Text = ""; };
        var bottom = new Border { Child = footer, Background = SpaceTheme.VoidBrush, BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(0, 1, 0, 0) };

        // ---- busy overlay while the trimmed file is written
        _busyBar = new ProgressBar { Width = 260, Height = 6, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 12, 0, 0), Foreground = SpaceTheme.CometBlueBrush, Background = SpaceTheme.NavyBrush };
        var busyStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        busyStack.Children.Add(new TextBlock { Text = T("Trimming…"), FontFamily = SpaceTheme.HeadingFont, FontSize = 18, Foreground = SpaceTheme.SunBrush, HorizontalAlignment = HorizontalAlignment.Center });
        busyStack.Children.Add(_busyBar);
        _busy = new Border { Background = SpaceTheme.WithAlpha(SpaceTheme.Void, 0xD8), Child = busyStack, IsVisible = false };

        _confirm = BuildConfirm();

        var stage = new Grid { Background = SpaceTheme.NebulaBrush };
        stage.Children.Add(center);
        stage.Children.Add(_confirm);
        var dock = new DockPanel();
        DockPanel.SetDock(topBorder, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(topBorder);
        dock.Children.Add(bottom);
        dock.Children.Add(stage);
        var root = new Grid();
        root.Children.Add(dock);
        root.Children.Add(_busy);
        Content = root;

        _tick.Tick += (_, _) => OnTick();
        _tick.Start();
        KeyDown += OnKeyDown;
        Closing += OnClosing;
        Closed += (_, _) => { _tick.Stop(); ReleasePreview(); };

        SizeTo(width, height);
        _preview.Position = _cuts.TrimStart;
        ShowPosition(_cuts.TrimStart);
        UpdateStatus();
        Opened += (_, _) => Flash(T("Drag the yellow handles to trim, or drag across the timeline and press Del to cut"));
    }

    /// <summary>Opens a recording in the trimmer (null, with a message, when it can't be read).</summary>
    public static TrimWindow? Open(CometApp app, string path)
    {
        try
        {
            if (!File.Exists(path)) throw new FileNotFoundException(T("The recording is no longer there."), path);
            var original = EditSidecar.OriginalRecordingFor(path);
            var source = File.Exists(original) ? original : path;
            CutList cuts;
            IClipPreview preview;
            int width, height;
            if (MediaKinds.IsGif(path))
            {
                var reader = GifReader.Open(source);
                cuts = new CutList(reader.Duration);
                preview = new GifPreview(reader);
                (width, height) = (reader.Width, reader.Height);
            }
            else
            {
                var ffmpeg = Tools.Ffmpeg ?? throw new InvalidOperationException(T("Trimming needs ffmpeg. Install it (for example: sudo apt install ffmpeg) and try again."));
                var info = RecordingTrimmer.Probe(source);
                cuts = new CutList(info.Duration);
                preview = new VideoPreview(source, ffmpeg, info);
                (width, height) = info.Width > 0 && info.Height > 0 ? (info.Width, info.Height) : (1280, 720);
            }
            var saved = EditSidecar.CutsFor(path);
            if (source == original && File.Exists(saved))
                cuts = CutList.FromJson(File.ReadAllText(saved), cuts.Duration);
            if (cuts.Duration <= CutList.MinPiece)
            {
                preview.Dispose();
                throw new InvalidDataException(T("This recording is too short to trim."));
            }
            return new TrimWindow(app, path, source, cuts, preview, width, height);
        }
        catch (Exception ex)
        {
            Log.Error("Could not open the trimmer", ex);
            app.Toast(T("Could not open this recording:") + " " + ex.Message, 6000);
            return null;
        }
    }

    private void SizeTo(int w, int h)
    {
        var p = X11.PointerPosition() ?? (0, 0);
        var (work, scaling) = _app.WorkAreaAt(p.X, p.Y);
        var areaW = work.Width / scaling;
        var areaH = work.Height / scaling;
        var scale = Math.Min(1, Math.Min((areaW * 0.85 - 48) / Math.Max(1, w), (areaH * 0.85 - 260) / Math.Max(1, h)));
        Width = Math.Min(Math.Max(w * scale + 48, 1060), areaW * 0.94);
        Height = Math.Min(Math.Max(h * scale + 260, 480), areaH * 0.92);
    }

    private string TitleText() => (Dirty ? "• " : "") + Path.GetFileName(_path) + " · " + T("Trim") + " · Cometsnap";

    private bool Dirty => !_cuts.SameAs(_saved);

    // ------------------------------------------------------------------ playback

    private void TogglePlay()
    {
        if (_working) return;
        if (_preview.IsPlaying)
        {
            _preview.Pause();
        }
        else
        {
            var at = _preview.Position;
            if (at >= _cuts.TrimEnd - TimeSpan.FromMilliseconds(50) || _cuts.NextKept(at) is null) at = _cuts.TrimStart;
            at = _cuts.NextKept(at) ?? _cuts.TrimStart;
            _preview.Position = at;
            _preview.Play();
        }
        UpdatePlayButton();
    }

    private void OnTick()
    {
        if (_working) return;
        var at = _preview.Position;
        if (_preview.IsPlaying)
        {
            // Jump over cut parts and stop at the trimmed end.
            var next = _cuts.NextKept(at);
            if (next is null || at >= _cuts.TrimEnd)
            {
                _preview.Pause();
                _preview.Position = _cuts.TrimEnd;
                at = _cuts.TrimEnd;
                UpdatePlayButton();
            }
            else if (next.Value - at > TimeSpan.FromMilliseconds(30))
            {
                _preview.Position = next.Value;
                at = next.Value;
            }
        }
        else if (_playButtonShowsPause)
        {
            UpdatePlayButton();
        }
        ShowPosition(at);
    }

    private void UpdatePlayButton()
    {
        var playing = _preview.IsPlaying;
        if (playing == _playButtonShowsPause) return;
        _playButtonShowsPause = playing;
        if (_playButton.Child is StackPanel row && row.Children.Count > 1)
        {
            row.Children[0] = Ui.Icon(playing ? PauseIcon : PlayIcon, 16);
            if (row.Children[1] is TextBlock label) label.Text = playing ? T("Pause") : T("Play");
        }
    }

    private void ShowPosition(TimeSpan at)
    {
        _timeline.Position = at;
        _time.Text = CutList.Label(_cuts.ToOutput(at)) + " / " + CutList.Label(_cuts.KeptLength);
    }

    private void Seek(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t > _cuts.Duration) t = _cuts.Duration;
        _preview.Position = t;
        ShowPosition(t);
    }

    // ------------------------------------------------------------------ editing

    private void Changed(CutList before)
    {
        _undo.Add(before);
        _redo.Clear();
        AfterChange();
    }

    private void AfterChange()
    {
        _timeline.Cuts = _cuts;
        Title = TitleText();
        UpdateStatus();
        ShowPosition(_preview.Position);
    }

    private void CutSelection()
    {
        if (_timeline.Selection is not { } s)
        {
            Flash(T("First drag across the timeline to select the part to cut"));
            return;
        }
        var before = _cuts.Clone();
        if (!_cuts.AddCut(s.Start, s.End))
        {
            Flash(s.Length < CutList.MinPiece ? T("That part is too short to cut") : T("Something has to stay: you can't cut everything"));
            return;
        }
        _timeline.Selection = null;
        Changed(before);
        Seek(_cuts.NextKept(s.Start) ?? _cuts.TrimStart);
        Flash(F("Cut out {0}", CutList.Label(s.Length)));
    }

    private void SetTrim(bool start)
    {
        var before = _cuts.Clone();
        var at = _preview.Position;
        if (start) _cuts.TrimStart = at; else _cuts.TrimEnd = at;
        if (_cuts.SameAs(before)) return;
        Changed(before);
        Flash(start ? F("Starts at {0}", CutList.Label(_cuts.TrimStart)) : F("Ends at {0}", CutList.Label(_cuts.TrimEnd)));
    }

    private void ResetAll()
    {
        if (_cuts.IsUntouched) return;
        var before = _cuts.Clone();
        _cuts = new CutList(_cuts.Duration);
        _timeline.Selection = null;
        Changed(before);
        Flash(T("The whole recording is back"));
    }

    private void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Add(_cuts);
        _cuts = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _timeline.Selection = null;
        AfterChange();
    }

    private void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Add(_cuts);
        _cuts = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        AfterChange();
    }

    private void UpdateStatus()
    {
        var parts = new List<string>
        {
            _cuts.IsUntouched ? F("{0} · whole recording", CutList.Label(_cuts.Duration)) : F("Keeps {0} of {1}", CutList.Label(_cuts.KeptLength), CutList.Label(_cuts.Duration)),
        };
        if (_cuts.Cuts.Count == 1) parts.Add(T("1 part cut out"));
        else if (_cuts.Cuts.Count > 1) parts.Add(F("{0} parts cut out", _cuts.Cuts.Count));
        if (_timeline.Selection is { } s) parts.Add(F("Selected {0}: press Del to cut it", CutList.Label(s.Length)));
        parts.Add(_gif ? "GIF" : "MP4");
        _status.Text = string.Join("  ·  ", parts);
    }

    private void Flash(string text)
    {
        _hint.Text = text;
        _hintTimer.Stop();
        _hintTimer.Start();
    }

    // ------------------------------------------------------------------ keys

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_working) { e.Handled = true; return; }
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var step = shift ? TimeSpan.FromSeconds(1) : _gif ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromMilliseconds(1000.0 / 30);
        e.Handled = true;
        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.Z when shift: Redo(); return;
                case Key.Z: Undo(); return;
                case Key.Y: Redo(); return;
                case Key.C or Key.Enter: _ = CopyAndCloseAsync(); return;
                case Key.S when shift: _ = SaveAsAsync(); return;
                case Key.S: _ = SaveAndCloseAsync(); return;
            }
            e.Handled = false;
            return;
        }
        switch (e.Key)
        {
            case Key.Space or Key.K: TogglePlay(); return;
            case Key.Left: _preview.Pause(); UpdatePlayButton(); Seek(_preview.Position - step); return;
            case Key.Right: _preview.Pause(); UpdatePlayButton(); Seek(_preview.Position + step); return;
            case Key.Home: Seek(_cuts.TrimStart); return;
            case Key.End: Seek(_cuts.TrimEnd); return;
            case Key.I: SetTrim(start: true); return;
            case Key.O: SetTrim(start: false); return;
            case Key.Delete or Key.Back or Key.X: CutSelection(); return;
            case Key.Escape:
                if (_confirm.IsVisible) _confirm.IsVisible = false;
                else if (_timeline.Selection is not null) _timeline.Selection = null;
                else Close();
                return;
        }
        e.Handled = false;
    }

    // ------------------------------------------------------------------ saving

    /// <summary>Writes the trimmed recording over the recording (the original is kept hidden).</summary>
    private async Task<bool> SaveAsync()
    {
        if (!Dirty) return true;
        _preview.Pause();
        UpdatePlayButton();
        try
        {
            var original = EditSidecar.OriginalRecordingFor(_path);
            var cutsFile = EditSidecar.CutsFor(_path);
            if (_cuts.IsUntouched)
            {
                // Back to the untouched recording.
                if (_source == original && File.Exists(original))
                {
                    ReleasePreview();
                    File.Move(original, _path, overwrite: true);
                }
                if (File.Exists(cutsFile)) File.Delete(cutsFile);
            }
            else
            {
                EditSidecar.EnsureFolder(_path);
                // Written in the hidden folder, so the recent list never sees a half-written file.
                var temp = Path.Combine(EditSidecar.FolderFor(_path), Path.GetFileNameWithoutExtension(_path) + ".trimming" + Path.GetExtension(_path));
                if (!await ExportAsync(temp)) return false;
                ReleasePreview();
                if (_source == _path) File.Move(_path, original, overwrite: true);
                File.Move(temp, _path, overwrite: true);
                File.WriteAllText(cutsFile, _cuts.ToJson());
            }
            _saved = _cuts.Clone();
            Title = TitleText();
            _app.Thumbs.Forget(_path);
            _app.Recent.Add(_path);
            Log.Info($"Trimmed recording saved: {_path} ({_cuts.KeptLength.TotalSeconds:0.0} s kept)");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Trim save failed", ex);
            Flash(T("Could not save the trimmed recording:") + " " + ex.Message);
            return false;
        }
    }

    /// <summary>Writes the kept parts of the source to <paramref name="output"/>, with a progress overlay.</summary>
    private async Task<bool> ExportAsync(string output)
    {
        _working = true;
        _busy.IsVisible = true;
        _busyBar.Value = 0;
        try
        {
            var progress = new Progress<double>(p => _busyBar.Value = p);
            await RecordingTrimmer.ExportAsync(_source, output, _cuts.Clone(), progress);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Trim export failed", ex);
            try { if (File.Exists(output)) File.Delete(output); } catch { /* best effort */ }
            Flash(T("Could not save the trimmed recording:") + " " + ex.Message);
            return false;
        }
        finally
        {
            _working = false;
            _busy.IsVisible = false;
        }
    }

    private async Task SaveAndCloseAsync()
    {
        if (_working) return;
        if (!await SaveAsync()) return;
        FlyHome();
        CloseNow();
    }

    private async Task CopyAndCloseAsync()
    {
        if (_working) return;
        if (!await SaveAsync()) return;
        if (!await _app.Clipboard.SetFilesAsync(new[] { _path }))
        {
            Flash(T("Could not copy, try again"));
            return;
        }
        _app.Toast(T("Copied the file ✓"), 1500);
        FlyHome();
        CloseNow();
    }

    private async Task SaveAsAsync()
    {
        if (_working) return;
        _preview.Pause();
        UpdatePlayButton();
        var ext = Path.GetExtension(_path).TrimStart('.').ToLowerInvariant();
        IStorageFolder? folder = null;
        try { folder = await StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(_path)!); }
        catch { /* the picker starts wherever it likes */ }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = T("Save a copy"),
            SuggestedFileName = Path.GetFileNameWithoutExtension(_path) + "-trimmed." + ext,
            DefaultExtension = ext,
            SuggestedStartLocation = folder,
            FileTypeChoices = new[]
            {
                _gif
                    ? new FilePickerFileType(T("GIF animation")) { Patterns = new[] { "*.gif" } }
                    : new FilePickerFileType(T("MP4 video")) { Patterns = new[] { "*.mp4" } },
            },
        });
        if (file?.TryGetLocalPath() is not { } target) return;
        if (!target.EndsWith("." + ext, StringComparison.OrdinalIgnoreCase)) target += "." + ext;
        if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(_path), StringComparison.Ordinal))
        {
            await SaveAndCloseAsync();
            return;
        }
        if (_cuts.IsUntouched)
        {
            try { File.Copy(_source, target, overwrite: true); }
            catch (Exception ex)
            {
                Flash($"{T("Could not save:")} {ex.Message}");
                return;
            }
        }
        else if (!await ExportAsync(target))
        {
            return;
        }
        FlyHome();
        CloseNow();
    }

    /// <summary>The comet carries the recording's poster frame home.</summary>
    private void FlyHome()
    {
        try
        {
            if (_poster is not { } image) return;
            var s = RenderScaling;
            var rect = new PixelRect(Position.X, Position.Y, (int)(Bounds.Width * s), (int)(Bounds.Height * s));
            _app.LaunchComet(rect, image);
        }
        catch (Exception ex)
        {
            Log.Error("Trim comet failed", ex);
        }
    }

    private void CloseNow()
    {
        _closingAfterSave = true;
        Close();
    }

    private void ReleasePreview()
    {
        if (_previewGone) return;
        _previewGone = true;
        _preview.Dispose();
    }

    private Border BuildConfirm()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock { Text = T("Save the trimmed recording?"), FontSize = 14, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        row.Children.Add(Ui.ActionButton(T("Save"), null, T("Save and close"), () => { _confirm.IsVisible = false; _ = SaveAndCloseAsync(); }, primary: true));
        row.Children.Add(Ui.ActionButton(T("Don't save"), null, T("Close without saving"), CloseNow));
        row.Children.Add(Ui.ActionButton(T("Cancel"), null, T("Keep editing"), () => _confirm.IsVisible = false));
        return new Border
        {
            Child = row,
            IsVisible = false,
            Background = SpaceTheme.NavyBrush,
            BorderBrush = SpaceTheme.CometBlueBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 12),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 16, 0, 0),
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 18, Color = Color.FromArgb(0x90, 0, 0, 0) }),
        };
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_working) { e.Cancel = true; return; }
        if (_closingAfterSave || !Dirty) return;
        e.Cancel = true;
        _preview.Pause();
        UpdatePlayButton();
        _confirm.IsVisible = true;
    }
}
