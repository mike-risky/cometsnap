using System.IO;
using Cometsnap.Core.Settings;
using Cometsnap.Core.Storage;
using Xunit;

namespace Cometsnap.Core.Tests;

public class SettingsAndSidecarTests
{
    [Fact]
    public void Settings_from_before_versioning_turn_the_sound_off()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cometsnap-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ \"PlaySounds\": true, \"LoupeCorner\": \"TopRight\" }");
            var s = AppSettings.Load(path);
            Assert.False(s.PlaySounds);
            Assert.Equal(LoupeCorner.TopRight, s.LoupeCorner);
            Assert.Equal(AppSettings.CurrentVersion, s.SettingsVersion);
            Assert.True(s.InstantCapture);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Current_settings_keep_the_sound_choice()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cometsnap-{Guid.NewGuid():N}.json");
        try
        {
            new AppSettings { PlaySounds = true }.Save(path);
            Assert.True(AppSettings.Load(path).PlaySounds);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void New_settings_are_quiet_and_instant()
    {
        var s = new AppSettings();
        Assert.False(s.PlaySounds);
        Assert.True(s.InstantCapture);
        Assert.True(s.ShowEditCard);
        Assert.False(s.TakeOverPrintScreen);
        Assert.False(s.ReplaceOfferAnswered);
    }

    [Fact]
    public void Sidecar_lives_in_a_hidden_folder_beside_the_capture()
    {
        var image = Path.Combine("C:", "Pics", "2026-11", "Snap 2026-11-26 at 16.30.12.png");
        var folder = Path.Combine("C:", "Pics", "2026-11", ".cometsnap");
        Assert.Equal(folder, EditSidecar.FolderFor(image));
        Assert.Equal(Path.Combine(folder, "Snap 2026-11-26 at 16.30.12.original.png"), EditSidecar.OriginalFor(image));
        Assert.Equal(Path.Combine(folder, "Snap 2026-11-26 at 16.30.12.annotations.json"), EditSidecar.AnnotationsFor(image));
        Assert.True(EditSidecar.IsSidecarFolder(folder));
    }

    [Fact]
    public void Recent_scan_skips_the_sidecar_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cometsnap-{Guid.NewGuid():N}");
        try
        {
            var month = Directory.CreateDirectory(Path.Combine(root, "2026-11")).FullName;
            var hidden = Directory.CreateDirectory(Path.Combine(root, ".cometsnap")).FullName;
            File.WriteAllBytes(Path.Combine(month, "a.png"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(hidden, "a.original.png"), new byte[] { 1 });
            var recent = new RecentCaptures(10);
            recent.Load(root);
            Assert.Single(recent.Snapshot());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
