using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Cometsnap.App.Editing;
using Cometsnap.App.Theme;
using Cometsnap.Core.Diagnostics;
using Cometsnap.Core.Localization;
using Cometsnap.Core.Native;
using Cometsnap.Core.Storage;
using Cometsnap.Video.Editing;

namespace Cometsnap.App.Trimming;

/// <summary>
/// The small video editor: trim the start and end of a recording and cut parts out of the middle.
/// Works on MP4 and GIF. Save writes over the recording and keeps the untouched original beside
/// it (hidden), so the trimmer can always re-open with every cut still adjustable.
/// </summary>
public sealed class TrimWindow : Window
{
    private static readonly string ScissorsIcon =
        "M5,4.5 m-2,0 a2,2 0 1 0 4,0 a2,2 0 1 0 -4,0 M5,11.5 m-2,0 a2,2 0 1 0 4,0 a2,2 0 1 0 -4,0 M6.6,5.6 L14,12 M6.6,10.4 L14,4";
    private static readonly string PlayIcon = "M5,3 L13,8 L5,13 Z";
    private static readonly string PauseIcon = "M4.5,3 V13 M11.5,3 V13";

    private readonly CometApp _app;
    private readonly string _path;      // the recording (what Save writes)
    private readonly string _source;    // what is shown and trimmed (the original, once trimmed before)
    private readonly bool _gif;
    private readonly IClipPreview _preview;
    private readonly TrimTimeline _timeline;
    private readonly TextBlock _time;
    private readonly TextBlock _status;
    private readonly TextBlock _hint;
    private readonly DispatcherTimer _hintTimer;
    private readonly DispatcherTimer _tick;
    private readonly Border _playButton;
    private readonly Border _cutButton;
    private readonly Grid _root;
    private readonly Border _busy;
    private readonly TextBlock _busyText;
    private readonly ProgressBar _busyBar;
    private readonly List<CutList> _undo = new();
    private readonly List<CutList> _redo = new();
    private CutList _saved;
    private CutList _cuts;
    private bool _working;
    private bool _closingAfterSave;
    private bool _previewGone;

