using Cometsnap.Linux.Core;
using Tmds.DBus.Protocol;

namespace Cometsnap.Linux.Platform;

/// <summary>
/// Single instance + remote control on the session bus, as <c>com.cometsnap.Cometsnap</c>.
/// A second launch (<c>cometsnap --capture</c>) forwards its command to the running app and exits.
/// Desktop keyboard shortcuts use the much lighter
/// <c>dbus-send --session --dest=com.cometsnap.Cometsnap /com/cometsnap/Cometsnap com.cometsnap.Cometsnap.Command string:capture</c>,
/// so the capture starts in a few milliseconds.
/// </summary>
public sealed class InstanceService : IMethodHandler, IDisposable
{
    public const string BusName = "com.cometsnap.Cometsnap";
    public const string ObjectPathName = "/com/cometsnap/Cometsnap";
    public const string Interface = "com.cometsnap.Cometsnap";

    private readonly Connection _connection;
    private readonly Action<string> _onCommand;

    private InstanceService(Connection connection, Action<string> onCommand)
    {
        _connection = connection;
        _onCommand = onCommand;
    }

    public string Path => ObjectPathName;

    /// <summary>The shell command that triggers <paramref name="command"/> in the running app.</summary>
    public static string TriggerCommand(string command) =>
        $"dbus-send --session --type=method_call --dest={BusName} {ObjectPathName} {Interface}.Command string:{command}";

    /// <summary>
    /// Becomes the running instance. Returns null when another instance owns the name: then the
    /// command was forwarded to it (unless <paramref name="forward"/> is null).
    /// </summary>
    public static async Task<InstanceService?> TryStartAsync(Action<string> onCommand, string? forward)
    {
        var connection = new Connection(Address.Session!);
        await connection.ConnectAsync();
        var service = new InstanceService(connection, onCommand);
        connection.AddMethodHandler(service);

        var request = Portal.Build(connection, "org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "RequestName", "su",
            (ref MessageWriter w) => { w.WriteString(BusName); w.WriteUInt32(4); /* DBUS_NAME_FLAG_DO_NOT_QUEUE */ });
        var reply = await connection.CallMethodAsync(request, (m, _) => m.GetBodyReader().ReadUInt32(), null);
        if (reply is 1 or 4) return service; // primary owner / already owner

        if (forward is not null) await SendAsync(connection, forward);
        connection.Dispose();
        return null;
    }

    private static async Task SendAsync(Connection connection, string command)
    {
        await connection.CallMethodAsync(Portal.Build(connection, BusName, ObjectPathName, Interface, "Command", "s",
            (ref MessageWriter w) => w.WriteString(command)));
    }

    public bool RunMethodHandlerSynchronously(Message message) => true;

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        var request = context.Request;
        if (request.InterfaceAsString == Interface && request.MemberAsString == "Command")
        {
            var command = request.GetBodyReader().ReadString();
            Log.Info($"Remote command: {command}");
            try { _onCommand(command); }
            catch (Exception ex) { Log.Error("Remote command failed", ex); }
            if (!context.NoReplyExpected)
            {
                using var w = context.CreateReplyWriter(null);
                context.Reply(w.CreateMessage());
            }
        }
        else if (context.IsDBusIntrospectRequest)
        {
            context.ReplyIntrospectXml(new ReadOnlyMemory<byte>[]
            {
                """
                <interface name="com.cometsnap.Cometsnap">
                  <method name="Command"><arg type="s" name="command" direction="in"/></method>
                </interface>
                """u8.ToArray(),
            });
        }
        return default;
    }

    public void Dispose() => _connection.Dispose();
}
