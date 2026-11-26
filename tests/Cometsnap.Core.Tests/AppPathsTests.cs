using Cometsnap.Core.Settings;
using Cometsnap.Core.Storage;

namespace Cometsnap.Core.Tests;

public class AppPathsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("cometsnap-test").FullName;

    [Fact]
    public void Normal_mode_uses_user_folders()
    {
        var p = new AppPaths(_dir, @"C:\AppData", @"C:\Pictures");
        Assert.False(p.IsPortable);
        Assert.Equal(Path.Combine(@"C:\AppData", "Cometsnap"), p.DataDirectory);
        Assert.Equal(Path.Combine(@"C:\Pictures", "Cometsnap"), p.DefaultCapturesDirectory);
    }

    [Fact]
    public void Portable_marker_keeps_everything_beside_the_exe()
    {
        File.WriteAllText(Path.Combine(_dir, AppPaths.PortableMarker), "");
        var p = new AppPaths(_dir, @"C:\AppData", @"C:\Pictures");
        Assert.True(p.IsPortable);
        Assert.StartsWith(_dir, p.DataDirectory);
        Assert.StartsWith(_dir, p.DefaultCapturesDirectory);
    }

    [Fact]
    public void Settings_round_trip_and_survive_corruption()
    {
        var file = Path.Combine(_dir, "settings.json");
        var s = new AppSettings { LoupeCorner = LoupeCorner.TopRight, CaptureHotkey = "Ctrl+Shift+X" };
        s.Save(file);
        var loaded = AppSettings.Load(file);
        Assert.Equal(LoupeCorner.TopRight, loaded.LoupeCorner);
        Assert.Equal("Ctrl+Shift+X", loaded.CaptureHotkey);

        File.WriteAllText(file, "{ this is not json");
        Assert.Equal(LoupeCorner.BottomLeft, AppSettings.Load(file).LoupeCorner);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
