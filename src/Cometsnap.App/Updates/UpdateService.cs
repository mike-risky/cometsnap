using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Cometsnap.Core.Diagnostics;
using Cometsnap.Core.Localization;
using Cometsnap.Core.Updates;

namespace Cometsnap.App.Updates;

/// <summary>
/// Once a day, asks GitHub Releases whether a newer Cometsnap is out and shows the update card.
/// Installed (Setup): Update now downloads the small installer, checks its SHA-256, runs it quietly
/// and Cometsnap restarts. Portable: the card links to the download. Nothing installs without a click.
/// </summary>
public sealed class UpdateService
{
    private readonly CometApp _app;
    private readonly string _prefsPath;
    private readonly UpdateChecker _checker;
    private readonly DispatcherTimer _timer;
    private UpdateCard? _card;
    private UpdateOffer? _offer;
    private bool _checking;

    public UpdatePrefs Prefs { get; }

    public UpdateService(CometApp app)
    {
        _app = app;
        _prefsPath = Path.Combine(app.Paths.DataDirectory, "updates.json");
        Prefs = UpdatePrefs.Load(_prefsPath);
        _checker = new UpdateChecker(Current);
        // First look a little after start (never in the way of the first capture), then hourly to see if a day has passed.
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(2) };
        _timer.Tick += (_, _) =>
        {
            _timer.Interval = TimeSpan.FromHours(1);
            if (Prefs.IsDue(DateTime.UtcNow, Current)) _ = CheckAsync(manual: false);
        };
    }

    public static AppVersion Current => AppVersion.Parse(CometApp.Version);

    /// <summary>Cometsnap-Setup build (installed per user, with an uninstaller beside the exe).</summary>
    public static bool IsInstalled
    {
        get
        {
#if INSTALLED_FLAVOR
            return true;
#else
            var dir = Path.GetDirectoryName(Environment.ProcessPath ?? "");
            return dir is not null && File.Exists(Path.Combine(dir, "unins000.exe"));
#endif
        }
    }

    private static string Arch => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";

    public void Start() => _timer.Start();

    public void SetAutomatic(bool on)
    {
        Prefs.CheckAutomatically = on;
        Prefs.Save(_prefsPath);
    }

    public enum Result { UpToDate, Offered, Unreachable }

    /// <summary>Asks GitHub; shows the card when there is a newer version. Settings' "Check now" passes manual.</summary>
    public async Task<Result> CheckAsync(bool manual)
    {
        if (_checking) return Result.UpToDate;
        _checking = true;
        try
        {
            Prefs.LastCheckUtc = DateTime.UtcNow;
            Prefs.Save(_prefsPath);
            var asset = IsInstalled ? $"Cometsnap-Setup-{Arch}.exe" : $"Cometsnap-{Arch}.exe";
            var offer = await _checker.CheckAsync(L.Language, Prefs.SkippedVersion, manual, asset);
            if (offer is null)
            {
                Log.Info($"Update check: {Current} is up to date");
                return Result.UpToDate;
            }
            Log.Info($"Update check: {offer.Version} is out ({offer.Notes.Count} notes)");
            ShowCard(offer);
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
            _card = new UpdateCard();
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
            _card.ShowEverything += () => Open(ChangelogUrl(L.Language));
        }
        _card.ShowOffer(offer,
            IsInstalled ? L.T("Update now") : L.T("Download"),
            IsInstalled ? null : L.T("Portable Cometsnap: download the new file and use it instead of this one."));
    }

    private async Task UpdateNowAsync()
    {
        if (_offer is not { } offer || _card is not { } card) return;
        var release = offer.Release;
        if (!IsInstalled)
        {
            // Portable: the new exe goes wherever the user keeps this one.
            var exe = release.Asset($"Cometsnap-{Arch}.exe");
            Open(exe?.Url ?? release.PageUrl);
            card.Hide();
            return;
        }

        var setupName = $"Cometsnap-Setup-{Arch}.exe";
        if (release.Asset(setupName) is null)
        {
            Open(release.PageUrl);
            card.Hide();
            return;
        }
        var target = Path.Combine(Path.GetTempPath(), "Cometsnap-update", $"Cometsnap-Setup-{Arch}-{offer.Version}.exe");
        card.SetBusy(true, L.F("Downloading… {0}%", 0));
        try
        {
            var progress = new Progress<double>(p => card.SetStatus(L.F("Downloading… {0}%", (int)Math.Round(p * 100))));
            await _checker.DownloadAsync(release, setupName, target, "SHA256SUMS.txt", progress);
        }
        catch (Exception ex)
        {
            Log.Error("Downloading the update failed", ex);
            card.SetBusy(false, L.T("The update could not be downloaded. Try again later."));
            return;
        }

        card.SetBusy(true, L.T("Cometsnap restarts in a moment."));
        try
        {
            // Setup shows its own small progress window, closes this Cometsnap, keeps every setting
            // and starts the new version (/UPDATE=1, see installer/Cometsnap.iss).
            Process.Start(new ProcessStartInfo(target, "/SILENT /SP- /SUPPRESSMSGBOXES /NORESTART /NOCANCEL /UPDATE=1") { UseShellExecute = true });
            Log.Info($"Running {Path.GetFileName(target)} to update to {offer.Version}");
        }
        catch (Exception ex)
        {
            Log.Error("Starting the update failed", ex);
            card.SetBusy(false, L.T("The update could not be downloaded. Try again later."));
            return;
        }
        await Task.Delay(800);
        _app.Quit();
    }

    /// <summary>cometsnap.com/changelog in the app's language.</summary>
    public static string ChangelogUrl(string language) =>
        language == "en" ? "https://cometsnap.com/changelog" : $"https://cometsnap.com/{language}/changelog";

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Could not open " + url, ex); }
    }
}
