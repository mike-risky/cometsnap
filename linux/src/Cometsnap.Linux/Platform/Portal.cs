using Cometsnap.Linux.Core;
using System.Runtime.InteropServices;
using Tmds.DBus.Protocol;

namespace Cometsnap.Linux.Platform;

/// <summary>
/// xdg-desktop-portal client (Screenshot, ScreenCast, GlobalShortcuts). These are the official
/// way for apps to see the screen and own shortcuts on Wayland (GNOME, KDE, Hyprland, Sway...).
/// </summary>
public sealed class Portal : IDisposable
{
    public const string Service = "org.freedesktop.portal.Desktop";
    public const string ObjectPath = "/org/freedesktop/portal/desktop";

    private static Portal? _shared;
    private readonly Connection _connection;
    private int _token;

    private Portal(Connection connection) => _connection = connection;

    public Connection Connection => _connection;

    public static async Task<Portal> GetAsync()
    {
        if (_shared is not null) return _shared;
        var c = new Connection(Address.Session!);
        await c.ConnectAsync();
        return _shared ??= new Portal(c);
    }

    public void Dispose() => _connection.Dispose();

    /// <summary>Reads a portal interface's "version" property, 0 when the interface is missing.</summary>
    public async Task<uint> GetVersionAsync(string iface)
    {
        try
        {
            var msg = Build(_connection, Service, ObjectPath, "org.freedesktop.DBus.Properties", "Get", "ss",
                (ref MessageWriter w) => { w.WriteString(iface); w.WriteString("version"); });
            return await _connection.CallMethodAsync(msg,
                (m, _) => m.GetBodyReader().ReadVariantValue().GetUInt32(), null);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    public delegate void ArgWriter(ref MessageWriter writer);

    /// <summary>Builds a method call (MessageWriter is a ref struct: it can't live in async methods).</summary>
    public static MessageBuffer Build(Connection c, string destination, string path, string iface, string member, string? signature, ArgWriter? args = null)
    {
        var w = c.GetMessageWriter();
        try
        {
            w.WriteMethodCallHeader(destination, path, iface, member, signature);
            args?.Invoke(ref w);
            return w.CreateMessage();
        }
        finally
        {
            w.Dispose();
        }
    }

    public sealed record Response(uint Code, Dictionary<string, VariantValue> Results)
    {
        public bool Ok => Code == 0;
        public bool Cancelled => Code == 1;
    }

    /// <summary>
    /// Calls a portal method that answers through a Request object: subscribes to the Response
    /// signal first (no race), then calls, then waits for the user / desktop to answer.
    /// </summary>
    public async Task<Response> RequestAsync(string iface, string method, string signature,
        ArgWriter writeLeadingArgs, Dictionary<string, VariantValue> options, TimeSpan timeout)
    {
        var token = $"cometsnap{Environment.ProcessId}_{Interlocked.Increment(ref _token)}";
        options["handle_token"] = VariantValue.String(token);
        var sender = (_connection.UniqueName ?? "").TrimStart(':').Replace('.', '_');
        var requestPath = $"/org/freedesktop/portal/desktop/request/{sender}/{token}";

        var tcs = new TaskCompletionSource<Response>(TaskCreationOptions.RunContinuationsAsynchronously);
        var rule = new MatchRule
        {
            Type = MessageType.Signal,
            Interface = "org.freedesktop.portal.Request",
            Member = "Response",
            Path = requestPath,
        };
        using var subscription = await _connection.AddMatchAsync(rule,
            (m, _) =>
            {
                var r = m.GetBodyReader();
                var code = r.ReadUInt32();
                var results = r.ReadDictionaryOfStringToVariantValue();
                return new Response(code, results);
            },
            (ex, response, _, _) =>
            {
                if (ex is not null) tcs.TrySetException(ex);
                else tcs.TrySetResult(response);
            }, null, null, emitOnCapturedContext: false, ObserverFlags.None);

        var call = Build(_connection, Service, ObjectPath, iface, method, signature, (ref MessageWriter w) =>
        {
            writeLeadingArgs(ref w);
            w.WriteDictionary(options);
        });
        await _connection.CallMethodAsync(call, (m, _) => m.GetBodyReader().ReadObjectPathAsString(), null);

        var done = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
        if (done != tcs.Task) throw new TimeoutException($"{iface}.{method} did not answer");
        return await tcs.Task;
    }

    // ------------------------------------------------------------------ Screenshot

    /// <summary>Takes a screenshot of all monitors, without any dialog. Returns the PNG path (the caller deletes it).</summary>
    public async Task<string?> ScreenshotAsync()
    {
        var response = await RequestAsync("org.freedesktop.portal.Screenshot", "Screenshot", "sa{sv}",
            (ref MessageWriter w) => w.WriteString(""),
            new Dictionary<string, VariantValue> { ["interactive"] = VariantValue.Bool(false) },
            TimeSpan.FromSeconds(60));
        if (!response.Ok || !response.Results.TryGetValue("uri", out var uri)) return null;
        return new Uri(uri.GetString()).LocalPath;
    }

    // ------------------------------------------------------------------ ScreenCast

    public sealed record CastStream(uint NodeId, int X, int Y, int Width, int Height);

    public sealed record CastSession(string Handle, IReadOnlyList<CastStream> Streams, string? RestoreToken, SafeHandle? PipeWireFd);

    /// <summary>
    /// Starts a screen-cast session of one monitor (the desktop asks which one, unless a restore
    /// token from an earlier recording is still valid). Returns null when the user cancels.
    /// </summary>
    public async Task<CastSession?> StartScreenCastAsync(bool showCursor, string? restoreToken)
    {
        const string iface = "org.freedesktop.portal.ScreenCast";
        var version = await GetVersionAsync(iface);
        if (version == 0) throw new NotSupportedException("This desktop has no screen-cast portal.");

        var created = await RequestAsync(iface, "CreateSession", "a{sv}", (ref MessageWriter _) => { },
            new Dictionary<string, VariantValue>
            {
                ["session_handle_token"] = VariantValue.String($"cometsnap_session{Environment.ProcessId}_{Interlocked.Increment(ref _token)}"),
            }, TimeSpan.FromSeconds(30));
        if (!created.Ok) return null;
        var session = created.Results["session_handle"].GetString();

        var select = new Dictionary<string, VariantValue>
        {
            ["types"] = VariantValue.UInt32(1),      // monitors
            ["multiple"] = VariantValue.Bool(false),
        };
        if (version >= 2)
        {
            var cursorModes = await GetUInt32PropertyAsync(iface, "AvailableCursorModes");
            // 2 = embedded in the video, 1 = hidden.
            var mode = showCursor ? 2u : 1u;
            if ((cursorModes & mode) != 0) select["cursor_mode"] = VariantValue.UInt32(mode);
        }
        if (version >= 4)
        {
            select["persist_mode"] = VariantValue.UInt32(2); // remember until revoked
            if (!string.IsNullOrEmpty(restoreToken)) select["restore_token"] = VariantValue.String(restoreToken);
        }

        var selected = await RequestAsync(iface, "SelectSources", "oa{sv}", (ref MessageWriter w) => w.WriteObjectPath(session), select, TimeSpan.FromMinutes(5));
        if (!selected.Ok) { await CloseSessionAsync(session); return null; }

        var started = await RequestAsync(iface, "Start", "osa{sv}", (ref MessageWriter w) => { w.WriteObjectPath(session); w.WriteString(""); },
            new Dictionary<string, VariantValue>(), TimeSpan.FromMinutes(5));
        if (!started.Ok) { await CloseSessionAsync(session); return null; }

        var streams = new List<CastStream>();
        if (started.Results.TryGetValue("streams", out var list))
        {
            for (var i = 0; i < list.Count; i++)
            {
                var s = list.GetItem(i);
                var node = s.GetItem(0).GetUInt32();
                var props = s.GetItem(1);
                int x = 0, y = 0, w = 0, h = 0;
                for (var j = 0; j < props.Count; j++)
                {
                    var e = props.GetDictionaryEntry(j);
                    var key = e.Key.GetString();
                    var v = e.Value.Type == VariantValueType.Variant ? e.Value.GetVariantValue() : e.Value;
                    if (key == "position") { x = v.GetItem(0).GetInt32(); y = v.GetItem(1).GetInt32(); }
                    if (key == "size") { w = v.GetItem(0).GetInt32(); h = v.GetItem(1).GetInt32(); }
                }
                streams.Add(new CastStream(node, x, y, w, h));
            }
        }
        var token = started.Results.TryGetValue("restore_token", out var t) ? t.GetString() : null;

        SafeHandle? fd = null;
        try
        {
            var msg = Build(_connection, Service, ObjectPath, iface, "OpenPipeWireRemote", "oa{sv}",
                (ref MessageWriter w) => { w.WriteObjectPath(session); w.WriteDictionary(new Dictionary<string, VariantValue>()); });
            fd = await _connection.CallMethodAsync(msg,
                (m, _) => m.GetBodyReader().ReadHandle<Microsoft.Win32.SafeHandles.SafeFileHandle>(), null);
        }
        catch (Exception ex)
        {
            Log.Error("OpenPipeWireRemote failed (will use the default PipeWire connection)", ex);
        }

        return new CastSession(session, streams, token, fd);
    }

    public async Task CloseSessionAsync(string sessionHandle)
    {
        try
        {
            await _connection.CallMethodAsync(Build(_connection, Service, sessionHandle, "org.freedesktop.portal.Session", "Close", null));
        }
        catch (Exception)
        {
            // already closed
        }
    }

    private async Task<uint> GetUInt32PropertyAsync(string iface, string name)
    {
        try
        {
            var msg = Build(_connection, Service, ObjectPath, "org.freedesktop.DBus.Properties", "Get", "ss",
                (ref MessageWriter w) => { w.WriteString(iface); w.WriteString(name); });
            return await _connection.CallMethodAsync(msg,
                (m, _) => m.GetBodyReader().ReadVariantValue().GetUInt32(), null);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    // ------------------------------------------------------------------ GlobalShortcuts

    public sealed record Shortcut(string Id, string Description, string PreferredTrigger);

    /// <summary>
    /// Registers global shortcuts through the portal (KDE Plasma 5.27+, GNOME 48+, Hyprland).
    /// The desktop may ask the user to confirm the keys once. <paramref name="activated"/> gets the shortcut id.
    /// </summary>
    public async Task<bool> BindGlobalShortcutsAsync(IReadOnlyList<Shortcut> shortcuts, Action<string> activated)
    {
        const string iface = "org.freedesktop.portal.GlobalShortcuts";
        if (await GetVersionAsync(iface) == 0) return false;

        var created = await RequestAsync(iface, "CreateSession", "a{sv}", (ref MessageWriter _) => { },
            new Dictionary<string, VariantValue>
            {
                ["session_handle_token"] = VariantValue.String($"cometsnap_keys{Environment.ProcessId}"),
            }, TimeSpan.FromSeconds(30));
        if (!created.Ok) return false;
        var session = created.Results["session_handle"].GetString();

        await _connection.AddMatchAsync(
            new MatchRule { Type = MessageType.Signal, Interface = iface, Member = "Activated", Path = ObjectPath },
            (m, _) =>
            {
                var r = m.GetBodyReader();
                var s = r.ReadObjectPathAsString();
                var id = r.ReadString();
                return (s, id);
            },
            (ex, e, _, _) =>
            {
                if (ex is null && e.s == session) activated(e.id);
            }, null, null, emitOnCapturedContext: false, ObserverFlags.None);

        // Only ask the desktop to bind keys it doesn't know yet (avoids a dialog at every start).
        var known = await RequestAsync(iface, "ListShortcuts", "oa{sv}", (ref MessageWriter w) => w.WriteObjectPath(session),
            new Dictionary<string, VariantValue>(), TimeSpan.FromSeconds(30));
        var knownIds = new HashSet<string>();
        if (known.Ok && known.Results.TryGetValue("shortcuts", out var list))
            for (var i = 0; i < list.Count; i++) knownIds.Add(list.GetItem(i).GetItem(0).GetString());
        if (shortcuts.All(s => knownIds.Contains(s.Id))) return true;

        var bound = await RequestAsync(iface, "BindShortcuts", "oa(sa{sv})sa{sv}", (ref MessageWriter w) =>
        {
            w.WriteObjectPath(session);
            var array = w.WriteArrayStart(DBusType.Struct);
            foreach (var s in shortcuts)
            {
                w.WriteStructureStart();
                w.WriteString(s.Id);
                w.WriteDictionary(new Dictionary<string, VariantValue>
                {
                    ["description"] = VariantValue.String(s.Description),
                    ["preferred_trigger"] = VariantValue.String(s.PreferredTrigger),
                });
            }
            w.WriteArrayEnd(array);
            w.WriteString("");
        }, new Dictionary<string, VariantValue>(), TimeSpan.FromMinutes(5));
        return bound.Ok;
    }
}
