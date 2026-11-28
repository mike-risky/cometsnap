using System.Runtime.InteropServices;
using Avalonia.Threading;
using Cometsnap.Core.Updates;
using Cometsnap.Linux.Core;
using Cometsnap.Linux.Platform;
using Cometsnap.Linux.Tray;

namespace Cometsnap.Linux.Services;

/// <summary>
/// Once a day, asks GitHub Releases whether a newer Cometsnap is out and shows the update card.
/// AppImage: Update now downloads the new AppImage, checks its SHA-256, puts it in place of this one
/// and restarts. Package installs (.deb) and anything else get the download link.
/// Nothing is downloaded or installed without a click.
/// </summary>
public sealed class UpdateService
{
    private readonly CometApp _app;
    private readonly string _prefsPath;
    private readonly UpdateChecker _checker;
    private readonly DispatcherTimer _timer;
    private UpdateWindow? _card;
    private UpdateOffer? _offer;
    private bool _checking;

    public UpdatePrefs Prefs { get; }

    public enum Result { UpToDate, Offered, Unreachable }

    public UpdateService(CometApp app)
    {
        _app = app;
        _prefsPath = Path.Combine(app.Paths.ConfigDirectory, "updates.json");
        Prefs = UpdatePrefs.Load(_prefsPath);
        _checker = new UpdateChecker(Current);
        _timer = new DispatcherTimer(TimeSpan.FromMinutes(2), DispatcherPriority.Background, (_, _) => Tick());
    }

    public static AppVersion Current => AppVersion.Parse(typeof(UpdateService).Assembly.GetName().Version?.ToString(3));

    private static string Arch => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "aarch64" : "x86_64";

    /// <summary>The AppImage file this Cometsnap runs from, when it is one.</summary>
    private static string? AppImage =>
        Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } p && File.Exists(p) ? p : null;

    private static bool IsPackage => AppImage is null && (Environment.ProcessPath ?? "").StartsWith("/usr/", StringComparison.Ordinal);

    public void Start() => _timer.Start();

    private void Tick()
    {
        _timer.Interval = TimeSpan.FromHours(1);
        if (Prefs.IsDue(DateTime.UtcNow, Current)) _ = CheckAsync(manual: false);
    }

    public void SetAutomatic(bool on)
    {
        Prefs.CheckAutomatically = on;
        Prefs.Save(_prefsPath);
    }

    public async Task<Result> CheckAsync(bool manual)
    {
        if (_checking) return Result.UpToDate;
        _checking = true;
        try
        {
            Prefs.LastCheckUtc = DateTime.UtcNow;
            Prefs.Save(_prefsPath);
            var asset = AppImage is not null ? $"Cometsnap-{Arch}.AppImage" : Arch == "aarch64" ? "cometsnap_arm64.deb" : "cometsnap_amd64.deb";
            var offer = await _checker.CheckAsync(Language, Prefs.SkippedVersion, manual, asset);
            if (offer is null)
            {
                Log.Info($"Update check: {Current} is up to date");
                return Result.UpToDate;
            }
            Log.Info($"Update check: {offer.Version} is out ({offer.Notes.Count} notes)");
            Dispatcher.UIThread.Post(() => ShowCard(offer));
            return Result.Offered;
        }
        catch (Exception ex)
        {
            Log.Info($"Update check failed: {ex.Message}");
            return Result.Unreachable;
        }
        finally
        {
            _checking = false;
        }
    }

    private void ShowCard(UpdateOffer offer)
    {
        _offer = offer;
        if (_card is null)
        {
            _card = new UpdateWindow();
            _card.UpdateNow += () => _ = UpdateNowAsync();
            _card.Later += () => _card?.Hide();
            _card.Skip += () =>
            {
                if (_offer is { } o)
                {
                    Prefs.SkippedVersion = o.Version.ToString();
                    Prefs.Save(_prefsPath);
                }
                _card?.Hide();
            };
            _card.ShowEverything += () => Tools.Open(ChangelogUrl(Language));
        }
        var p = X11.PointerPosition() ?? (0, 0);
        var (work, scale) = _app.WorkAreaAt(p.X, p.Y);
        var self = AppImage is not null;
        var note = self ? null : IsPackage ? T("Installed as a package: download the new .deb and install it like the first one.") : null;
        _card.ShowOffer(offer, self ? T("Update now") : T("Download"), note, work, scale);
    }

    private async Task UpdateNowAsync()
    {
        if (_offer is not { } offer || _card is not { } card) return;
        var release = offer.Release;
        var appImage = AppImage;
        var assetName = $"Cometsnap-{Arch}.AppImage";
        if (appImage is null || release.Asset(assetName) is null)
        {
            Tools.Open(release.PageUrl);
            card.Hide();
            return;
        }

        // Next to the running AppImage, so the final move is a simple rename on the same disk.
        var target = Path.Combine(Path.GetDirectoryName(appImage)!, $".{Path.GetFileName(appImage)}.update");
        card.SetBusy(true, F("Downloading… {0}%", 0));
        try
        {
            var progress = new Progress<double>(v => Dispatcher.UIThread.Post(() => card.SetStatus(F("Downloading… {0}%", (int)Math.Round(v * 100)))));
            await _checker.DownloadAsync(release, assetName, target, "SHA256SUMS-linux.txt", progress);
            File.SetUnixFileMode(target, File.GetUnixFileMode(appImage) | UnixFileMode.UserExecute | UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(target, appImage, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("Updating the AppImage failed", ex);
            try { if (File.Exists(target)) File.Delete(target); } catch { /* best effort */ }
            await Dispatcher.UIThread.InvokeAsync(() => card.SetBusy(false, T("The update could not be downloaded. Try again later.")));
            return;
        }

        Log.Info($"AppImage updated to {offer.Version}, restarting");
        await Dispatcher.UIThread.InvokeAsync(() => card.SetBusy(true, T("Cometsnap restarts in a moment.")));
        // The new AppImage starts once this one has let go of its single-instance name.
        Tools.Launch("/bin/sh", "-c", "sleep 2; exec \"$0\" --updated", appImage);
        await Task.Delay(600);
        Dispatcher.UIThread.Post(_app.Quit);
    }

    /// <summary>cometsnap.com/changelog in the app's language.</summary>
    public static string ChangelogUrl(string language) =>
        language == "en" ? "https://cometsnap.com/changelog" : $"https://cometsnap.com/{language}/changelog";
}
