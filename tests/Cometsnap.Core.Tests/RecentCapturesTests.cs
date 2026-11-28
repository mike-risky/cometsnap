using Cometsnap.Core.Storage;

namespace Cometsnap.Core.Tests;

public class RecentCapturesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("cometsnap-recent").FullName;

    private string Make(string month, string name, int minutesAgo)
    {
        var dir = Path.Combine(_root, month);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, new byte[] { 1 });
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-minutesAgo));
        return path;
    }

    [Fact]
    public void Loads_newest_first_across_months_and_respects_capacity()
    {
        var old = Make("2026-10", "snap_a.png", 60 * 24 * 30);
        var mid = Make("2026-11", "snap_b.png", 30);
        var newest = Make("2026-11", "snap_c.png", 1);
        Make("2026-11", "notes.txt", 0);

        var recent = new RecentCaptures(2);
        recent.Load(_root);

        Assert.Equal(new[] { newest, mid }, recent.Snapshot());
        Assert.DoesNotContain(old, recent.Snapshot());
    }

    [Fact]
    public void Finds_captures_in_nested_folders_and_jpegs()
    {
        var nested = Make(Path.Combine("2026", "11"), "shot.jpg", 2);
        var root = Make("", "loose.png", 5);
        var recent = new RecentCaptures(5);
        recent.Load(_root);
        Assert.Equal(new[] { nested, root }, recent.Snapshot());
    }

    [Fact]
    public void Add_puts_new_capture_on_top_without_duplicates()
    {
        var recent = new RecentCaptures(3);
        var changes = 0;
        recent.Changed += () => changes++;
        recent.Add("a.png");
        recent.Add("b.png");
        recent.Add("a.png");
        Assert.Equal(new[] { "a.png", "b.png" }, recent.Snapshot());
        recent.Remove("b.png");
        Assert.Equal(new[] { "a.png" }, recent.Snapshot());
        Assert.Equal(4, changes);
    }

    [Fact]
    public void Recordings_are_listed_with_captures()
    {
        var snap = Make("2026-11", "snap_a.png", 3);
        var video = Make("2026-11", "rec_b.mp4", 2);
        var gif = Make("2026-11", "rec_c.gif", 1);
        Make("2026-11", "snap_a.cometsnap.json", 0);

        var recent = new RecentCaptures(10);
        recent.Load(_root);

        Assert.Equal(new[] { gif, video, snap }, recent.Snapshot());
        Assert.True(MediaKinds.IsRecording(video));
        Assert.True(MediaKinds.IsGif(gif));
        Assert.False(MediaKinds.IsRecording(snap));
    }

    [Fact]
    public void Missing_folder_gives_empty_list()
    {
        var recent = new RecentCaptures(5);
        recent.Load(Path.Combine(_root, "nope"));
        Assert.Empty(recent.Snapshot());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }
}
