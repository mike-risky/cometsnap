using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Cometsnap.Core.Updates;

// Update checks: once a day Cometsnap asks GitHub Releases whether a newer stable version is out.
// Nothing is downloaded or installed until the user clicks "Update now".
// Pure .NET (no WPF), so the Linux app links this same file.

/// <summary>A version number like 1.4.0. Builds numbered 0.x are nightly and test builds.</summary>
public readonly record struct AppVersion(int Major, int Minor, int Patch) : IComparable<AppVersion>
{
    /// <summary>Nightly and pull-request builds (0.1.run): they never nag about updates on their own.</summary>
    public bool IsDevBuild => Major == 0;

    public int CompareTo(AppVersion other) =>
        Major != other.Major ? Major.CompareTo(other.Major)
        : Minor != other.Minor ? Minor.CompareTo(other.Minor)
        : Patch.CompareTo(other.Patch);

    public static bool operator <(AppVersion a, AppVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(AppVersion a, AppVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(AppVersion a, AppVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(AppVersion a, AppVersion b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    /// <summary>"v1.4.0", "1.4", "1.4.0.0" or "1.4.0-beta" (the suffix is ignored).</summary>
    public static bool TryParse(string? text, out AppVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var m = Regex.Match(text.Trim(), @"^[vV]?(\d+)(?:\.(\d+))?(?:\.(\d+))?");
        if (!m.Success) return false;
        static int Part(Group g) => g.Success ? int.Parse(g.Value, CultureInfo.InvariantCulture) : 0;
        version = new AppVersion(Part(m.Groups[1]), Part(m.Groups[2]), Part(m.Groups[3]));
        return true;
    }

    public static AppVersion Parse(string? text) => TryParse(text, out var v) ? v : default;
}

public sealed record ReleaseAsset(string Name, string Url, long Size);

/// <summary>A GitHub release, as read from the releases API.</summary>
public sealed record ReleaseInfo(string Tag, AppVersion Version, string PageUrl, IReadOnlyList<ReleaseAsset> Assets, bool Prerelease, bool Draft)
{
    public ReleaseAsset? Asset(string name) =>
        Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads one release object of the GitHub API (null when it is not one).</summary>
    public static ReleaseInfo? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("tag_name", out var tagEl)) return null;
            var tag = tagEl.GetString() ?? "";
            if (!AppVersion.TryParse(tag, out var version)) return null;
            var assets = new List<ReleaseAsset>();
            if (r.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var a in list.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    var size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var z) ? z : 0;
                    if (name is not null && url is not null) assets.Add(new ReleaseAsset(name, url, size));
                }
            return new ReleaseInfo(
                tag, version,
                r.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "",
                assets,
                r.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True,
                r.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>"Cometsnap 1.4 is here": the release and what it brings, in the user's language.</summary>
public sealed record UpdateOffer(ReleaseInfo Release, IReadOnlyList<string> Notes)
{
    public AppVersion Version => Release.Version;
}

/// <summary>
/// What a new version brings, for the update card. The main source is updates/whats-new.json
/// (short lines in all 8 languages); CHANGELOG.md (English) is the fallback.
/// </summary>
public static class WhatsNew
{
    public const int MaxLines = 4;

    /// <summary>
    /// The lines of every version after <paramref name="current"/> up to <paramref name="target"/>, newest first.
    /// A "latest" entry stands for <paramref name="target"/> when the file has no entry of its own for it
    /// (whats-new.json at a release tag: "latest" is exactly what that release adds).
    /// </summary>
    public static IReadOnlyList<string> FromJson(string json, AppVersion current, AppVersion target, string language, int max = MaxLines)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return Array.Empty<string>();
            var entries = new List<(AppVersion Version, JsonElement Texts)>();
            JsonElement? latest = null;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (p.Name.Equals("latest", StringComparison.OrdinalIgnoreCase)) latest = p.Value;
                else if (AppVersion.TryParse(p.Name, out var v) && v > current && v <= target) entries.Add((v, p.Value));
            }
            if (latest is { } l && entries.All(e => e.Version != target)) entries.Add((target, l));
            var lines = new List<string>();
            foreach (var (_, texts) in entries.OrderByDescending(e => e.Version))
            {
                if (texts.ValueKind != JsonValueKind.Object) continue;
                var pick = texts.TryGetProperty(language, out var t) && t.ValueKind == JsonValueKind.Array && t.GetArrayLength() > 0 ? t
                    : texts.TryGetProperty("en", out var en) ? en : default;
                if (pick.ValueKind != JsonValueKind.Array) continue;
                foreach (var line in pick.EnumerateArray())
                    if (line.GetString() is { Length: > 0 } s && !lines.Contains(s.Trim())) lines.Add(s.Trim());
            }
            return lines.Take(max).ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// The bold titles of the CHANGELOG.md sections after <paramref name="current"/> up to <paramref name="target"/>
    /// ("## v1.4.0 — …"); "## Latest build" stands for <paramref name="target"/> when it has no section yet.
    /// </summary>
    public static IReadOnlyList<string> FromChangelog(string markdown, AppVersion current, AppVersion target, int max = MaxLines)
    {
        var sections = new List<(AppVersion Version, List<string> Lines)>();
        List<string>? latest = null;
        List<string>? into = null;
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                into = null;
                var head = line[3..].Trim();
                if (head.StartsWith("Latest", StringComparison.OrdinalIgnoreCase)) into = latest = new List<string>();
                else if (AppVersion.TryParse(head, out var v) && v > current && v <= target)
                {
                    into = new List<string>();
                    sections.Add((v, into));
                }
                continue;
            }
            if (into is null) continue;
            var m = Regex.Match(line, @"^-\s+\*\*(.+?)\*\*"); // top-level items only, not their sub-points
            if (m.Success) into.Add(m.Groups[1].Value.Trim().TrimEnd('.', ':').Trim());
        }
        if (latest is { Count: > 0 } && sections.All(s => s.Version != target)) sections.Add((target, latest));
        var lines = new List<string>();
        foreach (var (_, ls) in sections.OrderByDescending(s => s.Version))
            foreach (var l in ls)
                if (l.Length > 0 && !lines.Contains(l)) lines.Add(l);
        return lines.Take(max).ToList();
    }
}

/// <summary>SHA256SUMS.txt files ("hash  name" per line).</summary>
public static class Checksums
{
    public static IReadOnlyDictionary<string, string> Parse(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var m = Regex.Match(raw.Trim(), @"^([0-9a-fA-F]{64})\s+\*?(.+)$");
            if (m.Success) map[Path.GetFileName(m.Groups[2].Value.Trim())] = m.Groups[1].Value.ToLowerInvariant();
        }
        return map;
    }

    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

/// <summary>Remembered between runs: the on/off switch, the last check and a skipped version (updates.json).</summary>
public sealed class UpdatePrefs
{
    /// <summary>Check GitHub once a day. On by default; Settings has the switch.</summary>
    public bool CheckAutomatically { get; set; } = true;

    public DateTime? LastCheckUtc { get; set; }

    /// <summary>"Skip this version": the card stays away until a newer one comes out.</summary>
    public string SkippedVersion { get; set; } = "";

    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <summary>Time for the daily check? Never on its own for nightly and test builds.</summary>
    public bool IsDue(DateTime nowUtc, AppVersion current) =>
        CheckAutomatically && !current.IsDevBuild && (LastCheckUtc is not { } last || nowUtc - last >= Interval || last > nowUtc);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static UpdatePrefs Load(string path)
    {
        try
        {
            if (File.Exists(path)) return JsonSerializer.Deserialize<UpdatePrefs>(File.ReadAllText(path), Json) ?? new UpdatePrefs();
        }
        catch (Exception)
        {
            // A broken file just means: check again.
        }
        return new UpdatePrefs();
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception)
        {
            // Not worth bothering anyone: the next check simply comes a bit early.
        }
    }
}

