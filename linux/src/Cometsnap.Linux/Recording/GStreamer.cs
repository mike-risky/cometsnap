using System.Runtime.InteropServices;

namespace Cometsnap.Linux.Recording;

/// <summary>
/// Just enough of libgstreamer to read a Wayland screen-cast (PipeWire) stream. GStreamer and its
/// PipeWire plugin come with GNOME and KDE, since their own screen recorders use them.
/// </summary>
internal static partial class GStreamer
{
    private const string Lib = "libgstreamer-1.0.so.0";
    public const int StateNull = 1, StatePlaying = 4;
    public const int MessageEos = 1, MessageError = 2;

    [LibraryImport(Lib)] private static partial void gst_init(IntPtr argc, IntPtr argv);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] private static partial IntPtr gst_parse_launch(string description, out IntPtr error);
    [LibraryImport(Lib)] public static partial int gst_element_set_state(IntPtr element, int state);
    [LibraryImport(Lib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool gst_element_send_event(IntPtr element, IntPtr ev);
    [LibraryImport(Lib)] public static partial IntPtr gst_event_new_eos();
    [LibraryImport(Lib)] public static partial IntPtr gst_element_get_bus(IntPtr element);
    [LibraryImport(Lib)] public static partial IntPtr gst_bus_timed_pop_filtered(IntPtr bus, ulong timeoutNs, int types);
    [LibraryImport(Lib)] public static partial void gst_message_unref(IntPtr message);
    [LibraryImport(Lib)] public static partial void gst_object_unref(IntPtr obj);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] private static partial IntPtr gst_element_factory_find(string name);

    private static bool _initialized;

    public static bool TryInit()
    {
        if (_initialized) return true;
        try
        {
            gst_init(IntPtr.Zero, IntPtr.Zero);
            _initialized = true;
        }
        catch (Exception)
        {
            return false;
        }
        return true;
    }

    public static bool HasElement(string name)
    {
        if (!TryInit()) return false;
        var f = gst_element_factory_find(name);
        if (f == IntPtr.Zero) return false;
        gst_object_unref(f);
        return true;
    }

    public static IntPtr Launch(string pipeline)
    {
        if (!TryInit()) throw new InvalidOperationException("GStreamer is not installed.");
        var element = gst_parse_launch(pipeline, out var error);
        if (element == IntPtr.Zero) throw new InvalidOperationException("Could not build the GStreamer pipeline.");
        return element;
    }

    /// <summary>Ends the stream cleanly (so the file is complete), then frees the pipeline.</summary>
    public static void Stop(IntPtr pipeline)
    {
        if (pipeline == IntPtr.Zero) return;
        gst_element_send_event(pipeline, gst_event_new_eos());
        var bus = gst_element_get_bus(pipeline);
        if (bus != IntPtr.Zero)
        {
            var msg = gst_bus_timed_pop_filtered(bus, 3_000_000_000UL, MessageEos | MessageError);
            if (msg != IntPtr.Zero) gst_message_unref(msg);
            gst_object_unref(bus);
        }
        gst_element_set_state(pipeline, StateNull);
        gst_object_unref(pipeline);
    }
}
