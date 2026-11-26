using System.Diagnostics;
using System.IO;

namespace Cometsnap.App;

public static class Program
{
    private const string MutexName = @"Local\Cometsnap.SingleInstance";
    public const string ActivateEventName = @"Local\Cometsnap.Activate";
    public const string QuitEventName = @"Local\Cometsnap.Quit";

    [STAThread]
    public static int Main(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            if (!ShouldTakeOver())
            {
                // Same Cometsnap is already in the tray: just open its panel.
                Signal(ActivateEventName);
                return 0;
            }
            // A different or newer exe: the running one steps aside and this one takes over.
            Signal(QuitEventName);
            if (!WaitForMutex(mutex, 6000))
            {
                foreach (var p in OtherInstances()) { try { p.Kill(); p.WaitForExit(2000); } catch { } }
                if (!WaitForMutex(mutex, 3000)) return 1;
            }
        }

        RecordInstance();
        var app = new CometApp();
        return app.Run();
    }

    /// <summary>
    /// Take over when the running Cometsnap is another exe file, or the same file was replaced
    /// by a newer build after it started. Otherwise the running one stays.
    /// </summary>
    private static bool ShouldTakeOver()
    {
        var me = Environment.ProcessPath;
        if (me is null) return false;
        foreach (var p in OtherInstances())
        {
            try
            {
                var path = p.MainModule?.FileName;
                if (path is null) continue;
                if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(me), StringComparison.OrdinalIgnoreCase)) return true;
                if (File.GetLastWriteTime(me) > p.StartTime) return true;
            }
            catch
            {
                // Can't inspect it (e.g. elevated): leave it running.
            }
        }
        return false;
    }

    /// <summary>The running instance writes its process id here (downloads can rename the exe).</summary>
    private static string InstanceFile => Path.Combine(Path.GetTempPath(), "Cometsnap.instance");

    public static void RecordInstance()
    {
        try { File.WriteAllText(InstanceFile, Environment.ProcessId.ToString()); }
        catch { /* only used to find us later */ }
    }

    private static IEnumerable<Process> OtherInstances()
    {
        var current = Process.GetCurrentProcess();
        var found = Process.GetProcessesByName(current.ProcessName).Where(p => p.Id != current.Id).ToList();
        try
        {
            if (int.TryParse(File.ReadAllText(InstanceFile).Trim(), out var pid) && pid != current.Id && found.All(p => p.Id != pid))
            {
                // Pids get reused: only trust it if that process really is a Cometsnap.
                var p = Process.GetProcessById(pid);
                if (p.MainModule?.FileVersionInfo.ProductName?.StartsWith("Cometsnap", StringComparison.OrdinalIgnoreCase) == true)
                    found.Add(p);
            }
        }
        catch { /* no record, or that process is gone */ }
        return found;
    }

    private static void Signal(string name)
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(name, out var handle))
                using (handle) handle.Set();
        }
        catch { /* the other instance is closing anyway */ }
    }

    private static bool WaitForMutex(Mutex mutex, int ms)
    {
        try { return mutex.WaitOne(ms); }
        catch (AbandonedMutexException) { return true; }
    }
}
