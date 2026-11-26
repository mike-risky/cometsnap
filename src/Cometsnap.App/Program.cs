namespace Cometsnap.App;

public static class Program
{
    private const string MutexName = @"Local\Cometsnap.SingleInstance";

    [STAThread]
    public static int Main(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            // Cometsnap already lives in the tray. Nothing else to do.
            return 0;
        }

        var app = new CometApp();
        return app.Run();
    }
}
