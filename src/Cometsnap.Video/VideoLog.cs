namespace Cometsnap.Video;

/// <summary>The app plugs its logger in here; the library stays independent of it.</summary>
public static class VideoLog
{
    public static Action<string>? Info { get; set; }
    public static Action<string, Exception?>? Error { get; set; }

    internal static void I(string message) => Info?.Invoke(message);
    internal static void E(string message, Exception? ex = null) => Error?.Invoke(message, ex);
}