    private TrimWindow(CometApp app, string path, string source, CutList cuts, IClipPreview preview, int width, int height)
    {
        _app = app;
        _path = path;
        _source = source;
        _gif = MediaKinds.IsGif(path);
        _cuts = cuts;
        _saved = cuts.Clone();
        _preview = preview;

        Title = TitleText();
        Background = SpaceTheme.VoidBrush;
        Foreground = SpaceTheme.StarWhiteBrush;
        FontFamily = SpaceTheme.UiFont;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        MinWidth = 640;
        MinHeight = 420;
        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/cometsnap.ico")); }
        catch { /* no icon is fine */ }

        // ---- top bar
        var top = new DockPanel { Margin = new Thickness(12, 8, 12, 8), LastChildFill = false };
        top.Children.Add(new TextBlock
        {
            Text = "COMETSNAP", FontFamily = SpaceTheme.HeadingFont, FontSize = 16, Foreground = SpaceTheme.SunBrush,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 6, 0),
        });
        top.Children.Add(new TextBlock
        {
            Text = L.T("TRIM"), FontFamily = SpaceTheme.HeadingFont, FontSize = 12, Foreground = SpaceTheme.DustBrush,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 14, 0),
        });
        top.Children.Add(EditorUi.PillButton(EditorUi.Icon("M5.5,4 L2.5,7 L5.5,10 M2.5,7 H10 A3.5,3.5 0 0 1 10,14 H7", null, 16), L.T("Undo"), L.T("Undo (Ctrl+Z)"), Undo));
        top.Children.Add(EditorUi.PillButton(EditorUi.Icon("M10.5,4 L13.5,7 L10.5,10 M13.5,7 H6 A3.5,3.5 0 0 0 6,14 H9", null, 16), L.T("Redo"), L.T("Redo (Ctrl+Y)"), Redo));
        top.Children.Add(EditorUi.Divider(vertical: true));
        top.Children.Add(EditorUi.PillButton(EditorUi.Icon("M3,8 A5,5 0 1 0 5,4 M3,2.5 V5.5 H6", null, 16), L.T("Reset"), L.T("Keep the whole recording again"), ResetAll));

        var copy = EditorUi.PillButton(EditorUi.Icon("M5.5,5.5 H13.5 V13.5 H5.5 Z M2.5,10.5 V2.5 H10.5", null, 16), L.T("Copy & close"), L.T("Save, copy the file to the clipboard, and close (Ctrl+C or Ctrl+Enter)"), () => _ = CopyAndCloseAsync(), SpaceTheme.TerracottaBrush, bold: true);
        var save = EditorUi.PillButton(EditorUi.Icon("M8,2.5 V10 M4.5,6.5 L8,10 L11.5,6.5 M2.5,13.5 H13.5", null, 16), L.T("Save"), L.T("Save and close (Ctrl+S)"), () => _ = SaveAndCloseAsync(), SpaceTheme.NavyBrush);
        var saveAs = EditorUi.PillButton(EditorUi.Icon("M2.5,2.5 H11 L13.5,5 V13.5 H2.5 Z M5,2.5 V6 H10 V2.5 M5,13.5 V9.5 H11 V13.5", null, 16), L.T("Save as…"), L.T("Save the trimmed copy somewhere else (Ctrl+Shift+S)"), () => _ = SaveAsAsync());
        DockPanel.SetDock(copy, Dock.Right);
        DockPanel.SetDock(save, Dock.Right);
        DockPanel.SetDock(saveAs, Dock.Right);
        top.Children.Add(copy);
        top.Children.Add(save);
        top.Children.Add(saveAs);

        // ---- preview
        var view = preview.View;
        if (view is FrameworkElement fe) { fe.HorizontalAlignment = HorizontalAlignment.Center; fe.VerticalAlignment = VerticalAlignment.Center; }
        var screen = new Border
        {
            Child = view, Background = Brushes.Black, Margin = new Thickness(24, 20, 24, 10),
            BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 28, ShadowDepth = 0, Opacity = 0.65, Color = Colors.Black },
            Cursor = Cursors.Hand, ToolTip = L.T("Click to play or pause (Space)"),
        };
        screen.MouseLeftButtonUp += (_, _) => TogglePlay();
        preview.Failed += message =>
        {
            Log.Error("Trim preview failed: " + message);
            Flash(L.T("Windows cannot show a preview of this video, but trimming still works."));
        };

        // ---- transport: play, time, cut
        _playButton = EditorUi.PillButton(EditorUi.Icon(PlayIcon, null, 16), L.T("Play"), L.T("Play or pause (Space)"), TogglePlay, SpaceTheme.NavyBrush, bold: true);
        _time = new TextBlock { FontSize = 13, Foreground = SpaceTheme.StarWhiteBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), FontFamily = new FontFamily("Cascadia Mono, Consolas, " + SpaceTheme.UiFont.Source) };
        _cutButton = EditorUi.PillButton(EditorUi.Icon(ScissorsIcon, null, 16), L.T("Cut out"), L.T("Remove the selected part (Del)"), CutSelection, SpaceTheme.TerracottaBrush, bold: true);
        _cutButton.Opacity = 0.45;
        var setIn = EditorUi.PillButton(EditorUi.Icon("M4,3 V13 M7,8 H13 M10,5 L13,8 L10,11", null, 16), L.T("Start here"), L.T("Trim the start to the playhead (I)"), () => SetTrim(start: true));
        var setOut = EditorUi.PillButton(EditorUi.Icon("M12,3 V13 M9,8 H3 M6,5 L3,8 L6,11", null, 16), L.T("End here"), L.T("Trim the end to the playhead (O)"), () => SetTrim(start: false));
        var transport = new DockPanel { Margin = new Thickness(24, 0, 24, 4), LastChildFill = false };
        transport.Children.Add(_playButton);
        transport.Children.Add(_time);
        DockPanel.SetDock(_cutButton, Dock.Right);
        DockPanel.SetDock(setOut, Dock.Right);
        DockPanel.SetDock(setIn, Dock.Right);
        transport.Children.Add(_cutButton);
        transport.Children.Add(setOut);
        transport.Children.Add(setIn);

        _timeline = new TrimTimeline { Cuts = _cuts, Margin = new Thickness(10, 6, 10, 8) };
        _timeline.SeekRequested += t => { _preview.Position = t; ShowPosition(t); };
        _timeline.CutsChanged += before => Changed(before);
        _timeline.SelectionChanged += s => { _cutButton.Opacity = s is null ? 0.45 : 1; UpdateStatus(); };

        var center = new Grid();
        center.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        center.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        center.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(screen, 0);
        Grid.SetRow(transport, 1);
        Grid.SetRow(_timeline, 2);
        center.Children.Add(screen);
        center.Children.Add(transport);
        center.Children.Add(_timeline);
        var stage = new Border { Background = SpaceTheme.NebulaBrush, Child = center };

        // ---- footer
        _status = new TextBlock { Foreground = SpaceTheme.DustBrush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _hint = new TextBlock { Foreground = SpaceTheme.SunBrush, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Background = SpaceTheme.VoidBrush, Padding = new Thickness(10, 0, 0, 0) };
        var footer = new Grid { Margin = new Thickness(14, 6, 14, 8) };
        footer.Children.Add(_status);
        footer.Children.Add(_hint);
        _hintTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hintTimer.Tick += (_, _) =>
        {
            _hintTimer.Stop();
            _hint.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(250)));
        };

        var topBorder = new Border { Background = SpaceTheme.VoidBrush, BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(0, 0, 0, 1), Child = top };
        var bottom = new Border { Background = SpaceTheme.VoidBrush, BorderBrush = SpaceTheme.NavyLightBrush, BorderThickness = new Thickness(0, 1, 0, 0), Child = footer };
        var dock = new DockPanel();
        DockPanel.SetDock(topBorder, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(topBorder);
        dock.Children.Add(bottom);
        dock.Children.Add(stage);

        // ---- busy overlay while the trimmed file is written
        _busyText = new TextBlock { Text = L.T("Trimming…"), FontFamily = SpaceTheme.HeadingFont, FontSize = 18, Foreground = SpaceTheme.SunBrush, HorizontalAlignment = HorizontalAlignment.Center };
        _busyBar = new ProgressBar { Width = 260, Height = 6, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 12, 0, 0), Foreground = SpaceTheme.CometBlueBrush, Background = SpaceTheme.NavyBrush, BorderThickness = new Thickness(0) };
        var busyStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        busyStack.Children.Add(_busyText);
        busyStack.Children.Add(_busyBar);
        _busy = new Border { Background = SpaceTheme.WithAlpha(SpaceTheme.Void, 0xD8), Child = busyStack, Visibility = Visibility.Collapsed };

        _root = new Grid();
        _root.Children.Add(dock);
        _root.Children.Add(_busy);
        Content = _root;

        _tick = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(30) };
        _tick.Tick += (_, _) => OnTick();
        _tick.Start();

        PreviewKeyDown += OnKeyDown;
        SourceInitialized += (_, _) => ApplyDarkFrame();
        Closing += OnClosing;
        Closed += (_, _) => { _tick.Stop(); ReleasePreview(); };

        SizeTo(width, height);
        _preview.Position = _cuts.TrimStart;
        ShowPosition(_cuts.TrimStart);
        UpdateStatus();
        Loaded += (_, _) => Flash(L.T("Drag the yellow handles to trim, or drag across the timeline and press Del to cut"));
    }

    /// <summary>Opens a recording in the trimmer (null, with a message, when it can't be read).</summary>
    public static TrimWindow? Open(CometApp app, string path)
    {
        try
        {
            if (!File.Exists(path)) throw new FileNotFoundException(L.T("The recording is no longer there."), path);
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
                cuts = new CutList(RecordingTrimmer.ProbeDuration(source));
                preview = new VideoPreview(source);
                (width, height) = VideoSize(source);
            }
            var saved = EditSidecar.CutsFor(path);
            if (source == original && File.Exists(saved))
                cuts = CutList.FromJson(File.ReadAllText(saved), cuts.Duration);
            if (cuts.Duration <= CutList.MinPiece) throw new InvalidDataException(L.T("This recording is too short to trim."));
            return new TrimWindow(app, path, source, cuts, preview, width, height);
        }
        catch (Exception ex)
        {
            Log.Error("Could not open the trimmer", ex);
            MessageBox.Show(L.T("Could not open this recording:") + "\n" + ex.Message, "Cometsnap", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    private static (int, int) VideoSize(string path)
    {
        var (w, h) = RecordingTrimmer.ProbeSize(path);
        return w > 0 && h > 0 ? (w, h) : (1280, 720);
    }

    private void SizeTo(int w, int h)
    {
        var area = SystemParameters.WorkArea;
        var scale = Math.Min(1, Math.Min((area.Width * 0.85 - 48) / Math.Max(1, w), (area.Height * 0.85 - 260) / Math.Max(1, h)));
        Width = Math.Min(Math.Max(w * scale + 48, 760), area.Width * 0.92);
        Height = Math.Min(Math.Max(h * scale + 260, 480), area.Height * 0.92);
    }

    private string TitleText() => (Dirty ? "• " : "") + Path.GetFileName(_path) + " · " + L.T("Trim") + " · Cometsnap";

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

    private bool _playButtonShowsPause;

    private void UpdatePlayButton()
    {
        var playing = _preview.IsPlaying;
        if (playing == _playButtonShowsPause) return;
        _playButtonShowsPause = playing;
        if (_playButton.Child is StackPanel row && row.Children.Count > 0)
        {
            row.Children[0] = EditorUi.Icon(playing ? PauseIcon : PlayIcon, null, 16);
            if (row.Children.Count > 1 && row.Children[1] is TextBlock label) label.Text = playing ? L.T("Pause") : L.T("Play");
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
            Flash(L.T("First drag across the timeline to select the part to cut"));
            return;
        }
        var before = _cuts.Clone();
        if (!_cuts.AddCut(s.Start, s.End))
        {
            Flash(s.Length < CutList.MinPiece ? L.T("That part is too short to cut") : L.T("Something has to stay: you can't cut everything"));
            return;
        }
        _timeline.Selection = null;
        Changed(before);
        Seek(_cuts.NextKept(s.Start) ?? _cuts.TrimStart);
        Flash(L.F("Cut out {0}", CutList.Label(s.Length)));
    }

    private void SetTrim(bool start)
    {
        var before = _cuts.Clone();
        var at = _preview.Position;
        if (start) _cuts.TrimStart = at; else _cuts.TrimEnd = at;
        if (_cuts.SameAs(before)) return;
        Changed(before);
        Flash(start ? L.F("Starts at {0}", CutList.Label(_cuts.TrimStart)) : L.F("Ends at {0}", CutList.Label(_cuts.TrimEnd)));
    }

    private void ResetAll()
    {
        if (_cuts.IsUntouched) return;
        var before = _cuts.Clone();
        _cuts = new CutList(_cuts.Duration);
        _timeline.Selection = null;
        Changed(before);
        Flash(L.T("The whole recording is back"));
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
            _cuts.IsUntouched ? L.F("{0} · whole recording", CutList.Label(_cuts.Duration)) : L.F("Keeps {0} of {1}", CutList.Label(_cuts.KeptLength), CutList.Label(_cuts.Duration)),
        };
        if (_cuts.Cuts.Count == 1) parts.Add(L.T("1 part cut out"));
        else if (_cuts.Cuts.Count > 1) parts.Add(L.F("{0} parts cut out", _cuts.Cuts.Count));
        if (_timeline.Selection is { } s) parts.Add(L.F("Selected {0}: press Del to cut it", CutList.Label(s.Length)));
        parts.Add(_gif ? "GIF" : "MP4");
        _status.Text = string.Join("  ·  ", parts);
    }

    private void Flash(string text)
    {
        _hint.Text = text;
        _hint.BeginAnimation(OpacityProperty, null);
        _hint.Opacity = 1;
        _hintTimer.Stop();
        _hintTimer.Start();
    }

    // ------------------------------------------------------------------ keys

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_working) { e.Handled = true; return; }
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var step = shift ? TimeSpan.FromSeconds(1) : _gif ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromMilliseconds(1000.0 / 30);
        e.Handled = true;
        if (ctrl)
        {
            switch (key)
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
        switch (key)
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
                if (_timeline.Selection is not null) _timeline.Selection = null;
                else Close();
                return;
        }
        e.Handled = false;
    }

    // ------------------------------------------------------------------ saving

    /// <summary>Writes the trimmed recording over the original file (the original is kept hidden).</summary>
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
                    await RetryAsync(() => { File.Delete(_path); File.Move(original, _path); });
                }
                if (File.Exists(cutsFile)) File.Delete(cutsFile);
            }
            else
            {
                var temp = Path.Combine(Path.GetDirectoryName(_path)!, Path.GetFileNameWithoutExtension(_path) + ".trimming" + Path.GetExtension(_path));
                if (!await ExportAsync(temp)) return false;
                ReleasePreview();
                EditSidecar.EnsureFolder(_path);
                await RetryAsync(() =>
                {
                    if (_source == _path) File.Move(_path, original, overwrite: true);
                    else File.Delete(_path);
                    File.Move(temp, _path);
                });
                File.WriteAllText(cutsFile, _cuts.ToJson());
            }
            _saved = _cuts.Clone();
            Title = TitleText();
            _app.Thumbnails.Forget(_path);
            _app.Recent.Add(_path);
            Log.Info($"Trimmed recording saved: {_path} ({_cuts.KeptLength.TotalSeconds:0.0} s kept)");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Trim save failed", ex);
            MessageBox.Show(this, L.T("Could not save the trimmed recording:") + "\n" + ex.Message, "Cometsnap", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    /// <summary>Writes the kept parts of the source to <paramref name="output"/>, with a progress overlay.</summary>
    private async Task<bool> ExportAsync(string output)
    {
        _working = true;
        _busy.Visibility = Visibility.Visible;
        _busyBar.Value = 0;
        _busy.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
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
            MessageBox.Show(this, L.T("Could not save the trimmed recording:") + "\n" + (ex.InnerException?.Message ?? ex.Message), "Cometsnap", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        finally
        {
            _working = false;
            _busy.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>File moves can briefly fail while Windows' player or Explorer still holds the file.</summary>
    private static async Task RetryAsync(Action action)
    {
        for (var i = 0; ; i++)
        {
            try
            {
                action();
                return;
            }
            catch (IOException) when (i < 20)
            {
                await Task.Delay(100);
            }
            catch (UnauthorizedAccessException) when (i < 20)
            {
                await Task.Delay(100);
            }
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
        var copied = false;
        for (var i = 0; i < 5 && !copied; i++)
        {
            try
            {
                Clipboard.SetFileDropList(new StringCollection { _path });
                copied = true;
            }
            catch (Exception)
            {
                await Task.Delay(60);
            }
        }
        if (!copied)
        {
            Flash(L.T("Could not copy, try again"));
            return;
        }
        FlyHome();
        CloseNow();
    }

    private async Task SaveAsAsync()
    {
        if (_working) return;
        _preview.Pause();
        UpdatePlayButton();
        var ext = Path.GetExtension(_path);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = L.T("Save a copy"),
            FileName = Path.GetFileNameWithoutExtension(_path) + "-trimmed",
            DefaultExt = ext,
            Filter = (_gif ? L.T("GIF animation") + " (*.gif)|*.gif" : L.T("MP4 video") + " (*.mp4)|*.mp4"),
            InitialDirectory = Path.GetDirectoryName(_path),
        };
        if (dialog.ShowDialog(this) != true) return;
        if (string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(_path), StringComparison.OrdinalIgnoreCase))
        {
            await SaveAndCloseAsync();
            return;
        }
        if (_cuts.IsUntouched)
        {
            try { File.Copy(_source, dialog.FileName, overwrite: true); }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.T("Could not save:") + "\n" + ex.Message, "Cometsnap", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        else if (!await ExportAsync(dialog.FileName))
        {
            return;
        }
        FlyHome();
        CloseNow();
    }

    private void FlyHome()
    {
        try
        {
            var center = PointToScreen(new Point(ActualWidth / 2, ActualHeight / 2));
            _app.LaunchComet(new Point(center.X, center.Y));
        }
        catch (InvalidOperationException) { /* not on screen */ }
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

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_working) { e.Cancel = true; return; }
        if (_closingAfterSave || !Dirty) return;
        e.Cancel = true;
        var answer = MessageBox.Show(this, L.T("Save the trimmed recording?"), "Cometsnap", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.No)
        {
            CloseNow();
        }
        else if (answer == MessageBoxResult.Yes)
        {
            // Closing from inside Closing isn't allowed: save first, then close.
            Dispatcher.BeginInvoke(async () =>
            {
                if (await SaveAsync()) CloseNow();
            });
        }
    }

    private void ApplyDarkFrame()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var dark = 1;
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }
}
