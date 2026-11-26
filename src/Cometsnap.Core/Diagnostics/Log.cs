using System.Globalization;

namespace Cometsnap.Core.Diagnostics;

/// <summary>Tiny append-only log file. Never throws.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _path;

    public static void Initialize(string path)
    {
        _path = path;
        try
        {
            // Keep the log small: start over when it passes 1 MB.
            var info = new FileInfo(path);
            if (info.Exists && info.Length > 1_000_000) info.Delete();
        }
        catch { /* ignore */ }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        if (_path is null) return;
        try
        {
            var line = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} [{level}] {message}{Environment.NewLine}";
            lock (Gate) File.AppendAllText(_path, line);
        }
        catch { /* logging must never crash the app */ }
    }
}