/// <summary>Talks to GitHub: the latest stable release, its notes, and downloads.</summary>
public sealed class UpdateChecker
{
    public const string Repo = "mike-risky/cometsnap";
    public const string LatestApi = "https://api.github.com/repos/" + Repo + "/releases/latest";
    public const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";

    private readonly HttpClient _http;

    public UpdateChecker(AppVersion current, HttpClient? http = null)
    {
        Current = current;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd($"Cometsnap/{current}");
    }

    public AppVersion Current { get; }

    /// <summary>
    /// The newer stable release, with its notes, or null when Cometsnap is up to date.
    /// Throws (HttpRequestException, TaskCanceledException) when GitHub can't be reached.
    /// <paramref name="manual"/> (Settings > Check now) also shows the skipped version, and on a
    /// nightly or test build it offers the latest release so the card can be tried.
    /// </summary>
    /// <paramref name="platformAsset"/>: this platform's download; until the release carries it (CI still
    /// uploading), the release counts as not out yet.
    public async Task<UpdateOffer?> CheckAsync(string language, string skippedVersion = "", bool manual = false,
        string? platformAsset = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestApi);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode(); // GitHub can't be reached: the caller says so (or stays quiet)
        var release = ReleaseInfo.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        if (!IsOffered(release, Current, skippedVersion, manual)) return null;
        if (platformAsset is not null && release!.Asset(platformAsset) is null) return null;
        var notes = await NotesAsync(release!, language, ct).ConfigureAwait(false);
        return new UpdateOffer(release!, notes);
    }

    /// <summary>The rules of <see cref="CheckAsync"/>, testable without the network.</summary>
    public static bool IsOffered(ReleaseInfo? release, AppVersion current, string skippedVersion, bool manual)
    {
        if (release is null || release.Draft || release.Prerelease || release.Version.IsDevBuild) return false;
        if (manual && current.IsDevBuild) return true;
        if (release.Version <= current) return false;
        return manual || !AppVersion.TryParse(skippedVersion, out var skipped) || release.Version != skipped;
    }

    /// <summary>What the release brings: whats-new.json at its tag, else its CHANGELOG.md section.</summary>
    public async Task<IReadOnlyList<string>> NotesAsync(ReleaseInfo release, string language, CancellationToken ct = default)
    {
        var from = Current.IsDevBuild ? new AppVersion(release.Version.Major, release.Version.Minor, Math.Max(0, release.Version.Patch - 1)) : Current;
        if (from >= release.Version) from = default;
        var raw = $"https://raw.githubusercontent.com/{Repo}/{Uri.EscapeDataString(release.Tag)}/";
        try
        {
            var json = await _http.GetStringAsync(raw + "updates/whats-new.json", ct).ConfigureAwait(false);
            var lines = WhatsNew.FromJson(json, from, release.Version, language);
            if (lines.Count > 0) return lines;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { }
        try
        {
            var md = await _http.GetStringAsync(raw + "CHANGELOG.md", ct).ConfigureAwait(false);
            return WhatsNew.FromChangelog(md, from, release.Version);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Downloads a release file to <paramref name="target"/> and checks it against the release's
    /// checksum file when there is one. Throws when the download fails or the file doesn't match.
    /// </summary>
    public async Task DownloadAsync(ReleaseInfo release, string assetName, string target, string? checksumsName,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var asset = release.Asset(assetName) ?? throw new FileNotFoundException($"{assetName} is not in {release.Tag}");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var part = target + ".part";
        using (var response = await _http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? asset.Size;
            await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var output = File.Create(part);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                if (total > 0) progress?.Report(Math.Min(1, (double)done / total));
            }
        }
        if (checksumsName is not null && release.Asset(checksumsName) is { } sums)
        {
            var expected = Checksums.Parse(await _http.GetStringAsync(sums.Url, ct).ConfigureAwait(false));
            if (expected.TryGetValue(assetName, out var hash) && !string.Equals(hash, Checksums.Sha256(part), StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(part);
                throw new InvalidDataException($"{assetName}: the download does not match {checksumsName}");
            }
        }
        File.Move(part, target, overwrite: true);
    }
}
